//! The app's look as data: `theme/reelshelf.json` holds the site's tokens from `frontend/src/styles/reelshelf.css`,
//! light and dark, plus the few colours the canvas adds. It is read once per appearance into [`Look`], which the
//! windows draw from, and mapped onto `rune-kit`'s [`Kit`] so the kit's components take the same colours. Fraunces and
//! Instrument Sans come as static TTFs (`fonts/`, with their OFL licences).

use std::sync::OnceLock;

use rune::{Color, Family, TextStyle, Theme};
use rune_kit::{Kit, PaletteFocus, PaletteSeed, Radius, StateColors};

/// The theme as data.
pub const THEME: &str = include_str!("../theme/reelshelf.json");

/// Instrument Sans (regular and semibold) and Fraunces (medium), for `AppSpec::fonts`.
pub const FONTS: &[&[u8]] = &[
    include_bytes!("../fonts/InstrumentSans-Regular.ttf"),
    include_bytes!("../fonts/InstrumentSans-SemiBold.ttf"),
    include_bytes!("../fonts/Fraunces-Medium.ttf"),
];

/// `AppSpec::families`: the sans family names both slots; the app sets no code.
pub const FAMILIES: (&str, &str) = ("Instrument Sans", "Instrument Sans");

/// The site's display face, for headings.
pub const DISPLAY: Family = Family::Named("Fraunces");

/// Every colour the windows paint, for one appearance.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Colors {
    pub bg: Color,
    pub bg_soft: Color,
    pub surface: Color,
    pub fg: Color,
    pub fg_soft: Color,
    pub line: Color,
    pub track_bg: Color,
    pub accent: Color,
    pub accent_ink: Color,
    pub fill: Color,
    pub fill_ink: Color,
    pub danger: Color,
    pub warning: Color,
    pub shadow: Color,
    pub case_back: Color,
    pub case_upright: Color,
    pub plank_top: Color,
    pub plank_front: Color,
    pub empty_cloth: Color,
    /// The chosen row in a list (S3).
    pub selected: Color,
    /// Words that need the person: "Waiting for a book", "No book".
    pub warn_ink: Color,
    /// Words that say something failed: "Didn't upload".
    pub danger_ink: Color,
    /// The dashed outline of a book that isn't chosen yet.
    pub no_book_edge: Color,
    /// The flyout's dot while uploads simply wait (T3, T4).
    pub tone_idle: Color,
    /// The shade down a spine's right edge.
    pub spine_shade: Color,
    pub segment_shadow: Color,
    pub flyout_shadow: Color,
}

/// One appearance: its colours, the decorative shelf's spines, radii, and the same tokens as a `rune-kit` [`Kit`].
#[derive(Clone, Debug)]
pub struct Look {
    pub dark: bool,
    pub c: Colors,
    /// S1's shelf, left to right; None is a book with no clips yet (an empty cloth, outlined).
    pub spines: Vec<Option<Color>>,
    /// The mark's three spines, the same in both appearances.
    pub logo: [Color; 3],
    /// Controls' corners (`--radius`).
    pub radius: f32,
    /// Cards' corners (`--radius-lg`).
    pub radius_lg: f32,
    pub radius_flyout: f32,
    pub kit: Kit,
}

/// The parsed theme for one appearance.
pub fn theme(dark: bool) -> &'static Theme {
    static DARK: OnceLock<Theme> = OnceLock::new();
    static LIGHT: OnceLock<Theme> = OnceLock::new();
    let load = || Theme::from_json(THEME, dark).unwrap_or_else(|error| panic!("theme/reelshelf.json: {error}"));
    if dark {
        DARK.get_or_init(load)
    } else {
        LIGHT.get_or_init(load)
    }
}

/// The look for one appearance, built once.
pub fn look(dark: bool) -> &'static Look {
    static DARK: OnceLock<Look> = OnceLock::new();
    static LIGHT: OnceLock<Look> = OnceLock::new();
    if dark {
        DARK.get_or_init(|| build(true))
    } else {
        LIGHT.get_or_init(|| build(false))
    }
}

