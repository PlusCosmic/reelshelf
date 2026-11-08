//! Signing the app in (ADR-0010, RFC 8628): ask for a code, open reelshelf.app/device on it, and poll at the interval
//! the API gives (5 seconds more after each `slow_down`) until the owner allows or turns it down, or the code expires.
//! The token goes to the credential store, never anywhere else.

use std::sync::Arc;
use std::time::Duration;

use chrono::{DateTime, TimeDelta, Utc};

use crate::api::{ApiError, UploaderApi};
use crate::platform::{Clock, CredentialStore};

/// Added to the interval after each `slow_down`.
pub const SLOW_DOWN_STEP: Duration = Duration::from_secs(5);

/// A sign-in waiting for its owner on reelshelf.app/device (S1).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PendingSignIn {
    pub device_code: String,
    /// Shown as `XXXX-XXXX`.
    pub user_code: String,
    pub verification_uri: String,
    /// The page with the code filled in, which the app opens in the browser.
    pub verification_uri_complete: String,
    pub interval: Duration,
    pub expires_at: DateTime<Utc>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SignInOutcome {
    /// A code was issued; the sign-in waits for its owner.
    Started(PendingSignIn),
    SignedIn,
    Denied,
    Expired,
    Unreachable,
    UpdateRequired,
}

fn is_absolute_url(value: &str) -> bool {
    value.starts_with("https://") || value.starts_with("http://")
}

pub struct DeviceSignIn<A> {
    api: Arc<A>,
    credentials: Arc<dyn CredentialStore>,
    clock: Arc<dyn Clock>,
}

impl<A: UploaderApi> DeviceSignIn<A> {
    pub fn new(api: Arc<A>, credentials: Arc<dyn CredentialStore>, clock: Arc<dyn Clock>) -> Self {
        DeviceSignIn {
            api,
            credentials,
            clock,
        }
    }

    pub async fn start(&self, device_name: &str) -> SignInOutcome {
        let started = match self.api.authorize(device_name).await {
            Ok(started) => started,
            Err(error) if error.status() == Some(426) => return SignInOutcome::UpdateRequired,
            Err(_) => return SignInOutcome::Unreachable,
        };

        if started.device_code.is_empty()
            || started.user_code.is_empty()
            || !is_absolute_url(&started.verification_uri)
            || !is_absolute_url(&started.verification_uri_complete)
        {
            return SignInOutcome::Unreachable;
        }

        let expires_in = TimeDelta::seconds(started.expires_in.unwrap_or(600).min(86_400) as i64);
        SignInOutcome::Started(PendingSignIn {
            device_code: started.device_code,
            user_code: started.user_code,
            verification_uri: started.verification_uri,
            verification_uri_complete: started.verification_uri_complete,
            interval: Duration::from_secs(started.interval.unwrap_or(5).max(1)),
            expires_at: self.clock.now() + expires_in,
        })
    }

    /// Polls until the sign-in is decided or expires; on approval the token is stored.
    pub async fn wait_for_approval(&self, pending: &PendingSignIn) -> SignInOutcome {
        let mut interval = pending.interval;
        loop {
            self.clock.sleep(interval).await;
            if self.clock.now() >= pending.expires_at {
                return SignInOutcome::Expired;
            }

            let error = match self.api.poll_token(&pending.device_code).await {
                Ok(token) if !token.is_empty() => {
                    self.credentials.write_token(&token);
                    return SignInOutcome::SignedIn;
                }
                Ok(_) => continue,
                Err(error) => error,
            };

            if error.status() == Some(426) {
                return SignInOutcome::UpdateRequired;
            }
            if let ApiError::Status(problem) = &error
                && problem.status == 400
            {
                match problem.error.as_deref() {
                    Some("access_denied") => return SignInOutcome::Denied,
                    Some("expired_token") => return SignInOutcome::Expired,
                    Some("slow_down") => interval += SLOW_DOWN_STEP,
                    _ => {}
                }
            }
            // authorization_pending, a network failure or a busy server: keep polling until the code expires.
        }
    }
}
