//! What Releases shows, put together from GitHub's answers by plain functions (the client only fetches): each deployed
//! image with its commit and the commits main has that it doesn't, the image-bump PR waiting in infrastructure, the
//! latest run of each of main's workflows, the uploader's latest tag and publish run, and the open pull requests. Each
//! part keeps its own error, so a token that can't read one repository still shows the other.

use chrono::{DateTime, Utc};

use super::deploy::Image;
use super::model::{self, Checks, Commit, Compare, Pull, Ref, Run};
use super::{BUMP_BRANCH, UPLOADER_TAG};

/// Prod or dev, from the compose file's name.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum Environment {
    Prod,
    Dev,
}

impl Environment {
    /// `services/clips-dev.yml` and `services/reelshelf-worker-dev.yml` are dev; the rest prod.
    pub fn of(file: &str) -> Environment {
        let name = file.rsplit('/').next().unwrap_or(file);
        if name.trim_end_matches(".yml").ends_with("-dev") {
            Environment::Dev
        } else {
            Environment::Prod
        }
    }
}

/// The commit an image was built from, and what main has that it doesn't.
#[derive(Clone, Debug, PartialEq)]
pub struct Behind {
    pub commit: Commit,
    /// How many commits main is ahead of it.
    pub ahead_by: i64,
    /// Those commits, newest first (GitHub gives at most 250, so maybe fewer than `ahead_by`).
    pub commits: Vec<Commit>,
    /// The comparison on GitHub.
    pub compare_url: String,
}

impl Behind {
    pub fn from_compare(c: Compare) -> Behind {
        let mut commits = c.commits;
        commits.reverse();
        Behind {
            commit: c.base_commit,
            ahead_by: c.ahead_by.max(0),
            commits,
            compare_url: c.html_url,
        }
    }
}

/// One deployed image.
#[derive(Clone, Debug, PartialEq)]
pub struct Deployed {
    pub environment: Environment,
    pub image: Image,
    /// Its commit and how far behind main it is; None for a tag that isn't a commit (dev's `main`); an error when
    /// GitHub couldn't compare it.
    pub at: Result<Option<Behind>, String>,
}

impl Deployed {
    /// A key that stays the same from one read to the next, for the row's controls.
    pub fn key(&self) -> String {
        format!(
            "{}:{}:{}",
            self.image.file,
            self.image.image,
            self.image.pools.join(",")
        )
    }

    pub fn behind(&self) -> Option<&Behind> {
        self.at.as_ref().ok().and_then(Option::as_ref)
    }
}

/// Where an image goes in the list: the app, its migrations, the worker, then the overflow pools' worker.
fn rank(image: &Image) -> (u8, &str) {
    let order = match image.image.as_str() {
        "reelshelf" | "reelshelf-dev-base" => 0,
        "reelshelf-migrations" => 1,
        "reelshelf-worker" if image.pools.is_empty() => 2,
        "reelshelf-worker" => 3,
        _ => 4,
    };
    (order, image.file.as_str())
}

/// The images in the order Releases lists them: prod, then dev.
pub fn order(mut images: Vec<Image>) -> Vec<(Environment, Image)> {
    images.sort_by(|a, b| (Environment::of(&a.file), rank(a)).cmp(&(Environment::of(&b.file), rank(b))));
    images.into_iter().map(|i| (Environment::of(&i.file), i)).collect()
}

/// A pull request and how its checks went.
#[derive(Clone, Debug, PartialEq)]
pub struct PullStatus {
    pub pull: Pull,
    /// None when its runs couldn't be read.
    pub checks: Option<Checks>,
}

/// The newest open PR from `publish-app-images.yml` (its branch is `automation/app-images-<sha>`; it closes older ones).
pub fn bump_pull(pulls: Vec<Pull>) -> Option<Pull> {
    pulls
        .into_iter()
        .filter(|p| p.head.branch.starts_with(BUMP_BRANCH))
        .max_by_key(|p| p.created_at)
}

/// A workflow's latest run on main.
#[derive(Clone, Debug, PartialEq)]
pub struct WorkflowStatus {
    /// `ci.yml`.
    pub file: String,
    pub latest: Option<Run>,
    /// The latest run that finished failed: main is red even while a newer run is going.
    pub red: bool,
}

