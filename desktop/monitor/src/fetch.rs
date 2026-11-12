//! Loading off the UI thread: each call runs on the fetcher's own Tokio runtime and hands its answer to the window
//! through Rune's `AppHandle::update`. It signs in to Reelshelf ([`SignIn`]), reads the ops answers, polls the section
//! showing every [`POLL_EVERY`](crate::state::POLL_EVERY), and loads Grafana. Answers for a dashboard load that has
//! since been replaced (another dashboard, a new range, a refresh) are dropped.

use std::sync::{Arc, Mutex};

use reelshelf_api::{ApiClient, ApiError, ClientApp, FeatureFlags};
use rune::AppHandle;
use tokio::task::AbortHandle;

use crate::grafana::{Client, TimeRange, query};
use crate::secrets::{Secret, Secrets};
use crate::session::{Refusal, Session, SignIn};
use crate::state::{Fetched, Load, Monitor, OpsKind, POLL_EVERY};
use crate::{copy, links};

pub struct Fetcher {
    runtime: tokio::runtime::Runtime,
    window: AppHandle<Monitor>,
    site: String,
    sign_in: Arc<SignIn<ApiClient>>,
    secrets: Arc<dyn Secrets>,
    grafana: Mutex<Option<Client>>,
    /// The sign-in waiting for its owner, so Cancel (or another Sign in) stops its polling.
    waiting: Mutex<Option<AbortHandle>>,
}

impl Fetcher {
    /// A fetcher signing in to `site`, keeping tokens in `secrets`, reading Grafana through `grafana` when set up.
    pub fn new(
        site: String,
        secrets: Arc<dyn Secrets>,
        grafana: Option<Client>,
        window: AppHandle<Monitor>,
    ) -> std::io::Result<Fetcher> {
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()?;
        let http = reqwest::Client::builder()
            .connect_timeout(std::time::Duration::from_secs(15))
            .build()
            .map_err(std::io::Error::other)?;
        let api = Arc::new(ApiClient::for_app(http, ClientApp::Monitor, &site, links::version()));
        let sign_in = Arc::new(SignIn::new(api, secrets.clone(), &site));
        let fetcher = Fetcher {
            runtime,
            window,
            site,
            sign_in,
            secrets,
            grafana: Mutex::new(grafana),
            waiting: Mutex::new(None),
        };
        fetcher.start_polling();
        Ok(fetcher)
    }

    /// Grafana as kept in the Keychain, when both its address and token are.
    pub fn kept_grafana(secrets: &dyn Secrets) -> Option<Client> {
        let url = secrets.read(&Secret::GrafanaUrl)?;
        let token = secrets.read(&Secret::GrafanaToken)?;
        Some(Client::new(&url, token))
    }

    pub fn site(&self) -> &str {
        &self.site
    }

    fn grafana(&self) -> Option<Client> {
        self.grafana.lock().unwrap_or_else(|p| p.into_inner()).clone()
    }

    /// Asks the window to read the section showing again, every [`POLL_EVERY`].
    fn start_polling(&self) {
        let window = self.window.clone();
        self.runtime.spawn(async move {
            let mut ticks = tokio::time::interval(POLL_EVERY);
            ticks.tick().await;
            loop {
                ticks.tick().await;
                window.update(|s: &mut Monitor, _| s.poll());
            }
        });
    }

    /// Signs in with the kept token, if there is one.
    pub fn restore(&self) {
        let (sign_in, window) = (self.sign_in.clone(), self.window.clone());
        self.runtime.spawn(async move {
            let session = sign_in.restore().await;
            let signed_in = session.signed_in();
            window.update(move |s: &mut Monitor, _| s.set_session(session));
            if signed_in {
                features(&sign_in, &window).await;
            }
        });
    }

