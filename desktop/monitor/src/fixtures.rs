//! Scenes for the headless checks and for running without Reelshelf or Grafana: the ops answers in `tests/fixtures/`
//! (`ops-*.json`, shaped as the API answers them), each step of signing in, and the fixture dashboards (trimmed copies
//! of the real ones) with made-up but plausible data, worked out from each panel's unit and legend so the charts look
//! like the real thing, and GitHub's answers for Releases (`tests/fixtures/github/`, trimmed copies of real ones, with
//! main's CI turned red), put together by the same functions as a live read. Nothing here reaches the network.

use std::collections::HashMap;

use chrono::FixedOffset;

use crate::admin::{AdminTab, Change, Confirm, StatusFilter};
use crate::github::deploy::{self, Image};
use crate::github::model::{checks, parse_compare, parse_entries, parse_pulls, parse_refs, parse_runs};
use crate::github::releases::{self, Behind, Deployed, PullStatus, Uploader, WorkflowStatus};
use crate::github::{MAIN_WORKFLOWS, Releases};
use crate::grafana::model::{parse_dashboard, parse_search};
use crate::grafana::{Dashboard, Panel, PanelData, PanelKind, Series, TimeRange, query};
use crate::session::{Pending, Refusal, Session, SignedOutReason};
use crate::state::{Fetched, Hover, Load, Monitor, Opened, Section};

/// The scenes' "now": a fixed instant, so renders don't change from run to run.
pub const NOW: i64 = 1_731_420_000_000;
pub const GRAFANA: &str = "http://grafana.example:3020";

const SEARCH: &str = include_str!("../tests/fixtures/search.json");
const API: &str = include_str!("../tests/fixtures/dashboard-api.json");
const PIPELINE: &str = include_str!("../tests/fixtures/dashboard-pipeline.json");
pub const OPS_STORAGE: &str = include_str!("../tests/fixtures/ops-storage.json");
pub const OPS_TRANSCODING: &str = include_str!("../tests/fixtures/ops-transcoding.json");
pub const OPS_AI: &str = include_str!("../tests/fixtures/ops-ai.json");
pub const OPS_COSTS: &str = include_str!("../tests/fixtures/ops-costs.json");
pub const ME: &str = include_str!("../tests/fixtures/me.json");
pub const ADMIN_REPORTS_OPEN: &str = include_str!("../tests/fixtures/admin-reports-open.json");
pub const ADMIN_REPORTS_RESOLVED: &str = include_str!("../tests/fixtures/admin-reports-resolved.json");
pub const ADMIN_ACCOUNTS: &str = include_str!("../tests/fixtures/admin-accounts.json");
pub const ADMIN_ACCOUNT: &str = include_str!("../tests/fixtures/admin-account.json");

/// "Now" for the GitHub scenes, which were read on 12 November 2026.
pub const GITHUB_NOW: i64 = 1_794_516_000_000;

/// GitHub's answers, by what they are.
pub const GITHUB: &[(&str, &str)] = &[
    ("services", include_str!("../tests/fixtures/github/services.json")),
    ("services/clips.yml", include_str!("../tests/fixtures/github/clips.yml")),
    (
        "services/clips-dev.yml",
        include_str!("../tests/fixtures/github/clips-dev.yml"),
    ),
    (
        "services/reelshelf-worker.yml",
        include_str!("../tests/fixtures/github/reelshelf-worker.yml"),
    ),
    (
        "services/reelshelf-worker-dev.yml",
        include_str!("../tests/fixtures/github/reelshelf-worker-dev.yml"),
    ),
    ("compare-app", include_str!("../tests/fixtures/github/compare-app.json")),
    (
        "compare-worker",
        include_str!("../tests/fixtures/github/compare-worker.json"),
    ),
    (
        "compare-main",
        include_str!("../tests/fixtures/github/compare-main.json"),
    ),
    (
        "pulls-infrastructure",
        include_str!("../tests/fixtures/github/pulls-infrastructure.json"),
    ),
    (
        "pulls-reelshelf",
        include_str!("../tests/fixtures/github/pulls-reelshelf.json"),
    ),
    ("runs-by-sha", include_str!("../tests/fixtures/github/runs-by-sha.json")),
    ("ci.yml", include_str!("../tests/fixtures/github/runs-ci.json")),
    (
        "publish-app-images.yml",
        include_str!("../tests/fixtures/github/runs-publish-app-images.json"),
    ),
    (
        "windlass.yml",
        include_str!("../tests/fixtures/github/runs-windlass.json"),
    ),
    (
        "publish-uploader.yml",
        include_str!("../tests/fixtures/github/runs-publish-uploader.json"),
    ),
    ("tags", include_str!("../tests/fixtures/github/tags-uploader.json")),
];

