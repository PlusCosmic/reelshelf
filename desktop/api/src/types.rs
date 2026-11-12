//! Request and response bodies, as the API's OpenAPI document (`frontend/src/api-client/Reelshelf.json`) describes
//! them: snake_case JSON, with nullable properties as `Option`. Each type names its schema; the contract test checks
//! them against the document.

use std::collections::HashMap;

use chrono::{DateTime, NaiveDate, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// `DeviceAuthorizeRequest`: the body of `POST /api/devices/authorize`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DeviceAuthorizeRequest {
    pub device_name: String,
    /// The app signing in (`monitor`); left out, the API takes the uploader.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub client: Option<String>,
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

/// `CurrentUserResponse`: the signed-in account, from `GET /api/me`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct CurrentUser {
    pub id: Uuid,
    pub username: String,
    pub global_name: Option<String>,
    pub avatar: Option<String>,
    pub email: Option<String>,
    pub suggested_email: Option<String>,
    pub needs_onboarding: bool,
    pub needs_username: bool,
    pub can_set_username: bool,
    pub is_admin: bool,
    pub color_scheme: String,
}

/// `FeatureFlagsResponse`: the known feature flags on for the account.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct FeatureFlags {
    pub enabled: Vec<String>,
}

impl FeatureFlags {
    /// The flag the ops endpoints sit behind.
    pub const OPS_MONITOR: &'static str = "ops-monitor";

    pub fn has(&self, key: &str) -> bool {
        self.enabled.iter().any(|flag| flag == key)
    }
}

/// `OpsStorageResponse`: clip storage as the database records it, and the store it is kept in.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsStorage {
    pub generated_at: DateTime<Utc>,
    pub store: OpsObjectStore,
    pub total: OpsStorageTotals,
    pub by_tier: Vec<OpsTierStorage>,
    pub top_accounts: Vec<OpsAccountStorage>,
    pub exports: OpsExportStorage,
}

/// `OpsObjectStore`: the configured object store, without credentials; every field but `configured` is null while
/// none is.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsObjectStore {
    pub configured: bool,
    /// `s3` (AWS S3 by region) or `s3-compatible` (an endpoint by its host).
    pub kind: Option<String>,
    pub region: Option<String>,
    pub endpoint_host: Option<String>,
    pub bucket: Option<String>,
    pub media_base_url: Option<String>,
}

/// `OpsStorageTotals`: every clip as the plan counts it, binned ones included, and the binned ones again.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsStorageTotals {
    pub clips: i64,
    pub bytes: i64,
    pub binned_clips: i64,
    pub binned_bytes: i64,
}

/// `OpsTierStorage`: the clips of the accounts on one tier.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsTierStorage {
    pub tier: String,
    /// Accounts on the tier holding at least one clip.
    pub accounts: i64,
    pub clips: i64,
    pub bytes: i64,
}

/// `OpsAccountStorage`: one of the accounts holding the most.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsAccountStorage {
    pub user_id: Uuid,
    pub username: String,
    pub tier: String,
    pub clips: i64,
    pub bytes: i64,
}

/// `OpsExportStorage`: over-limit exports held in storage and being made.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsExportStorage {
    pub held: i64,
    pub held_bytes: i64,
    pub pending: i64,
    pub running: i64,
}

/// `OpsQueueCounts`: one queue's numbers for one trigger (or, for transcoding, one job type).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsQueueCounts {
    pub name: String,
    /// Claimable now.
    pub pending: i64,
    /// Pending, waiting out the retry delay.
    pub retrying: i64,
    pub running: i64,
    /// Running past the queue's stale-after: their worker stopped.
    pub stale_running: i64,
    /// How long the oldest pending run has waited; 0 when none.
    pub oldest_pending_age_seconds: f64,
    pub last_hour: OpsFinishedCounts,
    pub last_day: OpsFinishedCounts,
}

/// `OpsFinishedCounts`: runs that finished in a window; exhausted ones used up their attempts.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct OpsFinishedCounts {
    pub succeeded: i64,
    pub failed: i64,
    pub exhausted: i64,
}

/// `OpsTranscodingResponse`: the transcode queue.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsTranscoding {
    pub generated_at: DateTime<Utc>,
    pub types: Vec<OpsQueueCounts>,
    pub recent_failures: Vec<OpsTranscodeFailure>,
    pub overflow: OpsOverflow,
    pub aac_backfill: OpsAacBackfill,
}

/// `OpsTranscodeFailure`: a failed job, newest first.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsTranscodeFailure {
    pub run_id: Uuid,
    pub clip_id: Uuid,
    #[serde(rename = "type")]
    pub job_type: String,
    pub error: Option<String>,
    pub attempts: i32,
    pub finished_at: Option<DateTime<Utc>>,
}

/// `OpsOverflow`: the overflow pools in use, as the scaler last counted them.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsOverflow {
    pub enabled: bool,
    pub max_tasks: i32,
    /// Every counted pool's tasks together; none while no pool has a count.
    pub running: Option<i64>,
    pub pools: Vec<OpsOverflowPool>,
}

/// `OpsOverflowPool`: one pool's cap and tasks.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsOverflowPool {
    pub name: String,
    pub max_tasks: i32,
    /// None before the pool's first count, after one failed, or on an instance not scaling.
    pub running: Option<i64>,
}

/// `OpsAacBackfill`: whether the AAC backfill runs, and the clips still carrying Opus audio.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsAacBackfill {
    pub enabled: bool,
    pub remaining: i64,
}

