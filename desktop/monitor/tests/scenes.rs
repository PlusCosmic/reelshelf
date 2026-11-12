//! The headless checks, in the uploader's style: every scene renders light and dark (to `$REELSHELF_RENDERS`, else a
//! folder under the target directory, at `REELSHELF_RENDER_SCALE`, 2 by default), shows only words from `copy.rs`,
//! Reelshelf's or Grafana's data, or numbers with their units, and names every control.

use std::collections::HashSet;
use std::path::PathBuf;

use chrono::FixedOffset;
use reelshelf_app::theme;
use reelshelf_monitor::grafana::query;
use reelshelf_monitor::session::Session;
use reelshelf_monitor::state::{Load, Monitor};
use reelshelf_monitor::{fixtures, ui};
use regex::Regex;
use rune::text::TextSystem;
use rune::{Env, Runtime};
use serde_json::Value;

const COPY: &str = include_str!("../src/copy.rs");

fn tz() -> FixedOffset {
    FixedOffset::east_opt(0).unwrap()
}

fn mode(dark: bool) -> &'static str {
    if dark { "dark" } else { "light" }
}

fn runtime(name: &str, dark: bool) -> Runtime<Monitor> {
    let s = fixtures::scene(name, tz()).unwrap();
    let text = TextSystem::new(theme::FONTS, theme::FAMILIES);
    let mut rt = Runtime::with_text(s, ui::view, ui::SIZE, Env::new(dark, 1.0), text);
    // The grid learns its width from its first layout; let it and the springs settle.
    for _ in 0..90 {
        rt.frame(1.0 / 60.0);
    }
    rt
}

