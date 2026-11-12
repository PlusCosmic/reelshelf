//! Signing the monitor in to Reelshelf (ADR-0010's device flow, RFC 8628, as the monitor: `client: "monitor"`), and
//! what the app does with the answers it gets once signed in.
//!
//! [`Session`] is what the window shows. [`SignIn`] moves it: it restores a kept token (`GET /api/me`), starts a
//! sign-in (`POST /api/devices/authorize`), polls for the token at the interval the API gives (5 seconds more after a
//! `slow_down`) until it is approved, turned down or expires, keeps the token in [`Secrets`] (the Keychain) and signs
//! out (`DELETE /api/devices/current`). [`SignIn::refused`] turns a refusal of any later call into the session it
//! means: 401 is signed out (the token is forgotten), 426 is update required.

use std::future::Future;
use std::sync::Arc;
use std::time::Duration;

use reelshelf_api::{
    ApiClient, ApiError, CurrentUser, DeviceAuthorization, DeviceTokenErrorCode, DeviceTokenPoll, FeatureFlags, OpsAi,
    OpsStorage, OpsTranscoding,
};
use tokio::time::Instant;

use crate::secrets::{Secret, Secrets};

/// Added to the polling interval after each `slow_down`.
pub const SLOW_DOWN_STEP: Duration = Duration::from_secs(5);

/// The calls the monitor makes, so tests can answer them with a fake. [`ApiClient`] is the real one.
pub trait MonitorApi: Send + Sync + 'static {
    fn set_token(&self, token: Option<String>);
    fn authorize(&self, device_name: &str) -> impl Future<Output = Result<DeviceAuthorization, ApiError>> + Send;
    fn poll_token(&self, device_code: &str) -> impl Future<Output = Result<DeviceTokenPoll, ApiError>> + Send;
    fn sign_out(&self) -> impl Future<Output = Result<(), ApiError>> + Send;
    fn me(&self) -> impl Future<Output = Result<CurrentUser, ApiError>> + Send;
    fn features(&self) -> impl Future<Output = Result<FeatureFlags, ApiError>> + Send;
    fn ops_storage(&self) -> impl Future<Output = Result<OpsStorage, ApiError>> + Send;
    fn ops_transcoding(&self) -> impl Future<Output = Result<OpsTranscoding, ApiError>> + Send;
    fn ops_ai(&self) -> impl Future<Output = Result<OpsAi, ApiError>> + Send;
}

impl MonitorApi for ApiClient {
    fn set_token(&self, token: Option<String>) {
        ApiClient::set_token(self, token);
    }
    fn authorize(&self, device_name: &str) -> impl Future<Output = Result<DeviceAuthorization, ApiError>> + Send {
        self.authorize_device(device_name)
    }
    fn poll_token(&self, device_code: &str) -> impl Future<Output = Result<DeviceTokenPoll, ApiError>> + Send {
        self.poll_device_token(device_code)
    }
    fn sign_out(&self) -> impl Future<Output = Result<(), ApiError>> + Send {
        self.sign_out_device()
    }
    fn me(&self) -> impl Future<Output = Result<CurrentUser, ApiError>> + Send {
        ApiClient::me(self)
    }
    fn features(&self) -> impl Future<Output = Result<FeatureFlags, ApiError>> + Send {
        ApiClient::features(self)
    }
    fn ops_storage(&self) -> impl Future<Output = Result<OpsStorage, ApiError>> + Send {
        ApiClient::ops_storage(self)
    }
    fn ops_transcoding(&self) -> impl Future<Output = Result<OpsTranscoding, ApiError>> + Send {
        ApiClient::ops_transcoding(self)
    }
    fn ops_ai(&self) -> impl Future<Output = Result<OpsAi, ApiError>> + Send {
        ApiClient::ops_ai(self)
    }
}

/// Why the app is signed out, shown above Sign in.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SignedOutReason {
    /// The code expired before it was approved.
    Expired,
    /// The owner turned the sign-in down on the device page.
    Denied,
    /// Reelshelf couldn't be reached to start or finish signing in.
    Unreachable,
    /// The token stopped working (401): removed in Settings → Devices, or the account can't sign in.
    Revoked,
}

