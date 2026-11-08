//! Scripted flows through the windows over the real engine (with a fake API): S2's Start watching saves the folder,
//! S3's Use this book saves the choice, and the flyout's actions reach the engine. The tray's menu and the
//! notifications' links reach the same commands.

mod support;

use std::collections::BTreeMap;
use std::path::Path;
use std::sync::{Arc, Mutex};

use reelshelf_app::app_core::{AppCore, CoreParts};
use reelshelf_app::fixtures::{self, FixtureCommands};
use reelshelf_app::links::{self, Link};
use reelshelf_app::model::{Change, Commands, FlyoutPage, SetupDraft, Ui, WindowHost, WindowKind};
use reelshelf_app::platform::{MemoryCredentials, NeverInAGame, NoStartup, Platform, Shell};
use reelshelf_app::{shell, tray};
use reelshelf_core::api::{
    ApiError, ApiResult, DeviceAuthorization, PartBody, PrepareClip, PreparedClip, PresignedParts, UploadedPart,
    UploaderApi,
};
use reelshelf_core::matching::Book;
use reelshelf_core::notifications::NotificationAction;
use reelshelf_core::platform::SilentNotifier;
use reelshelf_core::settings::{SettingsStore, UploaderPaths};
use reelshelf_core::state::{TrayAction, UploaderState, UploaderStateKind};
use reelshelf_core::uploads::PlanLimits;
use rune::Runtime;
use uuid::Uuid;

/// The API with the fixture's books and an empty plan; nothing uploads.
struct FakeApi;

fn offline<T>() -> ApiResult<T> {
    Err(ApiError::Network("the flows don't upload".into()))
}

impl UploaderApi for FakeApi {
    async fn authorize(&self, _device_name: &str) -> ApiResult<DeviceAuthorization> {
        offline()
    }
    async fn poll_token(&self, _device_code: &str) -> ApiResult<String> {
        offline()
    }
    async fn sign_out(&self) -> ApiResult<()> {
        Ok(())
    }
    async fn books(&self) -> ApiResult<Vec<Book>> {
        Ok(fixtures::books())
    }
    async fn plan(&self) -> ApiResult<PlanLimits> {
        Ok(PlanLimits::default())
    }
    async fn prepare(&self, _clip: &PrepareClip) -> ApiResult<PreparedClip> {
        offline()
    }
    async fn delete_clip(&self, _clip_id: Uuid) -> ApiResult<()> {
        offline()
    }
    async fn presign_parts(&self, _: Uuid, _: &str, _: &[u32]) -> ApiResult<PresignedParts> {
        offline()
    }
    async fn list_parts(&self, _: Uuid, _: &str) -> ApiResult<Vec<UploadedPart>> {
        offline()
    }
    async fn complete(&self, _: Uuid, _: &str, _: &BTreeMap<u32, String>) -> ApiResult<()> {
        offline()
    }
    async fn put_part(&self, _: &str, _: u64, _: PartBody) -> ApiResult<Option<String>> {
        offline()
    }
}

/// Remembers which windows opened and closed, and whether the app quit.
#[derive(Default)]
struct Host {
    calls: Mutex<Vec<String>>,
}

impl Host {
    fn calls(&self) -> Vec<String> {
        self.calls.lock().unwrap().clone()
    }
}

impl WindowHost for Host {
    fn open(&self, kind: WindowKind, _prepare: Change) {
        self.calls.lock().unwrap().push(format!("open {kind:?}"));
    }
    fn update(&self, _kind: WindowKind, _change: Change) {}
    fn close(&self, kind: WindowKind) {
        self.calls.lock().unwrap().push(format!("close {kind:?}"));
    }
    fn is_open(&self, _kind: WindowKind) -> bool {
        false
    }
    fn tray(&self, state: &UploaderState) {
        self.calls.lock().unwrap().push(format!("tray {}", state.id()));
    }
    fn quit(&self) {
        self.calls.lock().unwrap().push("quit".into());
    }
}

#[derive(Default)]
struct Opened(Mutex<Vec<String>>);

impl Shell for Opened {
    fn open(&self, target: &str) {
        self.0.lock().unwrap().push(target.to_owned());
    }
}

struct World {
    core: Arc<AppCore<FakeApi>>,
    host: Arc<Host>,
    opened: Arc<Opened>,
    paths: UploaderPaths,
    runtime: tokio::runtime::Runtime,
    _dir: tempfile::TempDir,
}

