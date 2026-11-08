//! The engine end to end against the fake API: full-screen games, a full library, books and notifications. Ported from
//! the .NET core's `EngineTests`.

mod support;

use std::sync::Arc;
use std::sync::Mutex;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use reelshelf_core::engine::{EngineEvent, RESCAN_EVERY, UploaderEngine};
use reelshelf_core::ledger::{LedgerState, RECENT_COUNT};
use reelshelf_core::platform::CredentialStore;
use reelshelf_core::settings::{FullScreenMode, UploaderSettings};
use reelshelf_core::state::{PauseLength, UploaderStateKind};
use reelshelf_core::uploads::UploadOutcome;
use support::{FakeReelshelf, TestUploader, problem};

type Engine = Arc<UploaderEngine<FakeReelshelf>>;

async fn start(test: &TestUploader, configure: impl FnOnce(&mut UploaderSettings)) -> Engine {
    let engine = test.engine(configure);
    engine.set_recordings_folder(test.recordings().to_str().unwrap());
    engine.tick().await;
    engine
}

/// Lets new files settle: a rescan, then checks over more than five seconds.
async fn settle(test: &TestUploader, engine: &Engine) {
    test.clock.advance(RESCAN_EVERY);
    engine.tick().await;
    for _ in 0..4 {
        test.clock.advance(Duration::from_secs(2));
        engine.tick().await;
    }
}

#[tokio::test]
async fn a_new_clip_settles_and_uploads_onto_its_book() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/new.mp4", 2000, None);

    settle(&test, &engine).await;
    assert_eq!(engine.state().kind, UploaderStateKind::Watching);
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));

    let clips = test.server.clips();
    assert_eq!(clips.len(), 1);
    assert_eq!(clips[0].category_id, apex.id);
    assert_eq!(engine.state().title, "Watching for clips");
    assert_eq!(engine.state().detail, "Videos\\NVIDIA · 1 clip on the shelf today");
    assert_eq!(engine.upload_next().await, None);
}

#[tokio::test]
async fn while_a_game_is_full_screen_a_new_clip_waits_then_uploads_once_the_game_closes() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.game.set(true);
    test.write_file("Apex Legends/during the game.mp4", 2000, None);

    settle(&test, &engine).await;
    assert_eq!(engine.upload_next().await, None);

    let state = engine.state();
    assert_eq!(state.kind, UploaderStateKind::GameRunning);
    assert_eq!(state.id(), "T3");
    assert_eq!(state.title, "Waiting for you to finish playing…");
    assert_eq!(state.detail, "1 clip will upload when you leave the game");
    assert!(test.server.clips().is_empty());

    test.game.set(false);
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
}

#[tokio::test]
async fn a_clip_already_uploading_finishes_its_part_then_waits_for_the_game() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/clip.mp4", 3000, None);
    settle(&test, &engine).await;
    let parts = Arc::new(Mutex::new(Vec::new()));
    let waits_during_game = Arc::new(AtomicUsize::new(0));
    {
        let parts = parts.clone();
        let game = test.game.clone();
        test.server.state().on_part = Some(Arc::new(move |number| {
            use reelshelf_core::platform::GameActivity;
            parts.lock().unwrap().push((number, game.is_game_running()));
            // The game starts while the first part is going up.
            if number == 1 {
                game.set(true);
            }
        }));
    }
    {
        let game = test.game.clone();
        let waits = waits_during_game.clone();
        test.clock.on_delay(move |_| {
            use reelshelf_core::platform::GameActivity;
            if game.is_game_running() && waits.fetch_add(1, Ordering::SeqCst) + 1 == 5 {
                game.set(false);
            }
        });
    }

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));

    assert_eq!(*parts.lock().unwrap(), [(1, false), (2, false), (3, false)]);
    assert_eq!(waits_during_game.load(Ordering::SeqCst), 5);
}

#[tokio::test]
async fn upload_slowly_keeps_uploading_during_a_game_at_five_megabits_without_a_limit() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    test.server.state().part_size = 1_000_000;
    let engine = start(&test, |settings| settings.full_screen = FullScreenMode::UploadSlowly).await;
    test.game.set(true);
    test.write_file("Apex Legends/clip.mp4", 1_250_000, None);
    settle(&test, &engine).await;
    test.clock.clear_delays();

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));

    // 1,250,000 bytes at 5 Mbit/s (625,000 bytes a second): two seconds.
    let waited: f64 = test.clock.delays().iter().map(Duration::as_secs_f64).sum();
    assert!((1.9..=2.1).contains(&waited), "{waited}");
}

