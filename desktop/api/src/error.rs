//! The answers the upload pipeline acts on differently, kept apart.

use reqwest::StatusCode;
use serde::Deserialize;

/// What a refused request's body said: a problem details body's `title` and `detail`, or a plain JSON string as the
/// detail (how the API answers `BadRequest<string>` and `Conflict<string>`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Problem {
    pub title: Option<String>,
    pub detail: Option<String>,
}

impl Problem {
    /// The title `GlobalExceptionHandler` gives a `PlanLimitExceededException`.
    pub const PLAN_LIMIT_TITLE: &'static str = "Plan Limit Reached";

    /// Reads a response body; anything that isn't a problem or a string reads as an empty problem.
    pub fn from_body(body: &[u8]) -> Self {
        #[derive(Deserialize)]
        #[serde(untagged)]
        enum Body {
            Text(String),
            Object {
                title: Option<String>,
                detail: Option<String>,
            },
        }

        match serde_json::from_slice::<Body>(body) {
            Ok(Body::Text(text)) => Self {
                title: None,
                detail: Some(text),
            },
            Ok(Body::Object { title, detail }) => Self { title, detail },
            Err(_) => Self::default(),
        }
    }

    /// The problem's detail, else its title.
    pub fn message(&self) -> Option<&str> {
        self.detail.as_deref().or(self.title.as_deref())
    }

    /// The account's plan can't take the clip (`PlanLimitExceededException`).
    pub fn is_plan_limit(&self) -> bool {
        self.title.as_deref() == Some(Self::PLAN_LIMIT_TITLE)
    }
}

/// Why a call didn't give its answer.
#[derive(Debug, thiserror::Error)]
pub enum ApiError {
    /// 401 to a request that carried a token: it was revoked, or the account can't sign in.
    #[error("the device was signed out")]
    SignedOut,
    /// 426: this version of the app is older than the API serves.
    #[error("the uploader needs updating (minimum {minimum_version:?})")]
    UpdateRequired { minimum_version: Option<String> },
    /// 403, such as a full plan ([`Problem::is_plan_limit`]) or deleting a clip a device may not.
    #[error("forbidden: {0:?}")]
    Forbidden(Problem),
    /// 404: the book, clip or upload is gone (or the feature is off).
    #[error("not found: {0:?}")]
    NotFound(Problem),
    /// 409, such as a clip of the same file already in the book.
    #[error("conflict: {0:?}")]
    Conflict(Problem),
    /// 422: the uploaded file was refused, with why.
    #[error("unprocessable: {0:?}")]
    Unprocessable(Problem),
    /// 503: the API can't do it just now; try again later.
    #[error("unavailable: {0:?}")]
    Unavailable(Problem),
    /// Any other status the API answered.
    #[error("the API answered {status}: {problem:?}")]
    Status { status: StatusCode, problem: Problem },
    /// Storage refused a part's PUT.
    #[error("storage answered {0}")]
    Storage(StatusCode),
    /// Storage took a part but sent no ETag, so the upload can't be completed.
    #[error("storage sent no ETag for the part")]
    MissingEtag,
    /// The request never got an answer: no network, a timeout, or the connection dropped.
    #[error("network error: {0}")]
    Network(#[from] reqwest::Error),
    /// The API answered success with a body this client can't read.
    #[error("couldn't read the API's answer: {0}")]
    Decode(#[from] serde_json::Error),
}

impl ApiError {
    /// Worth trying again later: the network, the server (5xx) or a rate limit (429).
    pub fn is_transient(&self) -> bool {
        match self {
            Self::Network(_) | Self::Unavailable(_) => true,
            Self::Status { status, .. } | Self::Storage(status) => {
                status.is_server_error() || *status == StatusCode::TOO_MANY_REQUESTS
            }
            _ => false,
        }
    }

    /// Maps a refused API response. `token_sent` says whether the request carried a token, since a 401 without one
    /// doesn't mean the device was signed out.
    pub(crate) fn from_response(status: StatusCode, body: &[u8], token_sent: bool) -> Self {
        match status {
            StatusCode::UNAUTHORIZED if token_sent => Self::SignedOut,
            StatusCode::UPGRADE_REQUIRED => {
                #[derive(Deserialize)]
                struct TooOld {
                    minimum_version: Option<String>,
                }
                Self::UpdateRequired {
                    minimum_version: serde_json::from_slice::<TooOld>(body)
                        .ok()
                        .and_then(|body| body.minimum_version),
                }
            }
            StatusCode::FORBIDDEN => Self::Forbidden(Problem::from_body(body)),
            StatusCode::NOT_FOUND => Self::NotFound(Problem::from_body(body)),
            StatusCode::CONFLICT => Self::Conflict(Problem::from_body(body)),
            StatusCode::UNPROCESSABLE_ENTITY => Self::Unprocessable(Problem::from_body(body)),
            StatusCode::SERVICE_UNAVAILABLE => Self::Unavailable(Problem::from_body(body)),
            status => Self::Status {
                status,
                problem: Problem::from_body(body),
            },
        }
    }
}