fn world() -> World {
    let dir = tempfile::tempdir().unwrap();
    let paths = UploaderPaths::new(dir.path().join("app"));
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .worker_threads(1)
        .enable_all()
        .build()
        .unwrap();
    let opened = Arc::new(Opened::default());
    let platform = Platform {
        credentials: Arc::new(MemoryCredentials::with_token("rsd_signed-in")),
        game: Arc::new(NeverInAGame),
        notifier: Arc::new(SilentNotifier),
        startup: Arc::new(NoStartup),
        shell: opened.clone(),
    };
    let core = AppCore::new(CoreParts {
        api: Arc::new(FakeApi),
        platform,
        paths: paths.clone(),
        site: links::PRODUCTION_SITE.to_owned(),
        runtime: runtime.handle().clone(),
    });
    let host = Arc::new(Host::default());
    core.set_host(host.clone());
    runtime.block_on(core.engine().refresh_books());
    World {
        core,
        host,
        opened,
        paths,
        runtime,
        _dir: dir,
    }
}

impl World {
    fn ui(&self, kind: WindowKind) -> Ui {
        let commands: Arc<dyn Commands> = self.core.clone();
        Ui::new(kind, commands)
    }

    fn saved(&self) -> reelshelf_core::settings::UploaderSettings {
        SettingsStore::new(self.paths.settings()).load()
    }
}

fn press(rt: &mut Runtime<Ui>, key: &str) {
    let (x, y) = rt.find(key).unwrap_or_else(|| panic!("{key} is on screen"));
    rt.pointer_down(x, y);
    rt.pointer_up(x, y);
    for _ in 0..6 {
        rt.frame(1.0 / 60.0);
    }
}

/// A recordings folder with clips in two games' folders, one of them a game with no book.
fn recordings(root: &Path) -> String {
    for folder in ["Apex Legends", "Overwatch 2"] {
        std::fs::create_dir_all(root.join(folder)).unwrap();
        std::fs::write(root.join(folder).join("clip.mp4"), b"not really a video").unwrap();
    }
    root.to_string_lossy().into_owned()
}

#[test]
fn start_watching_saves_the_folder_and_its_choices() {
    let world = world();
    let folder = recordings(&world._dir.path().join("Recordings"));
    let valorant = fixtures::book("Valorant").id;
    let mut ui = world.ui(WindowKind::Setup);
    ui.setup = SetupDraft {
        matches: world.core.folder_matches(&folder),
        folder: Some(folder.clone()),
        default_book: Some(valorant),
        start_with_windows: true,
    };
    assert_eq!(ui.setup.matches.len(), 2, "both games' folders are listed");
    assert_eq!(
        ui.setup.matches[0].1.as_ref().map(|book| book.name.as_str()),
        Some("Apex Legends")
    );
    assert!(ui.setup.matches[1].1.is_none(), "Overwatch 2 matches no book");
    let mut rt = support::runtime(ui, reelshelf_app::ui::size(WindowKind::Setup), false);

    // Choose… on the unmatched folder offers the books, and picking one saves it for the folder.
    press(&mut rt, "choose-1");
    assert!(rt.popup_menu().is_some(), "Choose… opens the books");
    let valorant_id = valorant.to_string();
    rt.resolve_popup(Some(valorant_id.as_str())).unwrap();
    rt.frame(1.0 / 60.0);
    assert_eq!(
        world.core.engine().settings().saved_book_for("Overwatch 2"),
        Some(valorant)
    );

    press(&mut rt, "start-watching");
    let settings = world.core.engine().settings();
    assert!(
        settings
            .recordings_folder
            .as_deref()
            .is_some_and(|saved| saved.ends_with("Recordings"))
    );
    assert_eq!(settings.default_book_id, Some(valorant));
    let saved = world.saved();
    assert_eq!(
        saved.recordings_folder, settings.recordings_folder,
        "the folder is saved to settings.json"
    );
    assert_eq!(saved.default_book_id, Some(valorant));
    assert!(world.core.engine().is_set_up());
    assert_eq!(
        rt.state.model.settings.recordings_folder, settings.recordings_folder,
        "S2 reads it back"
    );
}

#[test]
fn start_watching_without_a_folder_asks_for_one() {
    let world = world();
    let mut rt = support::runtime(
        world.ui(WindowKind::Setup),
        reelshelf_app::ui::size(WindowKind::Setup),
        false,
    );
    press(&mut rt, "start-watching");
    assert!(rt.file_dialog().is_some(), "the folder picker opened");
    let folder = recordings(&world._dir.path().join("Clips"));
    rt.resolve_file_dialog(Some(folder.clone().into())).unwrap();
    for _ in 0..3 {
        rt.frame(1.0 / 60.0);
    }
    assert_eq!(rt.state.setup.folder.as_deref(), Some(folder.as_str()));
    assert_eq!(rt.state.setup.matches.len(), 2);
    assert!(
        !world.core.engine().is_set_up(),
        "nothing is saved until Start watching"
    );
}

