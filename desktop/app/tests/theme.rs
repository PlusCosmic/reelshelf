//! The theme stays the site's: every colour `theme/reelshelf.json` names a CSS token for must match that token as the
//! site resolves it, light (`:root`) and dark (`[data-theme="dark"]`): `@pluscosmic/ui`'s `tokens.css` (the shared
//! tokens, from the frontend's installed dependencies) with `frontend/src/styles/reelshelf.css`'s own on top.

use std::collections::HashMap;

use reelshelf_app::theme;
use serde_json::Value;

const CSS: &str = include_str!("../../../frontend/src/styles/reelshelf.css");

/// `@pluscosmic/ui`'s tokens, read when the test runs: the package is a frontend dependency, so it is on disk only
/// after `bun install` (the Desktop CI job runs one first).
fn foundation_css() -> String {
    let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../frontend/node_modules/@pluscosmic/ui/src/styles/tokens.css");
    std::fs::read_to_string(&path)
        .unwrap_or_else(|error| panic!("{} ({error}); run `bun install` at the repo root first", path.display()))
}

/// The custom properties `selector` sets, as the site resolves them: the foundation's, then reelshelf.css's.
fn tokens(selector: &str) -> HashMap<String, String> {
    let mut tokens = block(&foundation_css(), selector).unwrap_or_default();
    tokens.extend(block(CSS, selector).unwrap_or_default());
    tokens
}

/// The custom properties set in the first block that `selector` opens in `css`, if it has one.
fn block(css: &str, selector: &str) -> Option<HashMap<String, String>> {
    let start = css.find(&format!("{selector} {{"))?;
    let body = &css[start..];
    let body = &body[body.find('{').unwrap() + 1..body.find('}').unwrap()];
    let body = without_comments(body);
    let mut tokens = HashMap::new();
    let mut rest = body.as_str();
    // Declarations end at `;`, and a value may run over several lines.
    while let Some(end) = rest.find(';') {
        let declaration = &rest[..end];
        rest = &rest[end + 1..];
        if let Some((name, value)) = declaration.trim().split_once(':')
            && name.trim().starts_with("--")
        {
            tokens.insert(name.trim().to_owned(), value.trim().to_owned());
        }
    }
    Some(tokens)
}

fn without_comments(css: &str) -> String {
    let mut out = String::with_capacity(css.len());
    let mut rest = css;
    while let Some(start) = rest.find("/*") {
        out.push_str(&rest[..start]);
        rest = rest[start..].find("*/").map_or("", |end| &rest[start + end + 2..]);
    }
    out.push_str(rest);
    out
}

/// A CSS colour (`#rrggbb` or `oklch(L C H)`) as sRGB bytes.
fn rgb(value: &str) -> Option<[u8; 3]> {
    if let Some(hex) = value.strip_prefix('#') {
        let byte = |at: usize| u8::from_str_radix(hex.get(at..at + 2)?, 16).ok();
        return Some([byte(0)?, byte(2)?, byte(4)?]);
    }
    let inner = value.strip_prefix("oklch(")?.strip_suffix(')')?;
    let parts: Vec<f64> = inner
        .split_whitespace()
        .map(|part| part.parse().ok())
        .collect::<Option<_>>()?;
    let [l, c, h] = parts[..] else { return None };
    let (a, b) = (c * h.to_radians().cos(), c * h.to_radians().sin());
    let l_ = (l + 0.396_337_777_4 * a + 0.215_803_757_3 * b).powi(3);
    let m_ = (l - 0.105_561_345_8 * a - 0.063_854_172_8 * b).powi(3);
    let s_ = (l - 0.089_484_177_5 * a - 1.291_485_548 * b).powi(3);
    let linear = [
        4.076_741_662_1 * l_ - 3.307_711_591_3 * m_ + 0.230_969_929_2 * s_,
        -1.268_438_004_6 * l_ + 2.609_757_401_1 * m_ - 0.341_319_396_5 * s_,
        -0.004_196_086_3 * l_ - 0.703_418_614_7 * m_ + 1.707_614_701 * s_,
    ];
    let encode = |x: f64| {
        let x = x.clamp(0.0, 1.0);
        let srgb = if x <= 0.003_130_8 {
            12.92 * x
        } else {
            1.055 * x.powf(1.0 / 2.4) - 0.055
        };
        (srgb * 255.0).round() as u8
    };
    Some(linear.map(encode))
}

#[test]
fn every_theme_colour_named_for_a_token_matches_the_sites_css() {
    let data: Value = serde_json::from_str(theme::THEME).unwrap();
    let mapping = data["css"].as_object().expect("the theme names its CSS tokens");
    assert!(mapping.len() >= 15);
    for (mode, selector) in [("light", ":root"), ("dark", "[data-theme=\"dark\"]")] {
        let css = tokens(selector);
        for (name, property) in mapping {
            let property = property.as_str().unwrap();
            let ours = data[mode][name]
                .as_str()
                .unwrap_or_else(|| panic!("the theme's {mode} colours have {name}"));
            let theirs = css
                .get(property)
                .unwrap_or_else(|| panic!("the site's {selector} sets {property}"));
            let (ours, theirs) = (rgb(ours).unwrap(), rgb(theirs).unwrap());
            let close = ours.iter().zip(theirs).all(|(a, b)| a.abs_diff(b) <= 1);
            assert!(
                close,
                "{mode} {name} is {ours:?} in the theme but {property} is {theirs:?} on the site"
            );
        }
    }
}

#[test]
fn the_kit_takes_the_themes_colours() {
    for dark in [false, true] {
        let look = theme::look(dark);
        let kit = theme::kit(dark);
        assert_eq!(kit.dark, dark);
        assert_eq!(kit.c.page, look.c.bg);
        assert_eq!(kit.c.layer_01, look.c.surface);
        assert_eq!(kit.c.text_base, look.c.fg);
        assert_eq!(kit.c.text_muted, look.c.fg_soft);
        assert_eq!(kit.c.border_base, look.c.line);
        assert_eq!(kit.c.bg_accent, look.c.fill);
        assert_eq!(kit.c.text_accent, look.c.accent);
        assert_eq!(kit.c.danger.fg, look.c.danger_ink);
        assert_eq!(kit.c.warning.fg, look.c.warn_ink);
        assert_eq!(kit.radius.sm, look.radius);
        assert_eq!(look.spines.len(), 10);
    }
    assert_ne!(theme::look(false).c.bg, theme::look(true).c.bg);
}

#[test]
fn the_fonts_are_bundled_with_their_licences() {
    assert_eq!(theme::FONTS.len(), 3);
    for licence in ["OFL-Fraunces.txt", "OFL-InstrumentSans.txt"] {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("fonts")
            .join(licence);
        let text = std::fs::read_to_string(&path).unwrap();
        assert!(text.contains("SIL Open Font License"), "{licence}");
    }
}
