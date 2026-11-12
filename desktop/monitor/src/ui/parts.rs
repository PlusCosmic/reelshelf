//! The ops sections' building blocks, all from the theme's tokens: a scrolling page, cards, big numbers, tables, the
//! red "needs attention" lines, and a clip id with its Copy button.

use rune::{El, ScrollSpec, Tx, col, el, row, text};
use rune_kit::{Button, ButtonVariant, ControlSize};
use uuid::Uuid;

use reelshelf_app::theme::{self, Look, ty};

use crate::copy;
use crate::state::Monitor;

/// Numbers that line up in columns.
pub const NUMBER: rune::TextStyle = rune::TextStyle::new(13.5, 400.0).lh(17.0).features("'tnum' 1");
pub const NUMBER_STRONG: rune::TextStyle = rune::TextStyle::new(13.5, 600.0).lh(17.0).features("'tnum' 1");
/// A headline number.
pub const BIG: rune::TextStyle = rune::TextStyle::new(28.0, 500.0)
    .family(theme::DISPLAY)
    .lh(32.0)
    .features("'tnum' 1");

/// A page of cards, scrolling, at most 1080 points wide.
pub fn page(key: &str, children: Vec<El>) -> El {
    let inner = children
        .into_iter()
        .fold(col().w_full().max_w(1080.0).gap(16.0), El::child);
    col()
        .key(key.to_owned())
        .w_full()
        .h_full()
        .scroll(ScrollSpec { stick_bottom: false })
        .child(col().w_full().padding(20.0).child(inner))
}

/// A card with a title; `children` below it.
pub fn card(l: &Look, key: &str, title: &str, children: Vec<El>) -> El {
    children.into_iter().fold(
        col()
            .key(key.to_owned())
            .w_full()
            .padding(16.0)
            .gap(12.0)
            .fill(l.c.surface)
            .border(1.0, l.c.line)
            .radius(l.radius_lg)
            .child(text(title.to_owned()).font(ty::ROW_TITLE).color(l.c.fg)),
        El::child,
    )
}

/// A big number with its label under it, red when it needs attention.
pub fn stat(l: &Look, label: &str, value: String, danger: bool) -> El {
    col()
        .gap(2.0)
        .min_w(120.0)
        .child(
            text(value)
                .font(BIG)
                .color(if danger { l.c.danger_ink } else { l.c.fg }),
        )
        .child(text(label.to_owned()).font(ty::META).color(l.c.fg_soft))
}

/// A row of big numbers, wrapping on a narrow window.
pub fn stats(items: Vec<El>) -> El {
    items.into_iter().fold(row().w_full().gap(28.0).flex_wrap(), El::child)
}

/// A line under a card's numbers: soft words.
pub fn note(l: &Look, words: impl Into<String>) -> El {
    text(words.into()).font(ty::META).color(l.c.fg_soft).wrap()
}

/// A line that needs attention, with a red dot.
pub fn alert(l: &Look, words: String) -> El {
    row()
        .gap(8.0)
        .center_y()
        .child(el().size(7.0, 7.0).radius(3.5).fill(l.c.danger).a11y_hidden())
        .child(text(words).font(ty::ROW).color(l.c.danger_ink).wrap().grow(1.0))
}

/// How a table's cell reads.
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Tone {
    Plain,
    Soft,
    Strong,
    Danger,
}

pub struct Cell {
    pub words: String,
    pub tone: Tone,
}

impl Cell {
    pub fn new(words: impl Into<String>) -> Cell {
        Cell {
            words: words.into(),
            tone: Tone::Plain,
        }
    }
    pub fn tone(mut self, tone: Tone) -> Cell {
        self.tone = tone;
        self
    }
    /// A count, red when it needs attention.
    pub fn count(n: i64, danger: bool) -> Cell {
        let tone = if danger && n > 0 {
            Tone::Danger
        } else if n == 0 {
            Tone::Soft
        } else {
            Tone::Plain
        };
        Cell::new(crate::units::count(n)).tone(tone)
    }
}

