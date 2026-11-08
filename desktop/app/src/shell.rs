//! Starting the app: one process, the engine on its own runtime, the tray, and a Rune app per window, each created
//! when it opens and destroyed (with its GPU device) when it closes, so while you play the app holds no window and no
//! GPU device.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use reelshelf_api::ApiClient;
use reelshelf_core::logs;
use reelshelf_core::notifications::NotificationAction;
use reelshelf_core::settings::UploaderPaths;
use reelshelf_core::state::{TrayBadge, UploaderState};
use rune::tray::{Tray, TrayEvent, TraySpec};
use rune::{App, AppHandle, RunOptions, Titlebar, Tx, WindowHandle, WindowOptions, WindowSpec};

use crate::api_adapter::ReelshelfApi;
use crate::app_core::{AppCore, CoreParts};
use crate::links;
use crate::model::{Change, Commands, FlyoutPage, Show, Ui, WindowHost, WindowKind};
use crate::platform::Platform;
use crate::tray::Placement;
use crate::{tray, ui};

/// A display's (x, y, width, height) in physical pixels and its pixels per point.
type DisplayBounds = ((i32, i32, u32, u32), f64);

/// The windows' handles and the tray, as the core opens, changes and closes them.
pub struct RuneHost {
    handles: HashMap<WindowKind, AppHandle<Ui>>,
    commands: Arc<dyn Commands>,
    tray: Mutex<Option<(TrayBadge, Tray)>>,
    /// The primary display (x, y, width, height in physical pixels) and its scale, for placing the flyout when the
    /// tray's own place isn't known.
    display: Mutex<Option<DisplayBounds>>,
    /// What the open flyout is placed from, so it stays beside the tray as its height follows its content.
    flyout: Mutex<Option<Placement>>,
    /// Each open window's native window, from `App::on_window`.
    native: Mutex<HashMap<WindowKind, WindowHandle>>,
}

fn lock<T>(mutex: &Mutex<T>) -> std::sync::MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

impl RuneHost {
    fn handle(&self, kind: WindowKind) -> &AppHandle<Ui> {
        &self.handles[&kind]
    }

    /// Where the flyout opens from: the tray icon on Windows, else the primary display's bottom right.
    fn flyout_placement(&self) -> Option<Placement> {
        #[cfg(windows)]
        if let Some(placement) = crate::win::flyout_placement() {
            return Some(placement);
        }
        lock(&self.display).map(|(display, scale)| Placement::bottom_right(display, scale))
    }

    /// After each frame of the flyout: it moves back beside the tray when its height changed (each page has its own).
    fn after_frame(&self, kind: WindowKind) {
        let Some(window) = lock(&self.native).get(&kind).cloned() else {
            return;
        };
        if kind == WindowKind::Flyout
            && let Some(placement) = *lock(&self.flyout)
            && let Some(now) = window.placement()
        {
            let wanted = tray::flyout_position(&placement, now.content_size);
            if now.position != wanted {
                window.set_position(wanted);
            }
        }
    }

    /// The tray's events: a primary click opens or closes the flyout, the menu does what it says.
    fn on_tray(commands: &Arc<dyn Commands>, host: &RuneHost, event: TrayEvent) {
        match event {
            TrayEvent::Activate { .. } => {
                if host.is_open(WindowKind::Flyout) {
                    host.close(WindowKind::Flyout);
                } else {
                    commands.show(Show::Flyout(FlyoutPage::Home));
                }
            }
            TrayEvent::Menu(id) => tray::on_menu(&id, commands.as_ref()),
            _ => {}
        }
    }
}

impl WindowHost for RuneHost {
    fn open(&self, kind: WindowKind, prepare: Change) {
        let handle = self.handle(kind);
        handle.update(move |ui: &mut Ui, tx: &mut Tx| prepare(ui, tx));
        if kind == WindowKind::Flyout {
            let placement = self.flyout_placement();
            *lock(&self.flyout) = placement;
            if let Some(placement) = placement {
                handle.set_position(tray::flyout_position(&placement, placement.pixels(ui::size(kind))));
            }
        }
        handle.open();
    }

    fn update(&self, kind: WindowKind, change: Change) {
        self.handle(kind).update(move |ui: &mut Ui, tx: &mut Tx| change(ui, tx));
    }

    fn close(&self, kind: WindowKind) {
        self.handle(kind).close();
    }

    fn is_open(&self, kind: WindowKind) -> bool {
        self.handle(kind).is_visible()
    }

    fn tray(&self, state: &UploaderState) {
        let mut tray = self.tray.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        if let Some((badge, current)) = tray.as_ref()
            && *badge == state.badge
        {
            current.set_tooltip(state.tooltip());
            return;
        }
        // Rune's tray can't change its icon, so a new badge means a new tray icon.
        *tray = None;
        let spec = TraySpec::new(links::APP_ID, reelshelf_core::copy::APP_NAME, tray::icon(state.badge))
            .tooltip(state.tooltip())
            .menu(tray::menu());
        let commands = self.commands.clone();
        let host = HOST.get().cloned();
        match Tray::new(spec, move |event| {
            if let Some(host) = &host {
                RuneHost::on_tray(&commands, host, event);
            }
        }) {
            Ok(created) => *tray = Some((state.badge, created)),
            Err(error) => tracing::warn!("Couldn't show the tray icon: {error}"),
        }
    }

