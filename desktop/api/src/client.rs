//! The client: one async method per endpoint the uploader calls.

use std::sync::RwLock;
use std::time::Duration;

use reqwest::header::{CONTENT_LENGTH, ETAG, HeaderValue};
use reqwest::{Body, RequestBuilder, StatusCode};
use serde::de::DeserializeOwned;
use uuid::Uuid;

use crate::endpoints::{self, Endpoint};
use crate::error::ApiError;
use crate::types::{
    AccountPlan, AdminAccount, AdminAccountDetail, AdminContentReport, CompleteUploadRequest, CompletedUpload,
    CreateClipResponse, CreateVideoQuery, CurrentUser, DeviceAuthorization, DeviceAuthorizeRequest,
    DeviceTokenErrorBody, DeviceTokenErrorCode, DeviceTokenPoll, DeviceTokenRequest, FeatureFlags,
    FindAdminAccountsQuery, GameCategory, ListContentReportsQuery, ListUploadPartsQuery, OpsAi, OpsCosts, OpsStorage,
    OpsTranscoding, PresignUploadPartsRequest, PresignedUploadParts, ResolveContentReportRequest,
    SetAccountRoleRequest, SetAccountSuspensionRequest, SetGrantedTierRequest, UploadedParts,
};

/// The header naming the app and its version, which the API's version gate reads.
pub const CLIENT_HEADER: &str = "X-Reelshelf-Client";

/// Which app a client calls as: the name `X-Reelshelf-Client` and a device sign-in's `client` carry.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClientApp {
    /// The Windows uploader, the default the API assumes when a sign-in names no app.
    Uploader,
    /// The macOS monitor app, whose device token alone reaches the admin and ops endpoints.
    Monitor,
}

impl ClientApp {
    pub fn name(self) -> &'static str {
        match self {
            ClientApp::Uploader => "uploader",
            ClientApp::Monitor => "monitor",
        }
    }
}

/// How long an API call may take.
const API_TIMEOUT: Duration = Duration::from_secs(120);
/// How long one part's PUT may take, slowed by the speed limit as it may be.
const PART_TIMEOUT: Duration = Duration::from_secs(600);
const CONNECT_TIMEOUT: Duration = Duration::from_secs(30);

/// Calls the Reelshelf API as the uploader (or, made with [`ApiClient::for_app`], another app). Every API request
/// carries `X-Reelshelf-Client: <app>/<version>` and, while there is one, `Authorization: Bearer <token>`. Part PUTs to
/// presigned URLs carry neither.
pub struct ApiClient {
    http: reqwest::Client,
    app: ClientApp,
    base_url: String,
    client_name: HeaderValue,
    token: RwLock<Option<String>>,
}

impl std::fmt::Debug for ApiClient {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ApiClient")
            .field("base_url", &self.base_url)
            .field("client_name", &self.client_name)
            .field("signed_in", &self.token().is_some())
            .finish()
    }
}

impl ApiClient {
    /// A client for the API at `base_url` (such as `https://reelshelf.app`), as uploader `version` (such as `1.0.3`).
    pub fn new(base_url: &str, version: &str) -> Result<Self, ApiError> {
        let http = reqwest::Client::builder().connect_timeout(CONNECT_TIMEOUT).build()?;
        Ok(Self::with_http(http, base_url, version))
    }

    /// The same, over a `reqwest::Client` the caller built.
    ///
    /// # Panics
    ///
    /// When `version` can't go in a header (it holds a control character).
    pub fn with_http(http: reqwest::Client, base_url: &str, version: &str) -> Self {
        Self::for_app(http, ClientApp::Uploader, base_url, version)
    }

    /// A client calling as `app`.
    ///
    /// # Panics
    ///
    /// When `version` can't go in a header (it holds a control character).
    pub fn for_app(http: reqwest::Client, app: ClientApp, base_url: &str, version: &str) -> Self {
        let client_name = HeaderValue::from_str(&format!("{}/{version}", app.name()))
            .expect("the app's version is a valid header value");
        Self {
            http,
            app,
            base_url: base_url.trim_end_matches('/').to_owned(),
            client_name,
            token: RwLock::new(None),
        }
    }

    /// Sets the device token to send, or none while signed out.
    pub fn set_token(&self, token: Option<String>) {
        *self.token.write().unwrap_or_else(|poisoned| poisoned.into_inner()) = token.filter(|token| !token.is_empty());
    }

