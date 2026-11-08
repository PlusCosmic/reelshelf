//! The API calls the uploader makes, as the core needs them. The `reelshelf-api` crate implements [`UploaderApi`] over
//! HTTP (the device token as `Authorization: Bearer`, `X-Reelshelf-Client: uploader/<version>`, and its own `serde`
//! types checked against the OpenAPI document); tests implement it with a fake.

use std::collections::BTreeMap;
use std::future::Future;
use std::io;
use std::pin::Pin;

use bytes::Bytes;
use chrono::{DateTime, Utc};
use futures_core::Stream;
use uuid::Uuid;

use crate::matching::Book;
use crate::uploads::PlanLimits;

/// What a call answered when it didn't succeed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ApiError {
    /// No answer: the network failed or the request timed out.
    Network(String),
    /// 401 on a request that carried the device token: it was revoked, or the account can't sign in.
    SignedOut,
    /// 426: this version of the app is older than the API serves.
    UpdateRequired,
    /// Any other status, with what its body said.
    Status(Problem),
}

/// A refusal's status and its body: a problem's `title` and `detail`, or an RFC 8628 `{ error }`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Problem {
    pub status: u16,
    pub title: Option<String>,
    pub detail: Option<String>,
    pub error: Option<String>,
}

impl Problem {
    pub fn new(status: u16) -> Self {
        Problem {
            status,
            ..Problem::default()
        }
    }
}

impl ApiError {
    /// The HTTP status, when there was an answer.
    pub fn status(&self) -> Option<u16> {
        match self {
            ApiError::Network(_) => None,
            ApiError::SignedOut => Some(401),
            ApiError::UpdateRequired => Some(426),
            ApiError::Status(problem) => Some(problem.status),
        }
    }

    pub fn problem(&self) -> Option<&Problem> {
        match self {
            ApiError::Status(problem) => Some(problem),
            _ => None,
        }
    }

    /// The network failed, or the server did (5xx or 429): worth trying again later.
    pub fn is_transient(&self) -> bool {
        match self.status() {
            None => true,
            Some(status) => status >= 500 || status == 429,
        }
    }
}

pub type ApiResult<T> = Result<T, ApiError>;

/// `POST /api/devices/authorize`: a sign-in waiting for its owner.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DeviceAuthorization {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub verification_uri_complete: String,
    /// Seconds between polls.
    pub interval: Option<u64>,
    /// Seconds until the code expires.
    pub expires_in: Option<u64>,
}

/// `POST /api/clips/categories/{id}/videos` (`CreateVideo`): what preparing a clip takes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PrepareClip {
    pub category_id: Uuid,
    /// `videoTitle`: the file name without its extension.
    pub title: String,
    /// `fileSize`, in bytes.
    pub file_size: u64,
    /// `createdAt`.
    pub created_at: DateTime<Utc>,
    /// `md5Hash`, lowercase hex.
    pub md5: String,
}

/// The prepared clip and where its upload goes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PreparedClip {
    pub clip_id: Uuid,
    pub upload: UploadTarget,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UploadTarget {
    /// `s3-multipart` is the only protocol this version can upload with.
    pub protocol: String,
    pub multipart: Option<MultipartTarget>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MultipartTarget {
    pub upload_id: String,
    pub part_size: u64,
    pub part_count: u32,
}

/// `POST /api/clips/videos/{id}/upload/parts`: presigned PUT URLs.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PresignedParts {
    pub expires_at: Option<DateTime<Utc>>,
    pub parts: Vec<PresignedPart>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PresignedPart {
    pub part_number: u32,
    pub url: String,
}

/// `GET /api/clips/videos/{id}/upload/parts`: one part storage already holds.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UploadedPart {
    pub part_number: u32,
    pub etag: String,
    pub size: u64,
}

/// A part's bytes, read and paced by the speed limit as the request sends them.
pub type PartBody = Pin<Box<dyn Stream<Item = io::Result<Bytes>> + Send>>;

/// Every call the uploader makes. 401 on a call that carried a token is [`ApiError::SignedOut`] and 426 is
/// [`ApiError::UpdateRequired`]; every other refusal keeps its status (403, 404, 409, 422 and 503 each mean something
/// different to the pipeline).
pub trait UploaderApi: Send + Sync + 'static {
    /// `POST /api/devices/authorize` with `{ device_name }` (no token).
    fn authorize(&self, device_name: &str) -> impl Future<Output = ApiResult<DeviceAuthorization>> + Send;

    /// `POST /api/devices/token` with `{ device_code }`: the token once the sign-in is approved, else 400 with an RFC
    /// 8628 `error` (`authorization_pending`, `slow_down`, `access_denied` or `expired_token`).
    fn poll_token(&self, device_code: &str) -> impl Future<Output = ApiResult<String>> + Send;

    /// `DELETE /api/devices/current`: S5's Sign out revokes this device's own token.
    fn sign_out(&self) -> impl Future<Output = ApiResult<()>> + Send;

    /// `GET /api/games/categories`: the account's books.
    fn books(&self) -> impl Future<Output = ApiResult<Vec<Book>>> + Send;

    /// `GET /api/me/plan`.
    fn plan(&self) -> impl Future<Output = ApiResult<PlanLimits>> + Send;

    /// `CreateVideo`: reserves a clip for a file and answers where its upload goes.
    fn prepare(&self, clip: &PrepareClip) -> impl Future<Output = ApiResult<PreparedClip>> + Send;

    /// `DELETE /api/clips/videos/{id}`: gives back a clip still waiting for its upload.
    fn delete_clip(&self, clip_id: Uuid) -> impl Future<Output = ApiResult<()>> + Send;

    /// `POST .../upload/parts`: URLs for up to 100 parts.
    fn presign_parts(
        &self,
        clip_id: Uuid,
        upload_id: &str,
        part_numbers: &[u32],
    ) -> impl Future<Output = ApiResult<PresignedParts>> + Send;

    /// `GET .../upload/parts?uploadId=`: the parts storage holds, for resuming.
    fn list_parts(&self, clip_id: Uuid, upload_id: &str) -> impl Future<Output = ApiResult<Vec<UploadedPart>>> + Send;

    /// `POST .../upload/complete` with every part's ETag, by part number.
    fn complete(
        &self,
        clip_id: Uuid,
        upload_id: &str,
        parts: &BTreeMap<u32, String>,
    ) -> impl Future<Output = ApiResult<()>> + Send;

    /// PUTs a part to its presigned URL, never with the device token. Answers the part's `ETag` header on a 2xx (None
    /// when storage sent none), [`ApiError::Status`] for any other answer and [`ApiError::Network`] for none.
    fn put_part(
        &self,
        url: &str,
        length: u64,
        body: PartBody,
    ) -> impl Future<Output = ApiResult<Option<String>>> + Send;
}
