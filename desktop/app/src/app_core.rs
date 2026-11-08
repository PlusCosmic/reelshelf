//! The app's core: the engine, sign-in and the platform parts, and what each window's buttons do with them
//! ([`Commands`]). It works nothing out about uploads itself; it calls the engine and shows what it says.

use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock, Weak};

use reelshelf_core::api::UploaderApi;
use reelshelf_core::engine::{EngineEvent, EngineParts, UploaderEngine};
use reelshelf_core::files;
use reelshelf_core::ledger::{Ledger, LedgerEntry};
use reelshelf_core::matching::Book;
use reelshelf_core::notifications::Notification;
use reelshelf_core::platform::{Clock, StartupState, SystemClock};
use reelshelf_core::settings::{FullScreenMode, SettingsStore, UploaderPaths};
use reelshelf_core::sign_in::{DeviceSignIn, SignInOutcome};
use reelshelf_core::state::{PauseLength, TrayAction};
use tokio::sync::broadcast::error::RecvError;
use uuid::Uuid;

use crate::links::{self, Link};
use crate::model::{
    Commands, FlyoutDraft, FlyoutPage, Model, SetupDraft, Show, SignInPhase, Ui, WindowHost, WindowKind,
};
use crate::platform::{self, Platform};
use crate::ui;

/// What the core is built from.
pub struct CoreParts<A> {
    pub api: Arc<A>,
    pub platform: Platform,
    pub paths: UploaderPaths,
    /// The site the API is served from (`links::site()`).
    pub site: String,
    /// Where the engine and sign-in run.
    pub runtime: tokio::runtime::Handle,
}

pub struct AppCore<A: UploaderApi> {
    me: Weak<AppCore<A>>,
    engine: Arc<UploaderEngine<A>>,
    sign_in: Arc<DeviceSignIn<A>>,
    platform: Platform,
    host: OnceLock<Arc<dyn WindowHost>>,
    runtime: tokio::runtime::Handle,
    site: String,
    logs: PathBuf,
    /// Each S1 sign-in counts up, so a sign-in that was started over can't change S1 when it ends.
    sign_in_run: AtomicU64,
    /// How many of the recordings folder's folders have a book (S5), worked out when the folder or books change.
    matched: Mutex<Option<usize>>,
}

impl<A: UploaderApi> AppCore<A> {
    /// The core over `parts`. Panics when the uploader's folder or ledger can't be opened, without which it can't run.
    pub fn new(parts: CoreParts<A>) -> Arc<Self> {
        let paths = parts.paths;
        if let Err(error) = std::fs::create_dir_all(&paths.root) {
            panic!("couldn't create {}: {error}", paths.root.display());
        }
        let ledger = Ledger::open(&paths.ledger())
            .unwrap_or_else(|error| panic!("couldn't open {}: {error}", paths.ledger().display()));
        let clock: Arc<dyn Clock> = Arc::new(SystemClock);
        let engine = UploaderEngine::new(EngineParts {
            api: parts.api.clone(),
            ledger: Arc::new(ledger),
            settings: SettingsStore::new(paths.settings()),
            credentials: parts.platform.credentials.clone(),
            game: parts.platform.game.clone(),
            notifier: parts.platform.notifier.clone(),
            clock: clock.clone(),
            can_open: Arc::new(files::can_open_for_reading),
        });
        let sign_in = DeviceSignIn::new(parts.api, parts.platform.credentials.clone(), clock);
        Arc::new_cyclic(|me| AppCore {
            me: me.clone(),
            engine,
            sign_in: Arc::new(sign_in),
            platform: parts.platform,
            host: OnceLock::new(),
            runtime: parts.runtime,
            site: parts.site,
            logs: paths.logs(),
            sign_in_run: AtomicU64::new(0),
            matched: Mutex::new(None),
        })
    }

    fn self_arc(&self) -> Option<Arc<Self>> {
        self.me.upgrade()
    }

    pub fn engine(&self) -> &Arc<UploaderEngine<A>> {
        &self.engine
    }

    /// Where windows open; set once, after the windows exist.
    pub fn set_host(&self, host: Arc<dyn WindowHost>) {
        let _ = self.host.set(host);
    }

    fn host(&self) -> Option<&Arc<dyn WindowHost>> {
        self.host.get()
    }

    /// Runs the engine and follows its events, and opens S1 or S2 when the app isn't signed in or set up.
    pub fn start(self: &Arc<Self>) {
        let engine = self.engine.clone();
        self.runtime.spawn(async move { engine.run().await });
        let mut events = self.engine.subscribe();
        let core = self.clone();
        self.runtime.spawn(async move {
            loop {
                match events.recv().await {
                    Ok(event) => core.on_event(event),
                    Err(RecvError::Lagged(_)) => core.refresh(),
                    Err(RecvError::Closed) => return,
                }
            }
        });
        if let Some(host) = self.host() {
            host.tray(&self.engine.state());
        }
        if !self.engine.is_signed_in() {
            self.show(Show::SignIn);
        } else if !self.engine.is_set_up() {
            self.show(Show::Setup);
        }
    }