#[test]
fn use_this_book_saves_the_choice() {
    let world = world();
    let mut ui = world.ui(WindowKind::Flyout);
    ui.flyout.page = FlyoutPage::Book;
    ui.picker.folder = "Marvel Rivals".to_owned();
    assert_eq!(ui.model.books.len(), 6);
    let mut rt = support::runtime(ui, (388.0, 700.0), false);

    // Nothing is saved before a book is picked.
    press(&mut rt, "use-book");
    assert_eq!(world.core.engine().settings().saved_book_for("Marvel Rivals"), None);

    press(&mut rt, "book-3");
    let rivals = fixtures::book("Marvel Rivals").id;
    assert_eq!(rt.state.picker.selected, Some(rivals));
    press(&mut rt, "use-book");
    assert_eq!(
        world.core.engine().settings().saved_book_for("Marvel Rivals"),
        Some(rivals)
    );
    assert_eq!(
        world.saved().saved_book_for("marvel rivals"),
        Some(rivals),
        "saved, ignoring case"
    );
}

#[test]
fn the_flyouts_actions_reach_the_engine() {
    let world = world();
    let mut rt = support::runtime(world.ui(WindowKind::Flyout), (388.0, 520.0), false);
    assert_eq!(rt.state.model.state.kind, UploaderStateKind::Watching);

    // Pause for… → 1 hour pauses uploads.
    press(&mut rt, "pause");
    assert!(rt.state.flyout.pause_open);
    press(&mut rt, "pause-hour");
    assert_eq!(world.core.engine().state().kind, UploaderStateKind::Paused);
    assert_eq!(
        rt.state.model.state.kind,
        UploaderStateKind::Paused,
        "the flyout shows T4"
    );
    assert_eq!(rt.state.model.state.action, TrayAction::Resume);

    // T4's Resume resumes them.
    press(&mut rt, "state-action");
    assert_eq!(world.core.engine().state().kind, UploaderStateKind::Watching);

    // Until I resume, then Resume again.
    press(&mut rt, "pause");
    press(&mut rt, "pause-resumed");
    assert_eq!(world.core.engine().state().kind, UploaderStateKind::Paused);
    world.core.tray_action(TrayAction::Resume);
    assert_eq!(world.core.engine().state().kind, UploaderStateKind::Watching);

    press(&mut rt, "open-library");
    assert_eq!(world.opened.0.lock().unwrap().as_slice(), ["https://reelshelf.app"]);

    // Settings and Recent uploads are pages of the flyout, each with Back to the menu.
    press(&mut rt, "settings");
    assert_eq!(rt.state.flyout.page, FlyoutPage::Settings);
    press(&mut rt, "back");
    assert_eq!(rt.state.flyout.page, FlyoutPage::Home);
    press(&mut rt, "recent");
    assert_eq!(rt.state.flyout.page, FlyoutPage::Recent);
    press(&mut rt, "back");

    // T1's action shows recent uploads too.
    rt.update(|s, _| s.refresh());
    rt.frame(1.0 / 60.0);
    press(&mut rt, "state-action");
    assert_eq!(rt.state.flyout.page, FlyoutPage::Recent);
    press(&mut rt, "back");

    // With nothing uploading, Quit quits without asking.
    press(&mut rt, "quit");
    assert!(world.host.calls().contains(&"quit".to_owned()));
    drop(world.runtime);
}

#[test]
fn the_tray_menu_and_notification_links_reach_the_commands() {
    let commands = FixtureCommands::new(fixtures::model(fixtures::tray_inputs("T1")));
    for id in [
        tray::ids::RECENT,
        tray::ids::LIBRARY,
        tray::ids::SETTINGS,
        tray::ids::QUIT,
    ] {
        tray::on_menu(id, commands.as_ref());
    }
    for action in [
        NotificationAction::OpenLibrary,
        NotificationAction::ChooseBook("Marvel Rivals".into()),
        NotificationAction::SeePlans,
        NotificationAction::OpenStore,
        NotificationAction::SignIn,
        NotificationAction::NotNow,
    ] {
        shell::on_app_link(commands.as_ref(), &links::app_link(&action));
    }
    shell::on_app_link(commands.as_ref(), "reelshelf-app://format-the-disk");
    assert_eq!(
        commands.calls(),
        [
            "show Flyout(Recent)".to_owned(),
            format!("open_link {:?}", Link::Library),
            "show Flyout(Settings)".to_owned(),
            "quit false".to_owned(),
            format!("open_link {:?}", Link::Library),
            "show ChooseBook(\"Marvel Rivals\")".to_owned(),
            format!("open_link {:?}", Link::Plans),
            format!("open_link {:?}", Link::Store),
            "show SignIn".to_owned(),
        ]
    );
}
