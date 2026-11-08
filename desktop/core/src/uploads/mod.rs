//! Uploading one clip at a time: the length check, the MD5, preparing the clip, the multipart upload with one part in
//! flight, and completing it, with the speed limit and the full-screen wait.

mod bucket;
mod multipart;
mod pipeline;

pub use bucket::TokenBucket;
pub use multipart::{
    MultipartUploader, PART_RETRY_DELAYS, PartsOutcome, ProgressFn, StopReason, URL_BATCH, part_range,
};
pub use pipeline::{
    CHECK_RETRY, MAX_ATTEMPTS, MAX_BACKOFF, MULTIPART_PROTOCOL, PLAN_LIMIT_TITLE, UploadOutcome, UploadPipeline,
    backoff, is_about_length,
};

use crate::BoxFuture;

/// The account's plan as uploading needs it (`GET /api/me/plan`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct PlanLimits {
    /// `max_clip_seconds`; None for no limit.
    pub max_clip_seconds: Option<i64>,
    /// `clip_limit`; None for no limit.
    pub clip_limit: Option<i64>,
    /// `new_clip_limit`, clips that may be added in 30 days; None for no limit.
    pub new_clip_limit: Option<i64>,
    /// `clip_count`.
    pub clip_count: i64,
    /// `new_clips_last30_days`.
    pub new_clips: i64,
}

impl PlanLimits {
    /// The server lets a clip run a second over the plan's length.
    pub const LENGTH_TOLERANCE_SECONDS: f64 = 1.0;

    pub fn is_too_long(&self, seconds: f64) -> bool {
        self.max_clip_seconds
            .is_some_and(|max| seconds > max as f64 + Self::LENGTH_TOLERANCE_SECONDS)
    }

    /// Whether the plan has room for another clip now.
    pub fn has_room(&self) -> bool {
        self.clip_limit.is_none_or(|clips| self.clip_count < clips)
            && self.new_clip_limit.is_none_or(|monthly| self.new_clips < monthly)
    }

    /// The 30-day limit is what's full, not the library.
    pub fn monthly_limit_reached(&self) -> bool {
        self.new_clip_limit.is_some_and(|monthly| self.new_clips >= monthly)
            && self.clip_limit.is_none_or(|clips| self.clip_count < clips)
    }
}

/// When uploads may go, and how fast: the full-screen game rule, a pause, and the speed limit.
pub trait UploadGate: Send + Sync {
    /// Returns once uploading may carry on (straight away unless a game or a pause holds it).
    fn wait_until_open(&self) -> BoxFuture<'_, ()>;

    /// The speed for the next part, in bytes a second; None for no limit.
    fn bytes_per_second(&self) -> Option<u64>;
}

/// A gate that is always open, at full speed.
pub struct OpenGate;

impl UploadGate for OpenGate {
    fn wait_until_open(&self) -> BoxFuture<'_, ()> {
        Box::pin(async {})
    }

    fn bytes_per_second(&self) -> Option<u64> {
        None
    }
}