    /// Starts a sign-in: asks for a code, opens the device page on it, and waits for its owner.
    pub fn start_sign_in(&self) {
        self.cancel_sign_in();
        let (sign_in, window) = (self.sign_in.clone(), self.window.clone());
        let task = self.runtime.spawn(async move {
            let session = sign_in.start(&links::device_name()).await;
            let Session::Waiting(pending) = session.clone() else {
                window.update(move |s: &mut Monitor, _| s.set_session(session));
                return;
            };
            let url = pending.verification_uri_complete.clone();
            window.update(move |s: &mut Monitor, tx| {
                s.set_session(session);
                let _ = tx.open_url(&url);
            });
            let session = sign_in.wait(&pending).await;
            let signed_in = session.signed_in();
            window.update(move |s: &mut Monitor, _| {
                // Unless Cancel was pressed meanwhile.
                if matches!(s.session, Session::Waiting(_)) {
                    s.set_session(session);
                }
            });
            if signed_in {
                features(&sign_in, &window).await;
            }
        });
        *self.waiting.lock().unwrap_or_else(|p| p.into_inner()) = Some(task.abort_handle());
    }

    pub fn cancel_sign_in(&self) {
        if let Some(task) = self.waiting.lock().unwrap_or_else(|p| p.into_inner()).take() {
            task.abort();
        }
    }

    pub fn sign_out(&self) {
        self.cancel_sign_in();
        let (sign_in, window) = (self.sign_in.clone(), self.window.clone());
        self.runtime.spawn(async move {
            let session = sign_in.sign_out().await;
            window.update(move |s: &mut Monitor, _| s.set_session(session));
        });
    }

    /// Reads one ops answer.
    pub fn ops(&self, kind: OpsKind) {
        let (sign_in, window) = (self.sign_in.clone(), self.window.clone());
        self.runtime.spawn(async move {
            let api = sign_in.api();
            match kind {
                OpsKind::Storage => {
                    let answer = api.ops_storage().await;
                    deliver(&sign_in, &window, answer, |s| &mut s.storage);
                }
                OpsKind::Transcoding => {
                    let answer = api.ops_transcoding().await;
                    deliver(&sign_in, &window, answer, |s| &mut s.transcoding);
                }
                OpsKind::Ai => {
                    let answer = api.ops_ai().await;
                    deliver(&sign_in, &window, answer, |s| &mut s.ai);
                }
                OpsKind::Costs => {
                    let answer = api.ops_costs().await;
                    deliver(&sign_in, &window, answer, |s| &mut s.costs);
                }
            }
        });
    }

    /// Keeps Grafana's address and token (None keeps the kept token) in the Keychain and loads through them.
    pub fn save_grafana(&self, url: &str, token: Option<&str>) -> Result<(), String> {
        let token = match token {
            Some(token) => token.to_owned(),
            None => self
                .secrets
                .read(&Secret::GrafanaToken)
                .or_else(|| self.grafana().map(|c| c.token().to_owned()))
                .ok_or_else(|| copy::NO_TOKEN.to_owned())?,
        };
        self.secrets.write(&Secret::GrafanaUrl, url)?;
        self.secrets.write(&Secret::GrafanaToken, &token)?;
        *self.grafana.lock().unwrap_or_else(|p| p.into_inner()) = Some(Client::new(url, token));
        Ok(())
    }

    pub fn remove_grafana(&self) {
        self.secrets.delete(&Secret::GrafanaUrl);
        self.secrets.delete(&Secret::GrafanaToken);
        *self.grafana.lock().unwrap_or_else(|p| p.into_inner()) = None;
    }

    pub fn list(&self) {
        let Some(client) = self.grafana() else {
            return;
        };
        let window = self.window.clone();
        self.runtime.spawn(async move {
            let list = match client.search().await {
                Ok(hits) => Load::Ready(hits),
                Err(e) => Load::Failed(e.to_string()),
            };
            window.update(move |s: &mut Monitor, _| s.list = list);
        });
    }