/// Where signing in is.
#[derive(Debug, Clone, PartialEq)]
pub enum Session {
    /// Reading the kept token and checking it.
    Checking,
    SignedOut(Option<SignedOutReason>),
    /// Asking for a code.
    Starting,
    /// A code was issued; waiting for its owner on the device page.
    Waiting(Pending),
    /// Signed in; `user` is None while `/api/me` couldn't be read (the network), though the token is kept.
    SignedIn {
        user: Option<CurrentUser>,
    },
    /// 426: this version is older than the API serves.
    UpdateRequired,
}

impl Session {
    pub fn signed_in(&self) -> bool {
        matches!(self, Session::SignedIn { .. })
    }
}

/// A sign-in waiting for its owner.
#[derive(Debug, Clone, PartialEq)]
pub struct Pending {
    pub device_code: String,
    /// As the API gives it (`BCDFGHJK`); [`Pending::shown_code`] is how it's shown.
    pub user_code: String,
    pub verification_uri: String,
    /// The device page with the code filled in, which the app opens.
    pub verification_uri_complete: String,
    pub interval: Duration,
    pub expires_at: Instant,
}

impl Pending {
    /// `BCDF-GHJK`, as the device page shows it.
    pub fn shown_code(&self) -> String {
        let code = self.user_code.to_uppercase();
        match code.len() {
            8 if code.is_ascii() => format!("{}-{}", &code[..4], &code[4..]),
            _ => code,
        }
    }
}

fn is_absolute_url(value: &str) -> bool {
    value.starts_with("https://") || value.starts_with("http://")
}

pub struct SignIn<A> {
    api: Arc<A>,
    secrets: Arc<dyn Secrets>,
    token: Secret,
}

impl<A: MonitorApi> SignIn<A> {
    /// Signs in to `site`, keeping its token in `secrets`.
    pub fn new(api: Arc<A>, secrets: Arc<dyn Secrets>, site: &str) -> Self {
        SignIn {
            api,
            secrets,
            token: Secret::DeviceToken { site: site.to_owned() },
        }
    }

    pub fn api(&self) -> &Arc<A> {
        &self.api
    }

    /// The session a start-up begins in: signed out without a kept token, else signed in once `/api/me` takes it.
    pub async fn restore(&self) -> Session {
        let Some(token) = self.secrets.read(&self.token) else {
            return Session::SignedOut(None);
        };
        self.api.set_token(Some(token));
        self.check().await
    }

    /// Reads the account with the token in use.
    async fn check(&self) -> Session {
        match self.api.me().await {
            Ok(user) => Session::SignedIn { user: Some(user) },
            Err(error) => self.refused(&error).unwrap_or(Session::SignedIn { user: None }),
        }
    }

    /// Asks for a code: [`Session::Waiting`], or why there isn't one.
    pub async fn start(&self, device_name: &str) -> Session {
        let started = match self.api.authorize(device_name).await {
            Ok(started) => started,
            Err(ApiError::UpdateRequired { .. }) => return Session::UpdateRequired,
            Err(_) => return Session::SignedOut(Some(SignedOutReason::Unreachable)),
        };
        if started.device_code.is_empty()
            || started.user_code.is_empty()
            || !is_absolute_url(&started.verification_uri)
            || !is_absolute_url(&started.verification_uri_complete)
        {
            return Session::SignedOut(Some(SignedOutReason::Unreachable));
        }
        Session::Waiting(Pending {
            device_code: started.device_code,
            user_code: started.user_code,
            verification_uri: started.verification_uri,
            verification_uri_complete: started.verification_uri_complete,
            interval: Duration::from_secs(u64::from(started.interval.max(1))),
            expires_at: Instant::now() + Duration::from_secs(u64::from(started.expires_in.min(86_400))),
        })
    }

