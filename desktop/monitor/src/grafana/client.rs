//! The HTTP client for Grafana: four calls, each authenticated with a service account token (a Viewer's), which is
//! never logged or shown (`Debug` leaves it out).

use reqwest::header::{AUTHORIZATION, CONTENT_TYPE};
use serde_json::Value;

use super::frames::{self, PanelData};
use super::model::{self, Dashboard, SearchHit};

/// What went wrong talking to Grafana, in words the app can show.
#[derive(Debug, thiserror::Error)]
pub enum Error {
    #[error("Grafana answered {status}: {message}")]
    Status { status: u16, message: String },
    #[error("Couldn't reach Grafana: {0}")]
    Network(String),
    #[error("Grafana's answer didn't parse: {0}")]
    Parse(#[from] serde_json::Error),
}

#[derive(Clone)]
pub struct Client {
    base: String,
    token: String,
    http: reqwest::Client,
}

impl std::fmt::Debug for Client {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Client")
            .field("base", &self.base)
            .finish_non_exhaustive()
    }
}

impl Client {
    /// A client for Grafana at `base` (`http://host:3020`, a trailing slash allowed).
    pub fn new(base: &str, token: String) -> Client {
        Client {
            base: base.trim_end_matches('/').to_owned(),
            token,
            http: reqwest::Client::new(),
        }
    }

    /// From `GRAFANA_URL` and `GRAFANA_TOKEN`, when both are set.
    pub fn from_env() -> Option<Client> {
        let url = std::env::var("GRAFANA_URL").ok().filter(|u| !u.trim().is_empty())?;
        let token = std::env::var("GRAFANA_TOKEN").ok().filter(|t| !t.trim().is_empty())?;
        Some(Client::new(url.trim(), token.trim().to_owned()))
    }

    pub fn base(&self) -> &str {
        &self.base
    }

    async fn send(&self, request: reqwest::RequestBuilder) -> Result<String, Error> {
        let response = request
            .header(AUTHORIZATION, format!("Bearer {}", self.token))
            .send()
            .await
            .map_err(|e| Error::Network(e.without_url().to_string()))?;
        let status = response.status();
        let body = response
            .text()
            .await
            .map_err(|e| Error::Network(e.without_url().to_string()))?;
        // `/api/ds/query` answers 400 with frames and per-query errors when one query fails; keep those.
        if status.is_success() || (status.as_u16() == 400 && body.contains("\"results\"")) {
            return Ok(body);
        }
        let message = serde_json::from_str::<Value>(&body)
            .ok()
            .and_then(|v| v["message"].as_str().map(str::to_owned))
            .unwrap_or_else(|| status.canonical_reason().unwrap_or_default().to_owned());
        Err(Error::Status {
            status: status.as_u16(),
            message,
        })
    }

    pub async fn search(&self) -> Result<Vec<SearchHit>, Error> {
        let url = format!("{}/api/search?type=dash-db&limit=500", self.base);
        Ok(model::parse_search(&self.send(self.http.get(url)).await?)?)
    }

    pub async fn dashboard(&self, uid: &str) -> Result<Dashboard, Error> {
        let url = format!("{}/api/dashboards/uid/{}", self.base, encode(uid));
        Ok(model::parse_dashboard(&self.send(self.http.get(url)).await?)?)
    }

    pub async fn query(&self, body: &Value) -> Result<PanelData, Error> {
        let url = format!("{}/api/ds/query", self.base);
        let request = self
            .http
            .post(url)
            .header(CONTENT_TYPE, "application/json")
            .body(body.to_string());
        Ok(frames::parse_response(&self.send(request).await?)?)
    }
}

/// A uid is letters, digits, `-` and `_`; anything else is percent-encoded.
fn encode(s: &str) -> String {
    s.bytes()
        .map(|b| match b {
            b'a'..=b'z' | b'A'..=b'Z' | b'0'..=b'9' | b'-' | b'_' | b'.' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debug_never_shows_the_token() {
        let client = Client::new("http://grafana:3020/", "glsa_secret".into());
        let shown = format!("{client:?}");
        assert!(!shown.contains("glsa_secret"), "{shown}");
        assert_eq!(client.base(), "http://grafana:3020");
    }

    #[test]
    fn uids_are_encoded() {
        assert_eq!(encode("reelshelf-api"), "reelshelf-api");
        assert_eq!(encode("a/b c"), "a%2Fb%20c");
    }
}
