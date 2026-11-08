//! The tray's state: the priority between T1–T8 and the words of each, as screens.html has them; and S4's rows. Ported
//! from the .NET core's `StateTests` and `RecentUploadTests`.

use chrono::{DateTime, TimeDelta, TimeZone, Utc};
use reelshelf_core::ledger::{LedgerEntry, LedgerState};
use reelshelf_core::matching::Book;
use reelshelf_core::recent::RecentUpload;
use reelshelf_core::state::{
    LibraryFullInfo, PauseInfo, PauseLength, StateInputs, TrayBadge, UploaderState, UploaderStateKind,
    short_folder_name,
};
use uuid::Uuid;

fn now() -> DateTime<Utc> {
    Utc.with_ymd_and_hms(2026, 11, 6, 21, 47, 0).unwrap()
}

fn everything() -> StateInputs {
    StateInputs {
        update_required: true,
        library_full: Some(LibraryFullInfo {
            clip_limit: Some(100),
            monthly_limit: false,
            uploads_from: None,
        }),
        unreachable_retry_at: Some(now() + TimeDelta::minutes(2)),
        needs_book: Some(("Marvel Rivals".to_string(), 2)),
        paused: Some(PauseInfo {
            length: PauseLength::OneHour,
            until: Some(now() + TimeDelta::hours(1)),
        }),
        game_holds_uploads: true,
        waiting: 1,
        current: Some(("Apex Legends 2026.11.06 - 21.52".to_string(), 64)),
        folder_name: "Videos\\NVIDIA".to_string(),
        shelved_today: 3,
    }
}

fn state(inputs: &StateInputs) -> UploaderState {
    UploaderState::from_inputs(inputs, now(), &Utc)
}

fn assert_state(inputs: &StateInputs, title: &str, detail: &str, action: &str, badge: TrayBadge) {
    let state = state(inputs);
    assert_eq!(state.title, title);
    assert_eq!(state.detail, detail);
    assert_eq!(state.action_label, action);
    assert_eq!(state.badge, badge);
}

#[test]
fn the_priority_is_t8_t6_t7_t5_t4_t3_t2_t1() {
    let mut inputs = everything();
    let clears: [fn(&mut StateInputs); 8] = [
        |inputs| inputs.update_required = false,
        |inputs| inputs.library_full = None,
        |inputs| inputs.unreachable_retry_at = None,
        |inputs| inputs.needs_book = None,
        |inputs| inputs.paused = None,
        |inputs| inputs.game_holds_uploads = false,
        |inputs| inputs.current = None,
        |_| {},
    ];
    let mut order = Vec::new();
    for clear in clears {
        order.push(state(&inputs).id());
        clear(&mut inputs);
    }

    assert_eq!(order, ["T8", "T6", "T7", "T5", "T4", "T3", "T2", "T1"]);
}

#[test]
fn each_state_has_the_words_from_the_screens() {
    let mut inputs = everything();

    assert_state(
        &inputs,
        "Update Reelshelf",
        "This version doesn't work with Reelshelf any more",
        "Open the Store",
        TrayBadge::Red,
    );
    inputs.update_required = false;
    assert_state(
        &inputs,
        "Your library is full",
        "Your plan keeps 100 clips · 1 clip waiting",
        "See plans",
        TrayBadge::Amber,
    );
    inputs.library_full = None;
    assert_state(
        &inputs,
        "Couldn't reach Reelshelf",
        "Trying again in 2 minutes · 1 clip waiting",
        "Try now",
        TrayBadge::Red,
    );
    inputs.unreachable_retry_at = None;
    assert_state(
        &inputs,
        "Marvel Rivals needs a book",
        "2 clips are waiting",
        "Choose a book…",
        TrayBadge::Amber,
    );
    inputs.needs_book = None;
    assert_state(
        &inputs,
        "Paused until 22:47",
        "New clips will upload when you resume",
        "Resume",
        TrayBadge::Amber,
    );
    inputs.paused = None;
    assert_state(
        &StateInputs {
            waiting: 2,
            ..inputs.clone()
        },
        "Waiting for you to finish playing…",
        "2 clips will upload when you leave the game",
        "Upload now",
        TrayBadge::Green,
    );
    inputs.game_holds_uploads = false;
    assert_state(
        &StateInputs {
            waiting: 2,
            ..inputs.clone()
        },
        "Uploading 2 clips…",
        "Apex Legends 2026.11.06 - 21.52 · 64%",
        "Show recent uploads",
        TrayBadge::Green,
    );
    assert_eq!(state(&inputs).percent, Some(64));
    inputs.current = None;
    assert_state(
        &inputs,
        "Watching for clips",
        "Videos\\NVIDIA · 3 clips on the shelf today",
        "Show recent uploads",
        TrayBadge::None,
    );
}

#[test]
fn a_game_with_nothing_waiting_leaves_the_tray_watching() {
    let inputs = StateInputs {
        game_holds_uploads: true,
        waiting: 0,
        folder_name: "Videos\\NVIDIA".to_string(),
        ..StateInputs::default()
    };

    assert_eq!(state(&inputs).kind, UploaderStateKind::Watching);
}