    /// Polls until the sign-in is decided or the code expires. Approved, the token is kept and the account read.
    pub async fn wait(&self, pending: &Pending) -> Session {
        let mut interval = pending.interval;
        loop {
            tokio::time::sleep(interval).await;
            if Instant::now() >= pending.expires_at {
                return Session::SignedOut(Some(SignedOutReason::Expired));
            }
            match self.api.poll_token(&pending.device_code).await {
                Ok(DeviceTokenPoll::Granted(token)) if !token.access_token.is_empty() => {
                    if self.secrets.write(&self.token, &token.access_token).is_err() {
                        // Not kept, it would be lost at the next start; it still works for now.
                        eprintln!("Couldn't keep the device token in the Keychain");
                    }
                    self.api.set_token(Some(token.access_token));
                    return self.check().await;
                }
                Ok(DeviceTokenPoll::Granted(_)) => {}
                Ok(DeviceTokenPoll::Refused(code)) => match code {
                    DeviceTokenErrorCode::AccessDenied => return Session::SignedOut(Some(SignedOutReason::Denied)),
                    DeviceTokenErrorCode::ExpiredToken => {
                        return Session::SignedOut(Some(SignedOutReason::Expired));
                    }
                    DeviceTokenErrorCode::SlowDown => interval += SLOW_DOWN_STEP,
                    DeviceTokenErrorCode::AuthorizationPending | DeviceTokenErrorCode::Other(_) => {}
                },
                Err(ApiError::UpdateRequired { .. }) => return Session::UpdateRequired,
                // The network or a busy server: keep polling until the code expires.
                Err(_) => {}
            }
        }
    }

    /// Signs out: revokes the token on Reelshelf (best effort: offline, it is still forgotten here) and forgets it.
    pub async fn sign_out(&self) -> Session {
        let _ = self.api.sign_out().await;
        self.forget();
        Session::SignedOut(None)
    }

    fn forget(&self) {
        self.secrets.delete(&self.token);
        self.api.set_token(None);
    }

    /// The session a refused call means, when it means one: 401 signs out (forgetting the token), 426 is update
    /// required. Anything else (403, 404, the network) leaves the session as it is.
    pub fn refused(&self, error: &ApiError) -> Option<Session> {
        match error {
            ApiError::SignedOut => {
                self.forget();
                Some(Session::SignedOut(Some(SignedOutReason::Revoked)))
            }
            ApiError::UpdateRequired { .. } => Some(Session::UpdateRequired),
            _ => None,
        }
    }
}

/// Why the ops numbers can't be shown though the app is signed in.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Refusal {
    /// 404: the `ops-monitor` flag is off for the account.
    FlagOff,
    /// 403: the account isn't an Admin.
    NotAdmin,
}

impl Refusal {
    pub fn of(error: &ApiError) -> Option<Refusal> {
        match error {
            ApiError::NotFound(_) => Some(Refusal::FlagOff),
            ApiError::Forbidden(_) => Some(Refusal::NotAdmin),
            _ => None,
        }
    }
}

#[cfg(test)]
mod tests {
    use std::collections::VecDeque;
    use std::sync::Mutex;

    use reelshelf_api::{DeviceToken, Problem};

    use super::*;
    use crate::secrets::MemorySecrets;

    const SITE: &str = "https://reelshelf.app";

    fn user() -> CurrentUser {
        CurrentUser {
            id: uuid::Uuid::nil(),
            username: "ada".to_owned(),
            global_name: None,
            avatar: None,
            email: None,
            suggested_email: None,
            needs_onboarding: false,
            needs_username: false,
            can_set_username: false,
            is_admin: true,
            color_scheme: "stacks".to_owned(),
        }
    }

    fn started() -> DeviceAuthorization {
        DeviceAuthorization {
            device_code: "dc".to_owned(),
            user_code: "bcdfghjk".to_owned(),
            verification_uri: "https://reelshelf.app/device".to_owned(),
            verification_uri_complete: "https://reelshelf.app/device?code=BCDF-GHJK".to_owned(),
            interval: 5,
            expires_in: 600,
        }
    }

    fn unavailable() -> ApiError {
        ApiError::Unavailable(Problem::default())
    }

