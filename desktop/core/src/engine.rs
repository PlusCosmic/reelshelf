//! The uploader's running part: watches the recordings folder, keeps the account's books and plan, uploads one clip at
//! a time, and works out the tray's [`UploaderState`] and the notifications. The app renders what it says and calls
//! its methods; it works nothing out itself.

use std::path::Path;
use std::sync::{Arc, Mutex, MutexGuard, Weak};
use std::time::Duration;

use chrono::{DateTime, Days, FixedOffset, TimeDelta, Utc};
use tokio::sync::{Notify, broadcast};
use uuid::Uuid;

use crate::BoxFuture;
use crate::api::{ApiError, UploaderApi};
use crate::files;
use crate::ledger::{Ledger, LedgerEntry, LedgerState, RECENT_COUNT};
use crate::matching::{self, Book};
use crate::monitor::{self, CanOpen, FolderMonitor};
use crate::notifications::{Notification, ShelvedBook};
use crate::platform::{Clock, CredentialStore, GameActivity, Notifier};
use crate::recent::RecentUpload;
use crate::settings::{FullScreenMode, SLOW_UPLOAD_MBPS, SettingsStore, UploaderSettings};
use crate::state::{self, LibraryFullInfo, PauseInfo, PauseLength, StateInputs, UploaderState};
use crate::uploads::{
    self, MAX_ATTEMPTS, MultipartUploader, PlanLimits, ProgressFn, TokenBucket, UploadGate, UploadOutcome,
    UploadPipeline,
};
use crate::watcher::{FolderWatcher, WatchSignals};

/// The whole folder is looked over this often, besides at start and after the watcher errs or overflows.
pub const RESCAN_EVERY: Duration = Duration::from_secs(60);

/// The account's books are read again this often, besides at start and when S3 opens.
pub const BOOKS_EVERY: Duration = Duration::from_secs(60 * 60);

/// A full library's plan is checked again this often.
pub const PLAN_FULL_RECHECK: Duration = Duration::from_secs(30 * 60);

/// N1 is shown at most this often.
pub const SHELVED_NOTIFICATION_GAP: Duration = Duration::from_secs(10 * 60);

/// How often a held upload looks again at the game and the pause.
const GATE_CHECK_EVERY: Duration = Duration::from_secs(2);

fn delta(duration: Duration) -> TimeDelta {
    TimeDelta::from_std(duration).unwrap_or(TimeDelta::MAX)
}

/// What the engine tells the app, besides the notifications.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum EngineEvent {
    /// The tray's state changed.
    StateChanged(UploaderState),
    /// The API refused the token; the app shows S1.
    SignedOut,
    /// A folder needs a book chosen (S3).
    BookNeeded(String),
    BooksChanged,
    /// A clip's upload moved on, for S4.
    LedgerChanged,
}

/// What the engine is built from: the API, the ledger and settings, and the app's platform parts.
pub struct EngineParts<A> {
    pub api: Arc<A>,
    pub ledger: Arc<Ledger>,
    pub settings: SettingsStore,
    pub credentials: Arc<dyn CredentialStore>,
    pub game: Arc<dyn GameActivity>,
    pub notifier: Arc<dyn Notifier>,
    pub clock: Arc<dyn Clock>,
    /// [`files::can_open_for_reading`] in the app.
    pub can_open: CanOpen,
}

struct CurrentUpload {
    path: String,
    title: String,
    percent: u8,
}

struct Status {
    books: Vec<Book>,
    plan: Option<PlanLimits>,
    last_rescan: Option<DateTime<Utc>>,
    last_books: Option<DateTime<Utc>>,
    last_plan_check: Option<DateTime<Utc>>,
    last_shelved_notice: Option<DateTime<Utc>>,
    update_required: bool,
    update_notified: bool,
    signed_out: bool,
    library_full_notified: bool,
    library_full: Option<LibraryFullInfo>,
    unreachable_retry_at: Option<DateTime<Utc>>,
    paused: Option<PauseInfo>,
    upload_now: bool,
    current: Option<CurrentUpload>,
    /// Folders N2 has asked about, ignoring case, with the count it gave.
    asked_folders: Vec<(String, i64)>,
    /// Clips shelved since the last N1, by book name.
    shelved_since_notice: Vec<(String, i64)>,
    state: UploaderState,
}