    /// The device token being sent.
    pub fn token(&self) -> Option<String> {
        self.token
            .read()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .clone()
    }

    /// `POST /api/devices/authorize`: starts a sign-in for this device.
    pub async fn authorize_device(&self, device_name: &str) -> Result<DeviceAuthorization, ApiError> {
        // The uploader names no app, as it always has; the API takes that as the uploader.
        let body = DeviceAuthorizeRequest {
            device_name: device_name.to_owned(),
            client: (self.app != ClientApp::Uploader).then(|| self.app.name().to_owned()),
        };
        self.call_json(&endpoints::AUTHORIZE_DEVICE, &[], |request| request.json(&body))
            .await
    }

    /// `POST /api/devices/token`: one poll for the device's token. An RFC 8628 error is an answer, not a failure.
    pub async fn poll_device_token(&self, device_code: &str) -> Result<DeviceTokenPoll, ApiError> {
        let body = DeviceTokenRequest {
            device_code: device_code.to_owned(),
        };
        let (status, bytes, token_sent) = self
            .send(&endpoints::GET_DEVICE_TOKEN, &[], |request| request.json(&body))
            .await?;
        if status.is_success() {
            return Ok(DeviceTokenPoll::Granted(serde_json::from_slice(&bytes)?));
        }
        if status == StatusCode::BAD_REQUEST
            && let Ok(error) = serde_json::from_slice::<DeviceTokenErrorBody>(&bytes)
        {
            return Ok(DeviceTokenPoll::Refused(DeviceTokenErrorCode::parse(&error.error)));
        }
        Err(ApiError::from_response(status, &bytes, token_sent))
    }

    /// `DELETE /api/devices/current`: signs this device out by revoking its token.
    pub async fn sign_out_device(&self) -> Result<(), ApiError> {
        self.call(&endpoints::SIGN_OUT_CURRENT_DEVICE, &[], |request| request)
            .await
    }

    /// `GET /api/games/categories`: the account's books.
    pub async fn game_categories(&self) -> Result<Vec<GameCategory>, ApiError> {
        self.call_json(&endpoints::GET_GAME_CATEGORIES, &[], |request| request)
            .await
    }

    /// `GET /api/me/plan`: the account's plan.
    pub async fn plan(&self) -> Result<AccountPlan, ApiError> {
        self.call_json(&endpoints::GET_MY_PLAN, &[], |request| request).await
    }

    /// `GET /api/me`: the signed-in account.
    pub async fn me(&self) -> Result<CurrentUser, ApiError> {
        self.call_json(&endpoints::GET_ME, &[], |request| request).await
    }

    /// `GET /api/me/features`: the feature flags on for the account.
    pub async fn features(&self) -> Result<FeatureFlags, ApiError> {
        self.call_json(&endpoints::GET_MY_FEATURES, &[], |request| request)
            .await
    }

    /// `GET /api/admin/ops/storage`: clips and bytes by tier and account, exports, and the object store.
    pub async fn ops_storage(&self) -> Result<OpsStorage, ApiError> {
        self.call_json(&endpoints::GET_OPS_STORAGE, &[], |request| request)
            .await
    }

    /// `GET /api/admin/ops/transcoding`: the transcode queue by job type, its failures, the overflow pools and the
    /// AAC backfill.
    pub async fn ops_transcoding(&self) -> Result<OpsTranscoding, ApiError> {
        self.call_json(&endpoints::GET_OPS_TRANSCODING, &[], |request| request)
            .await
    }

    /// `GET /api/admin/ops/ai`: the AI run queues, their failures and spend by model.
    pub async fn ops_ai(&self) -> Result<OpsAi, ApiError> {
        self.call_json(&endpoints::GET_OPS_AI, &[], |request| request).await
    }

    /// `GET /api/admin/ops/costs`: spend by provider, as the API last read it, and Stripe's revenue.
    pub async fn ops_costs(&self) -> Result<OpsCosts, ApiError> {
        self.call_json(&endpoints::GET_OPS_COSTS, &[], |request| request).await
    }

    /// `GET /api/admin/accounts?query=`: accounts matching a name or id (the most recent ones without a query).
    pub async fn find_accounts(&self, query: Option<&str>) -> Result<Vec<AdminAccount>, ApiError> {
        let query = FindAdminAccountsQuery {
            query: query.map(str::to_owned),
        };
        self.call_json(&endpoints::FIND_ADMIN_ACCOUNTS, &[], |request| request.query(&query))
            .await
    }

