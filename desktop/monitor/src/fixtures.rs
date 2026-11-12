//! Scenes for the headless checks and for running without Reelshelf or Grafana: the ops answers in `tests/fixtures/`
//! (`ops-*.json`, shaped as the API answers them), each step of signing in, and the fixture dashboards (trimmed copies
//! of the real ones) with made-up but plausible data, worked out from each panel's unit and legend so the charts look
//! like the real thing. Nothing here reaches the network.

use std::collections::HashMap;

use chrono::FixedOffset;

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
pub const ME: &str = include_str!("../tests/fixtures/me.json");

pub const SCENES: &[&str] = &[
    "overview",
    "storage",
    "transcoding",
    "transcoding-copied",
    "ai",
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
    "coming-soon",
    "not-connected",
];

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
        "transcoding" => s.section = Section::Transcoding,
        "transcoding-copied" => {
            s.section = Section::Transcoding;
            s.copied = s.transcoding.value.as_ref().map(|t| t.recent_failures[0].clip_id);
        }
        "ai" => s.section = Section::AiPipelines,
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
        "coming-soon" => s.section = Section::Costs,
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
    fn legends_are_filled_in() {
        assert_eq!(substitute("waiting: {{trigger}}", 1), "waiting: trim");
        assert_eq!(
            substitute("{{http_route}} {{http_response_status_code}}", 0),
            "/api/clips 500"
        );
    }
}
