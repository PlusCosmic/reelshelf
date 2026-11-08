use std::path::Path;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::Local;

use super::{MultipartUploader, PartsOutcome, PlanLimits, StopReason};
use crate::api::{ApiError, PrepareClip, UploaderApi};
use crate::dates;
use crate::files;
use crate::ledger::{Ledger, LedgerEntry, LedgerState};
use crate::media;
use crate::platform::Clock;

/// The only upload protocol this version can upload with.
pub const MULTIPART_PROTOCOL: &str = "s3-multipart";

/// Tries per file before it fails until the next start.
pub const MAX_ATTEMPTS: u32 = 5;

/// The longest wait between tries.
pub const MAX_BACKOFF: Duration = Duration::from_secs(5 * 60);

/// A completion the API couldn't check just now (503) is asked again after this.
pub const CHECK_RETRY: Duration = Duration::from_secs(60);

/// The title the API gives a refused clip whose plan is full (`PlanLimitExceededException`).
pub const PLAN_LIMIT_TITLE: &str = "Plan Limit Reached";

/// What one go at uploading a file came to.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UploadOutcome {
    Shelved,
    Duplicate,
    TooLong,
    /// The plan has no room (403 at preparation); uploads pause until it has.
    PlanFull,
    /// Its book is no longer on the shelf; the file waits for one.
    NeedsBook,
    /// Failed this time; it waits for its retry (`next_attempt_at`).
    RetryLater,
    /// Out of attempts, or refused for good.
    Failed,
    /// The API asked for an upload protocol this version can't do; only a newer app can (T8).
    Unsupported,
    /// The token was refused (401); the file waits until the app is signed in again.
    SignedOut,
    /// The API refused this version (426); the file waits for an update.
    UpdateRequired,
}

/// The wait before the next try after `attempts` failed ones: 15 s doubling to 5 minutes.
pub fn backoff(attempts: u32) -> Duration {
    let doublings = attempts.saturating_sub(1).min(16);
    Duration::from_secs(15 * (1u64 << doublings)).min(MAX_BACKOFF)
}

/// The probe's length refusal (`TierLimits.LengthProblem`) rather than an unreadable file.
pub fn is_about_length(detail: Option<&str>) -> bool {
    detail.is_some_and(|detail| {
        let detail = detail.to_lowercase();
        detail.contains("your plan keeps clips up to") || detail.contains("this clip is")
    })
}

/// Uploads one file, one step after another: its length against the plan (from the `mvhd` box, MP4 and MOV only), its
/// MD5, preparing the clip, the multipart upload and completing it. Every answer the API can give is turned into the
/// file's next state in the ledger.
pub struct UploadPipeline<A> {
    api: Arc<A>,
    ledger: Arc<Ledger>,
    uploader: MultipartUploader<A>,
    clock: Arc<dyn Clock>,
    last_problem: Mutex<Option<String>>,
}

impl<A: UploaderApi> UploadPipeline<A> {
    pub fn new(api: Arc<A>, ledger: Arc<Ledger>, uploader: MultipartUploader<A>, clock: Arc<dyn Clock>) -> Self {
        UploadPipeline {
            api,
            ledger,
            uploader,
            clock,
            last_problem: Mutex::new(None),
        }
    }

    /// The detail of the last 422 or 403, for the log and S4.
    pub fn last_problem(&self) -> Option<String> {
        self.last_problem
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .clone()
    }