fn github(name: &str) -> &'static str {
    GITHUB
        .iter()
        .find(|(n, _)| *n == name)
        .map(|(_, json)| *json)
        .unwrap_or_else(|| panic!("no GitHub fixture {name}"))
}

/// Releases as the fixtures answer it, put together as `github::Client::releases` does.
pub fn releases() -> Releases {
    let files = deploy::files(&parse_entries(github("services")).expect("services fixture"));
    let images: Vec<Image> = files.iter().flat_map(|f| deploy::images(f, github(f))).collect();
    let compares: Vec<Behind> = ["compare-app", "compare-worker", "compare-main"]
        .iter()
        .map(|n| Behind::from_compare(parse_compare(github(n)).expect("compare fixture")))
        .collect();
    let deployed = releases::order(images)
        .into_iter()
        .map(|(environment, image)| {
            let at = match image.sha() {
                None => Ok(None),
                Some(sha) => compares
                    .iter()
                    .find(|b| b.commit.sha == sha)
                    .cloned()
                    .map(Some)
                    .ok_or_else(|| "GitHub answered 404: No commit found".to_owned()),
            };
            Deployed { environment, image, at }
        })
        .collect();
    let runs: serde_json::Value = serde_json::from_str(github("runs-by-sha")).expect("runs fixture");
    let with_checks = |pull| {
        let pull: crate::github::Pull = pull;
        let found = runs
            .get(&pull.head.sha)
            .map(|r| parse_runs(&r.to_string()).expect("runs"));
        PullStatus {
            checks: found.map(|r| checks(&r)),
            pull,
        }
    };
    let bump =
        releases::bump_pull(parse_pulls(github("pulls-infrastructure")).expect("pulls fixture")).map(with_checks);
    let main = MAIN_WORKFLOWS
        .iter()
        .map(|f| WorkflowStatus::from_runs(f, parse_runs(github(f)).expect("runs fixture")))
        .collect();
    let uploader = Uploader {
        tag: releases::latest_uploader_tag(&parse_refs(github("tags")).expect("tags fixture")),
        run: parse_runs(github("publish-uploader.yml"))
            .expect("runs fixture")
            .into_iter()
            .next(),
    };
    let pulls = parse_pulls(github("pulls-reelshelf"))
        .expect("pulls fixture")
        .into_iter()
        .map(with_checks)
        .collect();
    Releases {
        deployed: Ok(deployed),
        bump: Ok(bump),
        main: Ok(main),
        uploader: Ok(uploader),
        pulls: Ok(pulls),
        read_at: chrono::DateTime::from_timestamp_millis(GITHUB_NOW).expect("a time"),
    }
}

pub const SCENES: &[&str] = &[
    "overview",
    "storage",
    "storage-hover",
    "storage-attention",
    "storage-denied",
    "storage-r2",
    "overview-storage-attention",
    "transcoding",
    "transcoding-copied",
    "ai",
    "costs",
    "costs-hover",
    "costs-not-set-up",
    "signed-out",
    "signed-out-revoked",
    "signing-in",
    "not-admin",
    "flag-off",
    "update-required",
    "settings",
    "settings-signed-out",
    "dashboards",
    "dashboard-api",
    "dashboard-pipeline",
    "dashboard-hover",
    "releases",
    "releases-collapsed",
    "releases-unreadable",
    "releases-not-connected",
    "admin-reports",
    "admin-report-child-safety",
    "admin-reports-removed",
    "admin-accounts",
    "admin-account-self",
    "admin-confirm-remove",
    "admin-confirm-suspend",
    "admin-confirm-error",
    "admin-forbidden",
    "not-connected",
];