    /// Answers each call from a script, recording what it was asked.
    #[derive(Default)]
    struct FakeApi {
        token: Mutex<Option<String>>,
        authorize: Mutex<Option<Result<DeviceAuthorization, ApiError>>>,
        polls: Mutex<VecDeque<Result<DeviceTokenPoll, ApiError>>>,
        poll_times: Mutex<Vec<Instant>>,
        me: Mutex<Option<Result<CurrentUser, ApiError>>>,
        signed_out: Mutex<u32>,
        sign_out_fails: bool,
    }

    fn take<T>(slot: &Mutex<Option<Result<T, ApiError>>>, otherwise: impl FnOnce() -> T) -> Result<T, ApiError> {
        slot.lock().unwrap().take().unwrap_or_else(|| Ok(otherwise()))
    }

    impl MonitorApi for FakeApi {
        fn set_token(&self, token: Option<String>) {
            *self.token.lock().unwrap() = token;
        }
        async fn authorize(&self, _: &str) -> Result<DeviceAuthorization, ApiError> {
            take(&self.authorize, started)
        }
        async fn poll_token(&self, _: &str) -> Result<DeviceTokenPoll, ApiError> {
            self.poll_times.lock().unwrap().push(Instant::now());
            self.polls
                .lock()
                .unwrap()
                .pop_front()
                .unwrap_or(Ok(DeviceTokenPoll::Refused(DeviceTokenErrorCode::AuthorizationPending)))
        }
        async fn sign_out(&self) -> Result<(), ApiError> {
            *self.signed_out.lock().unwrap() += 1;
            if self.sign_out_fails {
                Err(unavailable())
            } else {
                Ok(())
            }
        }
        async fn me(&self) -> Result<CurrentUser, ApiError> {
            take(&self.me, user)
        }
        async fn features(&self) -> Result<FeatureFlags, ApiError> {
            Ok(FeatureFlags { enabled: Vec::new() })
        }
        async fn ops_storage(&self) -> Result<OpsStorage, ApiError> {
            Err(unavailable())
        }
        async fn ops_transcoding(&self) -> Result<OpsTranscoding, ApiError> {
            Err(unavailable())
        }
        async fn ops_ai(&self) -> Result<OpsAi, ApiError> {
            Err(unavailable())
        }
    }

    fn granted(token: &str) -> Result<DeviceTokenPoll, ApiError> {
        Ok(DeviceTokenPoll::Granted(DeviceToken {
            access_token: token.to_owned(),
            token_type: "Bearer".to_owned(),
        }))
    }

    fn refused(code: DeviceTokenErrorCode) -> Result<DeviceTokenPoll, ApiError> {
        Ok(DeviceTokenPoll::Refused(code))
    }

    fn sign_in(api: FakeApi) -> (SignIn<FakeApi>, Arc<FakeApi>, Arc<MemorySecrets>) {
        let api = Arc::new(api);
        let secrets = Arc::new(MemorySecrets::default());
        (SignIn::new(api.clone(), secrets.clone(), SITE), api, secrets)
    }

    fn token_secret() -> Secret {
        Secret::DeviceToken { site: SITE.to_owned() }
    }

    async fn waiting(s: &SignIn<FakeApi>) -> Pending {
        match s.start("Studio Mac").await {
            Session::Waiting(pending) => pending,
            other => panic!("not waiting: {other:?}"),
        }
    }

    #[tokio::test(start_paused = true)]
    async fn without_a_kept_token_it_starts_signed_out() {
        let (s, api, _) = sign_in(FakeApi::default());
        assert_eq!(s.restore().await, Session::SignedOut(None));
        assert_eq!(*api.token.lock().unwrap(), None);
    }

    #[tokio::test(start_paused = true)]
    async fn a_kept_token_signs_in_at_start() {
        let (s, api, secrets) = sign_in(FakeApi::default());
        secrets.write(&token_secret(), "rsd_kept").unwrap();
        assert_eq!(s.restore().await, Session::SignedIn { user: Some(user()) });
        assert_eq!(api.token.lock().unwrap().as_deref(), Some("rsd_kept"));
    }