    fn on_event(&self, event: EngineEvent) {
        match event {
            EngineEvent::StateChanged(state) => {
                if let Some(host) = self.host() {
                    host.tray(&state);
                }
            }
            EngineEvent::SignedOut => {
                tracing::info!("The API refused the device token; showing sign-in");
                self.show(Show::SignIn);
            }
            EngineEvent::BooksChanged => self.forget_matches(),
            EngineEvent::BookNeeded(_) | EngineEvent::LedgerChanged => {}
        }
        self.refresh();
    }

    /// Gives every open window the engine as it is now.
    pub fn refresh(&self) {
        let Some(host) = self.host() else {
            return;
        };
        let model = self.snapshot();
        for kind in WindowKind::ALL {
            if host.is_open(kind) {
                let model = model.clone();
                host.update(kind, Box::new(move |ui: &mut Ui, _| ui.model = model));
            }
        }
    }

    fn forget_matches(&self) {
        *self.matched.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = None;
    }

    fn matched_folders(&self) -> usize {
        let mut matched = self.matched.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        *matched.get_or_insert_with(|| {
            self.engine
                .folder_matches(None)
                .iter()
                .filter(|(_, book)| book.is_some())
                .count()
        })
    }

    fn open(&self, kind: WindowKind, prepare: impl FnOnce(&mut Ui, &mut rune::Tx) + Send + 'static) {
        let model = self.snapshot();
        if let Some(host) = self.host() {
            host.open(
                kind,
                Box::new(move |ui: &mut Ui, tx: &mut rune::Tx| {
                    ui.model = model;
                    prepare(ui, tx);
                }),
            );
        }
    }

    fn set_sign_in_phase(&self, run: u64, phase: SignInPhase) {
        if self.sign_in_run.load(Ordering::SeqCst) != run {
            return;
        }
        if let Some(host) = self.host() {
            host.update(
                WindowKind::SignIn,
                Box::new(move |ui: &mut Ui, tx: &mut rune::Tx| {
                    tx.resize_window(ui::sign_in_size(&phase));
                    ui.sign_in = phase;
                }),
            );
        }
    }

    fn signed_in(&self, device_name: String) {
        tracing::info!("Signed in");
        self.engine
            .update_settings(|settings| settings.device_name = Some(device_name));
        self.engine.on_signed_in();
        if let Some(host) = self.host() {
            host.close(WindowKind::SignIn);
        }
        if !self.engine.is_set_up() {
            self.show(Show::Setup);
        }
        self.refresh();
    }

    /// S2's draft for the folder in the settings, or none yet.
    fn setup_draft(&self) -> SetupDraft {
        let settings = self.engine.settings();
        let first_run = settings.recordings_folder.is_none();
        let folder = settings.recordings_folder.clone();
        SetupDraft {
            matches: folder
                .as_deref()
                .map(|folder| self.folder_matches(folder))
                .unwrap_or_default(),
            folder,
            default_book: settings.default_book_id,
            start_with_windows: first_run || self.platform.startup.state() == StartupState::Enabled,
        }
    }
}

impl<A: UploaderApi> Commands for AppCore<A> {
    fn snapshot(&self) -> Model {
        let engine = &self.engine;
        Model {
            state: engine.state(),
            inputs: engine.inputs(),
            settings: engine.settings(),
            books: engine.books(),
            recent: engine.recent_uploads(),
            current_upload: engine.current_upload(),
            matched_folders: self.matched_folders(),
            uploads_in_progress: engine.uploads_in_progress(),
            startup: self.platform.startup.state(),
            version: links::version().to_owned(),
            device_page: links::device_page(&self.site),
        }
    }

    fn show(&self, window: Show) {
        match window {
            Show::SignIn => {
                self.open(WindowKind::SignIn, |ui, tx| {
                    ui.sign_in = SignInPhase::Starting;
                    tx.resize_window(ui::sign_in_size(&ui.sign_in));
                });
                self.start_sign_in();
            }
            Show::Setup => {
                let draft = self.setup_draft();
                self.open(WindowKind::Setup, move |ui, _| ui.setup = draft);
            }
            Show::ChooseBook(folder) => {
                let engine = self.engine.clone();
                self.runtime.spawn(async move { engine.refresh_books().await });
                self.open(WindowKind::Flyout, move |ui, _| {
                    ui.flyout = FlyoutDraft {
                        page: FlyoutPage::Book,
                        ..FlyoutDraft::default()
                    };
                    ui.picker.folder = folder;
                    ui.picker.selected = None;
                });
            }
            Show::Flyout(FlyoutPage::Settings) if !self.engine.is_signed_in() => self.show(Show::SignIn),
            Show::Flyout(page) => self.open(WindowKind::Flyout, move |ui, _| {
                ui.flyout = FlyoutDraft {
                    page,
                    ..FlyoutDraft::default()
                };
            }),
        }
    }

