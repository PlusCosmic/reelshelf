//! The engine's API ([`reelshelf_core::api::UploaderApi`]) on the real client ([`reelshelf_api::ApiClient`]).
//!
//! The core keeps its own plain types and errors so it builds and tests without HTTP; this maps between the two. It
//! lives in the app rather than in `reelshelf-api` because the orphan rule needs a type of the implementer's own to
//! join a trait from one crate to a type from another, and because that type also does app wiring: like the .NET
//! build's handler, it reads the device token from the [`CredentialStore`] before each call, so a token written by
//! sign-in or deleted by sign-out is the one the next request carries, with nothing to keep in step. `reelshelf-api`
//! stays the HTTP API alone, checked against the OpenAPI document.

use std::collections::BTreeMap;
use std::sync::Arc;

use reelshelf_api as http;
use reelshelf_core::api::{
    ApiError, ApiResult, DeviceAuthorization, MultipartTarget, PartBody, PrepareClip, PreparedClip, PresignedPart,
    PresignedParts, Problem, UploadTarget, UploadedPart, UploaderApi,
};
use reelshelf_core::matching::Book;
use reelshelf_core::platform::CredentialStore;
use reelshelf_core::uploads::PlanLimits;
use uuid::Uuid;

/// [`http::ApiClient`] as the engine calls it.
pub struct ReelshelfApi {
    client: http::ApiClient,
    credentials: Arc<dyn CredentialStore>,
}

impl ReelshelfApi {
    pub fn new(client: http::ApiClient, credentials: Arc<dyn CredentialStore>) -> Self {
        ReelshelfApi { client, credentials }
    }

    /// The client, with the stored token (or none) set for the next request.
    fn client(&self) -> &http::ApiClient {
        self.client.set_token(self.credentials.read_token());
        &self.client
    }
}

/// The core's error for the client's.
pub fn map_error(error: http::ApiError) -> ApiError {
    let status = |status: u16, problem: http::Problem| {
        ApiError::Status(Problem {
            status,
            title: problem.title,
            detail: problem.detail,
            error: None,
        })
    };
    match error {
        http::ApiError::SignedOut => ApiError::SignedOut,
        http::ApiError::UpdateRequired { .. } => ApiError::UpdateRequired,
        http::ApiError::Forbidden(problem) => status(403, problem),
        http::ApiError::NotFound(problem) => status(404, problem),
        http::ApiError::Conflict(problem) => status(409, problem),
        http::ApiError::Unprocessable(problem) => status(422, problem),
        http::ApiError::Unavailable(problem) => status(503, problem),
        http::ApiError::Status { status: code, problem } => status(code.as_u16(), problem),
        http::ApiError::Storage(code) => ApiError::Status(Problem::new(code.as_u16())),
        // Only a part's PUT answers this, and `put_part` turns it into `None` first.
        http::ApiError::MissingEtag => ApiError::Network("storage sent no ETag".to_owned()),
        http::ApiError::Network(error) => ApiError::Network(error.to_string()),
        // A success this version can't read: as if no answer came, so it is tried again later.
        http::ApiError::Decode(error) => ApiError::Network(format!("couldn't read the answer: {error}")),
    }
}

fn token_error(code: http::DeviceTokenErrorCode) -> ApiError {
    let code = match code {
        http::DeviceTokenErrorCode::AuthorizationPending => "authorization_pending".to_owned(),
        http::DeviceTokenErrorCode::SlowDown => "slow_down".to_owned(),
        http::DeviceTokenErrorCode::ExpiredToken => "expired_token".to_owned(),
        http::DeviceTokenErrorCode::AccessDenied => "access_denied".to_owned(),
        http::DeviceTokenErrorCode::Other(code) => code,
    };
    ApiError::Status(Problem {
        error: Some(code),
        ..Problem::new(400)
    })
}

pub fn book(category: http::GameCategory) -> Book {
    Book {
        id: category.id,
        name: category.name,
        slug: Some(category.slug).filter(|slug| !slug.is_empty()),
        cloth_color: category.cloth_color,
    }
}

pub fn plan_limits(plan: &http::AccountPlan) -> PlanLimits {
    PlanLimits {
        max_clip_seconds: plan.max_clip_seconds.map(i64::from),
        clip_limit: plan.clip_limit.map(i64::from),
        new_clip_limit: plan.new_clip_limit.map(i64::from),
        clip_count: i64::from(plan.clip_count),
        new_clips: i64::from(plan.new_clips_last30_days),
    }
}