pub struct UploaderEngine<A: UploaderApi> {
    api: Arc<A>,
    ledger: Arc<Ledger>,
    settings_store: SettingsStore,
    credentials: Arc<dyn CredentialStore>,
    game: Arc<dyn GameActivity>,
    notifier: Arc<dyn Notifier>,
    clock: Arc<dyn Clock>,
    monitor: FolderMonitor,
    pipeline: UploadPipeline<A>,
    signals: Arc<WatchSignals>,
    watcher: Mutex<Option<FolderWatcher>>,
    wake: Notify,
    events: broadcast::Sender<EngineEvent>,
    settings: Mutex<UploaderSettings>,
    status: Mutex<Status>,
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Clears the engine's current upload when the attempt ends, however it ends.
struct CurrentGuard<'a, A: UploaderApi>(&'a UploaderEngine<A>);

impl<A: UploaderApi> Drop for CurrentGuard<'_, A> {
    fn drop(&mut self) {
        lock(&self.0.status).current = None;
    }
}

impl<A: UploaderApi> UploaderEngine<A> {
    pub fn new(parts: EngineParts<A>) -> Arc<Self> {
        Arc::new_cyclic(|engine: &Weak<Self>| {
            let settings = parts.settings.load();
            let gate: Arc<dyn UploadGate> = Arc::new(EngineGate { engine: engine.clone() });
            let progress_engine = engine.clone();
            let progress: ProgressFn = Arc::new(move |entry: &LedgerEntry, sent: u64, total: u64| {
                if let Some(engine) = progress_engine.upgrade() {
                    engine.on_progress(entry, sent, total);
                }
            });
            let bucket = Arc::new(TokenBucket::new(parts.clock.clone()));
            let uploader = MultipartUploader::new(
                parts.api.clone(),
                parts.ledger.clone(),
                parts.clock.clone(),
                bucket,
                gate,
                Some(progress),
            );
            let pipeline = UploadPipeline::new(parts.api.clone(), parts.ledger.clone(), uploader, parts.clock.clone());
            let signed_out = parts.credentials.read_token().is_none_or(|token| token.is_empty());
            let now = parts.clock.now();
            let initial = UploaderState::from_inputs(&StateInputs::default(), now, &parts.clock.local_offset(now));
            let signals = Arc::new(WatchSignals::default());
            signals.request_rescan();
            UploaderEngine {
                monitor: FolderMonitor::new(parts.ledger.clone(), parts.clock.clone(), parts.can_open),
                api: parts.api,
                ledger: parts.ledger,
                settings_store: parts.settings,
                credentials: parts.credentials,
                game: parts.game,
                notifier: parts.notifier,
                clock: parts.clock,
                pipeline,
                signals,
                watcher: Mutex::new(None),
                wake: Notify::new(),
                events: broadcast::channel(256).0,
                settings: Mutex::new(settings),
                status: Mutex::new(Status {
                    books: Vec::new(),
                    plan: None,
                    last_rescan: None,
                    last_books: None,
                    last_plan_check: None,
                    last_shelved_notice: None,
                    update_required: false,
                    update_notified: false,
                    signed_out,
                    library_full_notified: false,
                    library_full: None,
                    unreachable_retry_at: None,
                    paused: None,
                    upload_now: false,
                    current: None,
                    asked_folders: Vec::new(),
                    shelved_since_notice: Vec::new(),
                    state: initial,
                }),
            }
        })
    }

    // ---- What the app reads ----

    pub fn subscribe(&self) -> broadcast::Receiver<EngineEvent> {
        self.events.subscribe()
    }

    pub fn settings(&self) -> UploaderSettings {
        lock(&self.settings).clone()
    }

    pub fn books(&self) -> Vec<Book> {
        lock(&self.status).books.clone()
    }