/// The words `copy.rs` can produce: its literals, and its format strings as patterns.
fn catalogue() -> (HashSet<String>, Vec<Regex>) {
    let literal = Regex::new(r#""((?:[^"\\]|\\.)*)""#).unwrap();
    let mut exact = HashSet::new();
    let mut patterns = Vec::new();
    for found in literal.captures_iter(COPY) {
        let raw = found[1].replace("\\\"", "\"");
        if raw.contains('{') {
            let pattern: String = raw
                .split(['{', '}'])
                .enumerate()
                .map(|(i, part)| {
                    if i % 2 == 0 {
                        regex::escape(part)
                    } else {
                        ".+".to_owned()
                    }
                })
                .collect();
            patterns.push(Regex::new(&format!("^{pattern}$")).unwrap());
        } else {
            exact.insert(raw);
        }
    }
    (exact, patterns)
}

/// Every string in a JSON answer.
fn strings(value: &Value, into: &mut HashSet<String>) {
    match value {
        Value::String(s) => {
            into.insert(s.clone());
        }
        Value::Array(items) => items.iter().for_each(|v| strings(v, into)),
        Value::Object(map) => map.values().for_each(|v| strings(v, into)),
        _ => {}
    }
}

/// What Reelshelf, GitHub and Grafana say in a scene: usernames, queue, trigger, pool and model names, errors and clip
/// ids; commit, pull request and run titles; dashboard, folder, panel and series names.
fn data(s: &Monitor) -> HashSet<String> {
    let mut words = HashSet::new();
    for json in [
        fixtures::OPS_STORAGE,
        fixtures::OPS_TRANSCODING,
        fixtures::OPS_AI,
        fixtures::OPS_COSTS,
        fixtures::ME,
        fixtures::ADMIN_REPORTS_OPEN,
        fixtures::ADMIN_REPORTS_RESOLVED,
        fixtures::ADMIN_ACCOUNTS,
        fixtures::ADMIN_ACCOUNT,
        &format!("{:?}", fixtures::FIXTURE_CONFLICT),
    ] {
        strings(&serde_json::from_str(json).unwrap(), &mut words);
    }
    // GitHub's: commit, pull request and run titles, logins, workflow names and tags.
    for (_, answer) in fixtures::GITHUB {
        if let Ok(json) = serde_json::from_str::<Value>(answer) {
            strings(&json, &mut words);
        }
    }
    // What the person typed.
    words.insert(s.grafana_form.url.text.clone());
    words.insert(s.admin.query.text.clone());
    if let Some(c) = &s.admin.confirm {
        words.insert(c.note.text.clone());
    }
    if let Session::Waiting(pending) = &s.session {
        words.insert(pending.shown_code());
    }
    if let Load::Ready(hits) = &s.list {
        for h in hits {
            words.insert(h.title.clone());
            words.extend(h.folder_title.clone());
        }
    }
    if let Some(o) = &s.opened
        && let Load::Ready(d) = &o.dashboard
    {
        words.insert(d.title.clone());
        for p in &d.panels {
            words.insert(p.title.clone());
            if let Some(Load::Ready(data)) = o.data.get(&p.id) {
                words.extend(data.series.iter().map(|s| s.name.clone()));
            }
        }
    }
    words
}

/// A number with a unit (`250 ms`, `6.75 req/s`, `1.35%`), an axis time (`13:00`, `Tue 12`) or a readout's
/// (`Tue 12 Nov, 09:48`).
fn is_number_or_time(words: &str) -> bool {
    let number = Regex::new(
        r"^[-$£€]?[\d.,]+ ?(%|req/s|ops/s|µs|ms|s|min|hour|day|K|M|B|T|B/s|KiB|MiB|GiB|TiB|PiB|kB|MB|GB|TB|PB|KiB/s|MiB/s)?$",
    )
    .unwrap();
    let time =
        Regex::new(r"^((Mon|Tue|Wed|Thu|Fri|Sat|Sun) \d{1,2}( [A-Z][a-z]{2})?,? ?)?(\d{2}:\d{2}(:\d{2})?)?$").unwrap();
    number.is_match(words) || time.is_match(words)
}

#[test]
fn every_scene_renders_light_and_dark() {
    let dir = std::env::var_os("REELSHELF_RENDERS")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(env!("CARGO_TARGET_TMPDIR")).join("monitor-renders"));
    std::fs::create_dir_all(&dir).unwrap();
    let scale = std::env::var("REELSHELF_RENDER_SCALE").unwrap_or_else(|_| "2".to_owned());
    for name in fixtures::SCENES {
        for dark in [false, true] {
            let path = dir.join(format!("monitor-{name}-{}.png", mode(dark)));
            let _ = std::fs::remove_file(&path);
            let args = [
                "--snap",
                &path.to_string_lossy(),
                "--theme",
                mode(dark),
                "--scale",
                &scale,
                // After the grid's first layout.
                "--at",
                "0.5",
            ]
            .map(str::to_owned)
            .to_vec();
            let scene = name.to_string();
            rune::App::new(ui::spec())
                .chrome(ui::CHROME)
                .init(move || fixtures::scene(&scene, tz()).unwrap())
                .args(args)
                .run();
            let written = std::fs::metadata(&path).map(|m| m.len()).unwrap_or(0);
            assert!(written > 0, "{name} ({}) rendered to {}", mode(dark), path.display());
        }
    }
}

#[test]
fn every_scene_shows_only_words_from_copy_grafana_or_numbers() {
    let (exact, patterns) = catalogue();
    for name in fixtures::SCENES {
        for dark in [false, true] {
            let rt = runtime(name, dark);
            let data = data(&rt.state);
            for element in rt.elements() {
                let Some(words) = element.text.as_deref().filter(|w| !w.trim().is_empty()) else {
                    continue;
                };
                let ok = exact.contains(words)
                    || data.contains(words)
                    || patterns.iter().any(|p| p.is_match(words))
                    || is_number_or_time(words)
                    // Legend and readout names cut short with an ellipsis.
                    || words.strip_suffix('…').is_some_and(|cut| data.iter().any(|d| d.starts_with(cut)));
                assert!(
                    ok,
                    "{name} ({}): {words:?} at {} isn't from copy.rs or Grafana",
                    mode(dark),
                    element.path
                );
            }
        }
    }
}

