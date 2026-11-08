use std::collections::{BTreeMap, HashMap};
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use bytes::Bytes;
use chrono::{DateTime, TimeDelta, Utc};

use super::{TokenBucket, UploadGate};
use crate::api::{ApiError, PartBody, UploaderApi};
use crate::ledger::{Ledger, LedgerEntry};
use crate::platform::Clock;

/// Part URLs are asked for this many at a time.
pub const URL_BATCH: usize = 10;

/// A part is retried after these waits before the attempt fails.
pub const PART_RETRY_DELAYS: [Duration; 4] = [
    Duration::from_secs(1),
    Duration::from_secs(2),
    Duration::from_secs(4),
    Duration::from_secs(8),
];

/// A part URL this close to expiring is asked for again rather than risked.
const URL_MARGIN: TimeDelta = TimeDelta::minutes(5);

/// Part bodies are sent in reads of this size, so the speed limit is felt smoothly rather than in bursts.
const MAX_READ: usize = 64 * 1024;

/// Why uploading has to stop until the person does something.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StopReason {
    /// 401: the token was refused.
    SignedOut,
    /// 426: this version is too old.
    UpdateRequired,
}

impl StopReason {
    pub fn of(error: &ApiError) -> Option<StopReason> {
        match error.status() {
            Some(401) => Some(StopReason::SignedOut),
            Some(426) => Some(StopReason::UpdateRequired),
            _ => None,
        }
    }
}

/// How sending a clip's parts went.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PartsOutcome {
    /// Every part is in storage, with its ETag in the ledger.
    Uploaded,
    /// The upload is gone (expired, or its clip was deleted): start again.
    UploadGone,
    /// The network or a server failed after the part's retries: this attempt failed.
    Failed,
    /// The API refused the token or this version of the app; stop until that is dealt with.
    Stopped(StopReason),
}

/// Called after each part with the entry, the bytes sent so far and the file's size.
pub type ProgressFn = Arc<dyn Fn(&LedgerEntry, u64, u64) + Send + Sync>;

/// A part's byte range in the file: `start..end`.
pub fn part_range(entry: &LedgerEntry, number: u32) -> (u64, u64) {
    let part_size = entry.part_size.unwrap_or(entry.size);
    let start = u64::from(number.saturating_sub(1)) * part_size;
    (start, (start + part_size).min(entry.size))
}

fn sent_bytes(entry: &LedgerEntry) -> u64 {
    entry
        .parts
        .keys()
        .map(|&number| {
            let (start, end) = part_range(entry, number);
            end.saturating_sub(start)
        })
        .sum()
}

fn percent(entry: &LedgerEntry) -> u8 {
    (sent_bytes(entry) * 100)
        .checked_div(entry.size)
        .map_or(0, |percent| percent.min(100) as u8)
}

/// Sends a prepared clip's parts with the protocol `frontend/src/utils/s3MultipartUpload.ts` uses, one part at a time:
/// part URLs are asked for 10 at a time, each part is PUT to its presigned URL (never with the device token), retried
/// after 1, 2, 4 and 8 seconds before the attempt fails, and its ETag is saved in the ledger as soon as it is in, so a
/// restart resumes from there.
pub struct MultipartUploader<A> {
    api: Arc<A>,
    ledger: Arc<Ledger>,
    clock: Arc<dyn Clock>,
    bucket: Arc<TokenBucket>,
    gate: Arc<dyn UploadGate>,
    urls: Mutex<HashMap<u32, (String, DateTime<Utc>)>>,
    progress: Option<ProgressFn>,
}

impl<A: UploaderApi> MultipartUploader<A> {
    pub fn new(
        api: Arc<A>,
        ledger: Arc<Ledger>,
        clock: Arc<dyn Clock>,
        bucket: Arc<TokenBucket>,
        gate: Arc<dyn UploadGate>,
        progress: Option<ProgressFn>,
    ) -> Self {
        MultipartUploader {
            api,
            ledger,
            clock,
            bucket,
            gate,
            urls: Mutex::new(HashMap::new()),
            progress,
        }
    }

    fn urls(&self) -> std::sync::MutexGuard<'_, HashMap<u32, (String, DateTime<Utc>)>> {
        self.urls.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn report(&self, entry: &LedgerEntry) {
        if let Some(progress) = &self.progress {
            progress(entry, sent_bytes(entry), entry.size);
        }
    }

    /// Takes the parts storage already holds as sent, for an upload that was under way before a restart: the API's list
    /// is the truth, and a part of the wrong size is sent again.
    pub async fn reconcile(&self, entry: &mut LedgerEntry) -> PartsOutcome {
        let (Some(clip_id), Some(upload_id)) = (entry.clip_id, entry.upload_id.clone()) else {
            return PartsOutcome::UploadGone;
        };
        let listed = match self.api.list_parts(clip_id, &upload_id).await {
            Ok(parts) => parts,
            Err(error) if error.status() == Some(404) => return PartsOutcome::UploadGone,
            Err(error) => {
                return StopReason::of(&error).map_or(PartsOutcome::Failed, PartsOutcome::Stopped);
            }
        };

        let part_count = entry.part_count.unwrap_or(0);
        let mut held = BTreeMap::new();
        for part in listed {
            if part.part_number < 1 || part.part_number > part_count || part.etag.is_empty() {
                continue;
            }
            let (start, end) = part_range(entry, part.part_number);
            if part.size == end - start {
                held.insert(part.part_number, part.etag);
            }
        }

        entry.parts = held;
        entry.progress = percent(entry);
        entry.updated_at = self.clock.now();
        self.ledger.save(entry);
        PartsOutcome::Uploaded
    }

