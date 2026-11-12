//! The HTTP client for GitHub's REST API, authenticated with a fine-grained, read-only token, which is never logged or
//! shown (`Debug` leaves it out, and errors drop the URL). [`Client::releases`] reads everything Releases shows, the
//! parts at once; putting it together is [`releases`](super::releases)' plain functions.

use futures_util::future::join_all;
use reqwest::header::{ACCEPT, AUTHORIZATION, USER_AGENT};
use serde_json::Value;

use super::deploy::{self, Image};
use super::model::{self, Compare, Pull, Run};
use super::releases::{self, Behind, Deployed, PullStatus, Releases, Uploader, WorkflowStatus};
use super::{INFRASTRUCTURE, MAIN, MAIN_WORKFLOWS, REELSHELF, SERVICES, UPLOADER_TAG, UPLOADER_WORKFLOW};

pub const API: &str = "https://api.github.com";

/// What went wrong talking to GitHub, in words the app can show.
#[derive(Debug, thiserror::Error)]
pub enum Error {
    #[error("GitHub answered {status}: {message}")]
    Status { status: u16, message: String },
    #[error("Couldn't reach GitHub: {0}")]
    Network(String),
    #[error("GitHub's answer didn't parse: {0}")]
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
    pub fn new(token: String) -> Client {
        Client {
            base: API.to_owned(),
            token,
            http: reqwest::Client::builder()
                .connect_timeout(std::time::Duration::from_secs(15))
                .timeout(std::time::Duration::from_secs(30))
                .build()
                .unwrap_or_default(),
        }
    }

    /// From `GITHUB_TOKEN`, when set.
    pub fn from_env() -> Option<Client> {
        let token = std::env::var("GITHUB_TOKEN").ok().filter(|t| !t.trim().is_empty())?;
        Some(Client::new(token.trim().to_owned()))
    }

