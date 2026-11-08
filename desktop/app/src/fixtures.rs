//! Fixture states for the headless checks: every window and flyout state, with the canvas's example folders, books
//! and numbers. The words come from the core and `copy.rs` as they would in the app; only the data is made up.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};

use chrono::{DateTime, FixedOffset, TimeDelta, TimeZone, Utc};
use reelshelf_core::ledger::{LedgerEntry, LedgerState};
use reelshelf_core::matching::Book;
use reelshelf_core::platform::StartupState;
use reelshelf_core::recent::RecentUpload;
use reelshelf_core::settings::{FullScreenMode, UploaderSettings};
use reelshelf_core::state::{LibraryFullInfo, PauseInfo, PauseLength, StateInputs, TrayAction, UploaderState};
use uuid::Uuid;

use crate::links::Link;
use crate::model::{Commands, Model, PickerDraft, SetupDraft, Show, SignInPhase, Ui, WindowKind};

pub const ROOT: &str = "D:\\Recordings";
pub const CODE: &str = "MFND-VRWZ";
pub const DEVICE: &str = "Gaming PC";
pub const VERSION: &str = "0.2.0";

/// 21:16 on 8 November 2026, the canvas's clock, in UTC (the fixtures' time zone).
pub fn now() -> DateTime<Utc> {
    Utc.with_ymd_and_hms(2026, 11, 8, 21, 16, 0).unwrap()
}

pub fn zone() -> FixedOffset {
    FixedOffset::east_opt(0).unwrap()
}

/// The canvas's books, by name and cloth.
pub fn books() -> Vec<Book> {
    [
        ("Apex Legends", "apex-legends", "#a34d4d"),
        ("Counter-Strike 2", "counter-strike-2", "#2f5d73"),
        ("Helldivers 2", "helldivers-2", "#b68a3e"),
        ("Marvel Rivals", "marvel-rivals", "#5b4a7a"),
        ("Rocket League", "rocket-league", "#3e6b5a"),
        ("Valorant", "valorant", "#8c5a3c"),
    ]
    .into_iter()
    .enumerate()
    .map(|(index, (name, slug, cloth))| Book {
        id: Uuid::from_u128(index as u128 + 1),
        name: name.to_owned(),
        slug: Some(slug.to_owned()),
        cloth_color: Some(cloth.to_owned()),
    })
    .collect()
}

pub fn book(name: &str) -> Book {
    books()
        .into_iter()
        .find(|book| book.name == name)
        .expect("a fixture book")
}

/// What the windows see of an engine watching `D:\Recordings`, in `inputs`' state.
pub fn model(inputs: StateInputs) -> Model {
    let state = UploaderState::from_inputs(&inputs, now(), &zone());
    Model {
        state,
        inputs,
        settings: UploaderSettings {
            recordings_folder: Some(ROOT.to_owned()),
            device_name: Some(DEVICE.to_owned()),
            ..UploaderSettings::default()
        },
        books: books(),
        recent: Vec::new(),
        current_upload: None,
        matched_folders: 3,
        uploads_in_progress: 0,
        startup: StartupState::Enabled,
        version: VERSION.to_owned(),
        device_page: "reelshelf.app/device".to_owned(),
    }
}

/// T1–T8's inputs, as the canvas shows each.
pub fn tray_inputs(id: &str) -> StateInputs {
    let watching = StateInputs {
        folder_name: ROOT.to_owned(),
        shelved_today: 3,
        ..StateInputs::default()
    };
    match id {
        "T1" => watching,
        "T2" => StateInputs {
            current: Some(("Apex Legends 2026.11.08 - 21.14.03".to_owned(), 64)),
            waiting: 2,
            ..watching
        },
        "T3" => StateInputs {
            game_holds_uploads: true,
            waiting: 2,
            ..watching
        },
        "T4" => StateInputs {
            paused: Some(PauseInfo {
                length: PauseLength::OneHour,
                until: Some(Utc.with_ymd_and_hms(2026, 11, 8, 22, 30, 0).unwrap()),
            }),
            ..watching
        },
        "T5" => StateInputs {
            needs_book: Some(("Marvel Rivals".to_owned(), 1)),
            ..watching
        },
        "T6" => StateInputs {
            library_full: Some(LibraryFullInfo {
                clip_limit: Some(100),
                monthly_limit: false,
                uploads_from: None,
            }),
            waiting: 4,
            ..watching
        },
        "T7" => StateInputs {
            unreachable_retry_at: Some(now() + TimeDelta::minutes(2)),
            waiting: 3,
            ..watching
        },
        "T8" => StateInputs {
            update_required: true,
            ..watching
        },
        other => panic!("no tray state {other}"),
    }
}