fn controls<'a>(node: &'a Value, found: &mut Vec<&'a Value>) {
    if node["actions"]
        .as_array()
        .is_some_and(|a| a.iter().any(|x| x == "click"))
    {
        found.push(node);
    }
    for child in node["children"].as_array().into_iter().flatten() {
        controls(child, found);
    }
}

#[test]
fn every_control_has_a_role_and_a_name() {
    for name in fixtures::SCENES {
        let rt = runtime(name, false);
        let tree = rt.accessibility_json();
        let mut found = Vec::new();
        controls(&tree, &mut found);
        assert!(!found.is_empty(), "{name} has controls");
        for control in found {
            let role = control["role"].as_str().unwrap_or_default();
            let label = control["name"].as_str().unwrap_or_default();
            assert!(
                !role.is_empty() && !matches!(role, "group" | "generic" | "label"),
                "{name}: {control}"
            );
            assert!(!label.trim().is_empty(), "{name}: a {role} without a name: {control}");
        }
    }
}

fn keys(rt: &Runtime<Monitor>) -> HashSet<String> {
    rt.elements().into_iter().filter_map(|e| e.key).collect()
}

fn texts(rt: &Runtime<Monitor>) -> Vec<String> {
    rt.elements().into_iter().filter_map(|e| e.text).collect()
}

#[test]
fn the_overview_calls_out_what_needs_attention() {
    let rt = runtime("overview", false);
    let shown = texts(&rt);
    for line in [
        "3 transcode failed in the last hour",
        "1 transcode running with no worker",
        "Oldest transcode has waited 18 min",
        "2 legend detection automatic failed in the last hour",
    ] {
        assert!(shown.iter().any(|t| t == line), "{line:?} in {shown:?}");
    }
    // Storage has a store and nothing failing.
    assert!(shown.iter().any(|t| t == "Nothing needs attention."));
    let keys = keys(&rt);
    assert!(shown.iter().any(|t| t == "OpenAI couldn't be read"), "{shown:?}");
    for section in ["Storage", "Transcoding", "AiPipelines", "Costs"] {
        assert!(keys.contains(&format!("card-{section}")), "{section}");
    }
}

#[test]
fn each_section_shows_its_answer() {
    let storage = texts(&runtime("storage", false));
    for words in [
        "marlowe",
        "reelshelf-clips",
        "S3-compatible",
        "Unlimited",
        "48,213",
        "2.11 TiB",
    ] {
        assert!(
            storage.iter().any(|t| t == words),
            "storage shows {words:?}: {storage:?}"
        );
    }
    let transcoding = runtime("transcoding", false);
    let shown = texts(&transcoding);
    for words in [
        "7e2b9c41-5d03-4a8f-b6e1-2c9d0f3a8b11",
        "batch",
        "4 of 4",
        "18,402",
        "Not counted",
    ] {
        assert!(
            shown.iter().any(|t| t == words),
            "transcoding shows {words:?}: {shown:?}"
        );
    }
    assert!(keys(&transcoding).contains("failure-0-copy"));
    let ai = texts(&runtime("ai", false));
    for words in ["Legend detection", "claude-sonnet-5-5", "$183.90", "–"] {
        assert!(ai.iter().any(|t| t == words), "ai shows {words:?}: {ai:?}");
    }
}

