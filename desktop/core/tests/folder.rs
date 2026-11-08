//! Watching the recordings folder: the baseline, settling, rescans, what counts as a clip, and ledger skips. Ported
//! from the .NET core's `FolderTests`.

mod support;

use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use chrono::TimeDelta;
use reelshelf_core::files;
use reelshelf_core::ledger::{LedgerState, RECENT_COUNT};
use reelshelf_core::monitor::{CHECK_EVERY, FolderMonitor};
use reelshelf_core::platform::Clock;
use reelshelf_core::settings::UploaderSettings;
use support::{TestUploader, set_modified};

fn monitor(test: &TestUploader) -> FolderMonitor {
    FolderMonitor::new(test.ledger.clone(), test.clock.clone(), Arc::new(|_: &Path| true))
}

fn recordings(test: &TestUploader) -> String {
    test.recordings().to_str().unwrap().to_string()
}

#[test]
fn files_already_in_the_folder_are_baselined_and_never_uploaded() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    test.write_file("Apex Legends/old.mp4", 100, None);
    test.write_file("old root clip.MKV", 100, None);
    let monitor = monitor(&test);

    let baselined = monitor.use_folder(&recordings(&test), true);
    monitor.rescan();
    test.clock.advance(Duration::from_secs(10));
    let added = monitor.settle(&[apex], &UploaderSettings::default());

    assert_eq!(baselined, 2);
    assert!(added.is_empty());
    assert_eq!(monitor.settling(), 0);
    assert_eq!(test.ledger.count(&monitor.root().unwrap(), &[LedgerState::Baseline]), 2);
    assert!(test.ledger.recent(RECENT_COUNT).is_empty());
}

#[test]
fn a_file_still_being_written_settles_five_seconds_after_it_stops_growing() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let monitor = monitor(&test);
    monitor.use_folder(&recordings(&test), true);
    let books = [apex.clone()];
    let settings = UploaderSettings::default();

    let path = test.write_file("Apex Legends/clip.mp4", 10, Some(test.clock.now()));
    monitor.mark_dirty("Apex Legends/clip.mp4");

    // The recorder writes for 20 seconds; the check runs every 2.
    for _ in (0..20).step_by(2) {
        assert!(monitor.settle(&books, &settings).is_empty());
        let mut bytes = std::fs::read(&path).unwrap();
        bytes.extend_from_slice(&[0; 1000]);
        std::fs::write(&path, bytes).unwrap();
        set_modified(&path, test.clock.now());
        test.clock.advance(CHECK_EVERY);
    }

    // It stopped: still settling at 2 and 4 seconds, ready once 5 have passed.
    assert!(monitor.settle(&books, &settings).is_empty());
    test.clock.advance(CHECK_EVERY);
    assert!(monitor.settle(&books, &settings).is_empty());
    test.clock.advance(CHECK_EVERY);
    assert!(monitor.settle(&books, &settings).is_empty());
    test.clock.advance(CHECK_EVERY);
    let added = monitor.settle(&books, &settings);

    assert_eq!(added.len(), 1);
    assert_eq!(added[0].size, 10_010);
    assert_eq!(added[0].state, LedgerState::Waiting);
    assert_eq!(added[0].category_id, Some(apex.id));
}

#[test]
fn a_file_a_recorder_still_holds_waits_until_it_opens_for_reading() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let locked = Arc::new(AtomicBool::new(true));
    let held = locked.clone();
    let monitor = FolderMonitor::new(
        test.ledger.clone(),
        test.clock.clone(),
        Arc::new(move |_: &Path| !held.load(Ordering::SeqCst)),
    );
    monitor.use_folder(&recordings(&test), true);
    test.write_file("Apex Legends/clip.mp4", 10, None);
    monitor.mark_dirty("Apex Legends/clip.mp4");
    let books = [apex];

    monitor.settle(&books, &UploaderSettings::default());
    test.clock.advance(Duration::from_secs(30));
    assert!(monitor.settle(&books, &UploaderSettings::default()).is_empty());

    locked.store(false, Ordering::SeqCst);
    assert_eq!(monitor.settle(&books, &UploaderSettings::default()).len(), 1);
}

#[test]
fn a_rescan_finds_a_file_whose_event_was_missed() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let monitor = monitor(&test);
    monitor.use_folder(&recordings(&test), true);
    let books = [apex];

    // No watcher event arrives for it.
    test.write_file("Apex Legends/missed.mp4", 10, None);
    assert_eq!(monitor.settling(), 0);

    monitor.rescan();
    monitor.settle(&books, &UploaderSettings::default());
    test.clock.advance(Duration::from_secs(6));
    let added = monitor.settle(&books, &UploaderSettings::default());
    assert_eq!(added.len(), 1);
    assert_eq!(added[0].path, "Apex Legends/missed.mp4");
}

#[test]
fn a_file_the_ledger_has_is_never_looked_at_again_unless_it_changes() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let monitor = monitor(&test);
    monitor.use_folder(&recordings(&test), true);
    let path = test.write_file("Apex Legends/clip.mp4", 10, None);
    let books = [apex];
    monitor.rescan();
    monitor.settle(&books, &UploaderSettings::default());
    test.clock.advance(Duration::from_secs(6));
    let mut added = monitor.settle(&books, &UploaderSettings::default());
    assert_eq!(added.len(), 1);
    let mut entry = added.remove(0);
    entry.state = LedgerState::Shelved;
    test.ledger.save(&entry);

    monitor.rescan();
    monitor.mark_dirty("Apex Legends/clip.mp4");
    assert_eq!(monitor.settling(), 0);

    // Replaced by a different file of the same name: that one is new.
    std::fs::write(&path, [0u8; 20]).unwrap();
    set_modified(&path, test.clock.now() - TimeDelta::seconds(30));
    monitor.rescan();
    assert_eq!(monitor.settling(), 1);
}