    #[tokio::test(start_paused = true)]
    async fn a_revoked_kept_token_is_forgotten() {
        let (s, api, secrets) = sign_in(FakeApi {
            me: Mutex::new(Some(Err(ApiError::SignedOut))),
            ..FakeApi::default()
        });
        secrets.write(&token_secret(), "rsd_revoked").unwrap();
        assert_eq!(s.restore().await, Session::SignedOut(Some(SignedOutReason::Revoked)));
        assert_eq!(secrets.read(&token_secret()), None);
        assert_eq!(*api.token.lock().unwrap(), None);
    }

    #[tokio::test(start_paused = true)]
    async fn offline_at_start_it_stays_signed_in_without_the_account() {
        let (s, _, secrets) = sign_in(FakeApi {
            me: Mutex::new(Some(Err(unavailable()))),
            ..FakeApi::default()
        });
        secrets.write(&token_secret(), "rsd_kept").unwrap();
        assert_eq!(s.restore().await, Session::SignedIn { user: None });
        assert_eq!(secrets.read(&token_secret()).as_deref(), Some("rsd_kept"));
    }

    #[tokio::test(start_paused = true)]
    async fn the_code_is_shown_in_two_halves() {
        let (s, _, _) = sign_in(FakeApi::default());
        let pending = waiting(&s).await;
        assert_eq!(pending.shown_code(), "BCDF-GHJK");
        assert_eq!(pending.interval, Duration::from_secs(5));
    }

    #[tokio::test(start_paused = true)]
    async fn approving_keeps_the_token_and_reads_the_account() {
        let (s, api, secrets) = sign_in(FakeApi {
            polls: Mutex::new(VecDeque::from([
                refused(DeviceTokenErrorCode::AuthorizationPending),
                granted("rsd_new"),
            ])),
            ..FakeApi::default()
        });
        let pending = waiting(&s).await;
        let began = Instant::now();
        assert_eq!(s.wait(&pending).await, Session::SignedIn { user: Some(user()) });
        assert_eq!(secrets.read(&token_secret()).as_deref(), Some("rsd_new"));
        assert_eq!(api.token.lock().unwrap().as_deref(), Some("rsd_new"));
        let times = api.poll_times.lock().unwrap().clone();
        assert_eq!(
            times,
            vec![began + Duration::from_secs(5), began + Duration::from_secs(10)]
        );
    }

    #[tokio::test(start_paused = true)]
    async fn slow_down_polls_less_often() {
        let (s, api, _) = sign_in(FakeApi {
            polls: Mutex::new(VecDeque::from([
                refused(DeviceTokenErrorCode::SlowDown),
                refused(DeviceTokenErrorCode::AuthorizationPending),
                granted("rsd_new"),
            ])),
            ..FakeApi::default()
        });
        let pending = waiting(&s).await;
        let began = Instant::now();
        s.wait(&pending).await;
        let gaps: Vec<u64> = api
            .poll_times
            .lock()
            .unwrap()
            .iter()
            .scan(began, |last, t| {
                let gap = (*t - *last).as_secs();
                *last = *t;
                Some(gap)
            })
            .collect();
        assert_eq!(gaps, vec![5, 10, 10]);
    }

    #[tokio::test(start_paused = true)]
    async fn turning_it_down_signs_out_with_why() {
        let (s, _, secrets) = sign_in(FakeApi {
            polls: Mutex::new(VecDeque::from([refused(DeviceTokenErrorCode::AccessDenied)])),
            ..FakeApi::default()
        });
        let pending = waiting(&s).await;
        assert_eq!(
            s.wait(&pending).await,
            Session::SignedOut(Some(SignedOutReason::Denied))
        );
        assert_eq!(secrets.read(&token_secret()), None);
    }