/// A table column: its heading and width; the first column takes what's left.
pub struct Column {
    pub heading: &'static str,
    pub width: f32,
}

pub const fn column(heading: &'static str, width: f32) -> Column {
    Column { heading, width }
}

/// A table: a heading row, then each row with a line above it. Every column but the first is right-aligned.
pub fn table(l: &Look, key: &str, columns: &[Column], rows: Vec<Vec<Cell>>) -> El {
    let line = |cells: Vec<(String, rune::TextStyle, rune::Color)>| {
        let mut r = row().w_full().gap(12.0).center_y().min_h(26.0);
        for (i, ((words, font, color), c)) in cells.into_iter().zip(columns).enumerate() {
            let t = text(words).font(font).color(color).truncate();
            r = r.child(if i == 0 {
                row().grow(1.0).min_w(60.0).child(t)
            } else {
                row().w(c.width).justify_end().child(t)
            });
        }
        r
    };
    let mut out = col().key(key.to_owned()).w_full().gap(4.0).child(line(
        columns
            .iter()
            .map(|c| (c.heading.to_owned(), ty::LABEL, l.c.fg_soft))
            .collect(),
    ));
    for cells in rows {
        out = out.child(el().w_full().h(1.0).fill(l.c.line)).child(line(
            cells
                .into_iter()
                .map(|cell| {
                    let (font, color) = match cell.tone {
                        Tone::Plain => (NUMBER, l.c.fg),
                        Tone::Soft => (NUMBER, l.c.fg_soft),
                        Tone::Strong => (NUMBER_STRONG, l.c.fg),
                        Tone::Danger => (NUMBER_STRONG, l.c.danger_ink),
                    };
                    (cell.words, font, color)
                })
                .collect(),
        ));
    }
    out
}

/// Two words side by side: a label and its value.
pub fn pair(l: &Look, label: &str, value: String) -> El {
    row()
        .w_full()
        .gap(12.0)
        .child(text(label.to_owned()).font(ty::ROW).color(l.c.fg_soft).w(110.0))
        .child(text(value).font(ty::ROW).color(l.c.fg).truncate().grow(1.0))
}

/// A clip's id and a button that copies it.
pub fn clip_id(s: &Monitor, l: &Look, key: &str, id: Uuid) -> El {
    let copied = s.copied == Some(id);
    row()
        .gap(8.0)
        .center_y()
        .child(text(id.to_string()).font(NUMBER).color(l.c.fg).selectable(true))
        .child(
            Button::new(if copied { copy::COPIED } else { copy::COPY })
                .variant(ButtonVariant::Ghost)
                .control_size(ControlSize::Small)
                .on_press_tx(move |s: &mut Monitor, tx: &mut Tx| {
                    tx.copy(id.to_string());
                    s.copied = Some(id);
                })
                .build(&l.kit)
                .label(copy::copy_clip_id(&id.to_string()))
                .key(format!("{key}-copy")),
        )
}

/// A failed run: what, the clip, the error and when.
pub fn failure(s: &Monitor, l: &Look, key: &str, head: Vec<String>, clip: Uuid, error: Option<&str>) -> El {
    let mut top = row().w_full().gap(10.0).center_y();
    for (i, words) in head.into_iter().enumerate() {
        top = top.child(
            text(words)
                .font(if i == 0 { ty::ROW_STRONG } else { ty::META })
                .color(if i == 0 { l.c.fg } else { l.c.fg_soft }),
        );
    }
    col()
        .key(key.to_owned())
        .w_full()
        .gap(4.0)
        .py(8.0)
        .border_sides([1.0, 0.0, 0.0, 0.0], l.c.line)
        .child(top)
        .child(clip_id(s, l, key, clip))
        .child(match error {
            Some(e) => text(e.to_owned()).font(ty::META).color(l.c.danger_ink).wrap(),
            None => text(copy::NO_ERROR).font(ty::META).color(l.c.fg_soft),
        })
}