    async fn get(&self, path: &str, accept: &str) -> Result<String, Error> {
        let response = self
            .http
            .get(format!("{}{path}", self.base))
            .header(AUTHORIZATION, format!("Bearer {}", self.token))
            .header(ACCEPT, accept)
            .header(USER_AGENT, format!("reelshelf-monitor/{}", crate::links::version()))
            .header("X-GitHub-Api-Version", "2022-11-28")
            .send()
            .await
            .map_err(|e| Error::Network(e.without_url().to_string()))?;
        let status = response.status();
        let body = response
            .text()
            .await
            .map_err(|e| Error::Network(e.without_url().to_string()))?;
        if status.is_success() {
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

    async fn json(&self, path: &str) -> Result<String, Error> {
        self.get(path, "application/vnd.github+json").await
    }

    /// A file's text, from infrastructure.
    async fn file(&self, path: &str) -> Result<String, Error> {
        self.get(
            &format!("/repos/{INFRASTRUCTURE}/contents/{path}"),
            "application/vnd.github.raw+json",
        )
        .await
    }

    async fn compare(&self, sha: &str) -> Result<Compare, Error> {
        Ok(model::parse_compare(
            &self.json(&format!("/repos/{REELSHELF}/compare/{sha}...{MAIN}")).await?,
        )?)
    }

    async fn open_pulls(&self, repo: &str) -> Result<Vec<Pull>, Error> {
        Ok(model::parse_pulls(
            &self
                .json(&format!("/repos/{repo}/pulls?state=open&per_page=100"))
                .await?,
        )?)
    }

    async fn runs_on(&self, repo: &str, sha: &str) -> Result<Vec<Run>, Error> {
        Ok(model::parse_runs(
            &self
                .json(&format!("/repos/{repo}/actions/runs?head_sha={sha}&per_page=100"))
                .await?,
        )?)
    }

    async fn workflow_runs(&self, file: &str, branch: Option<&str>, count: u8) -> Result<Vec<Run>, Error> {
        let branch = branch.map(|b| format!("&branch={b}")).unwrap_or_default();
        Ok(model::parse_runs(
            &self
                .json(&format!(
                    "/repos/{REELSHELF}/actions/workflows/{file}/runs?per_page={count}{branch}"
                ))
                .await?,
        )?)
    }

    /// Each pull request with how its checks went.
    async fn with_checks(&self, repo: &str, pulls: Vec<Pull>) -> Vec<PullStatus> {
        join_all(pulls.into_iter().map(|pull| async move {
            let checks = self.runs_on(repo, &pull.head.sha).await.ok().map(|r| model::checks(&r));
            PullStatus { pull, checks }
        }))
        .await
    }

    async fn deployed(&self) -> Result<Vec<Deployed>, Error> {
        let entries = model::parse_entries(
            &self
                .json(&format!("/repos/{INFRASTRUCTURE}/contents/{SERVICES}"))
                .await?,
        )?;
        let files = deploy::files(&entries);
        let texts = join_all(files.iter().map(|f| self.file(f))).await;
        let mut images: Vec<Image> = Vec::new();
        for (file, text) in files.iter().zip(texts) {
            images.extend(deploy::images(file, &text?));
        }
        let images = releases::order(images);
        let mut shas: Vec<&str> = images.iter().filter_map(|(_, i)| i.sha()).collect();
        shas.sort_unstable();
        shas.dedup();
        let compared: Vec<(String, Result<Behind, String>)> = join_all(shas.into_iter().map(|sha| async move {
            let behind = self
                .compare(sha)
                .await
                .map(Behind::from_compare)
                .map_err(|e| e.to_string());
            (sha.to_owned(), behind)
        }))
        .await;
        Ok(images
            .into_iter()
            .map(|(environment, image)| {
                let at = match image.sha() {
                    None => Ok(None),
                    Some(sha) => compared
                        .iter()
                        .find(|(s, _)| s == sha)
                        .map(|(_, b)| b.clone().map(Some))
                        .unwrap_or(Ok(None)),
                };
                Deployed { environment, image, at }
            })
            .collect())
    }

    async fn bump(&self) -> Result<Option<PullStatus>, Error> {
        let Some(pull) = releases::bump_pull(self.open_pulls(INFRASTRUCTURE).await?) else {
            return Ok(None);
        };
        Ok(self.with_checks(INFRASTRUCTURE, vec![pull]).await.pop())
    }

    async fn main(&self) -> Result<Vec<WorkflowStatus>, Error> {
        let runs = join_all(MAIN_WORKFLOWS.iter().map(|f| self.workflow_runs(f, Some(MAIN), 5))).await;
        MAIN_WORKFLOWS
            .iter()
            .zip(runs)
            .map(|(file, runs)| Ok(WorkflowStatus::from_runs(file, runs?)))
            .collect()
    }

    async fn uploader(&self) -> Result<Uploader, Error> {
        let (refs, runs) = futures_util::future::join(
            self.json(&format!("/repos/{REELSHELF}/git/matching-refs/tags/{UPLOADER_TAG}")),
            self.workflow_runs(UPLOADER_WORKFLOW, None, 1),
        )
        .await;
        Ok(Uploader {
            tag: releases::latest_uploader_tag(&model::parse_refs(&refs?)?),
            run: runs?.into_iter().next(),
        })
    }

    async fn pulls(&self) -> Result<Vec<PullStatus>, Error> {
        let pulls = self.open_pulls(REELSHELF).await?;
        Ok(self.with_checks(REELSHELF, pulls).await)
    }

    /// Everything Releases shows, each part read at once and failing on its own.
    pub async fn releases(&self) -> Releases {
        let (deployed, bump, main, uploader, pulls) =
            tokio::join!(self.deployed(), self.bump(), self.main(), self.uploader(), self.pulls());
        let words = |e: Error| e.to_string();
        Releases {
            deployed: deployed.map_err(words),
            bump: bump.map_err(words),
            main: main.map_err(words),
            uploader: uploader.map_err(words),
            pulls: pulls.map_err(words),
            read_at: chrono::Utc::now(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debug_never_shows_the_token() {
        let client = Client::new("github_pat_secret".into());
        let shown = format!("{client:?}");
        assert!(!shown.contains("github_pat_secret"), "{shown}");
        assert!(shown.contains("api.github.com"));
    }

    #[test]
    fn errors_name_the_status_and_message() {
        let e = Error::Status {
            status: 401,
            message: "Bad credentials".to_owned(),
        };
        assert_eq!(e.to_string(), "GitHub answered 401: Bad credentials");
    }
}