    fn start_sign_in(&self) {
        let run = self.sign_in_run.fetch_add(1, Ordering::SeqCst) + 1;
        self.set_sign_in_phase(run, SignInPhase::Starting);
        let sign_in = self.sign_in.clone();
        let shell = self.platform.shell.clone();
        let notifier = self.platform.notifier.clone();
        let device_name = platform::device_name();
        let core = self.self_arc();
        self.runtime.spawn(async move {
            let Some(core) = core else {
                return;
            };
            let pending = match sign_in.start(&device_name).await {
                SignInOutcome::Started(pending) => pending,
                SignInOutcome::UpdateRequired => {
                    notifier.post(Notification::UpdateRequired);
                    core.set_sign_in_phase(run, SignInPhase::Unreachable);
                    return;
                }
                _ => {
                    core.set_sign_in_phase(run, SignInPhase::Unreachable);
                    return;
                }
            };
            shell.open(&pending.verification_uri_complete);
            core.set_sign_in_phase(
                run,
                SignInPhase::Waiting {
                    user_code: pending.user_code.clone(),
                    verification_uri_complete: pending.verification_uri_complete.clone(),
                },
            );
            let phase = match sign_in.wait_for_approval(&pending).await {
                SignInOutcome::SignedIn => {
                    if core.sign_in_run.load(Ordering::SeqCst) == run {
                        core.signed_in(device_name);
                    }
                    return;
                }
                SignInOutcome::Denied => SignInPhase::Denied,
                SignInOutcome::Expired => SignInPhase::Expired,
                SignInOutcome::UpdateRequired => {
                    notifier.post(Notification::UpdateRequired);
                    SignInPhase::Unreachable
                }
                _ => SignInPhase::Unreachable,
            };
            core.set_sign_in_phase(run, phase);
        });
    }

    fn open_link(&self, link: Link) {
        self.platform.shell.open(&link.url(&self.site));
    }

    fn open_folder(&self, path: &str) {
        self.platform.shell.open(path);
    }

    fn open_log_folder(&self) {
        let logs = self.logs.to_string_lossy().into_owned();
        self.platform.shell.open(&logs);
    }

    fn folder_matches(&self, folder: &str) -> Vec<(String, Option<Book>)> {
        self.engine.folder_matches(Some(folder))
    }

    fn start_watching(&self, folder: &str, default_book: Option<Uuid>, start_with_windows: bool) {
        tracing::info!("Watching a new recordings folder");
        self.engine.set_recordings_folder(folder);
        self.engine.set_default_book(default_book);
        self.platform.startup.set_enabled(start_with_windows);
        self.forget_matches();
        self.refresh();
    }

    fn choose_book(&self, folder: &str, book: Uuid) {
        self.engine.choose_book_for_folder(folder, book);
        self.forget_matches();
        if let Some(host) = self.host() {
            host.update(
                WindowKind::Setup,
                Box::new(|ui: &mut Ui, _| {
                    if let Some(folder) = ui.setup.folder.clone() {
                        ui.setup.matches = ui.commands.folder_matches(&folder);
                    }
                }),
            );
        }
        self.refresh();
    }

    fn tray_action(&self, action: TrayAction) {
        match action {
            TrayAction::ShowRecent => self.show(Show::Flyout(FlyoutPage::Recent)),
            TrayAction::UploadNow => self.engine.upload_now(),
            TrayAction::Resume => self.engine.resume(),
            TrayAction::ChooseBook => {
                if let Some((folder, _)) = self.engine.inputs().needs_book {
                    self.show(Show::ChooseBook(folder));
                }
            }
            TrayAction::SeePlans => self.open_link(Link::Plans),
            TrayAction::TryNow => self.engine.try_now(None),
            TrayAction::OpenStore => self.open_link(Link::Store),
        }
        self.refresh();
    }

    fn pause(&self, length: PauseLength) {
        self.engine.pause(length);
        self.refresh();
    }

    fn quit(&self, confirmed: bool) {
        let Some(host) = self.host() else {
            return;
        };
        if !confirmed && self.engine.uploads_in_progress() > 0 {
            self.open(WindowKind::Quit, |_, _| {});
        } else {
            tracing::info!("Quitting");
            host.quit();
        }
    }

    fn set_default_book(&self, book: Option<Uuid>) {
        self.engine.set_default_book(book);
        self.forget_matches();
        self.refresh();
    }

    fn set_full_screen(&self, mode: FullScreenMode) {
        self.engine.update_settings(|settings| settings.full_screen = mode);
        self.refresh();
    }

    fn set_speed_limit(&self, mbps: Option<u32>) {
        self.engine.update_settings(|settings| settings.speed_limit_mbps = mbps);
        self.refresh();
    }

    fn set_start_with_windows(&self, enabled: bool) {
        self.platform.startup.set_enabled(enabled);
        self.refresh();
    }

    fn set_notify_shelved(&self, enabled: bool) {
        self.engine
            .update_settings(|settings| settings.notify_shelved = enabled);
        self.refresh();
    }

    fn sign_out(&self) {
        tracing::info!("Signing out");
        let engine = self.engine.clone();
        let core = self.self_arc();
        self.runtime.spawn(async move {
            engine.sign_out().await;
            if let Some(core) = core {
                core.show(Show::SignIn);
            }
        });
    }

    fn try_again(&self, entry: &LedgerEntry) {
        self.engine.try_now(Some(entry));
        self.refresh();
    }
}