    pub async fn send_parts(&self, entry: &mut LedgerEntry, full_path: &Path) -> PartsOutcome {
        self.urls().clear();
        let part_count = entry.part_count.unwrap_or(0);
        let pending: Vec<u32> = (1..=part_count)
            .filter(|number| !entry.parts.contains_key(number))
            .collect();
        self.report(entry);

        for index in 0..pending.len() {
            // A clip already uploading finishes its part, then waits for the game like the rest.
            self.gate.wait_until_open().await;
            self.bucket.set_rate(self.gate.bytes_per_second());

            if let Some(stopped) = self
                .send_part(entry, full_path, pending[index], &pending[index..])
                .await
            {
                return stopped;
            }

            entry.progress = percent(entry);
            entry.updated_at = self.clock.now();
            self.ledger.save(entry);
            self.report(entry);
        }

        PartsOutcome::Uploaded
    }

    /// Sends one part, retrying it; None once it is in, else why the attempt stops.
    async fn send_part(
        &self,
        entry: &mut LedgerEntry,
        full_path: &Path,
        number: u32,
        upcoming: &[u32],
    ) -> Option<PartsOutcome> {
        for attempt in 0.. {
            let (url, refused) = self.part_url(entry, number, upcoming).await;
            if let Some(outcome) = refused
                && outcome != PartsOutcome::Failed
            {
                return Some(outcome);
            }

            if let Some(url) = url {
                let (start, end) = part_range(entry, number);
                let answer = match read_slice(full_path.to_path_buf(), start, end - start).await {
                    // The file went away or can't be read now; another try may find it.
                    None => None,
                    Some(bytes) => Some(self.api.put_part(&url, end - start, self.throttled(bytes)).await),
                };

                match answer {
                    Some(Ok(Some(etag))) if !etag.is_empty() => {
                        entry.parts.insert(number, etag);
                        return None;
                    }
                    // The upload itself is gone or the request is wrong; another try won't change that.
                    Some(Err(ApiError::Status(problem))) if problem.status == 404 || problem.status == 400 => {
                        return Some(PartsOutcome::UploadGone);
                    }
                    // An expired or refused URL is asked for afresh on the next try.
                    Some(Err(ApiError::Status(problem))) if problem.status == 403 => {
                        self.urls().remove(&number);
                    }
                    _ => {}
                }
            }

            let Some(&delay) = PART_RETRY_DELAYS.get(attempt) else {
                return Some(PartsOutcome::Failed);
            };
            self.clock.sleep(delay).await;
        }
        unreachable!("the retries end the loop")
    }

    /// The part's URL, asking for it with the next few parts' when there is none still fresh.
    async fn part_url(
        &self,
        entry: &LedgerEntry,
        number: u32,
        upcoming: &[u32],
    ) -> (Option<String>, Option<PartsOutcome>) {
        if let Some(cached) = self.fresh(number) {
            return (Some(cached), None);
        }

        let (Some(clip_id), Some(upload_id)) = (entry.clip_id, entry.upload_id.as_deref()) else {
            return (None, Some(PartsOutcome::UploadGone));
        };
        let mut batch: Vec<u32> = upcoming
            .iter()
            .copied()
            .filter(|&part| part == number || self.fresh(part).is_none())
            .take(URL_BATCH)
            .collect();
        if !batch.contains(&number) {
            batch.insert(0, number);
        }

        let presigned = match self.api.presign_parts(clip_id, upload_id, &batch).await {
            Ok(presigned) => presigned,
            Err(error) if matches!(error.status(), Some(404) | Some(400)) => {
                return (None, Some(PartsOutcome::UploadGone));
            }
            Err(error) => {
                return (
                    None,
                    Some(StopReason::of(&error).map_or(PartsOutcome::Failed, PartsOutcome::Stopped)),
                );
            }
        };

        let expires_at = presigned
            .expires_at
            .unwrap_or_else(|| self.clock.now() + TimeDelta::minutes(30));
        {
            let mut urls = self.urls();
            for part in presigned.parts {
                if !part.url.is_empty() {
                    urls.insert(part.part_number, (part.url, expires_at));
                }
            }
        }

        let url = self.fresh(number);
        let refused = url.is_none().then_some(PartsOutcome::Failed);
        (url, refused)
    }

    fn fresh(&self, number: u32) -> Option<String> {
        let now = self.clock.now();
        self.urls()
            .get(&number)
            .filter(|(_, expires_at)| *expires_at - now > URL_MARGIN)
            .map(|(url, _)| url.clone())
    }

    /// A part's bytes as a body that takes every read from the speed limit's bucket.
    fn throttled(&self, bytes: Bytes) -> PartBody {
        let bucket = self.bucket.clone();
        Box::pin(futures_util::stream::unfold(
            (bytes, bucket),
            |(mut rest, bucket)| async move {
                if rest.is_empty() {
                    return None;
                }
                let chunk = rest.split_to(rest.len().min(MAX_READ));
                bucket.take(chunk.len()).await;
                Some((Ok(chunk), (rest, bucket)))
            },
        ))
    }
}

/// `length` bytes of the file from `offset`, or None when it can't be read now.
async fn read_slice(path: PathBuf, offset: u64, length: u64) -> Option<Bytes> {
    tokio::task::spawn_blocking(move || {
        let mut file = File::open(&path).ok()?;
        file.seek(SeekFrom::Start(offset)).ok()?;
        let mut bytes = vec![0u8; usize::try_from(length).ok()?];
        file.read_exact(&mut bytes).ok()?;
        Some(Bytes::from(bytes))
    })
    .await
    .ok()
    .flatten()
}