    #[tokio::test(start_paused = true)]
    async fn the_code_expires_while_nobody_approves_it() {
        let (s, api, _) = sign_in(FakeApi::default());
        let pending = waiting(&s).await;
        assert_eq!(
            s.wait(&pending).await,
            Session::SignedOut(Some(SignedOutReason::Expired))
        );
        // Polled every 5 s for the 10 minutes, and not after.
        assert_eq!(api.poll_times.lock().unwrap().len(), 119);
    }

    #[tokio::test(start_paused = true)]
    async fn the_network_failing_keeps_polling() {
        let (s, _, _) = sign_in(FakeApi {
            polls: Mutex::new(VecDeque::from([Err(unavailable()), granted("rsd_new")])),
            ..FakeApi::default()
        });
        let pending = waiting(&s).await;
        assert!(s.wait(&pending).await.signed_in());
    }

    #[tokio::test(start_paused = true)]
    async fn an_old_version_is_told_to_update() {
        let (s, _, _) = sign_in(FakeApi {
            authorize: Mutex::new(Some(Err(ApiError::UpdateRequired {
                minimum_version: Some("2.0".to_owned()),
            }))),
            ..FakeApi::default()
        });
        assert_eq!(s.start("Studio Mac").await, Session::UpdateRequired);

        let (s, _, _) = sign_in(FakeApi {
            polls: Mutex::new(VecDeque::from([Err(ApiError::UpdateRequired {
                minimum_version: None,
            })])),
            ..FakeApi::default()
        });
        let pending = waiting(&s).await;
        assert_eq!(s.wait(&pending).await, Session::UpdateRequired);
    }

    #[tokio::test(start_paused = true)]
    async fn unreachable_or_a_bad_answer_cant_start() {
        let (s, _, _) = sign_in(FakeApi {
            authorize: Mutex::new(Some(Err(unavailable()))),
            ..FakeApi::default()
        });
        assert_eq!(
            s.start("Studio Mac").await,
            Session::SignedOut(Some(SignedOutReason::Unreachable))
        );

        let (s, _, _) = sign_in(FakeApi {
            authorize: Mutex::new(Some(Ok(DeviceAuthorization {
                verification_uri: "javascript:alert(1)".to_owned(),
                ..started()
            }))),
            ..FakeApi::default()
        });
        assert_eq!(
            s.start("Studio Mac").await,
            Session::SignedOut(Some(SignedOutReason::Unreachable))
        );
    }

    #[tokio::test(start_paused = true)]
    async fn signing_out_revokes_and_forgets_even_offline() {
        let (s, api, secrets) = sign_in(FakeApi {
            sign_out_fails: true,
            ..FakeApi::default()
        });
        secrets.write(&token_secret(), "rsd_kept").unwrap();
        s.restore().await;
        assert_eq!(s.sign_out().await, Session::SignedOut(None));
        assert_eq!(*api.signed_out.lock().unwrap(), 1);
        assert_eq!(secrets.read(&token_secret()), None);
        assert_eq!(*api.token.lock().unwrap(), None);
    }

    #[tokio::test(start_paused = true)]
    async fn refusals_map_to_sessions_and_messages() {
        let (s, _, secrets) = sign_in(FakeApi::default());
        secrets.write(&token_secret(), "rsd_kept").unwrap();
        assert_eq!(s.refused(&unavailable()), None);
        assert_eq!(s.refused(&ApiError::NotFound(Problem::default())), None);
        assert_eq!(
            s.refused(&ApiError::UpdateRequired { minimum_version: None }),
            Some(Session::UpdateRequired)
        );
        assert_eq!(secrets.read(&token_secret()).as_deref(), Some("rsd_kept"));
        assert_eq!(
            s.refused(&ApiError::SignedOut),
            Some(Session::SignedOut(Some(SignedOutReason::Revoked)))
        );
        assert_eq!(secrets.read(&token_secret()), None);

        assert_eq!(
            Refusal::of(&ApiError::NotFound(Problem::default())),
            Some(Refusal::FlagOff)
        );
        assert_eq!(
            Refusal::of(&ApiError::Forbidden(Problem::default())),
            Some(Refusal::NotAdmin)
        );
        assert_eq!(Refusal::of(&unavailable()), None);
    }
}
