//! Request and response bodies, as the API's OpenAPI document (`frontend/src/api-client/Reelshelf.json`) describes
//! them: snake_case JSON, with nullable properties as `Option`. Each type names its schema; the contract test checks
//! them against the document.

use std::collections::HashMap;

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// `DeviceAuthorizeRequest`: the body of `POST /api/devices/authorize`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceAuthorizeRequest {
    pub device_name: String,
}

/// `DeviceAuthorizationResponse`: a started sign-in, with the code to show and the one to poll with.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceAuthorization {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub verification_uri_complete: String,
    /// Seconds between polls.
    pub interval: u32,
    /// Seconds until the codes expire.
    pub expires_in: u32,
}

/// `DeviceTokenRequest`: the body of `POST /api/devices/token`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceTokenRequest {
    pub device_code: String,
}

/// `DeviceTokenResponse`: the device's token, once its owner allowed it.
#[derive(Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceToken {
    pub access_token: String,
    pub token_type: String,
}

impl std::fmt::Debug for DeviceToken {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        // Never put the token in a log line.
        f.debug_struct("DeviceToken")
            .field("access_token", &"<redacted>")
            .field("token_type", &self.token_type)
            .finish()
    }
}

/// `DeviceTokenError`: the RFC 8628 error `POST /api/devices/token` answers as a 400.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceTokenErrorBody {
    pub error: String,
}

/// The RFC 8628 error codes the token poll answers before (or instead of) a token.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DeviceTokenErrorCode {
    /// The owner hasn't answered yet: poll again after the interval.
    AuthorizationPending,
    /// Polled too soon: wait 5 seconds longer between polls from now on.
    SlowDown,
    /// The codes expired, or were used already.
    ExpiredToken,
    /// The owner turned the sign-in down.
    AccessDenied,
    /// A code this client doesn't know.
    Other(String),
}

impl DeviceTokenErrorCode {
    pub fn parse(code: &str) -> Self {
        match code {
            "authorization_pending" => Self::AuthorizationPending,
            "slow_down" => Self::SlowDown,
            "expired_token" => Self::ExpiredToken,
            "access_denied" => Self::AccessDenied,
            other => Self::Other(other.to_owned()),
        }
    }
}

/// What one poll for the device's token answered.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DeviceTokenPoll {
    Granted(DeviceToken),
    Refused(DeviceTokenErrorCode),
}

/// `GameCategoryResponse`: one of the account's books.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct GameCategory {
    pub id: Uuid,
    pub name: String,
    pub slug: String,
    pub cover_url: Option<String>,
    pub key_art_url: Option<String>,
    pub game_logo_url: Option<String>,
    pub is_custom: bool,
    pub cloth_color: Option<String>,
}

/// `AccountPlanResponse`: the account's plan, what it keeps and how much is used.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AccountPlan {
    pub tier: String,
    pub clip_count: i32,
    pub clip_limit: Option<i32>,
    pub new_clips_last30_days: i32,
    pub new_clip_limit: Option<i32>,
    /// The longest clip the plan keeps, in seconds; none for no limit.
    pub max_clip_seconds: Option<i32>,
    pub storage_used_bytes: i64,
    pub has_transcripts: bool,
    pub billing_enabled: bool,
    pub can_manage_billing: bool,
    pub subscription: Option<PlanSubscription>,
    pub clips_over_limit: i32,
    pub over_limit_deletes_at: Option<DateTime<Utc>>,
    pub free_clip_limit: Option<i32>,
    pub over_limit_grace_days: Option<i32>,
    pub can_change_plan: bool,
    pub granted_tier: Option<String>,
}

/// `PlanSubscription`: the account's subscription, when it has one.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PlanSubscription {
    pub status: String,
    pub tier: Option<String>,
    pub interval: Option<String>,
    pub current_period_end: Option<DateTime<Utc>>,
    pub cancel_at_period_end: bool,
    pub cancel_at: Option<DateTime<Utc>>,
    pub ended_at: Option<DateTime<Utc>>,
    pub lookup_key: Option<String>,
    pub scheduled_tier: Option<String>,
    pub scheduled_interval: Option<String>,
    pub scheduled_at: Option<DateTime<Utc>>,
}

/// The query of `CreateVideo` (`POST /api/clips/categories/{categoryId}/videos`), in the document's camelCase.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateVideoQuery {
    pub video_title: String,
    pub file_size: u64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub created_at: Option<DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub md5_hash: Option<String>,
}

/// `CreateClipResponse`: the reserved clip and where its upload goes.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CreateClipResponse {
    pub clip_id: Uuid,
    pub video_id: Uuid,
    pub upload: UploadTarget,
}

/// `UploadTarget`: how to upload a reserved clip's file.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct UploadTarget {
    /// `s3-multipart` for R2; the uploader takes nothing else.
    pub protocol: String,
    pub endpoint: String,
    pub headers: HashMap<String, String>,
    pub metadata: HashMap<String, String>,
    pub expires_at: DateTime<Utc>,
    #[serde(default)]
    pub multipart: Option<MultipartUploadInfo>,
}

impl UploadTarget {
    /// The protocol the uploader speaks.
    pub const S3_MULTIPART: &'static str = "s3-multipart";
}

/// `MultipartUploadInfo`: the multipart upload started for the clip.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct MultipartUploadInfo {
    pub upload_id: String,
    pub part_size: u64,
    pub part_count: u32,
}

/// `PresignUploadPartsRequest`: the body of `POST .../upload/parts`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PresignUploadPartsRequest {
    pub upload_id: String,
    pub part_numbers: Vec<u32>,
}

/// `PresignedUploadParts`: presigned PUT URLs for the parts asked for.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PresignedUploadParts {
    pub parts: Vec<PresignedUploadPart>,
    pub expires_at: DateTime<Utc>,
}

/// `PresignedUploadPart`: one part's presigned PUT URL.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PresignedUploadPart {
    pub part_number: u32,
    pub url: String,
}

/// The query of `ListUploadParts` (`GET .../upload/parts`).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ListUploadPartsQuery {
    pub upload_id: String,
}

/// `UploadedParts`: the parts storage holds for an upload.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct UploadedParts {
    pub parts: Vec<UploadedPart>,
}

/// `UploadedPart`: one part storage holds.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct UploadedPart {
    pub part_number: u32,
    pub etag: String,
    pub size: u64,
}

/// `CompleteUploadRequest`: the body of `POST .../upload/complete`, every part with its ETag.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CompleteUploadRequest {
    pub upload_id: String,
    pub parts: Vec<CompletedUploadPart>,
}

/// `CompletedUploadPart`: one uploaded part and its ETag.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CompletedUploadPart {
    pub part_number: u32,
    pub etag: String,
}

/// `CompletedUpload`: the clip once its upload is complete.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CompletedUpload {
    pub clip_id: Uuid,
    pub status: ClipVideoStatus,
}

/// `ClipVideoStatus`: where a clip's video is.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ClipVideoStatus {
    AwaitingUpload,
    Uploaded,
    Processing,
    Ready,
    EncodeFailed,
    UploadFailed,
    /// A status newer than this client.
    #[serde(other)]
    Unknown,
}