    /// Loads dashboard `uid` and runs every panel the app draws, over `range` (else the dashboard's own).
    pub fn open(&self, uid: String, range: Option<TimeRange>, generation: u64) {
        let Some(client) = self.grafana() else {
            return;
        };
        let window = self.window.clone();
        self.runtime.spawn(async move {
            let dashboard = match client.dashboard(&uid).await {
                Ok(d) => d,
                Err(e) => {
                    let error = e.to_string();
                    window.update(move |s: &mut Monitor, _| {
                        if let Some(o) = s.current(generation) {
                            o.dashboard = Load::Failed(error);
                        }
                    });
                    return;
                }
            };
            let range = range.unwrap_or_else(|| TimeRange::from_grafana(&dashboard.time_from));
            let span = range.span(chrono::Utc::now().timestamp_millis());
            let bodies: Vec<_> = dashboard
                .panels
                .iter()
                .filter(|p| query::is_drawn(p))
                .map(|p| (p.id, query::query_body(p, &dashboard, span)))
                .collect();
            let ids: Vec<u32> = bodies.iter().map(|(id, _)| *id).collect();
            window.update(move |s: &mut Monitor, _| {
                if let Some(o) = s.current(generation) {
                    o.span = span;
                    for id in ids {
                        o.data.entry(id).or_insert(Load::Loading);
                    }
                    o.dashboard = Load::Ready(dashboard);
                    s.range = range;
                }
            });
            for (id, body) in bodies {
                let (client, window) = (client.clone(), window.clone());
                tokio::spawn(async move {
                    let data = match client.query(&body).await {
                        Ok(data) => Load::Ready(data),
                        Err(e) => Load::Failed(e.to_string()),
                    };
                    window.update(move |s: &mut Monitor, _| {
                        if let Some(o) = s.current(generation) {
                            o.data.insert(id, data);
                        }
                    });
                });
            }
        });
    }
}

/// Reads the account's flags once signed in, so a flag that's off says so before any ops read is refused.
async fn features(sign_in: &SignIn<ApiClient>, window: &AppHandle<Monitor>) {
    if let Ok(flags) = sign_in.api().features().await
        && !flags.has(FeatureFlags::OPS_MONITOR)
    {
        window.update(|s: &mut Monitor, _| s.refusal = Some(Refusal::FlagOff));
    }
}

/// Hands an ops answer to the window: the answer, the refusal it is (signed out, update, flag off, not an Admin), or
/// why it failed, keeping the last answer on screen.
fn deliver<T: Send + 'static>(
    sign_in: &SignIn<ApiClient>,
    window: &AppHandle<Monitor>,
    answer: Result<T, ApiError>,
    slot: fn(&mut Monitor) -> &mut Fetched<T>,
) {
    let now = chrono::Utc::now().timestamp_millis();
    match answer {
        Ok(value) => window.update(move |s: &mut Monitor, _| {
            s.refusal = None;
            s.now_ms = now;
            *slot(s) = Fetched::ready(value);
        }),
        Err(error) => {
            if let Some(session) = sign_in.refused(&error) {
                window.update(move |s: &mut Monitor, _| s.set_session(session));
            } else if let Some(refusal) = Refusal::of(&error) {
                window.update(move |s: &mut Monitor, _| s.refusal = Some(refusal));
            } else {
                let message = error_words(&error);
                window.update(move |s: &mut Monitor, _| slot(s).error = Some(message));
            }
        }
    }
}

/// A failure in a few words, never a token.
fn error_words(error: &ApiError) -> String {
    match error {
        ApiError::Network(_) => copy::UNREACHABLE.to_owned(),
        ApiError::Unavailable(p) | ApiError::Conflict(p) | ApiError::Unprocessable(p) => {
            p.message().unwrap_or("503").to_owned()
        }
        ApiError::Status { status, problem } => problem
            .message()
            .map(str::to_owned)
            .unwrap_or_else(|| status.to_string()),
        other => other.to_string(),
    }
}