/// The storage fixture gone wrong: the bucket's difference from the database grows by 3 GiB a day for the last week,
/// and CloudFront answered 1.84% of the last day's requests with a 5xx.
fn storage_attention(s: &mut Monitor) -> Option<()> {
    let storage = s.storage.value.as_mut()?;
    let gap = storage.bucket.as_mut()?.gap.as_mut()?;
    let days = gap.days.len();
    for (i, day) in gap.days.iter_mut().enumerate() {
        let extra = (i + 8).saturating_sub(days) as i64 * (1 << 30) * 3;
        day.bucket_bytes += extra;
        day.difference_bytes += extra;
    }
    let first = gap
        .days
        .iter()
        .find(|d| d.day >= gap.bucket_day - chrono::Days::new(7))?
        .difference_bytes;
    let last = gap.days.last()?;
    gap.bucket_bytes = last.bucket_bytes;
    gap.difference_bytes = last.difference_bytes;
    gap.growth_last_week_bytes = Some(last.difference_bytes - first);
    let cdn = storage.cdn.as_mut()?;
    let n = cdn.server_error_rate.len();
    for point in cdn.server_error_rate.iter_mut().skip(n.saturating_sub(5)) {
        point.value = 8.6;
    }
    cdn.server_error_rate_last_day = Some(1.84);
    Some(())
}

