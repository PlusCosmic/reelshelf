//! The pieces every window is built from, drawn as the canvas draws them: the titlebar band, buttons, cards and
//! their rows, fields, switches, the book spines.

use rune::{Color, Cursor, El, Shadow, TextStyle, col, el, icon, row, text};

use crate::model::Ui;
use crate::theme::{Look, ty};
use crate::ui::icons;

/// A button's look.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Tone {
    /// Filled with the accent: the window's main action.
    Primary,
    /// The soft fill with a line round it.
    Secondary,
    /// Accent words with no box, as a link reads.
    Link,
}

/// The window's own titlebar band: the mark and the window's name. On Windows the system's caption buttons sit over
/// its trailing end.
pub fn band(l: &Look, title: &str) -> El {
    row()
        .key("titlebar")
        .w_full()
        .h(36.0)
        .pl(14.0)
        .gap(9.0)
        .center_y()
        .fill(l.c.bg_soft)
        .window_drag()
        .child(logo(l))
        .child(text(title).key("titlebar-title").font(ty::BAND).color(l.c.fg_soft))
}

/// The Reelshelf mark: three spines, the last leaning.
pub fn logo(l: &Look) -> El {
    let [a, b, c] = l.logo;
    el().key("logo")
        .size(16.0, 16.0)
        .a11y_hidden()
        .child(el().absolute(1.0, 4.0).size(3.5, 11.0).radius(0.8).fill(a))
        .child(el().absolute(5.5, 2.0).size(3.5, 13.0).radius(0.8).fill(b))
        .child(
            el().absolute(10.0, 5.0)
                .size(3.5, 10.0)
                .radius(0.8)
                .fill(c)
                .rotate(-8f32.to_radians()),
        )
}

/// A window: the band, the body (which takes the room left) and an optional footer bar.
pub fn window(l: &Look, title: &str, body: El, footer: Option<El>) -> El {
    let mut window = col()
        .key("window")
        .w_full()
        .h_full()
        .fill(l.c.bg)
        .child(band(l, title))
        .child(body.grow(1.0).min_h(0.0).w_full());
    if let Some(footer) = footer {
        window = window.child(footer);
    }
    window
}

/// The bar along a window's foot, for its main action.
pub fn footer(l: &Look, px: f32) -> El {
    row()
        .key("footer")
        .w_full()
        .px(px)
        .py(14.0)
        .gap(12.0)
        .center_y()
        .fill(l.c.bg_soft)
        .border_sides([1.0, 0.0, 0.0, 0.0], l.c.line)
}

/// A window's heading in Fraunces.
pub fn heading(l: &Look, words: impl Into<String>, style: TextStyle) -> El {
    text(words)
        .key("heading")
        .role("heading")
        .level(1)
        .font(style)
        .color(l.c.fg)
        .wrap()
}

/// A button with the canvas's padding (`py` and `px` in points).
pub fn button(l: &Look, key: &str, label: impl Into<String>, tone: Tone, py: f32, px: f32) -> El {
    let label = label.into();
    let (fill, border, ink, font) = match tone {
        Tone::Primary => (l.c.fill, l.c.fill, l.c.fill_ink, ty::BUTTON),
        Tone::Secondary => (l.c.bg_soft, l.c.line, l.c.fg, ty::BUTTON),
        Tone::Link => (Color::TRANSPARENT, Color::TRANSPARENT, l.c.accent, ty::BUTTON),
    };
    let hover = match tone {
        Tone::Primary => Color::BLACK.alpha(0.12),
        Tone::Secondary | Tone::Link => l.c.fg.alpha(0.06),
    };
    row()
        .key(key)
        .role("button")
        .label(label.clone())
        .focusable(true)
        .focus_ring(2.0, 1.0, l.c.accent)
        .cursor(Cursor::Pointer)
        .px(px)
        .py(py)
        .radius(l.radius)
        .fill(fill)
        .border(1.0, border)
        .hover(hover)
        .press(hover.alpha(hover.a * 1.8))
        .center()
        .child(text(label).font(font).color(ink))
}

/// The surface cards hold their rows on.
pub fn card(l: &Look, key: &str) -> El {
    col()
        .key(key)
        .w_full()
        .padding(1.0)
        .fill(l.c.surface)
        .border(1.0, l.c.line)
        .radius(l.radius_lg)
        .clip_rounded(l.radius_lg)
}

/// A row inside a card, ruled off from the next unless it is the last.
pub fn card_row(l: &Look, last: bool) -> El {
    let row = row().w_full().center_y();
    if last {
        row
    } else {
        row.border_sides([0.0, 0.0, 1.0, 0.0], l.c.bg_soft)
    }
}

/// A book's spine in its cloth colour, shaded down its right edge; None draws a book not chosen yet, dashed.
pub fn spine(l: &Look, cloth: Option<Color>, w: f32, h: f32) -> El {
    let spine = el().size(w, h).radius(1.5).a11y_hidden();
    match cloth {
        Some(cloth) => spine.fill(cloth).box_shadow(Shadow {
            x: -2.0,
            y: 0.0,
            blur: 0.0,
            spread: 0.0,
            color: l.c.spine_shade,
            inset: true,
        }),
        None => spine
            .fill(l.c.empty_cloth)
            .border(1.0, l.c.no_book_edge)
            .border_dash(2.0, 2.0),
    }
}

