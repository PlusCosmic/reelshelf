//! GitHub's answers, as much of them as Releases reads: commits and comparisons, workflow runs, pull requests, a
//! folder's entries and git refs. Fields GitHub may leave out or null are optional, so a change on its side drops a
//! detail rather than the whole answer.

use chrono::{DateTime, Utc};
use serde::Deserialize;

/// Someone on GitHub.
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Login {
    pub login: String,
}

#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct GitAuthor {
    #[serde(default)]
    pub name: Option<String>,
    #[serde(default)]
    pub date: Option<DateTime<Utc>>,
}

#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct CommitBody {
    pub message: String,
    #[serde(default)]
    pub author: Option<GitAuthor>,
}

/// A commit (`/repos/{repo}/commits/{sha}`, and each of a comparison's).
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Commit {
    pub sha: String,
    pub html_url: String,
    pub commit: CommitBody,
    /// The GitHub account the commit is linked to; null when its email matches none.
    #[serde(default)]
    pub author: Option<Login>,
}

impl Commit {
    /// The message's first line.
    pub fn title(&self) -> &str {
        self.commit.message.lines().next().unwrap_or_default().trim()
    }

    pub fn date(&self) -> Option<DateTime<Utc>> {
        self.commit.author.as_ref().and_then(|a| a.date)
    }

    /// The GitHub login, else the name in the commit.
    pub fn author(&self) -> Option<&str> {
        self.author
            .as_ref()
            .map(|a| a.login.as_str())
            .or_else(|| self.commit.author.as_ref().and_then(|a| a.name.as_deref()))
    }

    pub fn short_sha(&self) -> &str {
        short(&self.sha)
    }
}

/// A sha's first 7 characters, as GitHub shows it.
pub fn short(sha: &str) -> &str {
    sha.get(..7).unwrap_or(sha)
}

/// `/repos/{repo}/compare/{base}...{head}`: how far `head` is ahead of `base`, with up to 250 of those commits,
/// oldest first.
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Compare {
    pub status: String,
    pub ahead_by: i64,
    pub html_url: String,
    pub base_commit: Commit,
    #[serde(default)]
    pub commits: Vec<Commit>,
}

/// A workflow run.
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Run {
    pub id: u64,
    #[serde(default)]
    pub name: Option<String>,
    /// `.github/workflows/ci.yml`.
    #[serde(default)]
    pub path: Option<String>,
    pub head_sha: String,
    /// `queued`, `in_progress`, `completed`, ...
    #[serde(default)]
    pub status: Option<String>,
    /// Once completed: `success`, `failure`, `cancelled`, `skipped`, `timed_out`, ...
    #[serde(default)]
    pub conclusion: Option<String>,
    pub html_url: String,
    pub created_at: DateTime<Utc>,
    #[serde(default)]
    pub updated_at: Option<DateTime<Utc>>,
    #[serde(default)]
    pub display_title: Option<String>,
}

impl Run {
    pub fn checks(&self) -> Checks {
        if self.status.as_deref() != Some("completed") {
            return Checks::Running;
        }
        match self.conclusion.as_deref() {
            Some("success" | "neutral" | "skipped") => Checks::Passing,
            Some("cancelled" | "stale") => Checks::Cancelled,
            Some(_) => Checks::Failing,
            None => Checks::Running,
        }
    }

    /// Which workflow it is a run of, to keep the latest of each.
    fn workflow(&self) -> &str {
        self.path.as_deref().or(self.name.as_deref()).unwrap_or_default()
    }
}

/// `/repos/{repo}/actions/runs` and `/actions/workflows/{file}/runs`, newest first.
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Runs {
    #[serde(default)]
    pub workflow_runs: Vec<Run>,
}

/// Where a pull request's branch points.
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Head {
    #[serde(rename = "ref")]
    pub branch: String,
    pub sha: String,
}

/// An open pull request (`/repos/{repo}/pulls`).
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Pull {
    pub number: u64,
    pub title: String,
    pub html_url: String,
    #[serde(default)]
    pub draft: bool,
    pub created_at: DateTime<Utc>,
    #[serde(default)]
    pub user: Option<Login>,
    pub head: Head,
}

/// One of a folder's entries (`/repos/{repo}/contents/{path}`).
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Entry {
    pub name: String,
    pub path: String,
    #[serde(rename = "type")]
    pub kind: String,
}

/// A git ref (`/repos/{repo}/git/matching-refs/tags/{prefix}`).
#[derive(Clone, Debug, PartialEq, Deserialize)]
pub struct Ref {
    #[serde(rename = "ref")]
    pub name: String,
}

/// How a commit's workflow runs went, worst first.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Checks {
    Failing,
    Running,
    Passing,
    Cancelled,
    /// No workflow ran on it.
    None,
}

/// The state of a commit's runs: the latest run of each workflow, any failing making it failing, else any still
/// running making it running.
pub fn checks(runs: &[Run]) -> Checks {
    let mut latest: Vec<&Run> = Vec::new();
    for run in runs {
        match latest.iter_mut().find(|r| r.workflow() == run.workflow()) {
            Some(r) if (run.created_at, run.id) > (r.created_at, r.id) => *r = run,
            Some(_) => {}
            None => latest.push(run),
        }
    }
    let states: Vec<Checks> = latest.iter().map(|r| r.checks()).collect();
    [Checks::Failing, Checks::Running, Checks::Passing, Checks::Cancelled]
        .into_iter()
        .find(|c| states.contains(c))
        .unwrap_or(Checks::None)
}