/// A scene's state, by name.
pub fn scene(name: &str, tz: FixedOffset) -> Option<Monitor> {
    let mut s = Monitor::new(None, Some(GRAFANA.to_owned()), tz);
    s.list = Load::Ready(parse_search(SEARCH).expect("search fixture"));
    s.now_ms = NOW;
    s.version = "0.1.0".to_owned();
    signed_in(&mut s);
    match name {
        "overview" => {}
        "storage" => s.section = Section::Storage,
        "storage-hover" => {
            s.section = Section::Storage;
            // Over the CloudFront error rates' chart.
            s.hover = Some(Hover {
                panel: 91_005,
                x: 700.0,
            });
        }
        "storage-attention" => {
            s.section = Section::Storage;
            storage_attention(&mut s)?;
        }
        "overview-storage-attention" => storage_attention(&mut s)?,
        "storage-denied" => {
            s.section = Section::Storage;
            let storage = s.storage.value.as_mut()?;
            let refused = "User: arn:aws:iam::123456789012:user/reelshelf-media is not authorized to perform: cloudwatch:GetMetricData";
            storage.bucket = Some(reelshelf_api::OpsBucketMetrics {
                status: "denied".to_owned(),
                error: Some(refused.to_owned()),
                permission: Some("cloudwatch:GetMetricData".to_owned()),
                sizes: Vec::new(),
                objects: Vec::new(),
                gap: None,
                ..storage.bucket.clone()?
            });
            storage.cdn = Some(reelshelf_api::OpsCdnMetrics {
                status: "denied".to_owned(),
                error: Some(refused.to_owned()),
                permission: Some("cloudwatch:GetMetricData".to_owned()),
                requests: Vec::new(),
                bytes_downloaded: Vec::new(),
                client_error_rate: Vec::new(),
                server_error_rate: Vec::new(),
                server_error_rate_last_day: None,
                ..storage.cdn.clone()?
            });
        }
        "storage-r2" => {
            s.section = Section::Storage;
            let storage = s.storage.value.as_mut()?;
            storage.store = reelshelf_api::OpsObjectStore {
                configured: true,
                kind: Some("s3-compatible".to_owned()),
                region: Some("auto".to_owned()),
                endpoint_host: Some("0f3c8e2a.r2.cloudflarestorage.com".to_owned()),
                bucket: Some("reelshelf-clips".to_owned()),
                media_base_url: Some("https://media.reelshelf.app".to_owned()),
            };
            storage.bucket = Some(reelshelf_api::OpsBucketMetrics {
                status: "unavailable".to_owned(),
                error: None,
                permission: None,
                read_at: None,
                sizes: Vec::new(),
                objects: Vec::new(),
                gap: None,
            });
            storage.cdn = Some(reelshelf_api::OpsCdnMetrics {
                status: "not_configured".to_owned(),
                error: None,
                permission: None,
                distribution_id: None,
                read_at: None,
                requests: Vec::new(),
                bytes_downloaded: Vec::new(),
                client_error_rate: Vec::new(),
                server_error_rate: Vec::new(),
                server_error_rate_last_day: None,
            });
        }
        "transcoding" => s.section = Section::Transcoding,
        "transcoding-copied" => {
            s.section = Section::Transcoding;
            s.copied = s.transcoding.value.as_ref().map(|t| t.recent_failures[0].clip_id);
        }
        "ai" => s.section = Section::AiPipelines,
        "costs" => s.section = Section::Costs,
        "costs-hover" => {
            s.section = Section::Costs;
            // Over AWS's chart (the first provider's).
            s.hover = Some(Hover {
                panel: 90_000,
                x: 520.0,
            });
        }
        "costs-not-set-up" => {
            s.section = Section::Costs;
            let costs = s.costs.value.as_mut()?;
            for p in &mut costs.providers {
                *p = reelshelf_api::OpsCostProvider {
                    status: "not_configured".to_owned(),
                    error: None,
                    last_success_at: None,
                    currency: None,
                    month_to_date: None,
                    last_month: None,
                    daily: Vec::new(),
                    breakdown: Vec::new(),
                    balance: None,
                    revenue: None,
                    ..p.clone()
                };
            }
            costs.month_to_date_usd = 0.0;
        }
        "signed-out" => s.set_session(Session::SignedOut(None)),
        "signed-out-revoked" => {
            s.section = Section::Transcoding;
            s.set_session(Session::SignedOut(Some(SignedOutReason::Revoked)));
        }
        "signing-in" => s.set_session(Session::Waiting(Pending {
            device_code: "fixture".to_owned(),
            user_code: "BCDFGHJK".to_owned(),
            verification_uri: "https://reelshelf.app/device".to_owned(),
            verification_uri_complete: "https://reelshelf.app/device?code=BCDF-GHJK".to_owned(),
            interval: std::time::Duration::from_secs(5),
            expires_at: tokio::time::Instant::now() + std::time::Duration::from_secs(600),
        })),
        "not-admin" => s.refusal = Some(Refusal::NotAdmin),
        "flag-off" => {
            s.section = Section::Storage;
            s.refusal = Some(Refusal::FlagOff);
        }
        "update-required" => s.set_session(Session::UpdateRequired),
        "settings" => s.section = Section::Settings,
        "settings-signed-out" => {
            s = Monitor::new(None, None, tz);
            s.version = "0.1.0".to_owned();
            s.section = Section::Settings;
        }
        "dashboards" => s.section = Section::Dashboards,
        "dashboard-api" => {
            s.section = Section::Dashboards;
            open(&mut s, API, TimeRange::Last6Hours);
        }
        "dashboard-pipeline" => {
            s.section = Section::Dashboards;
            open(&mut s, PIPELINE, TimeRange::Last6Hours);
        }
        "dashboard-hover" => {
            s.section = Section::Dashboards;
            open(&mut s, API, TimeRange::Last24Hours);
            let latency = dashboard(API)
                .panels
                .iter()
                .find(|p| p.title == "Latency")
                .map(|p| p.id)?;
            s.hover = Some(Hover {
                panel: latency,
                x: 380.0,
            });
        }
        "releases" => {
            s.section = Section::Releases;
            s.now_ms = GITHUB_NOW;
        }
        "releases-collapsed" => {
            s.section = Section::Releases;
            s.now_ms = GITHUB_NOW;
            s.release_open = None;
            s.release_touched = true;
        }
        "releases-unreadable" => {
            s.section = Section::Releases;
            s.now_ms = GITHUB_NOW;
            let mut r = releases();
            let refused = "GitHub answered 404: Not Found".to_owned();
            r.deployed = Err(refused.clone());
            r.bump = Err(refused);
            s.set_releases(r);
        }
        "releases-not-connected" => {
            s.section = Section::Releases;
            s.github = false;
            s.releases = Fetched::default();
        }
        "admin-reports" => {
            reports(&mut s, StatusFilter::Open);
            s.admin.report = Some(report(&s, "copyright"));
        }
        "admin-report-child-safety" => {
            reports(&mut s, StatusFilter::Open);
            s.admin.report = Some(report(&s, "child_safety"));
        }
        "admin-reports-removed" => {
            reports(&mut s, StatusFilter::Removed);
            s.admin.report = s.admin.reports().first().map(|r| r.id);
        }
        "admin-accounts" => accounts(&mut s, ADMIN_ACCOUNT),
        "admin-account-self" => {
            let me: reelshelf_api::AdminAccount =
                serde_json::from_str::<Vec<reelshelf_api::AdminAccount>>(ADMIN_ACCOUNTS).expect("accounts fixture")[2]
                    .clone();
            let detail = reelshelf_api::AdminAccountDetail {
                account: me,
                tier: "free".to_owned(),
                changes: Vec::new(),
            };
            accounts(&mut s, &serde_json::to_string(&detail).expect("detail"));
        }
        "admin-confirm-remove" => {
            reports(&mut s, StatusFilter::Open);
            let id = report(&s, "terrorism");
            s.admin.report = Some(id);
            let mut c = Confirm::new(Change::Remove {
                report: id,
                clip: "Lobby warm-up".to_owned(),
                owner: Some("driftwood".to_owned()),
            });
            c.note
                .replace_text("Extremist flag and chanting; referred to the police.");
            s.admin.confirm = Some(c);
        }
        "admin-confirm-suspend" => {
            accounts(&mut s, ADMIN_ACCOUNT);
            let id = s.admin.account?;
            s.admin.confirm = Some(Confirm::new(Change::Suspend {
                account: id,
                name: "Quiet Fox".to_owned(),
            }));
        }
        "admin-confirm-error" => {
            reports(&mut s, StatusFilter::Open);
            let id = report(&s, "copyright");
            s.admin.report = Some(id);
            let mut c = Confirm::new(Change::TakeDown {
                report: id,
                clip: "Triple down in Storm Point".to_owned(),
            });
            c.note.replace_text("Reupload of the reporter's stream.");
            c.error = Some(FIXTURE_CONFLICT.to_owned());
            s.admin.confirm = Some(c);
        }
        "admin-forbidden" => {
            s.section = Section::Admin;
            s.admin.forbidden = true;
        }
        "not-connected" => {
            s = Monitor::new(None, None, tz);
            s.section = Section::Dashboards;
        }
        _ => return None,
    }
    Some(s)
}