/// A row of S4.
fn entry(folder: &str, stamp: &str, state: LedgerState, length: f64, megabytes: u64) -> LedgerEntry {
    let book = books().into_iter().find(|book| book.name == folder);
    let path = format!("{folder}\\{folder} 2026.11.08 - {stamp}.mp4");
    let at = now();
    LedgerEntry {
        root: ROOT.to_owned(),
        path,
        size: megabytes * 1024 * 1024,
        last_write: at,
        state,
        clip_id: (state == LedgerState::Shelved).then(|| Uuid::from_u128(99)),
        md5: None,
        category_id: book.map(|book| book.id).filter(|_| state != LedgerState::NeedsBook),
        upload_id: None,
        part_size: None,
        part_count: None,
        parts: BTreeMap::new(),
        attempts: 0,
        last_error: None,
        length_seconds: Some(length),
        next_attempt_at: None,
        progress: 0,
        created_at: at,
        updated_at: at,
        shelved_at: (state == LedgerState::Shelved).then(|| Utc.with_ymd_and_hms(2026, 11, 8, 20, 34, 0).unwrap()),
    }
}

/// S4's rows as the canvas lists them: uploading, waiting, waiting for a book, on the shelf, didn't upload, too long
/// and already on the shelf.
pub fn recent_rows() -> (Vec<RecentUpload>, Option<(String, u8)>) {
    let entries = [
        entry("Apex Legends", "21.14.03", LedgerState::Uploading, 42.0, 86),
        entry("Apex Legends", "21.09.47", LedgerState::Waiting, 65.0, 132),
        entry("Marvel Rivals", "20.51.12", LedgerState::NeedsBook, 38.0, 74),
        entry("Valorant", "20.32.40", LedgerState::Shelved, 51.0, 98),
        entry("Apex Legends", "20.18.05", LedgerState::Failed, 29.0, 61),
        entry("Counter-Strike 2", "19.02.33", LedgerState::TooLong, 134.0, 280),
        entry("Valorant", "18.47.10", LedgerState::Duplicate, 44.0, 90),
    ];
    let current = (entries[0].path.clone(), 64);
    (rows(&entries, Some(&current)), Some(current))
}

/// The row states the canvas leaves out: retrying soon and waiting for room.
pub fn more_recent_rows() -> Vec<RecentUpload> {
    let mut retrying = entry("Rocket League", "17.30.12", LedgerState::Waiting, 22.0, 48);
    retrying.attempts = 1;
    retrying.next_attempt_at = Some(now() + TimeDelta::minutes(2));
    let entries = [
        retrying,
        entry("Helldivers 2", "17.02.51", LedgerState::PlanFull, 57.0, 120),
    ];
    rows(&entries, None)
}

fn rows(entries: &[LedgerEntry], current: Option<&(String, u8)>) -> Vec<RecentUpload> {
    entries
        .iter()
        .map(|entry| {
            let percent = current
                .filter(|(path, _)| *path == entry.path)
                .map(|&(_, percent)| percent);
            RecentUpload::from_entry(entry, &books(), now(), percent, &zone())
        })
        .collect()
}

/// S2's folders below `D:\Recordings`: three matched, one not.
pub fn setup_matches() -> Vec<(String, Option<Book>)> {
    vec![
        ("Apex Legends".to_owned(), Some(book("Apex Legends"))),
        ("Valorant".to_owned(), Some(book("Valorant"))),
        ("Counter-Strike 2".to_owned(), Some(book("Counter-Strike 2"))),
        ("Marvel Rivals".to_owned(), None),
    ]
}