    pub fn plan(&self) -> Option<PlanLimits> {
        lock(&self.status).plan
    }

    pub fn state(&self) -> UploaderState {
        lock(&self.status).state.clone()
    }

    pub fn is_signed_in(&self) -> bool {
        !lock(&self.status).signed_out
    }

    pub fn is_set_up(&self) -> bool {
        lock(&self.settings)
            .recordings_folder
            .as_deref()
            .is_some_and(|folder| !folder.is_empty())
    }

    /// The recordings folder as the ledger keys it, once it is being watched.
    pub fn root(&self) -> Option<String> {
        self.monitor.root()
    }

    /// The file uploading now (its path below the root) and how far it has got, for S4.
    pub fn current_upload(&self) -> Option<(String, u8)> {
        lock(&self.status)
            .current
            .as_ref()
            .map(|current| (current.path.clone(), current.percent))
    }

    pub fn recent(&self) -> Vec<LedgerEntry> {
        self.ledger.recent(RECENT_COUNT)
    }

    /// S4's rows, in the words screens.html gives each state.
    pub fn recent_uploads(&self) -> Vec<RecentUpload> {
        let now = self.clock.now();
        let zone = self.clock.local_offset(now);
        let books = self.books();
        let current = self.current_upload();
        self.recent()
            .iter()
            .map(|entry| {
                let percent = current
                    .as_ref()
                    .filter(|(path, _)| *path == entry.path && Some(&entry.root) == self.root().as_ref())
                    .map(|&(_, percent)| percent);
                RecentUpload::from_entry(entry, &books, now, percent, &zone)
            })
            .collect()
    }

    /// Clips uploading or waiting for their turn, which Quit would stop.
    pub fn uploads_in_progress(&self) -> usize {
        self.root().map_or(0, |root| {
            self.ledger
                .count(&root, &[LedgerState::Uploading, LedgerState::Waiting])
        })
    }

    /// The folders below the recordings folder with videos in them, with the book each goes on (S2, S5).
    pub fn folder_matches(&self, folder: Option<&str>) -> Vec<(String, Option<Book>)> {
        let settings = self.settings();
        let Some(folder) = folder.map(str::to_string).or(settings.recordings_folder.clone()) else {
            return Vec::new();
        };
        let books = self.books();
        files::folders_with_videos(Path::new(&folder))
            .into_iter()
            .map(|name| {
                let found = matching::resolve(&name, &books, settings.saved_book_for(&name), None);
                let book = books.iter().find(|book| Some(book.id) == found.category_id).cloned();
                (name, book)
            })
            .collect()
    }

    // ---- Setup and settings (S2, S3, S5) ----

    fn save_settings(&self, settings: &UploaderSettings) {
        if let Err(error) = self.settings_store.save(settings) {
            tracing::error!("Couldn't save the settings: {error}");
        }
    }

    /// Watches a new recordings folder. Files already in it are baselined; the ledger is kept, so a folder used before
    /// doesn't upload twice.
    pub fn set_recordings_folder(&self, folder: &str) {
        let root = monitor::root_key(folder);
        {
            let mut settings = lock(&self.settings);
            settings.recordings_folder = Some(root.clone());
            self.save_settings(&settings);
        }
        self.monitor.use_folder(&root, true);
        self.start_watcher();
        self.signals.request_rescan();
        self.wake.notify_one();
    }

    /// Saves the book for a folder; its waiting clips go on it at once.
    pub fn choose_book_for_folder(&self, folder: &str, book_id: Uuid) {
        {
            let mut settings = lock(&self.settings);
            settings.folder_books.insert(folder, book_id);
            self.save_settings(&settings);
        }
        {
            let lower = folder.to_lowercase();
            lock(&self.status)
                .asked_folders
                .retain(|(asked, _)| asked.to_lowercase() != lower);
        }
        if let Some(root) = self.root() {
            self.ledger.assign_book(&root, folder, book_id, self.clock.now());
        }
        self.changed();
        self.wake.notify_one();
    }