#[test]
fn costs_show_each_provider_its_chart_and_whats_not_set_up() {
    let rt = runtime("costs", false);
    let shown = texts(&rt);
    for words in [
        "$81.98",
        "$158.42",
        "Amazon Simple Storage Service",
        "google/gemini-3.5-transcribe",
        "$182.40",
        "£463.95",
        "£1,262.35",
        "Failed",
        "Couldn't read it: OpenAI answered 401 (the key was refused): Incorrect API key provided.",
        "Anthropic: Ops:Costs:Anthropic:AdminKey, an Admin API key.",
    ] {
        assert!(shown.iter().any(|t| t == words), "costs shows {words:?}: {shown:?}");
    }
    let shown_keys = keys(&rt);
    for provider in ["aws", "openrouter", "openai", "stripe"] {
        assert!(
            shown_keys.contains(&format!("costs-{provider}-chart")),
            "{provider}'s chart"
        );
    }
    // Anthropic isn't set up: no card of its own, only its line.
    assert!(!shown_keys.contains("costs-anthropic"));
    assert!(shown_keys.contains("costs-not-set-up"));

    let hover = runtime("costs-hover", true);
    assert!(keys(&hover).contains("costs-aws-chart-readout"));

    let none = texts(&runtime("costs-not-set-up", false));
    assert!(none.iter().any(|t| t == "$0.00"), "{none:?}");
    assert!(
        none.iter().any(|t| t.starts_with("Stripe: Billing:SecretKey")),
        "{none:?}"
    );
}

#[test]
fn copying_a_clip_id_says_so() {
    let rt = runtime("transcoding-copied", false);
    assert!(texts(&rt).iter().any(|t| t == "Copied"));
}

#[test]
fn signing_in_shows_the_code_and_refusals_say_why() {
    let shown = texts(&runtime("signing-in", false));
    assert!(shown.iter().any(|t| t == "BCDF-GHJK"), "{shown:?}");
    assert!(shown.iter().any(|t| t.contains("reelshelf.app/device")), "{shown:?}");
    let shown = texts(&runtime("not-admin", false));
    assert!(shown.iter().any(|t| t == "Only Admins can see this"));
    let shown = texts(&runtime("flag-off", false));
    assert!(shown.iter().any(|t| t == "The ops monitor isn't on for this account"));
    let shown = texts(&runtime("update-required", false));
    assert!(shown.iter().any(|t| t == "Update the monitor"));
    let shown = texts(&runtime("signed-out-revoked", false));
    assert!(shown.iter().any(|t| t.starts_with("This Mac was signed out")));
}

#[test]
fn the_dashboards_draw_their_prometheus_panels_and_link_the_rest() {
    let rt = runtime("dashboard-pipeline", false);
    let o = rt.state.opened.as_ref().unwrap();
    let Load::Ready(d) = &o.dashboard else {
        panic!("the fixture dashboard is open")
    };
    let keys: HashSet<String> = rt.elements().into_iter().filter_map(|e| e.key).collect();
    for p in &d.panels {
        if query::is_drawn(p) {
            let drawn =
                keys.contains(&format!("panel-{}-chart", p.id)) || keys.contains(&format!("panel-{}-stat", p.id));
            assert!(drawn, "{} is drawn", p.title);
        }
    }
    let vms = d.panels.iter().find(|p| p.title == "Worker VMs").unwrap();
    assert!(
        keys.contains(&format!("open-panel-{}", vms.id)),
        "Cloud Monitoring links to Grafana"
    );
}

#[test]
fn hovering_a_chart_shows_the_readout() {
    let rt = runtime("dashboard-hover", true);
    let latency = rt.state.hover.unwrap().panel;
    let keys: HashSet<String> = rt.elements().into_iter().filter_map(|e| e.key).collect();
    assert!(keys.contains(&format!("panel-{latency}-chart-readout")), "{keys:?}");
}

#[test]
fn releases_shows_whats_deployed_and_whats_coming() {
    let rt = runtime("releases", false);
    let shown = texts(&rt);
    for words in [
        "Overflow workers (batch, ecs, ec2)",
        "Not built from a commit",
        "Up to date",
        "Bump app images to sha-db118fec3e4857421ec21bff126e158ecb567c0b",
        "uploader-v0.1.0",
        "No checks",
        "Draft",
    ] {
        assert!(shown.iter().any(|t| t == words), "releases shows {words:?}: {shown:?}");
    }
    let keys = keys(&rt);
    // Prod's app lists the 6 commits main has that it doesn't; the worker offers its 29.
    assert!(keys.contains("deployed-0-commits"), "{keys:?}");
    assert!(keys.contains("deployed-0-commit-5"));
    assert!(keys.contains("deployed-2-behind"));
    let tree = rt.accessibility_json().to_string();
    assert!(tree.contains("Show the 29 commits on main not in Worker"), "{tree}");

    let shown = texts(&runtime("releases-collapsed", false));
    assert!(shown.iter().any(|t| t == "6 behind"), "{shown:?}");

    let shown = texts(&runtime("releases-unreadable", false));
    assert!(
        shown
            .iter()
            .any(|t| t == "Couldn't refresh: GitHub answered 404: Not Found")
    );
    assert!(shown.iter().any(|t| t == "uploader-v0.1.0"), "the rest still shows");

    let shown = texts(&runtime("releases-not-connected", false));
    assert!(shown.iter().any(|t| t == "GitHub isn't connected"));
}