impl WorkflowStatus {
    /// From the workflow's runs on main, newest first.
    pub fn from_runs(file: &str, runs: Vec<Run>) -> WorkflowStatus {
        let red = runs
            .iter()
            .find(|r| r.checks() != Checks::Running)
            .is_some_and(|r| r.checks() == Checks::Failing);
        WorkflowStatus {
            file: file.to_owned(),
            latest: runs.into_iter().next(),
            red,
        }
    }

    /// The workflow's name, else its file.
    pub fn name(&self) -> &str {
        self.latest
            .as_ref()
            .and_then(|r| r.name.as_deref())
            .unwrap_or(&self.file)
    }
}

/// The uploader's latest release.
#[derive(Clone, Debug, PartialEq)]
pub struct Uploader {
    /// `uploader-v0.1.0`.
    pub tag: Option<String>,
    /// `publish-uploader.yml`'s last run.
    pub run: Option<Run>,
}

/// The highest `uploader-v<major>.<minor>.<patch>` among the refs.
pub fn latest_uploader_tag(refs: &[Ref]) -> Option<String> {
    refs.iter()
        .filter_map(|r| {
            let tag = r.name.strip_prefix("refs/tags/")?;
            let version: Vec<u64> = tag
                .strip_prefix(UPLOADER_TAG)?
                .split('.')
                .map(|n| n.parse().ok())
                .collect::<Option<_>>()?;
            Some((version, tag.to_owned()))
        })
        .max()
        .map(|(_, tag)| tag)
}

/// Everything Releases shows.
#[derive(Clone, Debug, PartialEq)]
pub struct Releases {
    pub deployed: Result<Vec<Deployed>, String>,
    pub bump: Result<Option<PullStatus>, String>,
    pub main: Result<Vec<WorkflowStatus>, String>,
    pub uploader: Result<Uploader, String>,
    pub pulls: Result<Vec<PullStatus>, String>,
    pub read_at: DateTime<Utc>,
}

impl Releases {
    /// Prod's app image, the one people use.
    pub fn prod_app(&self) -> Option<&Deployed> {
        self.deployed
            .as_ref()
            .ok()?
            .iter()
            .find(|d| d.environment == Environment::Prod && d.image.image == "reelshelf" && d.image.pools.is_empty())
    }

    /// How many commits main is ahead of prod's app.
    pub fn prod_behind(&self) -> Option<i64> {
        self.prod_app()?.behind().map(|b| b.ahead_by)
    }

    /// Main's workflows whose latest finished run failed.
    pub fn red(&self) -> Vec<&WorkflowStatus> {
        self.main
            .as_ref()
            .map(|m| m.iter().filter(|w| w.red).collect())
            .unwrap_or_default()
    }

    /// The worst of main's workflows: failing if one's latest finished run failed, else running if one is going.
    pub fn main_checks(&self) -> Option<Checks> {
        let main = self.main.as_ref().ok()?;
        if main.iter().any(|w| w.red) {
            return Some(Checks::Failing);
        }
        let runs: Vec<Run> = main.iter().filter_map(|w| w.latest.clone()).collect();
        Some(model::checks(&runs))
    }
}

#[cfg(test)]
mod tests {
    use super::super::deploy::images;
    use super::super::model::{parse_compare, parse_pulls, parse_refs, parse_runs};
    use super::*;

    const CLIPS: &str = include_str!("../../tests/fixtures/github/clips.yml");
    const WORKER_DEV: &str = include_str!("../../tests/fixtures/github/reelshelf-worker-dev.yml");
    const CLIPS_DEV: &str = include_str!("../../tests/fixtures/github/clips-dev.yml");
    const COMPARE_APP: &str = include_str!("../../tests/fixtures/github/compare-app.json");
    const INFRA_PULLS: &str = include_str!("../../tests/fixtures/github/pulls-infrastructure.json");
    const TAGS: &str = include_str!("../../tests/fixtures/github/tags-uploader.json");
    const CI: &str = include_str!("../../tests/fixtures/github/runs-ci.json");
    const WINDLASS: &str = include_str!("../../tests/fixtures/github/runs-windlass.json");