    pub fn set_default_book(&self, book_id: Option<Uuid>) {
        let settings = {
            let mut settings = lock(&self.settings);
            settings.default_book_id = book_id;
            self.save_settings(&settings);
            settings.clone()
        };
        self.monitor.rematch(&self.books(), &settings);
        self.changed();
        self.wake.notify_one();
    }

    pub fn update_settings(&self, change: impl FnOnce(&mut UploaderSettings)) {
        {
            let mut settings = lock(&self.settings);
            change(&mut settings);
            self.save_settings(&settings);
        }
        self.changed();
    }

    // ---- The tray's actions ----

    pub fn pause(&self, length: PauseLength) {
        let now = self.clock.now();
        let until = match length {
            PauseLength::OneHour => Some(now + TimeDelta::hours(1)),
            PauseLength::UntilTomorrow => Some(self.tomorrow_morning(now)),
            PauseLength::UntilResumed => None,
        };
        lock(&self.status).paused = Some(PauseInfo { length, until });
        self.changed();
    }

    pub fn resume(&self) {
        lock(&self.status).paused = None;
        self.changed();
        self.wake.notify_one();
    }

    /// T3's "Upload now": uploads carry on while this game is running.
    pub fn upload_now(&self) {
        lock(&self.status).upload_now = true;
        self.changed();
        self.wake.notify_one();
    }

    /// T7's "Try now", and S4's "Try again" for a file that failed.
    pub fn try_now(&self, failed: Option<&LedgerEntry>) {
        let now = self.clock.now();
        if let Some(root) = self.root() {
            for mut waiting in self
                .ledger
                .in_state(&root, &[LedgerState::Waiting, LedgerState::Uploading])
            {
                waiting.next_attempt_at = None;
                self.ledger.save(&waiting);
            }
        }

        if let Some(mut entry) = failed
            .and_then(|entry| self.ledger.find(&entry.root, &entry.path))
            .filter(|entry| entry.state == LedgerState::Failed)
        {
            entry.state = if entry.clip_id.is_none() {
                LedgerState::Waiting
            } else {
                LedgerState::Uploading
            };
            entry.attempts = MAX_ATTEMPTS - 1;
            entry.next_attempt_at = None;
            entry.updated_at = now;
            self.ledger.save(&entry);
        }

        lock(&self.status).unreachable_retry_at = None;
        self.changed();
        self.wake.notify_one();
    }

    // ---- Signing in and out ----

    /// The app signed in (S1): uploads carry on.
    pub fn on_signed_in(&self) {
        {
            let mut status = lock(&self.status);
            status.signed_out = false;
            status.last_books = None;
            status.last_plan_check = None;
        }
        self.changed();
        self.wake.notify_one();
    }

    /// S5's Sign out: revokes this device's token and forgets it. The folder settings and ledger stay.
    pub async fn sign_out(&self) {
        let _ = self.api.sign_out().await;
        self.credentials.delete_token();
        lock(&self.status).signed_out = true;
        self.changed();
    }

    fn on_signed_out(&self) {
        {
            let mut status = lock(&self.status);
            if status.signed_out {
                return;
            }
            status.signed_out = true;
        }
        tracing::info!("The API refused the device token; signing out");
        self.credentials.delete_token();
        self.notifier.post(Notification::SignedOut);
        let _ = self.events.send(EngineEvent::SignedOut);
        self.changed();
    }

    fn on_update_required(&self) {
        let notify = {
            let mut status = lock(&self.status);
            status.update_required = true;
            !std::mem::replace(&mut status.update_notified, true)
        };
        if notify {
            tracing::info!("The API needs a newer version of the app");
            self.notifier.post(Notification::UpdateRequired);
        }
        self.changed();
    }

    /// A 401 or 426 from any call stops uploading as the pipeline's own would.
    fn observe(&self, error: &ApiError) {
        match error.status() {
            Some(401) => self.on_signed_out(),
            Some(426) => self.on_update_required(),
            _ => {}
        }
    }

    // ---- Running ----

