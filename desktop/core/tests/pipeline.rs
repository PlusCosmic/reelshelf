//! Uploading one file against the fake API and storage, and every answer preparing and completing can give. Ported
//! from the .NET core's `UploadPipelineTests`.

mod support;

use std::sync::Arc;
use std::time::Duration;

use chrono::TimeDelta;
use reelshelf_core::BoxFuture;
use reelshelf_core::ledger::{LedgerEntry, LedgerState};
use reelshelf_core::media::md5_hex;
use reelshelf_core::platform::Clock;
use reelshelf_core::uploads::{MAX_ATTEMPTS, TokenBucket, UploadGate, UploadOutcome, backoff};
use support::{PLAN, TestUploader, mp4, problem};
use uuid::Uuid;

/// Uploads the entry once and answers the outcome with the row as the ledger now has it.
async fn run(
    test: &TestUploader,
    entry: &LedgerEntry,
    gate: Option<Arc<dyn UploadGate>>,
) -> (UploadOutcome, LedgerEntry) {
    let mut entry = entry.clone();
    let path = test.full_path(&entry);
    let outcome = test.pipeline(gate).upload(&mut entry, &path, &PLAN).await;
    (outcome, test.ledger.find(&entry.root, &entry.path).unwrap())
}

/// A gate that is always open, at a fixed speed.
struct SlowGate(Option<u64>);

impl UploadGate for SlowGate {
    fn wait_until_open(&self) -> BoxFuture<'_, ()> {
        Box::pin(async {})
    }

    fn bytes_per_second(&self) -> Option<u64> {
        self.0
    }
}

#[tokio::test]
async fn a_file_is_hashed_prepared_uploaded_in_parts_and_completed() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/Apex Legends 2026.11.06 - 21.52.10.07.mp4", apex.id, 2500);

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::Shelved);
    let clips = test.server.clips();
    assert_eq!(clips.len(), 1);
    let clip = &clips[0];
    assert!(clip.completed);
    assert_eq!(clip.title, "Apex Legends 2026.11.06 - 21.52.10.07");
    assert_eq!(clip.category_id, apex.id);
    assert_eq!(clip.md5, md5_hex(&test.full_path(&entry)).unwrap());
    assert_eq!(clip.created_at, entry.last_write);
    // 2,500 bytes in 1,024-byte parts, one at a time.
    assert_eq!(
        clip.parts.values().map(|(_, size)| *size).collect::<Vec<_>>(),
        [1024, 1024, 452]
    );
    assert_eq!(test.server.puts(), 3);
    assert_eq!(
        test.server
            .requests()
            .iter()
            .filter(|request| request.ends_with("/upload/parts"))
            .count(),
        1
    );

    assert_eq!(saved.state, LedgerState::Shelved);
    assert_eq!(saved.clip_id, Some(clip.id));
    assert_eq!(saved.parts.len(), 3);
    assert_eq!(saved.shelved_at, Some(test.clock.now()));
    assert_eq!(saved.progress, 100);
}

#[tokio::test]
async fn a_duplicate_is_marked_already_on_the_shelf() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    test.server.state().prepare_answers.push_back(problem(
        409,
        None,
        Some("A video with this MD5 hash already exists"),
    ));

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::Duplicate);
    assert_eq!(saved.state, LedgerState::Duplicate);
    assert_eq!(test.server.puts(), 0);
}

#[tokio::test]
async fn a_full_plan_marks_the_plan_full() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    test.server.state().prepare_answers.push_back(problem(
        403,
        Some("Plan Limit Reached"),
        Some("Your plan keeps up to 100 clips and you have 100. Delete some, or upgrade to keep more."),
    ));

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::PlanFull);
    assert_eq!(saved.state, LedgerState::PlanFull);
}

#[tokio::test]
async fn another_refusal_fails_the_file() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    // A Viewer can't add clips: 403 with no plan problem.
    test.server.state().prepare_answers.push_back(problem(403, None, None));

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::Failed);
    assert_eq!(saved.state, LedgerState::Failed);
}

#[tokio::test]
async fn a_book_taken_off_the_shelf_leaves_the_file_waiting_for_a_book() {
    let test = TestUploader::new();
    let entry = test.waiting("Apex Legends/clip.mp4", Uuid::new_v4(), 2500);

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::NeedsBook);
    assert_eq!(saved.state, LedgerState::NeedsBook);
    assert_eq!(saved.category_id, None);
}