#[test]
fn only_videos_outside_dot_folders_are_candidates() {
    for (path, candidate) in [
        ("clip.mp4", true),
        ("Apex Legends/clip.MOV", true),
        ("a/b/clip.Mkv", true),
        ("clip.webm", true),
        ("clip.avi", false),
        ("clip.mp4.part", false),
        (".hidden.mp4", false),
        (".thumbnails/clip.mp4", false),
        ("Apex/.cache/clip.mp4", false),
        ("Apex\\.cache\\clip.mp4", false),
    ] {
        assert_eq!(files::is_candidate(path), candidate, "{path}");
    }
}

#[test]
fn a_scan_skips_hidden_files_and_dot_folders() {
    let test = TestUploader::new();
    test.write_file("Apex Legends/clip.mp4", 10, None);
    test.write_file(".tmp/clip.mp4", 10, None);
    test.write_file("Apex Legends/.partial.mp4", 10, None);
    test.write_file("notes.txt", 10, None);

    let scanned = files::scan(&test.recordings());

    assert_eq!(
        scanned
            .iter()
            .map(|file| file.relative_path.as_str())
            .collect::<Vec<_>>(),
        ["Apex Legends/clip.mp4"]
    );
    assert_eq!(files::folders_with_videos(&test.recordings()), ["Apex Legends"]);
}

#[test]
fn clips_without_a_book_wait_as_needs_book_until_the_folder_gets_one() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let monitor = monitor(&test);
    monitor.use_folder(&recordings(&test), true);
    test.write_file("Marvel Rivals/one.mp4", 10, None);
    test.write_file("straight in.mp4", 10, None);
    monitor.rescan();
    monitor.settle(std::slice::from_ref(&apex), &UploaderSettings::default());
    test.clock.advance(Duration::from_secs(6));

    let added = monitor.settle(std::slice::from_ref(&apex), &UploaderSettings::default());

    assert_eq!(added.len(), 2);
    assert!(added.iter().all(|entry| entry.state == LedgerState::NeedsBook));
    let rivals = test.add_book("Marvel Rivals");
    let books = [apex.clone(), rivals];
    assert_eq!(monitor.rematch(&books, &UploaderSettings::default()), 1);
    let with_default = UploaderSettings {
        default_book_id: Some(apex.id),
        ..UploaderSettings::default()
    };
    assert_eq!(monitor.rematch(&books, &with_default), 1);
    assert!(
        test.ledger
            .in_state(&monitor.root().unwrap(), &[LedgerState::NeedsBook])
            .is_empty()
    );
}

#[test]
fn a_choice_saved_for_a_folder_puts_its_waiting_clips_on_that_book() {
    let test = TestUploader::new();
    let rivals = test.add_book("Overwatch 2");
    let monitor = monitor(&test);
    monitor.use_folder(&recordings(&test), true);
    test.write_file("Marvel Rivals/one.mp4", 10, None);
    monitor.rescan();
    monitor.settle(std::slice::from_ref(&rivals), &UploaderSettings::default());
    test.clock.advance(Duration::from_secs(6));
    monitor.settle(std::slice::from_ref(&rivals), &UploaderSettings::default());

    let root = monitor.root().unwrap();
    assert_eq!(
        test.ledger
            .assign_book(&root, "marvel rivals", rivals.id, test.clock.now()),
        1
    );
    let waiting = test.ledger.in_state(&root, &[LedgerState::Waiting]);
    assert_eq!(waiting.len(), 1);
    assert_eq!(waiting[0].category_id, Some(rivals.id));
}

/// Waits up to five seconds of real time for the watcher to see something.
fn eventually(mut check: impl FnMut() -> bool) -> bool {
    let deadline = std::time::Instant::now() + Duration::from_secs(5);
    while std::time::Instant::now() < deadline {
        if check() {
            return true;
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    false
}

#[test]
fn the_watcher_marks_new_clips_dirty_and_asks_for_a_rescan_when_a_folder_appears() {
    use reelshelf_core::watcher::{FolderWatcher, WatchSignals};

    let test = TestUploader::new();
    let signals = Arc::new(WatchSignals::default());
    let _watcher = FolderWatcher::start(&test.recordings(), &signals).unwrap();

    std::fs::create_dir(test.recordings().join("Apex Legends")).unwrap();
    assert!(eventually(|| signals.take_rescan()), "a new folder asks for a rescan");

    test.write_file("Apex Legends/clip.mp4", 10, None);
    test.write_file("Apex Legends/notes.txt", 10, None);
    let mut dirty = Vec::new();
    assert!(eventually(|| {
        dirty.extend(signals.take_dirty());
        dirty.iter().any(|path| path == "Apex Legends/clip.mp4")
    }));
    assert!(dirty.iter().all(|path| path.ends_with(".mp4")), "{dirty:?}");
}