    fn set_problem(&self, problem: Option<String>) {
        *self
            .last_problem
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner()) = problem;
    }

    pub async fn upload(&self, entry: &mut LedgerEntry, full_path: &Path, plan: &PlanLimits) -> UploadOutcome {
        self.set_problem(None);

        // An upload under way before a restart carries on from the parts storage has.
        if entry.state == LedgerState::Uploading && entry.clip_id.is_some() && entry.upload_id.is_some() {
            match self.uploader.reconcile(entry).await {
                PartsOutcome::UploadGone => self.give_up_upload(entry).await,
                PartsOutcome::Stopped(reason) => return self.stopped(entry, reason),
                PartsOutcome::Failed => {
                    return self.retry_later(entry, backoff(entry.attempts + 1), true);
                }
                PartsOutcome::Uploaded => {}
            }
        }

        if entry.clip_id.is_none()
            && let Some(outcome) = self.prepare(entry, full_path, plan).await
        {
            return outcome;
        }

        match self.uploader.send_parts(entry, full_path).await {
            PartsOutcome::UploadGone => {
                self.give_up_upload(entry).await;
                return self.retry_later(entry, Duration::ZERO, true);
            }
            PartsOutcome::Stopped(reason) => return self.stopped(entry, reason),
            PartsOutcome::Failed => {
                return self.retry_later(entry, backoff(entry.attempts + 1), true);
            }
            PartsOutcome::Uploaded => {}
        }

        self.complete(entry).await
    }

    /// Checks, hashes and prepares the file; None once it has a clip to upload into.
    async fn prepare(&self, entry: &mut LedgerEntry, full_path: &Path, plan: &PlanLimits) -> Option<UploadOutcome> {
        let Some(category_id) = entry.category_id else {
            return Some(self.finish(entry, LedgerState::NeedsBook, UploadOutcome::NeedsBook, None));
        };

        if files::has_readable_length(&entry.path) {
            if entry.length_seconds.is_none() {
                entry.length_seconds = media::mp4_duration(full_path);
            }
            if entry.length_seconds.is_some_and(|length| plan.is_too_long(length)) {
                return Some(self.finish(entry, LedgerState::TooLong, UploadOutcome::TooLong, None));
            }
        }

        if entry.md5.is_none() {
            let path = full_path.to_path_buf();
            match tokio::task::spawn_blocking(move || media::md5_hex(&path)).await {
                Ok(Ok(md5)) => entry.md5 = Some(md5),
                Ok(Err(error)) => {
                    entry.last_error = Some(error.to_string());
                    return Some(self.retry_later(entry, backoff(entry.attempts + 1), true));
                }
                Err(error) => {
                    entry.last_error = Some(error.to_string());
                    return Some(self.retry_later(entry, backoff(entry.attempts + 1), true));
                }
            }
        }

        let request = PrepareClip {
            category_id,
            title: entry.title().to_string(),
            file_size: entry.size,
            created_at: dates::derive_created_at(entry.last_write, &entry.path, &Local),
            md5: entry.md5.clone().unwrap_or_default(),
        };
        let prepared = match self.api.prepare(&request).await {
            Ok(prepared) => prepared,
            Err(error) => return Some(self.prepare_refused(entry, error)),
        };

        let multipart = prepared
            .upload
            .multipart
            .filter(|_| prepared.upload.protocol == MULTIPART_PROTOCOL);
        let Some(multipart) = multipart else {
            // Only a newer app can upload there; give the reservation back.
            let _ = self.api.delete_clip(prepared.clip_id).await;
            let error = format!("The API asked for the {} upload protocol", prepared.upload.protocol);
            return Some(self.finish(entry, LedgerState::Failed, UploadOutcome::Unsupported, Some(error)));
        };

        entry.clip_id = Some(prepared.clip_id);
        entry.upload_id = Some(multipart.upload_id);
        entry.part_size = Some(multipart.part_size);
        entry.part_count = Some(multipart.part_count);
        entry.parts.clear();
        entry.progress = 0;
        entry.state = LedgerState::Uploading;
        entry.updated_at = self.clock.now();
        self.ledger.save(entry);
        None
    }

    fn prepare_refused(&self, entry: &mut LedgerEntry, error: ApiError) -> UploadOutcome {
        if let Some(reason) = StopReason::of(&error) {
            return self.stopped(entry, reason);
        }

        let problem = error.problem().cloned();
        match error.status() {
            Some(409) => {
                return self.finish(entry, LedgerState::Duplicate, UploadOutcome::Duplicate, None);
            }
            Some(403) if problem.as_ref().and_then(|p| p.title.as_deref()) == Some(PLAN_LIMIT_TITLE) => {
                let detail = problem.and_then(|p| p.detail);
                self.set_problem(detail.clone());
                return self.finish(entry, LedgerState::PlanFull, UploadOutcome::PlanFull, detail);
            }
            Some(404) => {
                // The book was taken off the shelf.
                entry.category_id = None;
                return self.finish(entry, LedgerState::NeedsBook, UploadOutcome::NeedsBook, None);
            }
            _ => {}
        }

        if error.is_transient() {
            return self.retry_later(entry, backoff(entry.attempts + 1), true);
        }

        let detail = problem
            .as_ref()
            .and_then(|p| p.detail.clone())
            .unwrap_or_else(|| format!("Preparing answered {}", error.status().unwrap_or_default()));
        self.finish(entry, LedgerState::Failed, UploadOutcome::Failed, Some(detail))
    }

    async fn complete(&self, entry: &mut LedgerEntry) -> UploadOutcome {
        let (Some(clip_id), Some(upload_id)) = (entry.clip_id, entry.upload_id.clone()) else {
            return self.retry_later(entry, Duration::ZERO, true);
        };
        let error = match self.api.complete(clip_id, &upload_id, &entry.parts).await {
            Ok(()) => {
                entry.shelved_at = Some(self.clock.now());
                entry.progress = 100;
                return self.finish(entry, LedgerState::Shelved, UploadOutcome::Shelved, None);
            }
            Err(error) => error,
        };

        if let Some(reason) = StopReason::of(&error) {
            return self.stopped(entry, reason);
        }

        match error.status() {
            Some(422) => {
                // The server's probe refused the file, and the clip with it.
                let detail = error.problem().and_then(|p| p.detail.clone());
                self.set_problem(detail.clone());
                entry.forget_upload();
                if is_about_length(detail.as_deref()) {
                    self.finish(entry, LedgerState::TooLong, UploadOutcome::TooLong, detail)
                } else {
                    self.finish(entry, LedgerState::Failed, UploadOutcome::Failed, detail)
                }
            }
            // The check couldn't finish; completing again picks up the joined file.
            Some(503) => self.retry_later(entry, CHECK_RETRY, false),
            Some(404) => {
                self.give_up_upload(entry).await;
                self.retry_later(entry, Duration::ZERO, true)
            }
            // 400: the parts don't match what storage has; they are listed again on the next try. Anything else
            // (the network, a 5xx) backs off too.
            _ => self.retry_later(entry, backoff(entry.attempts + 1), true),
        }
    }

    /// The upload can't go on: deletes the clip waiting on it, so the file is prepared afresh.
    async fn give_up_upload(&self, entry: &mut LedgerEntry) {
        if let Some(clip_id) = entry.clip_id {
            let _ = self.api.delete_clip(clip_id).await;
        }
        entry.forget_upload();
        entry.state = LedgerState::Waiting;
        entry.updated_at = self.clock.now();
        self.ledger.save(entry);
    }

    fn retry_later(&self, entry: &mut LedgerEntry, wait: Duration, count_attempt: bool) -> UploadOutcome {
        if count_attempt {
            entry.attempts += 1;
        }
        if entry.attempts >= MAX_ATTEMPTS {
            let error = entry.last_error.clone();
            return self.finish(entry, LedgerState::Failed, UploadOutcome::Failed, error);
        }

        let now = self.clock.now();
        entry.next_attempt_at = Some(now + chrono::TimeDelta::from_std(wait).unwrap_or_default());
        // A prepared clip keeps uploading from where it is; one not yet prepared waits its turn.
        entry.state = if entry.clip_id.is_none() {
            LedgerState::Waiting
        } else {
            LedgerState::Uploading
        };
        entry.updated_at = now;
        self.ledger.save(entry);
        UploadOutcome::RetryLater
    }

    /// Signed out or too old: the file stays as it is until the app can carry on.
    fn stopped(&self, entry: &mut LedgerEntry, reason: StopReason) -> UploadOutcome {
        entry.updated_at = self.clock.now();
        self.ledger.save(entry);
        match reason {
            StopReason::SignedOut => UploadOutcome::SignedOut,
            StopReason::UpdateRequired => UploadOutcome::UpdateRequired,
        }
    }

    fn finish(
        &self,
        entry: &mut LedgerEntry,
        state: LedgerState,
        outcome: UploadOutcome,
        error: Option<String>,
    ) -> UploadOutcome {
        entry.state = state;
        if error.is_some() {
            entry.last_error = error;
        }
        entry.next_attempt_at = None;
        entry.updated_at = self.clock.now();
        self.ledger.save(entry);
        outcome
    }
}