impl UploaderApi for ReelshelfApi {
    async fn authorize(&self, device_name: &str) -> ApiResult<DeviceAuthorization> {
        let started = self.client().authorize_device(device_name).await.map_err(map_error)?;
        Ok(DeviceAuthorization {
            device_code: started.device_code,
            user_code: started.user_code,
            verification_uri: started.verification_uri,
            verification_uri_complete: started.verification_uri_complete,
            interval: Some(u64::from(started.interval)),
            expires_in: Some(u64::from(started.expires_in)),
        })
    }

    async fn poll_token(&self, device_code: &str) -> ApiResult<String> {
        match self.client().poll_device_token(device_code).await.map_err(map_error)? {
            http::DeviceTokenPoll::Granted(token) => Ok(token.access_token),
            http::DeviceTokenPoll::Refused(code) => Err(token_error(code)),
        }
    }

    async fn sign_out(&self) -> ApiResult<()> {
        self.client().sign_out_device().await.map_err(map_error)
    }

    async fn books(&self) -> ApiResult<Vec<Book>> {
        let categories = self.client().game_categories().await.map_err(map_error)?;
        Ok(categories.into_iter().map(book).collect())
    }

    async fn plan(&self) -> ApiResult<PlanLimits> {
        let plan = self.client().plan().await.map_err(map_error)?;
        Ok(plan_limits(&plan))
    }

    async fn prepare(&self, clip: &PrepareClip) -> ApiResult<PreparedClip> {
        let query = http::CreateVideoQuery {
            video_title: clip.title.clone(),
            file_size: clip.file_size,
            created_at: Some(clip.created_at),
            md5_hash: Some(clip.md5.clone()),
        };
        let created = self
            .client()
            .create_video(clip.category_id, &query)
            .await
            .map_err(map_error)?;
        Ok(PreparedClip {
            clip_id: created.clip_id,
            upload: UploadTarget {
                protocol: created.upload.protocol,
                multipart: created.upload.multipart.map(|multipart| MultipartTarget {
                    upload_id: multipart.upload_id,
                    part_size: multipart.part_size,
                    part_count: multipart.part_count,
                }),
            },
        })
    }

    async fn delete_clip(&self, clip_id: Uuid) -> ApiResult<()> {
        self.client().delete_clip(clip_id).await.map_err(map_error)
    }

    async fn presign_parts(&self, clip_id: Uuid, upload_id: &str, part_numbers: &[u32]) -> ApiResult<PresignedParts> {
        let request = http::PresignUploadPartsRequest {
            upload_id: upload_id.to_owned(),
            part_numbers: part_numbers.to_vec(),
        };
        let presigned = self
            .client()
            .presign_parts(clip_id, &request)
            .await
            .map_err(map_error)?;
        Ok(PresignedParts {
            expires_at: Some(presigned.expires_at),
            parts: presigned
                .parts
                .into_iter()
                .map(|part| PresignedPart {
                    part_number: part.part_number,
                    url: part.url,
                })
                .collect(),
        })
    }

    async fn list_parts(&self, clip_id: Uuid, upload_id: &str) -> ApiResult<Vec<UploadedPart>> {
        let uploaded = self.client().list_parts(clip_id, upload_id).await.map_err(map_error)?;
        Ok(uploaded
            .parts
            .into_iter()
            .map(|part| UploadedPart {
                part_number: part.part_number,
                etag: part.etag,
                size: part.size,
            })
            .collect())
    }

    async fn complete(&self, clip_id: Uuid, upload_id: &str, parts: &BTreeMap<u32, String>) -> ApiResult<()> {
        let request = http::CompleteUploadRequest {
            upload_id: upload_id.to_owned(),
            parts: parts
                .iter()
                .map(|(&part_number, etag)| http::CompletedUploadPart {
                    part_number,
                    etag: etag.clone(),
                })
                .collect(),
        };
        self.client()
            .complete_upload(clip_id, &request)
            .await
            .map(|_| ())
            .map_err(map_error)
    }

    async fn put_part(&self, url: &str, length: u64, body: PartBody) -> ApiResult<Option<String>> {
        // Presigned PUTs never carry the token; the client leaves it off them.
        match self
            .client
            .put_part(url, reqwest::Body::wrap_stream(body), length)
            .await
        {
            Ok(etag) => Ok(Some(etag)),
            Err(http::ApiError::MissingEtag) => Ok(None),
            Err(error) => Err(map_error(error)),
        }
    }
}