/// Commands that change nothing and remember what was asked, over a fixed model.
pub struct FixtureCommands {
    pub model: Mutex<Model>,
    pub calls: Mutex<Vec<String>>,
}

impl FixtureCommands {
    pub fn new(model: Model) -> Arc<FixtureCommands> {
        Arc::new(FixtureCommands {
            model: Mutex::new(model),
            calls: Mutex::new(Vec::new()),
        })
    }

    fn record(&self, call: String) {
        self.calls.lock().unwrap().push(call);
    }

    pub fn calls(&self) -> Vec<String> {
        self.calls.lock().unwrap().clone()
    }
}

impl Commands for FixtureCommands {
    fn snapshot(&self) -> Model {
        self.model.lock().unwrap().clone()
    }
    fn show(&self, window: Show) {
        self.record(format!("show {window:?}"));
    }
    fn start_sign_in(&self) {
        self.record("start_sign_in".into());
    }
    fn open_link(&self, link: Link) {
        self.record(format!("open_link {link:?}"));
    }
    fn open_folder(&self, path: &str) {
        self.record(format!("open_folder {path}"));
    }
    fn open_log_folder(&self) {
        self.record("open_log_folder".into());
    }
    fn folder_matches(&self, _folder: &str) -> Vec<(String, Option<Book>)> {
        setup_matches()
    }
    fn start_watching(&self, folder: &str, default_book: Option<Uuid>, start_with_windows: bool) {
        self.record(format!("start_watching {folder} {default_book:?} {start_with_windows}"));
    }
    fn choose_book(&self, folder: &str, book: Uuid) {
        self.record(format!("choose_book {folder} {book}"));
    }
    fn tray_action(&self, action: TrayAction) {
        self.record(format!("tray_action {action:?}"));
    }
    fn pause(&self, length: PauseLength) {
        self.record(format!("pause {length:?}"));
    }
    fn quit(&self, confirmed: bool) {
        self.record(format!("quit {confirmed}"));
    }
    fn set_default_book(&self, book: Option<Uuid>) {
        self.record(format!("set_default_book {book:?}"));
    }
    fn set_full_screen(&self, mode: FullScreenMode) {
        self.record(format!("set_full_screen {mode:?}"));
    }
    fn set_speed_limit(&self, mbps: Option<u32>) {
        self.record(format!("set_speed_limit {mbps:?}"));
    }
    fn set_start_with_windows(&self, enabled: bool) {
        self.record(format!("set_start_with_windows {enabled}"));
    }
    fn set_notify_shelved(&self, enabled: bool) {
        self.record(format!("set_notify_shelved {enabled}"));
    }
    fn sign_out(&self) {
        self.record("sign_out".into());
    }
    fn try_again(&self, entry: &LedgerEntry) {
        self.record(format!("try_again {}", entry.path));
    }
}

/// One fixture state of one window.
pub struct Scene {
    pub name: String,
    pub kind: WindowKind,
    pub size: (f32, f32),
    pub model: Model,
    pub prepare: fn(&mut Ui),
}

impl Scene {
    /// The window's state for this scene, over commands that remember what was asked.
    pub fn ui(&self) -> (Ui, Arc<FixtureCommands>) {
        let commands = FixtureCommands::new(self.model.clone());
        let mut ui = Ui::new(self.kind, commands.clone());
        (self.prepare)(&mut ui);
        (ui, commands)
    }

    /// The data the scene shows besides `copy.rs`'s words: the fixtures' folders, books, files, code and numbers.
    pub fn data(&self) -> Vec<String> {
        let mut data = vec![ROOT.to_owned(), CODE.to_owned(), DEVICE.to_owned(), VERSION.to_owned()];
        data.extend(books().into_iter().map(|book| book.name));
        for row in &self.model.recent {
            data.push(row.title.clone());
        }
        data
    }
}