    /// Runs until dropped: the folder's watcher, a settle check every 2 s, a rescan every 60 s (and after the watcher
    /// errs or overflows), the books every hour, and uploads one at a time.
    pub async fn run(&self) {
        if let Some(folder) = self.settings().recordings_folder {
            self.monitor.use_folder(&folder, false);
            if let Some(root) = self.root() {
                self.ledger.retry_failed_once(&root, MAX_ATTEMPTS, self.clock.now());
            }
            self.start_watcher();
        }

        tokio::join!(self.monitor_loop(), self.upload_loop());
    }

    async fn monitor_loop(&self) {
        loop {
            self.tick().await;
            self.clock.sleep(monitor::CHECK_EVERY).await;
        }
    }

    async fn upload_loop(&self) {
        loop {
            if self.upload_next().await.is_none() {
                // Nothing to do: wait for a new clip, a change, or the next retry.
                tokio::select! {
                    _ = self.wake.notified() => {}
                    _ = self.clock.sleep(GATE_CHECK_EVERY) => {}
                }
            }
        }
    }

    /// One pass of the monitor: books, the plan, rescans, settling and notifications.
    pub async fn tick(&self) {
        let now = self.clock.now();
        if lock(&self.status)
            .paused
            .and_then(|paused| paused.until)
            .is_some_and(|until| now >= until)
        {
            self.resume();
        }
        if !self.game.is_game_running() {
            lock(&self.status).upload_now = false;
        }

        let (stopped, books_due, plan_due) = {
            let status = lock(&self.status);
            let due = |last: Option<DateTime<Utc>>, every: Duration| last.is_none_or(|last| now - last >= delta(every));
            (
                status.signed_out || status.update_required,
                due(status.last_books, BOOKS_EVERY),
                status.plan.is_none()
                    || (status.library_full.is_some() && due(status.last_plan_check, PLAN_FULL_RECHECK)),
            )
        };
        if !stopped && books_due {
            self.refresh_books().await;
        }
        if !stopped && plan_due {
            self.refresh_plan().await;
        }

        if self.root().is_some() {
            for path in self.signals.take_dirty() {
                self.monitor.mark_dirty(&path);
            }

            let rescan_requested = self.signals.take_rescan();
            let rescan_due = {
                let mut status = lock(&self.status);
                let due = rescan_requested || status.last_rescan.is_none_or(|last| now - last >= delta(RESCAN_EVERY));
                if due {
                    status.last_rescan = Some(now);
                }
                due
            };
            if rescan_due {
                self.monitor.rescan();
            }

            let added = self.monitor.settle(&self.books(), &self.settings());
            if !added.is_empty() {
                for entry in &added {
                    tracing::info!("{} settled ({:?})", entry.path, entry.state);
                }
                self.ask_for_books();
                let _ = self.events.send(EngineEvent::LedgerChanged);
                self.wake.notify_one();
            }
        }

        self.notify_shelved();
        self.changed();
    }

    /// The account's books, at start, hourly and when S3 opens. A folder whose name now matches is matched.
    pub async fn refresh_books(&self) {
        let mut books = match self.api.books().await {
            Ok(books) => books,
            Err(error) => {
                self.observe(&error);
                return;
            }
        };

        books.sort_by_key(|book| book.name.to_lowercase());
        {
            let mut status = lock(&self.status);
            status.last_books = Some(self.clock.now());
            status.books = books.clone();
        }
        if self.monitor.rematch(&books, &self.settings()) > 0 {
            self.wake.notify_one();
        }
        let _ = self.events.send(EngineEvent::BooksChanged);
    }

    pub async fn refresh_plan(&self) {
        let plan = match self.api.plan().await {
            Ok(plan) => plan,
            Err(error) => {
                self.observe(&error);
                return;
            }
        };

        let room_again = {
            let mut status = lock(&self.status);
            status.last_plan_check = Some(self.clock.now());
            status.plan = Some(plan);
            let room_again = status.library_full.is_some() && plan.has_room();
            if room_again {
                status.library_full = None;
            }
            room_again
        };
        if room_again {
            if let Some(root) = self.root() {
                self.ledger.requeue_plan_full(&root, self.clock.now());
            }
            self.wake.notify_one();
        }
    }