#[test]
fn the_other_pauses_and_limits_have_their_own_words() {
    let paused = |length, until| StateInputs {
        paused: Some(PauseInfo { length, until }),
        ..StateInputs::default()
    };
    assert_eq!(
        state(&paused(PauseLength::UntilTomorrow, Some(now() + TimeDelta::hours(8)))).title,
        "Paused until tomorrow"
    );
    assert_eq!(state(&paused(PauseLength::UntilResumed, None)).title, "Paused");
    assert_eq!(
        state(&StateInputs {
            needs_book: Some((String::new(), 1)),
            ..StateInputs::default()
        })
        .title,
        "Your recordings folder needs a book"
    );
    let monthly = state(&StateInputs {
        library_full: Some(LibraryFullInfo {
            clip_limit: Some(1000),
            monthly_limit: true,
            uploads_from: Some(Utc.with_ymd_and_hms(2026, 11, 20, 9, 0, 0).unwrap()),
        }),
        waiting: 1,
        ..StateInputs::default()
    });
    assert_eq!(monthly.title, "You've added a lot of clips this month");
    assert_eq!(monthly.detail, "New clips upload from 20 Nov 2026 · 1 clip waiting");
}

#[test]
fn the_tooltip_repeats_the_first_line() {
    let state = state(&everything());

    assert_eq!(state.tooltip(), state.title);
}

#[test]
fn the_folder_is_named_by_its_last_two_folders() {
    for (path, name) in [
        ("C:\\Users\\you\\Videos\\NVIDIA", "Videos\\NVIDIA"),
        ("D:\\Clips\\", "D:\\Clips"),
        ("/home/you/Videos/OBS", "Videos\\OBS"),
    ] {
        assert_eq!(short_folder_name(Some(path)), name, "{path}");
    }
    assert_eq!(short_folder_name(None), "");
}

// ---- S4's rows ----

fn recent_now() -> DateTime<Utc> {
    Utc.with_ymd_and_hms(2026, 11, 6, 21, 30, 0).unwrap()
}

fn apex() -> Book {
    Book {
        cloth_color: Some("#8a3b2f".to_string()),
        ..Book::new(Uuid::from_u128(1), "Apex Legends", Some("apex-legends"))
    }
}

fn entry(state: LedgerState, attempts: u32) -> LedgerEntry {
    let now = recent_now();
    LedgerEntry {
        root: "/videos".to_string(),
        path: "Apex Legends/Apex Legends 2026.11.06 - 21.20.08.01.mp4".to_string(),
        size: 41_943_040,
        last_write: now - TimeDelta::minutes(10),
        state,
        clip_id: (state == LedgerState::Shelved).then(Uuid::new_v4),
        md5: None,
        category_id: Some(apex().id),
        upload_id: None,
        part_size: None,
        part_count: None,
        parts: Default::default(),
        attempts,
        last_error: None,
        length_seconds: Some(22.0),
        next_attempt_at: (attempts > 0 && attempts < 5).then(|| now + TimeDelta::minutes(1)),
        progress: 0,
        created_at: now - TimeDelta::minutes(10),
        updated_at: now,
        shelved_at: (state == LedgerState::Shelved).then(|| Utc.with_ymd_and_hms(2026, 11, 6, 21, 23, 0).unwrap()),
    }
}

#[test]
fn each_row_state_reads_as_the_screens_say() {
    for (state, percent, attempts, status) in [
        (LedgerState::Waiting, None, 0, "Waiting"),
        (LedgerState::Uploading, Some(64), 0, "Uploading… 64%"),
        (LedgerState::Shelved, None, 0, "On the shelf"),
        (LedgerState::Duplicate, None, 0, "Already on the shelf"),
        (LedgerState::TooLong, None, 0, "Too long for your plan"),
        (LedgerState::Uploading, None, 2, "Didn't upload, trying again soon"),
        (LedgerState::Failed, None, 5, "Didn't upload"),
        (LedgerState::NeedsBook, None, 0, "Waiting for a book"),
        (LedgerState::PlanFull, None, 0, "Waiting for room in your library"),
    ] {
        let row = RecentUpload::from_entry(&entry(state, attempts), &[apex()], recent_now(), percent, &Utc);

        assert_eq!(row.status, status, "{state:?}");
        assert_eq!(row.title, "Apex Legends 2026.11.06 - 21.20.08.01");
        assert_eq!(row.can_try_again, state == LedgerState::Failed);
    }
}

#[test]
fn a_shelved_clip_shows_its_book_length_size_and_time_and_opens_on_the_site() {
    let row = RecentUpload::from_entry(&entry(LedgerState::Shelved, 0), &[apex()], recent_now(), None, &Utc);

    assert_eq!(row.meta, "Apex Legends · 0:22 · 40 MB · 21:23");
    assert!(row.can_open());
    assert_eq!(row.book_slug.as_deref(), Some("apex-legends"));
    assert_eq!(row.cloth_color.as_deref(), Some("#8a3b2f"));
}

#[test]
fn a_row_whose_book_is_gone_says_so() {
    let row = RecentUpload::from_entry(&entry(LedgerState::Waiting, 0), &[], recent_now(), None, &Utc);

    assert_eq!(row.meta, "No book · 0:22 · 40 MB");
    assert!(!row.can_open());
}