pub fn parse_compare(json: &str) -> serde_json::Result<Compare> {
    serde_json::from_str(json)
}

pub fn parse_runs(json: &str) -> serde_json::Result<Vec<Run>> {
    Ok(serde_json::from_str::<Runs>(json)?.workflow_runs)
}

pub fn parse_pulls(json: &str) -> serde_json::Result<Vec<Pull>> {
    serde_json::from_str(json)
}

pub fn parse_entries(json: &str) -> serde_json::Result<Vec<Entry>> {
    serde_json::from_str(json)
}

pub fn parse_refs(json: &str) -> serde_json::Result<Vec<Ref>> {
    serde_json::from_str(json)
}

#[cfg(test)]
mod tests {
    use super::*;

    const COMPARE: &str = include_str!("../../tests/fixtures/github/compare-worker.json");
    const RUNS: &str = include_str!("../../tests/fixtures/github/runs-by-sha.json");
    const PULLS: &str = include_str!("../../tests/fixtures/github/pulls-reelshelf.json");

    fn runs_for(sha: &str) -> Vec<Run> {
        let all: serde_json::Value = serde_json::from_str(RUNS).unwrap();
        parse_runs(&all[sha].to_string()).unwrap()
    }

    #[test]
    fn a_comparison_reads_its_commits() {
        let c = parse_compare(COMPARE).unwrap();
        assert_eq!(c.ahead_by, 29);
        assert_eq!(c.base_commit.short_sha(), "118e6fc");
        assert_eq!(
            c.base_commit.title(),
            "Merge pull request #381 from PlusCosmic/claude/iphone-av1-playback-issue-mm9dup"
        );
        assert_eq!(c.base_commit.author(), Some("PlusCosmic"));
        assert_eq!(c.commits.len(), 8);
        let last = c.commits.last().unwrap();
        assert_eq!(
            last.title(),
            "Monitor app: sign in, Overview, Storage, Transcoding and AI pipelines (#391)"
        );
        assert!(last.date().is_some());
    }

    #[test]
    fn a_commit_without_an_account_shows_its_name() {
        let c: Commit = serde_json::from_str(
            r#"{"sha":"abc","html_url":"u","commit":{"message":"Fix\n\nMore","author":{"name":"Someone","date":null}},"author":null}"#,
        )
        .unwrap();
        assert_eq!(c.title(), "Fix");
        assert_eq!(c.author(), Some("Someone"));
        assert_eq!(c.short_sha(), "abc");
    }

    #[test]
    fn checks_take_the_latest_run_of_each_workflow() {
        assert_eq!(
            checks(&runs_for("dc6b0c71f814f8de9f404941b5f89a2b8abe973e")),
            Checks::Passing
        );
        assert_eq!(
            checks(&runs_for("130a179e367836ee5d376a0e7f916e236dc49f9a")),
            Checks::Failing
        );
        assert_eq!(
            checks(&runs_for("11433cedf0a0ceab1c1459ea412a68693ad47c89")),
            Checks::Running
        );
        assert_eq!(
            checks(&runs_for("e15394fe1e34ca22673a907fc8232059b25bf145")),
            Checks::None
        );
    }

    #[test]
    fn a_rerun_that_passed_replaces_the_failure() {
        let mut runs = runs_for("dc6b0c71f814f8de9f404941b5f89a2b8abe973e");
        let mut failed = runs[2].clone();
        failed.id -= 1;
        failed.created_at -= chrono::Duration::minutes(5);
        failed.conclusion = Some("failure".to_owned());
        runs.push(failed);
        assert_eq!(checks(&runs), Checks::Passing);
    }

    #[test]
    fn run_states() {
        let mut run = runs_for("dc6b0c71f814f8de9f404941b5f89a2b8abe973e").remove(0);
        for (status, conclusion, want) in [
            ("completed", Some("success"), Checks::Passing),
            ("completed", Some("timed_out"), Checks::Failing),
            ("completed", Some("cancelled"), Checks::Cancelled),
            ("queued", None, Checks::Running),
            ("in_progress", None, Checks::Running),
        ] {
            run.status = Some(status.to_owned());
            run.conclusion = conclusion.map(str::to_owned);
            assert_eq!(run.checks(), want, "{status} {conclusion:?}");
        }
    }

    #[test]
    fn pulls_read_their_head_and_draft() {
        let pulls = parse_pulls(PULLS).unwrap();
        assert_eq!(pulls.len(), 3);
        assert_eq!(pulls[0].number, 391);
        assert!(!pulls[0].draft && pulls[2].draft);
        assert_eq!(pulls[0].head.sha, "dc6b0c71f814f8de9f404941b5f89a2b8abe973e");
        assert_eq!(pulls[0].user.as_ref().unwrap().login, "PlusCosmic");
    }
}