#[tokio::test]
async fn a_clip_longer_than_the_plan_is_too_long_without_being_hashed_or_prepared() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let mut entry = test.waiting("Apex Legends/long.mp4", apex.id, 2500);
    std::fs::write(test.full_path(&entry), mp4(61.5)).unwrap();
    entry.size = std::fs::metadata(test.full_path(&entry)).unwrap().len();

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::TooLong);
    assert_eq!(saved.state, LedgerState::TooLong);
    assert_eq!(saved.length_seconds, Some(61.5));
    assert_eq!(saved.md5, None);
    assert!(test.server.clips().is_empty());
}

#[tokio::test]
async fn a_clip_a_second_over_the_plan_still_uploads() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let mut entry = test.waiting("Apex Legends/edge.mov", apex.id, 2500);
    std::fs::write(test.full_path(&entry), mp4(60.9)).unwrap();
    entry.size = std::fs::metadata(test.full_path(&entry)).unwrap().len();

    assert_eq!(run(&test, &entry, None).await.0, UploadOutcome::Shelved);
}

#[tokio::test]
async fn an_unreadable_length_is_left_to_the_servers_probe() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mkv", apex.id, 2500);

    assert_eq!(run(&test, &entry, None).await.0, UploadOutcome::Shelved);
}

#[tokio::test]
async fn the_probe_refusing_its_length_marks_it_too_long_and_an_unreadable_file_failed() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let too_long = test.waiting("Apex Legends/a.mkv", apex.id, 2500);
    let unreadable = test.waiting("Apex Legends/b.mkv", apex.id, 2500);
    {
        let mut state = test.server.state();
        state.complete_answers.push_back(problem(
            422,
            None,
            Some("This clip is 2:14. Your plan keeps clips up to 1:00, so trim it or upgrade for clips up to 2:00."),
        ));
        state
            .complete_answers
            .push_back(problem(422, None, Some("The file couldn't be read as a video.")));
    }

    let first = run(&test, &too_long, None).await;
    let second = run(&test, &unreadable, None).await;

    assert_eq!(first.0, UploadOutcome::TooLong);
    assert_eq!(first.1.state, LedgerState::TooLong);
    assert_eq!(second.0, UploadOutcome::Failed);
    assert_eq!(second.1.state, LedgerState::Failed);
    assert_eq!(
        second.1.last_error.as_deref(),
        Some("The file couldn't be read as a video.")
    );
}

#[tokio::test]
async fn a_check_that_couldnt_finish_is_tried_again_in_a_minute_without_using_an_attempt() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    test.server.state().complete_answers.push_back(problem(
        503,
        None,
        Some("The upload couldn't be checked just now; try again"),
    ));

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::RetryLater);
    assert_eq!(saved.state, LedgerState::Uploading);
    assert_eq!(saved.next_attempt_at, Some(test.clock.now() + TimeDelta::minutes(1)));
    assert_eq!(saved.attempts, 0);

    // The next try completes the same upload; nothing is sent twice.
    test.clock.advance(Duration::from_secs(60));
    assert_eq!(run(&test, &saved, None).await.0, UploadOutcome::Shelved);
    assert_eq!(test.server.clips().len(), 1);
    assert_eq!(test.server.puts(), 3);
}

#[tokio::test]
async fn another_upload_protocol_fails_the_file_and_gives_the_clip_back() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    test.server.state().protocol = "tus".to_string();

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::Unsupported);
    assert_eq!(saved.state, LedgerState::Failed);
    let clips = test.server.clips();
    assert_eq!(clips.len(), 1);
    assert!(clips[0].deleted);
    assert_eq!(test.server.puts(), 0);
}

#[tokio::test]
async fn an_upload_under_way_resumes_from_the_parts_storage_has() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 3000);
    // The first part went in before the app stopped; its answer never came back.
    test.server.state().hang_after_part = Some(1);
    {
        let pipeline = test.pipeline(None);
        let mut uploading = entry.clone();
        let path = test.full_path(&entry);
        tokio::select! {
            _ = pipeline.upload(&mut uploading, &path, &PLAN) => panic!("the upload should have stopped"),
            _ = test.server.hung.notified() => {}
        }
    }
    let saved = test.ledger.find(&entry.root, &entry.path).unwrap();
    assert_eq!(saved.state, LedgerState::Uploading);
    assert!(saved.parts.is_empty());
    test.server.state().hang_after_part = None;
    let puts_before = test.server.puts();

    let (outcome, _) = run(&test, &saved, None).await;

    assert_eq!(outcome, UploadOutcome::Shelved);
    assert!(test.server.requests().contains(&format!(
        "GET /api/clips/videos/{}/upload/parts",
        saved.clip_id.unwrap()
    )));
    // Part 1 was in storage already, so only parts 2 and 3 were sent.
    assert_eq!(test.server.puts() - puts_before, 2);
    assert_eq!(test.server.clips().len(), 1);
}