    /// `GET /api/admin/accounts/{userId}`: the account, its tier and its recorded changes.
    pub async fn admin_account(&self, user_id: Uuid) -> Result<AdminAccountDetail, ApiError> {
        let id = user_id.to_string();
        self.call_json(&endpoints::GET_ADMIN_ACCOUNT, &[("userId", &id)], |request| request)
            .await
    }

    /// `PUT .../role`: changes the account's role, answering the account as it now is.
    pub async fn set_account_role(
        &self,
        user_id: Uuid,
        request: &SetAccountRoleRequest,
    ) -> Result<AdminAccountDetail, ApiError> {
        let id = user_id.to_string();
        self.call_json(&endpoints::SET_ADMIN_ACCOUNT_ROLE, &[("userId", &id)], |builder| {
            builder.json(request)
        })
        .await
    }

    /// `PUT .../granted-tier`: grants a tier or takes the grant away, answering the account as it now is.
    pub async fn set_granted_tier(
        &self,
        user_id: Uuid,
        request: &SetGrantedTierRequest,
    ) -> Result<AdminAccountDetail, ApiError> {
        let id = user_id.to_string();
        self.call_json(
            &endpoints::SET_ADMIN_ACCOUNT_GRANTED_TIER,
            &[("userId", &id)],
            |builder| builder.json(request),
        )
        .await
    }

    /// `PUT .../suspension`: suspends the account or lifts its suspension, answering the account as it now is.
    pub async fn set_account_suspension(
        &self,
        user_id: Uuid,
        request: &SetAccountSuspensionRequest,
    ) -> Result<AdminAccountDetail, ApiError> {
        let id = user_id.to_string();
        self.call_json(
            &endpoints::SET_ADMIN_ACCOUNT_SUSPENSION,
            &[("userId", &id)],
            |builder| builder.json(request),
        )
        .await
    }

    /// `POST .../take-down`: blocks the reported clip's sharing, answering the report as it now is.
    pub async fn take_down_report(&self, report_id: Uuid, note: &str) -> Result<AdminContentReport, ApiError> {
        self.resolve_report(&endpoints::TAKE_DOWN_CONTENT_REPORT, report_id, Some(note))
            .await
    }

    /// `POST .../dismiss`: dismisses the report, leaving the clip alone.
    pub async fn dismiss_report(&self, report_id: Uuid, note: Option<&str>) -> Result<AdminContentReport, ApiError> {
        self.resolve_report(&endpoints::DISMISS_CONTENT_REPORT, report_id, note)
            .await
    }

    /// `POST .../remove`: deletes an urgent report's clip, keeping it as evidence, and suspends its owner.
    pub async fn remove_report_clip(&self, report_id: Uuid, note: &str) -> Result<AdminContentReport, ApiError> {
        self.resolve_report(&endpoints::REMOVE_CONTENT_REPORT_CLIP, report_id, Some(note))
            .await
    }

    async fn resolve_report(
        &self,
        endpoint: &Endpoint,
        report_id: Uuid,
        note: Option<&str>,
    ) -> Result<AdminContentReport, ApiError> {
        let id = report_id.to_string();
        let body = ResolveContentReportRequest {
            note: note.map(str::to_owned),
        };
        self.call_json(endpoint, &[("reportId", &id)], |builder| builder.json(&body))
            .await
    }

    /// `GET /api/admin/reports?resolved=`: content reports, open ones unless `resolved`.
    pub async fn content_reports(&self, resolved: Option<bool>) -> Result<Vec<AdminContentReport>, ApiError> {
        let query = ListContentReportsQuery { resolved };
        self.call_json(&endpoints::LIST_CONTENT_REPORTS, &[], |request| request.query(&query))
            .await
    }

    /// `CreateVideo`: reserves a clip for a file in the book and answers where its upload goes.
    pub async fn create_video(
        &self,
        category_id: Uuid,
        query: &CreateVideoQuery,
    ) -> Result<CreateClipResponse, ApiError> {
        let id = category_id.to_string();
        self.call_json(&endpoints::CREATE_VIDEO, &[("categoryId", &id)], |request| {
            request.query(query)
        })
        .await
    }