#[test]
fn the_overview_says_how_far_prod_is_behind_and_that_ci_is_red() {
    let rt = runtime("overview", false);
    let shown = texts(&rt);
    assert!(keys(&rt).contains("card-Releases"));
    for words in ["Prod behind main", "6", "Failing", "CI failed on main"] {
        assert!(shown.iter().any(|t| t == words), "{words:?} in {shown:?}");
    }
}

#[test]
fn reports_list_open_ones_and_urgent_ones_are_removed_not_taken_down() {
    let rt = runtime("admin-reports", false);
    let found = keys(&rt);
    for key in [
        "report-0",
        "report-1",
        "report-2",
        "take-down",
        "dismiss",
        "open-shared-clip",
    ] {
        assert!(found.contains(key), "{key} in {found:?}");
    }
    assert!(
        !found.contains("remove"),
        "a copyright report is taken down, not removed"
    );

    let rt = runtime("admin-report-child-safety", false);
    let found = keys(&rt);
    assert!(found.contains("remove") && found.contains("child-safety-warning"));
    assert!(
        !found.contains("take-down"),
        "an urgent report is removed, not taken down"
    );
    assert!(
        !found.contains("open-shared-clip"),
        "a child-safety report never links its clip"
    );

    let shown = texts(&runtime("admin-reports-removed", false));
    for words in [
        "Confirmed extremist material; referred.",
        "The owner's account is suspended.",
        "Kept privately as evidence until 10 Nov 2025, and gone from public storage.",
    ] {
        assert!(shown.iter().any(|t| t == words), "{words:?} in {shown:?}");
    }
}

#[test]
fn an_admin_cant_change_their_own_role_or_suspend_themselves() {
    let rt = runtime("admin-account-self", false);
    assert!(keys(&rt).contains("cant-self"));
    let rt = runtime("admin-accounts", false);
    let shown = texts(&rt);
    for words in ["Pro, by a grant", "Suspension lifted", "Granted plan: none → Pro"] {
        assert!(shown.iter().any(|t| t == words), "{words:?} in {shown:?}");
    }
    assert!(!keys(&rt).contains("cant-self"));
}

#[test]
fn confirmations_name_what_they_change_and_show_the_apis_words() {
    let rt = runtime("admin-confirm-remove", false);
    let shown = texts(&rt);
    assert!(shown.iter().any(|t| t == "Remove “Lobby warm-up”?"), "{shown:?}");
    assert!(shown.iter().any(|t| t.contains("suspends driftwood")), "{shown:?}");
    assert!(shown.iter().any(|t| t == "This can't be undone."));
    let shown = texts(&runtime("admin-confirm-suspend", false));
    assert!(shown.iter().any(|t| t == "Suspend Quiet Fox?"), "{shown:?}");
    let rt = runtime("admin-confirm-error", false);
    assert!(texts(&rt).iter().any(|t| t == fixtures::FIXTURE_CONFLICT));
    assert!(keys(&rt).contains("confirm-error"));
}

#[test]
fn the_overview_counts_open_reports_with_urgent_ones_in_red() {
    let shown = texts(&runtime("overview", false));
    assert!(shown.iter().any(|t| t == "2 urgent reports open"), "{shown:?}");
}