/// A book's cloth from its `#rrggbb`, or the plank's colour when it has none.
pub fn cloth(l: &Look, color: Option<&str>) -> Color {
    color.and_then(rune::color::hex).unwrap_or(l.c.plank_front)
}

/// A field that opens a menu of choices: its value and a chevron.
pub fn select_field(l: &Look, key: &str, name: &str, value: &str, compact: bool) -> El {
    let (h, px, font) = if compact {
        (36.0, 10.0, ty::BUTTON.lh(16.0))
    } else {
        (40.0, 12.0, ty::FIELD)
    };
    let font = TextStyle { weight: 500.0, ..font };
    let field = row()
        .key(key)
        .role("combobox")
        .label(name.to_owned())
        .a11y_value(value.to_owned())
        .focusable(true)
        .focus_ring(2.0, 1.0, l.c.accent)
        .cursor(Cursor::Pointer)
        .h(h)
        .px(px)
        .gap(if compact { 8.0 } else { 9.0 })
        .center_y()
        .fill(l.c.bg_soft)
        .border(1.0, l.c.line)
        .radius(l.radius)
        .hover(l.c.fg.alpha(0.04));
    let value = text(value.to_owned()).font(font).color(l.c.fg);
    let value = if compact { value } else { value.grow(1.0).truncate() };
    field
        .child(value)
        .child(icon(&icons::CHEVRON_DOWN, 12.0).color(l.c.fg_soft).a11y_hidden())
}

/// A switch, as the canvas draws one: a 38 × 22 track, its knob at the end that's on.
pub fn switch(l: &Look, key: &str, name: &str, on: bool) -> El {
    el().key(key)
        .role("switch")
        .label(name.to_owned())
        .checked(on)
        .focusable(true)
        .focus_ring(2.0, 2.0, l.c.accent)
        .cursor(Cursor::Pointer)
        .size(38.0, 22.0)
        .radius(11.0)
        .fill(if on { l.c.fill } else { l.c.track_bg })
        .child(
            el().key(format!("{key}-knob"))
                .absolute(if on { 18.0 } else { 2.0 }, 2.0)
                .size(18.0, 18.0)
                .radius(9.0)
                .fill(l.c.fill_ink),
        )
}

/// A checkbox and its words.
pub fn checkbox(l: &Look, key: &str, label: &str, on: bool) -> El {
    let mut tick = el().size(16.0, 16.0).radius(l.radius).center().a11y_hidden();
    tick = if on {
        tick.fill(l.c.fill).child(icon(&icons::CHECK, 14.0).color(l.c.fill_ink))
    } else {
        tick.fill(l.c.surface).border(1.0, l.c.case_upright)
    };
    row()
        .key(key)
        .role("checkbox")
        .label(label.to_owned())
        .checked(on)
        .focusable(true)
        .focus_ring(2.0, 2.0, l.c.accent)
        .cursor(Cursor::Pointer)
        .gap(9.0)
        .center_y()
        .child(tick)
        .child(
            text(label.to_owned())
                .font(TextStyle {
                    weight: 500.0,
                    ..ty::ROW
                })
                .color(l.c.fg),
        )
}

/// Two or more choices side by side, the chosen one raised.
pub fn segmented(
    l: &Look,
    key: &str,
    name: &str,
    options: &[(&str, bool)],
    on_pick: impl Fn(&mut Ui, usize) + Clone + 'static,
) -> El {
    let mut group = row()
        .key(key)
        .role("radiogroup")
        .label(name.to_owned())
        .focus_group()
        .padding(2.0)
        .fill(l.c.bg_soft)
        .border(1.0, l.c.line)
        .radius(l.radius_lg);
    for (index, &(label, chosen)) in options.iter().enumerate() {
        let mut option = row()
            .key(format!("{key}-{index}"))
            .role("radio")
            .label(label.to_owned())
            .checked(chosen)
            .focusable(true)
            .focus_selected(chosen)
            .cursor(Cursor::Pointer)
            .on_press({
                let on_pick = on_pick.clone();
                move |s: &mut Ui| on_pick(s, index)
            })
            .px(12.0)
            .py(6.0)
            .radius(l.radius)
            .child(
                text(label.to_owned())
                    .font(ty::STATUS)
                    .color(if chosen { l.c.fg } else { l.c.fg_soft }),
            );
        if chosen {
            option = option.fill(l.c.surface).box_shadow(Shadow {
                x: 0.0,
                y: 1.0,
                blur: 2.0,
                spread: 0.0,
                color: l.c.segment_shadow,
                inset: false,
            });
        }
        group = group.child(option);
    }
    group
}

/// A bar showing how far something has got.
pub fn progress(l: &Look, key: &str, percent: u8, w: Option<f32>, h: f32) -> El {
    let share = f32::from(percent.min(100)) / 100.0;
    let track = el()
        .key(key)
        .role("progressbar")
        .range_value(f64::from(percent), 0.0, 100.0)
        .h(h)
        .radius(h / 2.0)
        .fill(l.c.track_bg)
        .clip_rounded(h / 2.0);
    let track = match w {
        Some(w) => track.w(w),
        None => track.w_full(),
    };
    track.child(el().w_frac(share).h_full().fill(l.c.accent))
}

/// A small round dot.
pub fn dot(color: Color, size: f32) -> El {
    el().size(size, size).radius(size / 2.0).fill(color).a11y_hidden()
}