/// `OpsAiResponse`: the AI run queues and spend by model.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsAi {
    pub generated_at: DateTime<Utc>,
    pub queues: Vec<OpsAiQueue>,
    pub spend: Vec<OpsAiSpend>,
}

/// `OpsAiQueue`: one run queue by trigger, with its latest failures.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsAiQueue {
    /// `legend_detection`, `clip_transcription` or `clip_summary`.
    pub queue: String,
    pub triggers: Vec<OpsQueueCounts>,
    pub recent_failures: Vec<OpsAiFailure>,
}

/// `OpsAiFailure`: a failed run, newest first.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct OpsAiFailure {
    pub run_id: Uuid,
    pub clip_id: Uuid,
    pub trigger: String,
    pub provider: Option<String>,
    pub model: String,
    pub error: Option<String>,
    pub attempts: i32,
    pub completed_at: Option<DateTime<Utc>>,
}

/// `OpsAiSpend`: one model's runs over the last day and the last 30 days.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsAiSpend {
    pub queue: String,
    pub provider: Option<String>,
    pub model: String,
    pub last_day: OpsAiUsage,
    pub last_month: OpsAiUsage,
}

/// `OpsAiUsage`: runs, tokens and what they cost.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsAiUsage {
    pub runs: i64,
    pub input_tokens: i64,
    pub output_tokens: i64,
    /// The cost of the runs whose cost is known; none when none is.
    pub cost_usd: Option<f64>,
    /// Runs whose cost isn't known, left out of `cost_usd`.
    pub unpriced_runs: i64,
}

/// `OpsCostsResponse`: each provider's spend (and Stripe's revenue) as the API last read it.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsCosts {
    pub generated_at: DateTime<Utc>,
    /// Every cost provider's month-to-date spend reported in USD, added up.
    pub month_to_date_usd: f64,
    pub providers: Vec<OpsCostProvider>,
}

/// `OpsCostProvider`: one provider's numbers in its own currency, and how its last read went.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsCostProvider {
    /// `aws`, `openrouter`, `openai`, `anthropic` or `stripe`.
    pub provider: String,
    /// `cost`, or `revenue` (Stripe).
    pub kind: String,
    /// `ok`, `not_configured`, `pending` or `failed`.
    pub status: String,
    pub error: Option<String>,
    /// When the numbers shown were read.
    pub last_success_at: Option<DateTime<Utc>>,
    pub currency: Option<String>,
    pub month_to_date: Option<f64>,
    /// None when the provider keeps too little history to say.
    pub last_month: Option<f64>,
    /// The last 30 days the provider reports, oldest first.
    pub daily: Vec<OpsCostDay>,
    /// The last 30 days by service or model, largest first.
    pub breakdown: Vec<OpsCostItem>,
    /// Credit left (OpenRouter).
    pub balance: Option<f64>,
    pub revenue: Option<OpsRevenue>,
}

impl OpsCostProvider {
    pub fn is_revenue(&self) -> bool {
        self.kind == "revenue"
    }
}

/// `OpsCostDay`: one UTC day's amount.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsCostDay {
    pub date: NaiveDate,
    pub amount: f64,
}

/// `OpsCostItem`: a service or model and what it cost over the last 30 days.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsCostItem {
    pub name: String,
    pub amount: f64,
}

/// `OpsRevenue`: Stripe's gross charges and net after fees and refunds, this month and last.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct OpsRevenue {
    pub gross_month_to_date: f64,
    pub net_month_to_date: f64,
    pub gross_last_month: f64,
    pub net_last_month: f64,
}

/// The query of `FindAdminAccounts` (`GET /api/admin/accounts`).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct FindAdminAccountsQuery {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub query: Option<String>,
}

/// `AdminAccount`: an account as the admin pages list it.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AdminAccount {
    pub id: Uuid,
    pub username: String,
    pub global_name: Option<String>,
    pub avatar_url: Option<String>,
    pub role: String,
    pub granted_tier: Option<String>,
    pub suspended_at: Option<DateTime<Utc>>,
    pub identities: Vec<AdminAccountIdentity>,
}

/// `AdminAccountIdentity`: one of an account's sign-ins.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AdminAccountIdentity {
    pub provider: String,
    pub provider_user_id: String,
    pub username: String,
}

/// The query of `ListContentReports` (`GET /api/admin/reports`).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ListContentReportsQuery {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub resolved: Option<bool>,
}

/// `AdminContentReport`: a content report, as admins review it.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AdminContentReport {
    pub id: Uuid,
    pub reason: String,
    pub details: String,
    pub reporter_email: Option<String>,
    pub status: String,
    pub created_at: DateTime<Utc>,
    pub resolved_at: Option<DateTime<Utc>>,
    pub resolved_by_name: Option<String>,
    pub resolution_note: Option<String>,
    pub clip_id: Option<Uuid>,
    pub clip_title: Option<String>,
    pub owner_id: Option<Uuid>,
    pub owner_name: Option<String>,
    pub share_url: Option<String>,
    pub share_hidden: bool,
    pub sharing_blocked: bool,
    pub open_reports_on_clip: i32,
    pub owner_suspended: bool,
    pub evidence: Option<AdminContentReportEvidence>,
}

/// `AdminContentReportEvidence`: the evidence kept for a removed clip.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AdminContentReportEvidence {
    pub status: String,
    pub error: Option<String>,
    pub cdn_purge: Option<String>,
    pub delete_after: DateTime<Utc>,
}