#[tokio::test]
async fn a_full_library_pauses_uploads_until_the_plan_has_room() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/one.mp4", 500, None);
    test.write_file("Apex Legends/two.mp4", 500, None);
    settle(&test, &engine).await;
    {
        let mut state = test.server.state();
        state.prepare_answers.push_back(problem(
            403,
            Some("Plan Limit Reached"),
            Some("Your plan keeps up to 100 clips"),
        ));
        state.clip_count = 100;
    }

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::PlanFull));
    assert_eq!(engine.upload_next().await, None);

    let state = engine.state();
    assert_eq!(state.id(), "T6");
    assert_eq!(state.title, "Your library is full");
    assert_eq!(state.detail, "Your plan keeps 100 clips · 2 clips waiting");
    assert_eq!(
        test.notifier
            .sent()
            .into_iter()
            .filter(|sent| sent.starts_with("N3"))
            .collect::<Vec<_>>(),
        ["N3 100:false"]
    );

    // Checked again every 30 minutes; once there is room, both upload.
    test.server.state().clip_count = 50;
    test.clock.advance(Duration::from_secs(29 * 60));
    engine.tick().await;
    assert_eq!(engine.state().id(), "T6");
    test.clock.advance(Duration::from_secs(2 * 60));
    engine.tick().await;
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
    assert_eq!(test.server.clips().len(), 2);
}

#[tokio::test]
async fn an_unmatched_folder_asks_once_for_a_book_and_its_clips_upload_once_one_is_chosen() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let rivals = test.add_book("Overwatch 2");
    let engine = start(&test, |_| {}).await;
    let mut events = engine.subscribe();
    test.write_file("Marvel Rivals 2/one.mp4", 500, None);
    settle(&test, &engine).await;
    test.write_file("Marvel Rivals 2/two.mp4", 500, None);
    settle(&test, &engine).await;

    let mut asked = Vec::new();
    while let Ok(event) = events.try_recv() {
        if let EngineEvent::BookNeeded(folder) = event {
            asked.push(folder);
        }
    }
    assert_eq!(asked, ["Marvel Rivals 2"]);
    assert_eq!(test.notifier.sent(), ["N2 Marvel Rivals 2:1", "N2 Marvel Rivals 2:2"]);
    let state = engine.state();
    assert_eq!(state.id(), "T5");
    assert_eq!(state.title, "Marvel Rivals 2 needs a book");
    assert_eq!(state.detail, "2 clips are waiting");
    assert_eq!(engine.upload_next().await, None);

    engine.choose_book_for_folder("Marvel Rivals 2", rivals.id);
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
    assert!(test.server.clips().iter().all(|clip| clip.category_id == rivals.id));
    assert_eq!(
        test.settings_store().load().saved_book_for("marvel rivals 2"),
        Some(rivals.id)
    );
}

#[tokio::test]
async fn shelved_clips_are_notified_together_at_most_every_ten_minutes_and_not_during_a_game() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    test.add_book("Valorant");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/a.mp4", 300, None);
    test.write_file("Apex Legends/b.mp4", 300, None);
    test.write_file("VALORANT/c.mp4", 300, None);
    settle(&test, &engine).await;
    while engine.upload_next().await.is_some() {}

    engine.tick().await;
    assert_eq!(test.notifier.sent(), ["N1 Apex Legends:2, Valorant:1"]);

    test.write_file("Apex Legends/d.mp4", 300, None);
    settle(&test, &engine).await;
    engine.upload_next().await;
    test.game.set(true);
    test.clock.advance(Duration::from_secs(11 * 60));
    engine.tick().await;
    assert_eq!(test.notifier.sent().len(), 1);

    test.game.set(false);
    engine.tick().await;
    assert_eq!(
        test.notifier.sent().last().map(String::as_str),
        Some("N1 Apex Legends:1")
    );
}

#[tokio::test]
async fn a_revoked_token_signs_the_app_out_and_an_old_version_asks_for_an_update() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    let mut events = engine.subscribe();
    test.write_file("Apex Legends/a.mp4", 300, None);
    settle(&test, &engine).await;
    test.server.state().prepare_answers.push_back(problem(401, None, None));

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::SignedOut));
    let mut signed_out = false;
    while let Ok(event) = events.try_recv() {
        signed_out |= event == EngineEvent::SignedOut;
    }
    assert!(signed_out);
    assert!(!engine.is_signed_in());
    assert_eq!(test.credentials.token(), None);
    assert!(test.notifier.sent().contains(&"N5".to_string()));
    let recent = test.ledger.recent(RECENT_COUNT);
    assert_eq!(recent.len(), 1);
    assert_eq!(recent[0].state, LedgerState::Waiting);

    test.credentials.write_token("rsd_again");
    engine.on_signed_in();
    test.server.state().prepare_answers.push_back(problem(426, None, None));
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::UpdateRequired));
    assert_eq!(engine.state().id(), "T8");
    assert_eq!(engine.state().title, "Update Reelshelf");
    assert_eq!(test.notifier.sent().iter().filter(|sent| *sent == "N4").count(), 1);
}