/// The theme's tokens on `rune-kit`'s schema, for one appearance.
pub fn kit(dark: bool) -> &'static Kit {
    &look(dark).kit
}

fn colors(t: &Theme) -> Colors {
    let c = |name: &str| t.color(name);
    Colors {
        bg: c("bg"),
        bg_soft: c("bg-soft"),
        surface: c("surface"),
        fg: c("fg"),
        fg_soft: c("fg-soft"),
        line: c("line"),
        track_bg: c("track-bg"),
        accent: c("accent"),
        accent_ink: c("accent-ink"),
        fill: c("fill"),
        fill_ink: c("fill-ink"),
        danger: c("danger"),
        warning: c("warning"),
        shadow: c("shadow"),
        case_back: c("case-back"),
        case_upright: c("case-upright"),
        plank_top: c("plank-top"),
        plank_front: c("plank-front"),
        empty_cloth: c("empty-cloth"),
        selected: c("selected"),
        warn_ink: c("warn-ink"),
        danger_ink: c("danger-ink"),
        no_book_edge: c("no-book-edge"),
        tone_idle: c("tone-idle"),
        spine_shade: c("spine-shade"),
        segment_shadow: c("segment-shadow"),
        flyout_shadow: c("flyout-shadow"),
    }
}

fn build(dark: bool) -> Look {
    let t = theme(dark);
    let c = colors(t);
    let data: serde_json::Value = serde_json::from_str(THEME).expect("theme/reelshelf.json is JSON");
    let spines = data["spines"]
        .as_array()
        .map(|spines| {
            spines
                .iter()
                .filter_map(serde_json::Value::as_str)
                .map(parse_color)
                .collect()
        })
        .unwrap_or_default();
    let logo = |index: usize| {
        data["logo"][index]
            .as_str()
            .and_then(parse_color)
            .expect("theme/reelshelf.json names the mark's three colours")
    };
    Look {
        dark,
        c,
        spines,
        logo: [logo(0), logo(1), logo(2)],
        radius: t.num("radius"),
        radius_lg: t.num("radius-lg"),
        radius_flyout: t.num("radius-flyout"),
        kit: map_kit(&c, dark, t),
    }
}

/// The site's tokens on the kit's roles: the page and its layers, text, borders, the filled accent, and the state
/// colours, over a palette the kit derives from the page, its ink and the fill.
fn map_kit(c: &Colors, dark: bool, t: &Theme) -> Kit {
    let mut kit = Kit::from_palette_focus(
        PaletteSeed {
            bg: c.bg,
            fg: c.fg,
            accent: c.fill,
        },
        PaletteFocus::Accent,
    );
    kit.dark = dark;
    let p = &mut kit.c;
    p.page = c.bg;
    p.bg_base = c.bg;
    p.bg_deep = c.bg_soft;
    p.layer_01 = c.surface;
    p.layer_02 = c.bg_soft;
    p.layer_03 = c.case_back;
    p.layer_04 = c.track_bg;
    p.bg_button_neutral = c.bg_soft;
    p.bg_accent = c.fill;
    p.submit_fg = c.fill_ink;
    p.submit_icon = c.fill_ink;
    p.text_base = c.fg;
    p.text_muted = c.fg_soft;
    p.text_faint = c.tone_idle;
    p.text_contrast = c.fill_ink;
    p.text_accent = c.accent;
    p.icon_base = c.fg_soft;
    p.icon_muted = c.fg_soft;
    p.icon_faint = c.tone_idle;
    p.icon_contrast = c.fill_ink;
    p.icon_accent = c.accent;
    p.border_muted = c.bg_soft;
    p.border_base = c.line;
    p.border_strong = c.case_upright;
    p.border_focus = c.accent;
    p.knob = c.fill_ink;
    p.knob_on = c.fill_ink;
    p.success = StateColors {
        bg: c.selected,
        fg: c.accent,
        border: c.accent,
        icon: c.accent,
    };
    p.warning = StateColors {
        bg: c.surface,
        fg: c.warn_ink,
        border: c.no_book_edge,
        icon: c.warn_ink,
    };
    p.danger = StateColors {
        bg: c.surface,
        fg: c.danger_ink,
        border: c.danger,
        icon: c.danger_ink,
    };
    p.surface_base = c.surface;
    p.surface_weak = c.bg_soft;
    p.surface_raised = c.surface;
    p.surface_raised_base = c.surface;
    p.text_strong = c.fg;
    p.text_weak = c.fg_soft;
    p.std_text = c.fg;
    p.std_border = c.line;
    kit.radius = Radius {
        xs: 1.5,
        sm: t.num("radius"),
        md: t.num("radius-lg"),
        lg: t.num("radius-flyout"),
        xl: t.num("radius-flyout"),
    };
    kit.blend_over = Some(c.bg);
    kit.ty.ui = ty::BUTTON;
    kit.ty.ui_medium = ty::BUTTON;
    kit.ty.body = ty::BODY;
    kit.ty.body_medium = ty::ROW_TITLE;
    kit.ty.text_small = ty::SMALL;
    kit
}

