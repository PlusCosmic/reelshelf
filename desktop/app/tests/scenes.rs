//! The headless checks: every window and flyout state, light and dark, renders without a panic, and its element tree
//! shows only words from `copy.rs` (or the fixture's data), names every control, and keeps everything inside the
//! window.
//!
//! The renders go to `$REELSHELF_RENDERS` when it is set (CI uploads them), else to a folder under the target
//! directory. `REELSHELF_RENDER_SCALE` sets their scale (2 by default).

mod support;

use std::collections::HashSet;
use std::path::PathBuf;

use reelshelf_app::fixtures::{self, Scene};
use reelshelf_app::model::WindowKind;
use reelshelf_core::copy;
use regex::Regex;
use serde_json::Value;

/// `copy.rs` as the build has it: every word the app may show comes from here.
const COPY: &str = include_str!("../../core/src/copy.rs");

/// What `copy.rs` can produce: its literal strings exactly, and its format strings as patterns.
struct Catalogue {
    exact: HashSet<String>,
    patterns: Vec<Regex>,
}

impl Catalogue {
    fn read() -> Catalogue {
        let source: String = COPY
            .split("#[cfg(test)]")
            .next()
            .unwrap_or(COPY)
            .lines()
            .filter(|line| !line.trim_start().starts_with("//"))
            .collect::<Vec<_>>()
            .join("\n");
        let literal = Regex::new(r#""((?:[^"\\]|\\.)*)""#).unwrap();
        let placeholder = Regex::new(r"\{[^}]*\}").unwrap();
        let mut exact = HashSet::new();
        let mut patterns = Vec::new();
        for found in literal.captures_iter(&source) {
            let raw = found[1]
                .replace("\\\\", "\u{0}")
                .replace("\\\"", "\"")
                .replace('\u{0}', "\\");
            if raw.contains('{') {
                let letters = placeholder
                    .replace_all(&raw, "")
                    .chars()
                    .filter(|c| c.is_alphabetic())
                    .count();
                // Format strings with words of their own; "{} {}" and the like say nothing.
                if letters >= 3 {
                    let mut pattern = String::from("^");
                    for (index, part) in raw.split(['{', '}']).enumerate() {
                        if index % 2 == 0 {
                            pattern.push_str(&regex::escape(part));
                        } else {
                            pattern.push_str(".+");
                        }
                    }
                    pattern.push('$');
                    patterns.push(Regex::new(&pattern).unwrap());
                }
            } else {
                exact.insert(raw);
            }
        }
        Catalogue { exact, patterns }
    }

    fn allows_one(&self, words: &str, data: &HashSet<String>) -> bool {
        self.exact.contains(words)
            || data.contains(words)
            || self.patterns.iter().any(|pattern| pattern.is_match(words))
    }

    /// One string from the catalogue or the data, or two of them joined by a space (a paragraph of two sentences).
    fn allows(&self, words: &str, data: &HashSet<String>) -> bool {
        self.allows_one(words, data)
            || words
                .match_indices(' ')
                .any(|(at, _)| self.allows_one(&words[..at], data) && self.allows_one(&words[at + 1..], data))
    }
}

/// The scene's data, and the words the core worked out for it (its state's lines and the rows of S4), which come from
/// `copy.rs` through the core.
fn data(scene: &Scene) -> HashSet<String> {
    let mut data: HashSet<String> = scene.data().into_iter().collect();
    // Folders are named with Windows' separator after them (`copy::folder`).
    data.extend(scene.data().iter().map(|name| copy::folder(name)));
    let state = &scene.model.state;
    data.extend([state.title.clone(), state.detail.clone(), state.action_label.clone()]);
    for row in &scene.model.recent {
        data.extend([row.title.clone(), row.status.clone(), row.meta.clone()]);
    }
    data
}

fn renders_dir() -> PathBuf {
    let dir = std::env::var_os("REELSHELF_RENDERS")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(env!("CARGO_TARGET_TMPDIR")).join("renders"));
    std::fs::create_dir_all(&dir).unwrap();
    dir
}

fn mode(dark: bool) -> &'static str {
    if dark { "dark" } else { "light" }
}

