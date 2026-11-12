//! The headless checks, in the uploader's style: every scene renders light and dark (to `$REELSHELF_RENDERS`, else a
//! folder under the target directory, at `REELSHELF_RENDER_SCALE`, 2 by default), shows only words from `copy.rs`,
//! Grafana's data or numbers with their units, and names every control.

use std::collections::HashSet;
use std::path::PathBuf;

use chrono::FixedOffset;
use reelshelf_app::theme;
use reelshelf_monitor::grafana::query;
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

/// What Grafana says in a scene: dashboard, folder, panel and series names.
fn data(s: &Monitor) -> HashSet<String> {
    let mut words = HashSet::new();
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
        r"^[-$]?[\d.]+ ?(%|req/s|ops/s|µs|ms|s|min|hour|day|K|M|B|T|B/s|KiB|MiB|GiB|TiB|PiB|kB|MB|GB|TB|PB|KiB/s|MiB/s)?$",
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