#[tokio::test]
async fn a_refused_token_on_any_call_signs_the_app_out() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.server.state().refuse_with = Some(problem(401, None, None));

    // The hourly books refresh is the call that finds out.
    test.clock.advance(Duration::from_secs(61 * 60));
    engine.tick().await;

    assert!(!engine.is_signed_in());
    assert_eq!(test.credentials.token(), None);
    assert_eq!(test.notifier.sent(), ["N5"]);
}

#[tokio::test]
async fn an_unsupported_upload_protocol_asks_for_an_update() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/a.mp4", 300, None);
    settle(&test, &engine).await;
    test.server.state().protocol = "tus".to_string();

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Unsupported));
    assert_eq!(engine.state().id(), "T8");
    assert_eq!(engine.upload_next().await, None);
}

#[tokio::test]
async fn when_the_api_cant_be_reached_the_tray_says_when_it_tries_again() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/a.mp4", 300, None);
    settle(&test, &engine).await;
    test.server.state().api_down = true;

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::RetryLater));

    let state = engine.state();
    assert_eq!(state.id(), "T7");
    assert_eq!(state.title, "Couldn't reach Reelshelf");
    assert_eq!(state.detail, "Trying again in 15 seconds · 1 clip waiting");
}

#[tokio::test]
async fn pausing_holds_uploads_until_resumed() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/a.mp4", 300, None);
    settle(&test, &engine).await;

    engine.pause(PauseLength::UntilResumed);
    assert_eq!(engine.upload_next().await, None);
    assert_eq!(engine.state().id(), "T4");
    assert_eq!(engine.state().title, "Paused");

    engine.pause(PauseLength::OneHour);
    test.clock.advance(Duration::from_secs(61 * 60));
    engine.tick().await;
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
}

#[tokio::test]
async fn choosing_a_folder_baselines_what_is_there_and_the_folder_matches_its_books() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let valorant = test.add_book("Valorant");
    test.write_file("Apex Legends/old.mp4", 100, None);
    test.write_file("VALORANT/old.mp4", 100, None);
    test.write_file("Marvel Rivals/old.mp4", 100, None);
    let engine = test.engine(|_| {});
    engine.refresh_books().await;

    engine.set_recordings_folder(test.recordings().to_str().unwrap());
    settle(&test, &engine).await;

    assert!(test.ledger.recent(RECENT_COUNT).is_empty());
    let matches: Vec<(String, Option<String>)> = engine
        .folder_matches(None)
        .into_iter()
        .map(|(folder, book)| (folder, book.map(|book| book.name)))
        .collect();
    assert_eq!(
        matches,
        [
            ("Apex Legends".to_string(), Some(apex.name)),
            ("Marvel Rivals".to_string(), None),
            ("VALORANT".to_string(), Some(valorant.name)),
        ]
    );
}

#[tokio::test]
async fn a_failed_file_is_tried_again_from_recent_uploads() {
    let test = TestUploader::new();
    test.add_book("Apex Legends");
    let engine = start(&test, |_| {}).await;
    test.write_file("Apex Legends/a.mp4", 300, None);
    settle(&test, &engine).await;
    test.server.state().prepare_answers.push_back(problem(403, None, None));
    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Failed));
    let rows = engine.recent_uploads();
    assert_eq!(rows.len(), 1);
    assert_eq!(rows[0].status, "Didn't upload");
    assert!(rows[0].can_try_again);

    engine.try_now(Some(&rows[0].entry));

    assert_eq!(engine.upload_next().await, Some(UploadOutcome::Shelved));
    assert_eq!(engine.recent_uploads()[0].status, "On the shelf");
}

/// The app runs the engine on its own task, so its loop must be able to move between threads.
#[test]
fn the_engine_can_run_on_a_spawned_task() {
    fn assert_spawnable<T: std::future::Future + Send + 'static>(_: &T) {}

    let test = TestUploader::new();
    let engine = test.engine(|_| {});
    let running = async move { engine.run().await };
    assert_spawnable(&running);
}