    /// `DeleteClip`: deletes a clip still waiting for its upload, which also aborts the upload.
    pub async fn delete_clip(&self, clip_id: Uuid) -> Result<(), ApiError> {
        let id = clip_id.to_string();
        self.call(&endpoints::DELETE_CLIP, &[("clipId", &id)], |request| request)
            .await
    }

    /// `POST .../upload/parts`: presigned PUT URLs for the parts named.
    pub async fn presign_parts(
        &self,
        clip_id: Uuid,
        request: &PresignUploadPartsRequest,
    ) -> Result<PresignedUploadParts, ApiError> {
        let id = clip_id.to_string();
        self.call_json(&endpoints::PRESIGN_UPLOAD_PARTS, &[("clipId", &id)], |builder| {
            builder.json(request)
        })
        .await
    }

    /// `GET .../upload/parts`: the parts storage already holds for the upload.
    pub async fn list_parts(&self, clip_id: Uuid, upload_id: &str) -> Result<UploadedParts, ApiError> {
        let id = clip_id.to_string();
        let query = ListUploadPartsQuery {
            upload_id: upload_id.to_owned(),
        };
        self.call_json(&endpoints::LIST_UPLOAD_PARTS, &[("clipId", &id)], |request| {
            request.query(&query)
        })
        .await
    }

    /// `POST .../upload/complete`: joins the parts and checks the file.
    pub async fn complete_upload(
        &self,
        clip_id: Uuid,
        request: &CompleteUploadRequest,
    ) -> Result<CompletedUpload, ApiError> {
        let id = clip_id.to_string();
        self.call_json(&endpoints::COMPLETE_UPLOAD, &[("clipId", &id)], |builder| {
            builder.json(request)
        })
        .await
    }

    /// PUTs one part's bytes to its presigned URL, with no token or client header, and answers the part's ETag.
    pub async fn put_part(&self, url: &str, body: impl Into<Body>, content_length: u64) -> Result<String, ApiError> {
        let response = self
            .http
            .put(url)
            .timeout(PART_TIMEOUT)
            .header(CONTENT_LENGTH, content_length)
            .body(body)
            .send()
            .await?;
        let status = response.status();
        if !status.is_success() {
            return Err(ApiError::Storage(status));
        }
        response
            .headers()
            .get(ETAG)
            .and_then(|etag| etag.to_str().ok())
            .filter(|etag| !etag.is_empty())
            .map(str::to_owned)
            .ok_or(ApiError::MissingEtag)
    }

    async fn call_json<T: DeserializeOwned>(
        &self,
        endpoint: &Endpoint,
        params: &[(&str, &str)],
        configure: impl FnOnce(RequestBuilder) -> RequestBuilder,
    ) -> Result<T, ApiError> {
        let (status, bytes, token_sent) = self.send(endpoint, params, configure).await?;
        if !status.is_success() {
            return Err(ApiError::from_response(status, &bytes, token_sent));
        }
        Ok(serde_json::from_slice(&bytes)?)
    }

    async fn call(
        &self,
        endpoint: &Endpoint,
        params: &[(&str, &str)],
        configure: impl FnOnce(RequestBuilder) -> RequestBuilder,
    ) -> Result<(), ApiError> {
        let (status, bytes, token_sent) = self.send(endpoint, params, configure).await?;
        if !status.is_success() {
            return Err(ApiError::from_response(status, &bytes, token_sent));
        }
        Ok(())
    }

    /// Sends one API request and reads its whole answer: the status, the body and whether a token went with it.
    async fn send(
        &self,
        endpoint: &Endpoint,
        params: &[(&str, &str)],
        configure: impl FnOnce(RequestBuilder) -> RequestBuilder,
    ) -> Result<(StatusCode, Vec<u8>, bool), ApiError> {
        let url = format!("{}{}", self.base_url, endpoint.path_with(params));
        let mut request = self
            .http
            .request(endpoint.method.clone(), url)
            .timeout(API_TIMEOUT)
            .header(CLIENT_HEADER, self.client_name.clone());
        let token = self.token();
        if let Some(token) = &token {
            request = request.bearer_auth(token);
        }
        let response = configure(request).send().await?;
        let status = response.status();
        let bytes = response.bytes().await?;
        Ok((status, bytes.to_vec(), token.is_some()))
    }
}