/// Signed in as an Admin, with the fixture ops answers read.
fn signed_in(s: &mut Monitor) {
    s.session = Session::SignedIn {
        user: Some(serde_json::from_str(ME).expect("me fixture")),
    };
    s.storage = Fetched::ready(serde_json::from_str(OPS_STORAGE).expect("storage fixture"));
    s.transcoding = Fetched::ready(serde_json::from_str(OPS_TRANSCODING).expect("transcoding fixture"));
    s.ai = Fetched::ready(serde_json::from_str(OPS_AI).expect("ai fixture"));
    s.costs = Fetched::ready(serde_json::from_str(OPS_COSTS).expect("costs fixture"));
    s.github = true;
    s.set_releases(releases());
    s.admin.open = Fetched::ready(serde_json::from_str(ADMIN_REPORTS_OPEN).expect("reports fixture"));
}

/// The API's words for a report someone else dealt with first.
pub const FIXTURE_CONFLICT: &str = "That report has already been dealt with.";

/// Admin's Reports tab on `status`, both lists read.
fn reports(s: &mut Monitor, status: StatusFilter) {
    s.section = Section::Admin;
    s.admin.tab = AdminTab::Reports;
    s.admin.status = status;
    s.admin.resolved = Fetched::ready(serde_json::from_str(ADMIN_REPORTS_RESOLVED).expect("resolved fixture"));
}

/// The first report for `reason` in either list.
fn report(s: &Monitor, reason: &str) -> uuid::Uuid {
    s.admin
        .open
        .value
        .iter()
        .chain(s.admin.resolved.value.iter())
        .flatten()
        .find(|r| r.reason == reason)
        .map(|r| r.id)
        .expect("a report for the reason")
}

/// Admin's Accounts tab, searched for "quiet", with `detail` open.
fn accounts(s: &mut Monitor, detail: &str) {
    s.section = Section::Admin;
    s.admin.tab = AdminTab::Accounts;
    s.admin.query.replace_text("quiet");
    s.admin.searched = "quiet".to_owned();
    s.admin.results = Some(Load::Ready(
        serde_json::from_str(ADMIN_ACCOUNTS).expect("accounts fixture"),
    ));
    let detail: reelshelf_api::AdminAccountDetail = serde_json::from_str(detail).expect("account fixture");
    s.admin.account = Some(detail.account.id);
    s.admin.detail = Some(Load::Ready(detail));
}

fn dashboard(json: &str) -> Dashboard {
    parse_dashboard(json).expect("dashboard fixture")
}