    fn quit(&self) {
        *self.tray.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = None;
        if let Some(handle) = self.handles.values().next() {
            handle.quit();
        }
    }
}

static HOST: std::sync::OnceLock<Arc<RuneHost>> = std::sync::OnceLock::new();

/// A window's Rune app: windowless, kept running, its window and GPU device released when it closes.
fn window_app(kind: WindowKind, commands: &Arc<dyn Commands>) -> App<Ui> {
    let commands = commands.clone();
    let mut app = App::new(ui::spec(kind))
        .init(move || Ui::new(kind, commands))
        .args(Vec::new())
        .windowless()
        .keep_running()
        .release_gpu_when_closed();
    app = match kind {
        WindowKind::Flyout => app
            .window_options(WindowOptions {
                borderless: true,
                transparent: true,
                always_on_top: true,
                resizable: false,
                min_size: Some((0.0, 0.0)),
                ..WindowOptions::default()
            })
            .fit_content_height()
            .on_focus(|s: &mut Ui, focused: bool, tx: &mut Tx| {
                // A folder picker or menu the flyout opened takes the focus; the flyout stays for its answer.
                if !focused && !s.flyout.held_open {
                    tx.close_window();
                }
            }),
        _ => app.titlebar(Titlebar::new(36.0)).window_options(WindowOptions {
            resizable: false,
            min_size: Some((0.0, 0.0)),
            ..WindowOptions::default()
        }),
    };
    app = app
        .on_window(move |window: &WindowHandle| {
            if let Some(host) = HOST.get() {
                lock(&host.native).insert(kind, window.clone());
                host.after_frame(kind);
            }
        })
        .on_frame_drawn(move |_| {
            if let Some(host) = HOST.get() {
                host.after_frame(kind);
            }
        });
    if kind == WindowKind::Flyout {
        // A second launch reopens the app (Windows hands it over through `single_instance`): it shows the flyout's
        // Settings. Links from notifications arrive here too.
        app = app
            .on_reopen(|s: &mut Ui, _: bool, _: &mut Tx| {
                s.commands.show(Show::Flyout(FlyoutPage::Settings));
                true
            })
            .on_open_url(|s: &mut Ui, url: &str, _: &mut Tx| on_app_link(s.commands.as_ref(), url));
    }
    app
}

/// What a notification's button asks for.
pub fn on_app_link(commands: &dyn Commands, url: &str) {
    match links::parse_app_link(url) {
        Some(NotificationAction::OpenLibrary) => commands.open_link(links::Link::Library),
        Some(NotificationAction::ChooseBook(folder)) => commands.show(Show::ChooseBook(folder)),
        Some(NotificationAction::NotNow) => {}
        Some(NotificationAction::SeePlans) => commands.open_link(links::Link::Plans),
        Some(NotificationAction::OpenStore) => commands.open_link(links::Link::Store),
        Some(NotificationAction::SignIn) => commands.show(Show::SignIn),
        None => tracing::warn!("Ignored a link the app doesn't know"),
    }
}

/// Runs the app until Quit.
pub fn run() {
    if !rune::single_instance(links::APP_ID) {
        return;
    }
    let paths = UploaderPaths::default_location();
    let _log = logs::init(paths.logs());
    let site = links::site();
    tracing::info!("Starting the uploader {} against {site}", links::version());

    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .enable_all()
        .build()
        .expect("the uploader's runtime starts");
    let platform = Platform::native();
    let client = ApiClient::new(&site, links::version()).expect("the HTTP client starts");
    let api = Arc::new(ReelshelfApi::new(client, platform.credentials.clone()));
    let core = AppCore::new(CoreParts {
        api,
        platform,
        paths,
        site,
        runtime: runtime.handle().clone(),
    });
    let commands: Arc<dyn Commands> = core.clone();

    let apps: Vec<(WindowKind, App<Ui>)> = WindowKind::ALL
        .into_iter()
        .map(|kind| (kind, window_app(kind, &commands)))
        .collect();
    let host = Arc::new(RuneHost {
        handles: apps.iter().map(|(kind, app)| (*kind, app.handle())).collect(),
        commands: commands.clone(),
        tray: Mutex::new(None),
        display: Mutex::new(None),
        flyout: Mutex::new(None),
        native: Mutex::new(HashMap::new()),
    });
    let _ = HOST.set(host.clone());
    core.set_host(host.clone());

    let placed = host.clone();
    let started = core.clone();
    let result =
        rune::try_run_windows_with_args(RunOptions { accessory: true }, Vec::<String>::new(), move |displays| {
            if let Some(display) = displays.iter().find(|display| display.primary).or(displays.first()) {
                let bounds = (display.position.0, display.position.1, display.size.0, display.size.1);
                *placed.display.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = Some((bounds, display.scale));
            }
            started.start();
            apps.into_iter()
                .map(|(kind, app)| WindowSpec::from_app(kind.key(), app))
                .collect::<Vec<_>>()
        });
    if let Err(error) = result {
        tracing::error!("The app couldn't start: {error}");
    }
    runtime.shutdown_background();
}