    #[test]
    fn environments_come_from_the_file_name() {
        assert_eq!(Environment::of("services/clips.yml"), Environment::Prod);
        assert_eq!(Environment::of("services/clips-dev.yml"), Environment::Dev);
        assert_eq!(Environment::of("services/reelshelf-worker.yml"), Environment::Prod);
        assert_eq!(Environment::of("services/reelshelf-worker-dev.yml"), Environment::Dev);
    }

    #[test]
    fn images_list_prod_first_app_first() {
        let mut all = images("services/reelshelf-worker-dev.yml", WORKER_DEV);
        all.extend(images("services/clips-dev.yml", CLIPS_DEV));
        all.extend(images("services/clips.yml", CLIPS));
        let listed: Vec<(Environment, String, usize)> = order(all)
            .into_iter()
            .map(|(e, i)| (e, i.image, i.pools.len()))
            .collect();
        assert_eq!(
            listed,
            vec![
                (Environment::Prod, "reelshelf".to_owned(), 0),
                (Environment::Prod, "reelshelf-migrations".to_owned(), 0),
                (Environment::Prod, "reelshelf-worker".to_owned(), 3),
                (Environment::Dev, "reelshelf-dev-base".to_owned(), 0),
                (Environment::Dev, "reelshelf-worker".to_owned(), 0),
            ]
        );
    }

    #[test]
    fn behind_lists_main_newest_first() {
        let b = Behind::from_compare(parse_compare(COMPARE_APP).unwrap());
        assert_eq!(b.ahead_by, 6);
        assert_eq!(b.commits.len(), 6);
        assert_eq!(
            b.commits[0].title(),
            "Monitor app: sign in, Overview, Storage, Transcoding and AI pipelines (#391)"
        );
        assert_eq!(b.commit.short_sha(), "3f8936c");
    }

    #[test]
    fn the_bump_pr_is_the_automation_branch() {
        let bump = bump_pull(parse_pulls(INFRA_PULLS).unwrap()).unwrap();
        assert_eq!(bump.number, 354);
        assert!(bump.title.starts_with("Bump app images to sha-"));
        let others: Vec<Pull> = parse_pulls(INFRA_PULLS)
            .unwrap()
            .into_iter()
            .filter(|p| p.number != 354)
            .collect();
        assert_eq!(bump_pull(others), None);
    }

    #[test]
    fn the_uploader_tag_is_the_highest_version() {
        let refs = parse_refs(TAGS).unwrap();
        assert_eq!(latest_uploader_tag(&refs).as_deref(), Some("uploader-v0.1.0"));
        let more: Vec<Ref> = ["uploader-v0.9.0", "uploader-v0.10.0", "uploader-vX", "other-v9.0.0"]
            .iter()
            .map(|t| Ref {
                name: format!("refs/tags/{t}"),
            })
            .collect();
        assert_eq!(latest_uploader_tag(&more).as_deref(), Some("uploader-v0.10.0"));
        assert_eq!(latest_uploader_tag(&[]), None);
    }

    #[test]
    fn main_is_red_when_its_latest_finished_run_failed() {
        let ci = WorkflowStatus::from_runs("ci.yml", parse_runs(CI).unwrap());
        assert!(ci.red);
        assert_eq!(ci.name(), "CI");
        let windlass = WorkflowStatus::from_runs("windlass.yml", parse_runs(WINDLASS).unwrap());
        assert!(!windlass.red, "an older failure doesn't count");

        // A run going on top of a failure keeps main red.
        let mut runs = parse_runs(CI).unwrap();
        let mut going = runs[0].clone();
        going.id += 1;
        going.status = Some("in_progress".to_owned());
        going.conclusion = None;
        runs.insert(0, going);
        let ci = WorkflowStatus::from_runs("ci.yml", runs);
        assert!(ci.red);
        assert_eq!(ci.latest.unwrap().checks(), Checks::Running);

        assert_eq!(WorkflowStatus::from_runs("ci.yml", Vec::new()).name(), "ci.yml");
    }
}