fn open(s: &mut Monitor, json: &str, range: TimeRange) {
    let d = dashboard(json);
    let span = range.span(NOW);
    let data: HashMap<u32, Load<PanelData>> = d
        .panels
        .iter()
        .filter(|p| query::is_drawn(p))
        .map(|p| (p.id, Load::Ready(made_up(p, span))))
        .collect();
    s.range = range;
    s.opened = Some(Opened {
        uid: d.uid.clone(),
        dashboard: Load::Ready(d),
        data,
        span,
        generation: 0,
    });
}

/// Values to stand in for a legend's labels (`{{status}}` and the like), by label name.
fn label_values(label: &str) -> [&'static str; 3] {
    match label {
        "status" => ["2xx", "4xx", "5xx"],
        "http_route" => ["/api/clips", "/api/me/plan", "/api/clips/{id}"],
        "http_response_status_code" => ["500", "502", "503"],
        "server_address" => ["api.openrouter.ai", "s3.amazonaws.com", "api.igdb.com"],
        "aspnetcore_rate_limiting_policy" => ["clip-prepare", "device-token", "clip-search"],
        "error_type" => ["TaskCanceledException", "HttpRequestException", "NpgsqlException"],
        "pool" => ["ec2", "ecs", "batch"],
        _ => ["transcode", "trim", "audio"],
    }
}

/// Data shaped like the panel's: a typical size for its unit, one series per target, several for a target that
/// splits by a label.
pub fn made_up(panel: &Panel, (from, to): (i64, i64)) -> PanelData {
    let base = match panel.unit.as_deref() {
        Some("reqps") => 6.0,
        Some("s") => 0.18,
        Some("percentunit") => 0.012,
        Some("bytes") => 310.0 * 1024.0 * 1024.0,
        _ => 4.0,
    };
    let step = (to - from) / 240;
    let mut series = Vec::new();
    for (ti, target) in panel.targets.iter().enumerate() {
        let legend = target["legendFormat"].as_str().unwrap_or_default();
        let splits = if legend.contains("{{") { 3 } else { 1 };
        for k in 0..splits {
            let n = series.len() as f64;
            let name = if legend.is_empty() {
                target["refId"].as_str().unwrap_or("A").to_owned()
            } else {
                substitute(legend, k)
            };
            let scale = base * (1.0 + ti as f64 * 1.6) / (1.0 + k as f64 * 0.8);
            let points = (0..=240)
                .map(|i| {
                    let t = from + i * step;
                    let x = i as f64 / 240.0;
                    let v = scale
                        * (1.0
                            + 0.35 * (x * 9.0 + n * 1.3).sin()
                            + 0.12 * (x * 41.0 + n).sin()
                            + 0.25 * (-((x - 0.62 - n * 0.05) * 14.0).powi(2)).exp());
                    // A gap, as a scrape that didn't happen leaves one.
                    let value = (!(150..154).contains(&i) || k > 0).then_some(v.max(0.0));
                    (t, value)
                })
                .collect();
            series.push(Series { name, points });
        }
    }
    if panel.kind == PanelKind::Stat {
        for s in &mut series {
            s.points = s.points.split_off(s.points.len() - 1);
        }
    }
    PanelData { series, error: None }
}

/// The legend with each `{{label}}` filled in with that label's `k`th stand-in value.
fn substitute(legend: &str, k: usize) -> String {
    let mut out = String::new();
    let mut rest = legend;
    while let Some(at) = rest.find("{{") {
        out.push_str(&rest[..at]);
        let Some(end) = rest[at..].find("}}") else {
            break;
        };
        out.push_str(label_values(rest[at + 2..at + end].trim())[k % 3]);
        rest = &rest[at + end + 2..];
    }
    out.push_str(rest);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_scene_builds() {
        let tz = FixedOffset::east_opt(0).unwrap();
        for name in SCENES {
            assert!(scene(name, tz).is_some(), "{name}");
        }
    }

    #[test]
    fn sections_are_found_by_name() {
        assert_eq!(Section::named("ai-pipelines"), Some(Section::AiPipelines));
        assert_eq!(Section::named("Storage"), Some(Section::Storage));
        assert_eq!(Section::named("nope"), None);
    }

    #[test]
    fn legends_are_filled_in() {
        assert_eq!(substitute("waiting: {{trigger}}", 1), "waiting: trim");
        assert_eq!(
            substitute("{{http_route}} {{http_response_status_code}}", 0),
            "/api/clips 500"
        );
    }
}