    /// A full-screen game holds uploads ("Wait"), unless "Upload now" was chosen during it.
    fn holds_new_clips(&self) -> bool {
        let waits = lock(&self.settings).full_screen == FullScreenMode::Wait;
        let upload_now = lock(&self.status).upload_now;
        waits && !upload_now && self.game.is_game_running()
    }

    fn bytes_per_second(&self) -> Option<u64> {
        let (limit, mode) = {
            let settings = lock(&self.settings);
            (settings.speed_limit_mbps, settings.full_screen)
        };
        let upload_now = lock(&self.status).upload_now;
        let slow = mode == FullScreenMode::UploadSlowly && !upload_now && self.game.is_game_running();
        TokenBucket::from_mbps(if slow { limit.or(Some(SLOW_UPLOAD_MBPS)) } else { limit })
    }

    /// Uploads the next file whose turn it is, if uploads may go now; None when there was nothing to do. A clip that
    /// hasn't started waits while a game is full screen ("Wait"); one already uploading stops between parts.
    pub async fn upload_next(&self) -> Option<UploadOutcome> {
        let root = self.root()?;
        {
            let status = lock(&self.status);
            if status.signed_out || status.update_required || status.library_full.is_some() || status.paused.is_some() {
                return None;
            }
        }
        if self.holds_new_clips() {
            return None;
        }

        let mut entry = self.ledger.next_to_upload(&root, self.clock.now())?;

        if self.plan().is_none() {
            self.refresh_plan().await;
            if self.plan().is_none() {
                self.mark_unreachable(&root);
                self.changed();
                return None;
            }
        }
        let plan = self.plan().unwrap_or_default();

        let full_path = files::full_path(Path::new(&root), &entry.path);
        if !full_path.is_file() {
            // Deleted or moved before it could go; a rescan finds it again wherever it went.
            entry.state = LedgerState::Failed;
            entry.last_error = Some("The file is no longer in the recordings folder".to_string());
            entry.updated_at = self.clock.now();
            self.ledger.save(&entry);
            return Some(UploadOutcome::Failed);
        }

        lock(&self.status).current = Some(CurrentUpload {
            path: entry.path.clone(),
            title: entry.title().to_string(),
            percent: entry.progress,
        });
        self.changed();
        let outcome = {
            let _current = CurrentGuard(self);
            self.pipeline.upload(&mut entry, &full_path, &plan).await
        };

        tracing::info!("{}: {outcome:?}", entry.path);
        self.handle_outcome(&root, &entry, outcome);
        let _ = self.events.send(EngineEvent::LedgerChanged);
        self.changed();
        Some(outcome)
    }

    fn handle_outcome(&self, root: &str, entry: &LedgerEntry, outcome: UploadOutcome) {
        match outcome {
            UploadOutcome::Shelved => {
                let mut status = lock(&self.status);
                status.unreachable_retry_at = None;
                status.library_full_notified = false;
                let book = status
                    .books
                    .iter()
                    .find(|book| Some(book.id) == entry.category_id)
                    .map(|book| book.name.clone())
                    .unwrap_or_default();
                match status.shelved_since_notice.iter_mut().find(|(name, _)| *name == book) {
                    Some((_, count)) => *count += 1,
                    None => status.shelved_since_notice.push((book, 1)),
                }
            }
            UploadOutcome::PlanFull => {
                let notify = {
                    let mut status = lock(&self.status);
                    let plan = *status.plan.get_or_insert_with(PlanLimits::default);
                    let full = LibraryFullInfo {
                        clip_limit: plan.clip_limit,
                        monthly_limit: plan.monthly_limit_reached(),
                        uploads_from: None,
                    };
                    status.library_full = Some(full);
                    status.last_plan_check = Some(self.clock.now());
                    (!std::mem::replace(&mut status.library_full_notified, true)).then_some(full)
                };
                if let Some(full) = notify {
                    self.notifier.post(Notification::LibraryFull(full));
                }
            }
            UploadOutcome::Unsupported | UploadOutcome::UpdateRequired => self.on_update_required(),
            UploadOutcome::SignedOut => {
                lock(&self.status).unreachable_retry_at = None;
                self.on_signed_out();
            }
            UploadOutcome::NeedsBook => self.ask_for_books(),
            UploadOutcome::RetryLater => self.mark_unreachable(root),
            UploadOutcome::Duplicate | UploadOutcome::TooLong | UploadOutcome::Failed => {
                lock(&self.status).unreachable_retry_at = None;
            }
        }
    }

