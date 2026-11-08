//! What the windows show and what they ask for. Each window's state is a [`Ui`]: a [`Model`] snapshot of the engine,
//! the window's own draft (the folder S2 is about to watch, the book S3 has picked), and the [`Commands`] its buttons
//! call. The app's core implements [`Commands`] over the engine; tests can drive the same views over a real engine
//! with a fake API.

use std::sync::Arc;

use reelshelf_core::ledger::LedgerEntry;
use reelshelf_core::matching::Book;
use reelshelf_core::platform::StartupState;
use reelshelf_core::recent::RecentUpload;
use reelshelf_core::settings::{FullScreenMode, UploaderSettings};
use reelshelf_core::state::{PauseLength, StateInputs, TrayAction, UploaderState};
use uuid::Uuid;

use crate::links::Link;

/// The app's windows. Each is created when it opens and destroyed when it closes.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum WindowKind {
    /// S1.
    SignIn,
    /// S2.
    Setup,
    /// S3.
    BookPicker,
    /// S4.
    Recent,
    /// S5.
    Settings,
    /// Quitting while clips upload.
    Quit,
    /// T1–T8 above the tray.
    Flyout,
}

impl WindowKind {
    pub const ALL: [WindowKind; 7] = [
        WindowKind::SignIn,
        WindowKind::Setup,
        WindowKind::BookPicker,
        WindowKind::Recent,
        WindowKind::Settings,
        WindowKind::Quit,
        WindowKind::Flyout,
    ];

    /// The window's key, for `--window` in headless runs and in the tree.
    pub fn key(self) -> &'static str {
        match self {
            WindowKind::SignIn => "sign-in",
            WindowKind::Setup => "setup",
            WindowKind::BookPicker => "book-picker",
            WindowKind::Recent => "recent",
            WindowKind::Settings => "settings",
            WindowKind::Quit => "quit",
            WindowKind::Flyout => "flyout",
        }
    }
}

/// A window to show, with what it opens on.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Show {
    SignIn,
    Setup,
    /// S3 for a folder below the recordings folder ("" for the recordings folder itself).
    BookPicker(String),
    Recent,
    Settings,
    Flyout,
}

/// The engine as the windows read it, taken whenever it changes.
#[derive(Debug, Clone, PartialEq)]
pub struct Model {
    pub state: UploaderState,
    pub inputs: StateInputs,
    pub settings: UploaderSettings,
    pub books: Vec<Book>,
    pub recent: Vec<RecentUpload>,
    /// The file uploading now (its path below the recordings folder) and how far it has got.
    pub current_upload: Option<(String, u8)>,
    /// Folders below the recordings folder with videos in them that have a book (S5).
    pub matched_folders: usize,
    pub uploads_in_progress: usize,
    pub startup: StartupState,
    pub version: String,
    /// "reelshelf.app/device", as S1 names the page.
    pub device_page: String,
}

/// S1: where signing in has got to.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SignInPhase {
    Starting,
    /// Waiting for the code to be approved on the device page.
    Waiting {
        user_code: String,
        verification_uri_complete: String,
    },
    Expired,
    Denied,
    Unreachable,
}

/// S2's choices before Start watching saves them.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct SetupDraft {
    pub folder: Option<String>,
    /// The folders below it with videos, and the book each goes on.
    pub matches: Vec<(String, Option<Book>)>,
    pub default_book: Option<Uuid>,
    pub start_with_windows: bool,
}

/// S3: the folder asking for a book, and the book picked.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct PickerDraft {
    pub folder: String,
    pub selected: Option<Uuid>,
}

/// The flyout's own state: whether "Pause for…" is open.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct FlyoutDraft {
    pub pause_open: bool,
}

/// What the windows' buttons do. Mutating commands save through the engine; the window then reads a fresh
/// [`Commands::snapshot`].
pub trait Commands: Send + Sync {
    fn snapshot(&self) -> Model;
    fn show(&self, window: Show);
    /// S1: asks for a new code and waits for it to be approved.
    fn start_sign_in(&self);
    fn open_link(&self, link: Link);
    fn open_folder(&self, path: &str);
    fn open_log_folder(&self);
    /// The folders below `folder` with videos, and the book each would go on.
    fn folder_matches(&self, folder: &str) -> Vec<(String, Option<Book>)>;
    /// S2's Start watching.
    fn start_watching(&self, folder: &str, default_book: Option<Uuid>, start_with_windows: bool);
    /// S3's Use this book.
    fn choose_book(&self, folder: &str, book: Uuid);
    /// The state's main action (the flyout's button).
    fn tray_action(&self, action: TrayAction);
    fn pause(&self, length: PauseLength);
    /// Quit, asking first while clips are uploading unless `confirmed`.
    fn quit(&self, confirmed: bool);
    fn set_default_book(&self, book: Option<Uuid>);
    fn set_full_screen(&self, mode: FullScreenMode);
    fn set_speed_limit(&self, mbps: Option<u32>);
    fn set_start_with_windows(&self, enabled: bool);
    fn set_notify_shelved(&self, enabled: bool);
    fn sign_out(&self);
    /// S4's Try again.
    fn try_again(&self, entry: &LedgerEntry);
}

/// A window's state.
pub struct Ui {
    pub kind: WindowKind,
    pub model: Model,
    pub sign_in: SignInPhase,
    pub setup: SetupDraft,
    pub picker: PickerDraft,
    pub flyout: FlyoutDraft,
    pub commands: Arc<dyn Commands>,
}

impl Ui {
    pub fn new(kind: WindowKind, commands: Arc<dyn Commands>) -> Ui {
        Ui {
            kind,
            model: commands.snapshot(),
            sign_in: SignInPhase::Starting,
            setup: SetupDraft::default(),
            picker: PickerDraft::default(),
            flyout: FlyoutDraft::default(),
            commands,
        }
    }

    /// Reads the engine again after a command changed it.
    pub fn refresh(&mut self) {
        self.model = self.commands.snapshot();
    }
}

/// A change to a window's state, made on the UI thread.
pub type Change = Box<dyn FnOnce(&mut Ui, &mut rune::Tx) + Send>;

/// Where windows open and close, and the tray. The app implements it over Rune's handles; tests record it.
pub trait WindowHost: Send + Sync {
    /// Opens `kind` (or brings it forward), after `prepare` sets up its state.
    fn open(&self, kind: WindowKind, prepare: Change);
    /// Changes an open window's state; nothing when it is closed.
    fn update(&self, kind: WindowKind, change: Change);
    fn close(&self, kind: WindowKind);
    fn is_open(&self, kind: WindowKind) -> bool;
    /// The tray's icon and tooltip for the state.
    fn tray(&self, state: &UploaderState);
    fn quit(&self);
}
