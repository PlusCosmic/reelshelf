//! The tray's flyout, T1–T8: the state with its dot, what's happening, its main action, then Open your library,
//! Pause for…, Settings and Quit. It opens above the tray and closes when it loses focus.

use reelshelf_core::copy::tray as words;
use reelshelf_core::state::{PauseLength, UploaderStateKind};
use rune::{Color, Cursor, El, Icon, Tx, col, el, icon, row, text};

use crate::links::Link;
use crate::model::{Show, Ui};
use crate::theme::{Look, ty};
use crate::ui::icons;
use crate::ui::parts::{self, Tone};

/// The state's tone, as the flyout's dot shows it.
pub fn tone(l: &Look, kind: UploaderStateKind) -> Color {
    match kind {
        UploaderStateKind::Watching | UploaderStateKind::Uploading => l.c.accent,
        UploaderStateKind::GameRunning | UploaderStateKind::Paused => l.c.tone_idle,
        UploaderStateKind::NeedsBook | UploaderStateKind::LibraryFull => l.c.warn_ink,
        UploaderStateKind::Unreachable | UploaderStateKind::UpdateRequired => l.c.danger_ink,
    }
}

/// Whether the state's action is the filled button: it's what the person needs to do.
pub fn action_is_primary(kind: UploaderStateKind) -> bool {
    matches!(
        kind,
        UploaderStateKind::Paused
            | UploaderStateKind::NeedsBook
            | UploaderStateKind::LibraryFull
            | UploaderStateKind::UpdateRequired
    )
}

pub fn view(s: &Ui, l: &Look) -> El {
    let state = &s.model.state;
    let mut head = col()
        .key("state")
        .w_full()
        .gap(10.0)
        .px(18.0)
        .py(16.0)
        .fill(l.c.bg)
        .border_sides([0.0, 0.0, 1.0, 0.0], l.c.line)
        .child(
            row()
                .gap(10.0)
                .center_y()
                .child(parts::dot(tone(l, state.kind), 10.0))
                .child(
                    text(state.title.clone())
                        .key("state-title")
                        .role("heading")
                        .level(1)
                        .font(ty::FLYOUT_TITLE)
                        .color(l.c.fg)
                        .wrap()
                        .grow(1.0)
                        .min_w(0.0),
                ),
        )
        .child(
            text(state.detail.clone())
                .key("state-detail")
                .font(ty::SMALL.lh(18.85))
                .color(l.c.fg_soft)
                .wrap(),
        );
    if let Some(percent) = state.percent {
        head = head.child(parts::progress(l, "state-progress", percent, None, 5.0));
    }
    let tone = if action_is_primary(state.kind) {
        Tone::Primary
    } else {
        Tone::Secondary
    };
    let action = state.action;
    head = head.child(
        parts::button(l, "state-action", state.action_label.clone(), tone, 8.0, 13.0)
            .self_start()
            .on_press_tx(move |s: &mut Ui, tx: &mut Tx| {
                s.commands.tray_action(action);
                tx.close_window();
            }),
    );

    let mut menu = col()
        .key("menu")
        .role("menu")
        .w_full()
        .padding(6.0)
        .focus_group()
        .child(
            item(l, "open-library", &icons::LIBRARY, words::OPEN_LIBRARY, false).on_press_tx(
                |s: &mut Ui, tx: &mut Tx| {
                    s.commands.open_link(Link::Library);
                    tx.close_window();
                },
            ),
        )
        .child(
            item(l, "pause", &icons::PAUSE, words::PAUSE_FOR, true)
                .expanded(s.flyout.pause_open)
                .on_press(|s: &mut Ui| s.flyout.pause_open = !s.flyout.pause_open),
        );
    if s.flyout.pause_open {
        for (key, label, length) in [
            ("pause-hour", words::PAUSE_HOUR, PauseLength::OneHour),
            ("pause-tomorrow", words::PAUSE_TOMORROW, PauseLength::UntilTomorrow),
            ("pause-resumed", words::PAUSE_UNTIL_RESUMED, PauseLength::UntilResumed),
        ] {
            menu = menu.child(sub_item(l, key, label).on_press_tx(move |s: &mut Ui, tx: &mut Tx| {
                s.commands.pause(length);
                s.flyout.pause_open = false;
                s.refresh();
                tx.close_window();
            }));
        }
    }
    menu = menu
        .child(
            item(l, "settings", &icons::SETTINGS, words::SETTINGS, false).on_press_tx(|s: &mut Ui, tx: &mut Tx| {
                s.commands.show(Show::Settings);
                tx.close_window();
            }),
        )
        .child(el().h(1.0).mx(8.0).my(5.0).fill(l.c.bg_soft).a11y_hidden())
        .child(
            item(l, "quit", &icons::QUIT, words::QUIT, false).on_press_tx(|s: &mut Ui, tx: &mut Tx| {
                tx.close_window();
                s.commands.quit(false);
            }),
        );

    col()
        .key("flyout")
        .w_full()
        .padding(1.0)
        .fill(l.c.surface)
        .border(1.0, l.c.case_upright)
        .radius(l.radius_flyout)
        .clip_rounded(l.radius_flyout)
        .child(head)
        .child(menu)
}

fn item(l: &Look, key: &str, glyph: &'static Icon, label: &str, submenu: bool) -> El {
    let mut row = row()
        .key(key)
        .role("menuitem")
        .label(label.to_owned())
        .focusable(true)
        .focus_highlight(l.c.fg.alpha(0.06))
        .cursor(Cursor::Pointer)
        .w_full()
        .gap(10.0)
        .px(12.0)
        .py(9.0)
        .radius(l.radius_lg)
        .center_y()
        .hover(l.c.fg.alpha(0.06))
        .child(icon(glyph, 16.0).color(l.c.fg_soft).a11y_hidden())
        .child(text(label.to_owned()).font(ty::MENU).color(l.c.fg).grow(1.0));
    if submenu {
        row = row.child(icon(&icons::CHEVRON_RIGHT, 12.0).color(l.c.fg_soft).a11y_hidden());
    }
    row
}

fn sub_item(l: &Look, key: &str, label: &str) -> El {
    row()
        .key(key)
        .role("menuitem")
        .label(label.to_owned())
        .focusable(true)
        .focus_highlight(l.c.fg.alpha(0.06))
        .cursor(Cursor::Pointer)
        .w_full()
        .pl(38.0)
        .pr(12.0)
        .py(7.0)
        .radius(l.radius_lg)
        .hover(l.c.fg.alpha(0.06))
        .child(text(label.to_owned()).font(ty::MENU).color(l.c.fg))
}