    fn mark_unreachable(&self, root: &str) {
        let retry_at = self
            .ledger
            .next_retry_at(root)
            .unwrap_or_else(|| self.clock.now() + delta(uploads::backoff(1)));
        lock(&self.status).unreachable_retry_at = Some(retry_at);
    }

    // ---- Notifications ----

    /// The folders with clips waiting for a book, ignoring case, in the order their clips arrived, with their counts.
    fn needs_book_folders(&self, root: &str) -> Vec<(String, i64)> {
        let mut folders: Vec<(String, i64)> = Vec::new();
        for entry in self.ledger.in_state(root, &[LedgerState::NeedsBook]) {
            let folder = entry.folder().to_string();
            let lower = folder.to_lowercase();
            match folders.iter_mut().find(|(name, _)| name.to_lowercase() == lower) {
                Some((_, count)) => *count += 1,
                None => folders.push((folder, 1)),
            }
        }
        folders
    }

    /// N2 once per folder, the first time a clip there settles; again as its count changes.
    fn ask_for_books(&self) {
        let Some(root) = self.root() else {
            return;
        };

        for (folder, count) in self.needs_book_folders(&root) {
            let first = {
                let mut status = lock(&self.status);
                let lower = folder.to_lowercase();
                match status
                    .asked_folders
                    .iter_mut()
                    .find(|(asked, _)| asked.to_lowercase() == lower)
                {
                    Some((_, asked)) if *asked == count => continue,
                    Some((_, asked)) => {
                        *asked = count;
                        false
                    }
                    None => {
                        status.asked_folders.push((folder.clone(), count));
                        true
                    }
                }
            };
            self.notifier.post(Notification::NeedsBook {
                folder: folder.clone(),
                waiting: count,
            });
            if first {
                let _ = self.events.send(EngineEvent::BookNeeded(folder));
            }
        }
    }