fn scene(name: &str, kind: WindowKind, model: Model, prepare: fn(&mut Ui)) -> Scene {
    let size = if kind == WindowKind::SignIn {
        let mut ui = Ui::new(kind, FixtureCommands::new(model.clone()));
        prepare(&mut ui);
        crate::ui::sign_in_size(&ui.sign_in)
    } else {
        crate::ui::size(kind)
    };
    Scene {
        name: name.to_owned(),
        kind,
        size,
        model,
        prepare,
    }
}

/// An account with no books yet.
fn empty_shelf() -> Model {
    let mut model = model(tray_inputs("T1"));
    model.books.clear();
    model
}

/// Every window and flyout state.
pub fn scenes() -> Vec<Scene> {
    let base = || model(tray_inputs("T1"));
    let mut scenes = vec![
        scene("s1-waiting", WindowKind::SignIn, base(), |ui| {
            ui.sign_in = SignInPhase::Waiting {
                user_code: CODE.to_owned(),
                verification_uri_complete: format!("https://reelshelf.app/device?code={CODE}"),
            };
        }),
        scene("s1-starting", WindowKind::SignIn, base(), |ui| {
            ui.sign_in = SignInPhase::Starting
        }),
        scene("s1-expired", WindowKind::SignIn, base(), |ui| {
            ui.sign_in = SignInPhase::Expired
        }),
        scene("s1-denied", WindowKind::SignIn, base(), |ui| {
            ui.sign_in = SignInPhase::Denied
        }),
        scene("s1-unreachable", WindowKind::SignIn, base(), |ui| {
            ui.sign_in = SignInPhase::Unreachable;
        }),
        scene("s2-matches", WindowKind::Setup, base(), |ui| {
            ui.setup = SetupDraft {
                folder: Some(ROOT.to_owned()),
                matches: setup_matches(),
                default_book: None,
                start_with_windows: true,
            };
        }),
        scene("s2-no-folder", WindowKind::Setup, base(), |ui| {
            ui.setup = SetupDraft {
                start_with_windows: true,
                ..SetupDraft::default()
            };
        }),
        scene("s2-empty-shelf", WindowKind::Setup, empty_shelf(), |ui| {
            ui.setup = SetupDraft {
                folder: Some(ROOT.to_owned()),
                matches: vec![("Apex Legends".to_owned(), None)],
                default_book: None,
                start_with_windows: true,
            };
        }),
        scene("s3", WindowKind::BookPicker, base(), |ui| {
            ui.picker = PickerDraft {
                folder: "Marvel Rivals".to_owned(),
                selected: Some(book("Marvel Rivals").id),
            };
        }),
        scene("s3-recordings-folder", WindowKind::BookPicker, base(), |ui| {
            ui.picker = PickerDraft::default();
        }),
    ];
    let (recent, current) = recent_rows();
    let mut with_rows = base();
    with_rows.recent = recent;
    with_rows.current_upload = current;
    scenes.push(scene("s4", WindowKind::Recent, with_rows, |_| {}));
    let mut more = base();
    more.recent = more_recent_rows();
    scenes.push(scene("s4-more-states", WindowKind::Recent, more, |_| {}));
    scenes.push(scene("s4-empty", WindowKind::Recent, base(), |_| {}));
    scenes.push(scene("s5", WindowKind::Settings, base(), |_| {}));
    let mut blocked = base();
    blocked.startup = StartupState::DisabledByUser;
    blocked.settings.full_screen = FullScreenMode::UploadSlowly;
    blocked.settings.speed_limit_mbps = Some(10);
    blocked.settings.default_book_id = Some(book("Apex Legends").id);
    blocked.settings.notify_shelved = false;
    scenes.push(scene("s5-changed", WindowKind::Settings, blocked, |_| {}));
    for id in ["T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8"] {
        scenes.push(scene(
            &id.to_lowercase(),
            WindowKind::Flyout,
            model(tray_inputs(id)),
            |_| {},
        ));
    }
    scenes.push(scene("t1-pause-open", WindowKind::Flyout, base(), |ui| {
        ui.flyout.pause_open = true
    }));
    let mut quitting = model(tray_inputs("T2"));
    quitting.uploads_in_progress = 2;
    scenes.push(scene("quit", WindowKind::Quit, quitting, |_| {}));
    scenes
}