#[tokio::test]
async fn an_upload_that_is_gone_deletes_its_clip_and_starts_again() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    test.server.state().complete_answers.push_back(problem(503, None, None));
    let saved = run(&test, &entry, None).await.1;
    let first_clip = saved.clip_id.unwrap();
    test.server.state().clips.get_mut(&first_clip).unwrap().upload_gone = true;

    let (outcome, again) = run(&test, &saved, None).await;

    assert_eq!(outcome, UploadOutcome::Shelved);
    assert!(test.server.clip(first_clip).deleted);
    assert_ne!(again.clip_id, Some(first_clip));
    assert_eq!(test.server.clips().len(), 2);
}

#[tokio::test]
async fn a_part_is_retried_after_one_two_four_and_eight_seconds_then_the_attempt_fails() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 500);
    test.server
        .state()
        .put_answers
        .extend([Some(500), None, Some(503), None, Some(502)]);

    let (outcome, saved) = run(&test, &entry, None).await;

    assert_eq!(outcome, UploadOutcome::RetryLater);
    assert_eq!(test.clock.delays(), [1, 2, 4, 8].map(Duration::from_secs));
    assert_eq!(saved.attempts, 1);
    assert_eq!(saved.state, LedgerState::Uploading);
    assert_eq!(
        saved.next_attempt_at,
        Some(test.clock.now() + TimeDelta::from_std(backoff(1)).unwrap())
    );

    // A part that fails then goes in carries on without using an attempt.
    test.server.state().put_answers.push_back(Some(500));
    test.clock.advance(Duration::from_secs(5 * 60));
    assert_eq!(run(&test, &saved, None).await.0, UploadOutcome::Shelved);
}

#[tokio::test]
async fn network_failures_back_off_up_to_five_minutes_and_fail_the_file_after_five_attempts() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 2500);
    test.server.state().api_down = true;
    let mut waits = Vec::new();

    let mut result = (UploadOutcome::RetryLater, entry.clone());
    for _ in 1..=MAX_ATTEMPTS {
        result = run(&test, &result.1, None).await;
        if let Some(next) = result.1.next_attempt_at {
            waits.push((next - test.clock.now()).to_std().unwrap());
            test.clock.set(next);
        }
    }

    assert_eq!(result.0, UploadOutcome::Failed);
    assert_eq!(result.1.state, LedgerState::Failed);
    assert_eq!(waits, [15, 30, 60, 120].map(Duration::from_secs));
    assert_eq!(backoff(9), Duration::from_secs(5 * 60));

    // A failed file gets one more try when the app next starts.
    assert_eq!(
        test.ledger
            .retry_failed_once(&entry.root, MAX_ATTEMPTS, test.clock.now()),
        1
    );
    test.server.state().api_down = false;
    let again = test.ledger.find(&entry.root, &entry.path).unwrap();
    assert_eq!(run(&test, &again, None).await.0, UploadOutcome::Shelved);
}

#[tokio::test]
async fn the_speed_limit_is_applied_to_part_bodies() {
    let test = TestUploader::new();
    let apex = test.add_book("Apex Legends");
    test.server.state().part_size = 400_000;
    let entry = test.waiting("Apex Legends/clip.mp4", apex.id, 1_000_000);
    // 2 Mbit/s: 250,000 bytes a second.
    let gate: Arc<dyn UploadGate> = Arc::new(SlowGate(TokenBucket::from_mbps(Some(2))));

    let (outcome, _) = run(&test, &entry, Some(gate)).await;

    assert_eq!(outcome, UploadOutcome::Shelved);
    // 1,000,000 bytes at 250,000 a second take four seconds.
    let waited: f64 = test.clock.delays().iter().map(Duration::as_secs_f64).sum();
    assert!((3.9..=4.1).contains(&waited), "{waited}");
}