    /// N1: grouped, at most one every 10 minutes, and never while a game is full screen.
    fn notify_shelved(&self) {
        let now = self.clock.now();
        let game = self.game.is_game_running();
        let books = {
            let mut status = lock(&self.status);
            let too_soon = status
                .last_shelved_notice
                .is_some_and(|last| now - last < delta(SHELVED_NOTIFICATION_GAP));
            if status.shelved_since_notice.is_empty() || game || too_soon {
                return;
            }
            let mut books = std::mem::take(&mut status.shelved_since_notice);
            books.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.to_lowercase().cmp(&b.0.to_lowercase())));
            status.last_shelved_notice = Some(now);
            books
        };
        if lock(&self.settings).notify_shelved {
            self.notifier.post(Notification::ClipsShelved {
                books: books
                    .into_iter()
                    .map(|(book_name, clips)| ShelvedBook { book_name, clips })
                    .collect(),
            });
        }
    }

    // ---- State ----

    /// The local midnight of `now`, in UTC.
    fn local_midnight(&self, now: DateTime<Utc>) -> DateTime<Utc> {
        let offset = self.clock.local_offset(now);
        let midnight = now
            .with_timezone(&offset)
            .date_naive()
            .and_hms_opt(0, 0, 0)
            .unwrap_or_default();
        (midnight - TimeDelta::seconds(i64::from(offset.local_minus_utc()))).and_utc()
    }

    fn tomorrow_morning(&self, now: DateTime<Utc>) -> DateTime<Utc> {
        let offset: FixedOffset = self.clock.local_offset(now);
        let tomorrow = now
            .with_timezone(&offset)
            .date_naive()
            .checked_add_days(Days::new(1))
            .unwrap_or_default()
            .and_hms_opt(0, 0, 0)
            .unwrap_or_default();
        let guess = (tomorrow - TimeDelta::seconds(i64::from(offset.local_minus_utc()))).and_utc();
        // The clocks may change overnight; midnight takes its own offset.
        let at_midnight = self.clock.local_offset(guess);
        (tomorrow - TimeDelta::seconds(i64::from(at_midnight.local_minus_utc()))).and_utc()
    }

    pub fn inputs(&self) -> StateInputs {
        let now = self.clock.now();
        let mut inputs = StateInputs::default();
        if let Some(root) = self.root() {
            inputs.needs_book = self.needs_book_folders(&root).into_iter().next();
            inputs.waiting = self.ledger.count(
                &root,
                &[LedgerState::Waiting, LedgerState::Uploading, LedgerState::PlanFull],
            ) as i64;
            inputs.shelved_today = self.ledger.shelved_since(&root, self.local_midnight(now)) as i64;
        }
        inputs.game_holds_uploads = self.holds_new_clips();
        inputs.folder_name = state::short_folder_name(lock(&self.settings).recordings_folder.as_deref());

        let status = lock(&self.status);
        inputs.update_required = status.update_required;
        inputs.library_full = status.library_full;
        inputs.unreachable_retry_at = status.unreachable_retry_at;
        inputs.paused = status.paused;
        inputs.current = status
            .current
            .as_ref()
            .map(|current| (current.title.clone(), current.percent));
        inputs
    }

    fn changed(&self) {
        let now = self.clock.now();
        let state = UploaderState::from_inputs(&self.inputs(), now, &self.clock.local_offset(now));
        {
            let mut status = lock(&self.status);
            if status.state == state {
                return;
            }
            status.state = state.clone();
        }
        let _ = self.events.send(EngineEvent::StateChanged(state));
    }

    fn on_progress(&self, entry: &LedgerEntry, sent: u64, total: u64) {
        {
            let percent = (sent * 100)
                .checked_div(total)
                .map_or(0, |percent| percent.min(100) as u8);
            lock(&self.status).current = Some(CurrentUpload {
                path: entry.path.clone(),
                title: entry.title().to_string(),
                percent,
            });
        }
        self.changed();
        let _ = self.events.send(EngineEvent::LedgerChanged);
    }

    fn start_watcher(&self) {
        let mut watcher = lock(&self.watcher);
        *watcher = None;
        let Some(root) = self.root() else {
            return;
        };
        let root = Path::new(&root);
        if !root.is_dir() {
            return;
        }
        // Without events the 60-second rescan still finds every clip.
        match FolderWatcher::start(root, &self.signals) {
            Ok(started) => *watcher = Some(started),
            Err(error) => tracing::warn!("Couldn't watch the recordings folder, so it is rescanned instead: {error}"),
        }
    }
}

/// The engine's rule for when uploads go and how fast.
struct EngineGate<A: UploaderApi> {
    engine: Weak<UploaderEngine<A>>,
}

impl<A: UploaderApi> UploadGate for EngineGate<A> {
    fn wait_until_open(&self) -> BoxFuture<'_, ()> {
        Box::pin(async move {
            loop {
                let Some(engine) = self.engine.upgrade() else {
                    return;
                };
                if !engine.holds_new_clips() && lock(&engine.status).paused.is_none() {
                    return;
                }

                engine.changed();
                engine.clock.sleep(GATE_CHECK_EVERY).await;
                let now = engine.clock.now();
                if lock(&engine.status)
                    .paused
                    .and_then(|paused| paused.until)
                    .is_some_and(|until| now >= until)
                {
                    engine.resume();
                }
                if !engine.game.is_game_running() {
                    lock(&engine.status).upload_now = false;
                }
            }
        })
    }

    fn bytes_per_second(&self) -> Option<u64> {
        self.engine.upgrade().and_then(|engine| engine.bytes_per_second())
    }
}