/// `#rrggbb` or `#rrggbbaa`.
pub fn parse_color(value: &str) -> Option<Color> {
    rune::color::hex(value)
}

/// The type scale, as the canvas sets it (CSS line heights in points).
pub mod ty {
    use rune::TextStyle;

    use super::DISPLAY;

    /// A window's heading: Fraunces 27, `line-height: 1.12`, `letter-spacing: -0.01em`.
    pub const TITLE: TextStyle = TextStyle::new(27.0, 500.0).family(DISPLAY).lh(30.24).tracking(-0.27);
    /// S3's heading.
    pub const TITLE_SMALL: TextStyle = TextStyle::new(23.0, 500.0).family(DISPLAY).lh(26.45).tracking(-0.23);
    /// The flyout's state.
    pub const FLYOUT_TITLE: TextStyle = TextStyle::new(19.0, 500.0).family(DISPLAY).lh(22.8);
    /// The titlebar band's name.
    pub const BAND: TextStyle = TextStyle::new(12.5, 600.0).lh(15.0);
    pub const BODY: TextStyle = TextStyle::new(14.0, 400.0).lh(21.0);
    pub const SMALL: TextStyle = TextStyle::new(13.0, 400.0).lh(19.5);
    /// A field's label above it.
    pub const LABEL: TextStyle = TextStyle::new(12.0, 600.0).lh(15.0);
    pub const BUTTON: TextStyle = TextStyle::new(13.0, 600.0).lh(16.0);
    pub const FIELD: TextStyle = TextStyle::new(13.5, 500.0).lh(17.0);
    pub const ROW: TextStyle = TextStyle::new(13.5, 400.0).lh(17.0);
    pub const ROW_STRONG: TextStyle = TextStyle::new(13.5, 600.0).lh(17.0);
    pub const ROW_TITLE: TextStyle = TextStyle::new(14.0, 600.0).lh(18.0);
    pub const META: TextStyle = TextStyle::new(12.5, 400.0).lh(16.0);
    pub const STATUS: TextStyle = TextStyle::new(12.5, 600.0).lh(16.0);
    pub const MENU: TextStyle = TextStyle::new(13.5, 500.0).lh(17.0);
    /// S1's code: 34, semibold, `letter-spacing: 0.16em`, tabular figures.
    pub const CODE: TextStyle = TextStyle::new(34.0, 600.0).lh(41.0).tracking(5.44).features("'tnum' 1");
}

/// A `TextStyle` in the display face.
pub fn display(size: f32) -> TextStyle {
    TextStyle::new(size, 500.0).family(DISPLAY)
}