#[test]
fn every_scene_renders_light_and_dark() {
    let dir = renders_dir();
    let scale = std::env::var("REELSHELF_RENDER_SCALE").unwrap_or_else(|_| "2".to_owned());
    let scenes = fixtures::scenes();
    assert!(scenes.len() >= 24, "every window and flyout state has a scene");
    for scene in &scenes {
        for dark in [false, true] {
            let path = dir.join(format!("{}-{}.png", scene.name, mode(dark)));
            let _ = std::fs::remove_file(&path);
            let args = vec![
                "--snap".to_owned(),
                path.to_string_lossy().into_owned(),
                "--theme".to_owned(),
                mode(dark).to_owned(),
                "--scale".to_owned(),
                scale.clone(),
            ];
            support::scene_app(scene, args).run();
            let written = std::fs::metadata(&path).map(|meta| meta.len()).unwrap_or(0);
            assert!(
                written > 0,
                "{} ({}) rendered to {}",
                scene.name,
                mode(dark),
                path.display()
            );
        }
    }
}

#[test]
fn every_scene_shows_only_words_from_copy_or_the_fixtures_data() {
    let catalogue = Catalogue::read();
    for scene in fixtures::scenes() {
        let data = data(&scene);
        for dark in [false, true] {
            let rt = support::scene_runtime(&scene, dark);
            for element in rt.elements() {
                let Some(words) = element.text.as_deref().filter(|words| !words.trim().is_empty()) else {
                    continue;
                };
                assert!(
                    catalogue.allows(words, &data),
                    "{} ({}): {:?} at {} isn't from copy.rs or the fixture",
                    scene.name,
                    mode(dark),
                    words,
                    element.path
                );
            }
        }
    }
}

/// Every node the person can press, as accessibility reports it.
fn controls<'a>(node: &'a Value, found: &mut Vec<&'a Value>) {
    let presses = node["actions"]
        .as_array()
        .is_some_and(|actions| actions.iter().any(|action| action == "click"));
    if presses {
        found.push(node);
    }
    for child in node["children"].as_array().into_iter().flatten() {
        controls(child, found);
    }
}

#[test]
fn every_control_has_a_role_and_a_name() {
    for scene in fixtures::scenes() {
        let rt = support::scene_runtime(&scene, false);
        let tree = rt.accessibility_json();
        let mut found = Vec::new();
        controls(&tree, &mut found);
        for control in found {
            let role = control["role"].as_str().unwrap_or_default();
            let name = control["name"].as_str().unwrap_or_default();
            assert!(
                !role.is_empty() && !matches!(role, "group" | "generic" | "label"),
                "{}: a control without a role: {control}",
                scene.name
            );
            assert!(
                !name.trim().is_empty(),
                "{}: a {role} without a name: {control}",
                scene.name
            );
        }
    }
}

#[test]
fn nothing_overflows_the_window() {
    const SLACK: f32 = 0.5;
    for scene in fixtures::scenes() {
        for dark in [false, true] {
            let rt = support::scene_runtime(&scene, dark);
            let (w, h) = if scene.kind == WindowKind::Flyout {
                (scene.size.0, support::flyout_height(&rt))
            } else {
                scene.size
            };
            for element in rt.elements() {
                if element.exiting || element.opacity <= 0.01 || element.virtual_extent {
                    continue;
                }
                let [x, y, ew, eh] = element.visible;
                if ew <= 0.0 || eh <= 0.0 {
                    // Scrolled out of view inside a list.
                    continue;
                }
                assert!(
                    x >= -SLACK && y >= -SLACK && x + ew <= w + SLACK && y + eh <= h + SLACK,
                    "{} ({}): {} {:?} at {:?} leaves the {w} × {h} window",
                    scene.name,
                    mode(dark),
                    element.path,
                    element.text,
                    element.visible
                );
                if element.text.is_some() && element.scrolled == [0.0, 0.0] {
                    let [_, _, rw, rh] = element.rect;
                    assert!(
                        ew + SLACK >= rw && eh + SLACK >= rh,
                        "{} ({}): {:?} is cut off: drawn {:?}, shown {:?}",
                        scene.name,
                        mode(dark),
                        element.text,
                        element.rect,
                        element.visible
                    );
                }
            }
        }
    }
}
