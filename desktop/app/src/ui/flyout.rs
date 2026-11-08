//! The tray's flyout, T1–T8: the state with its dot, what's happening, its main action, then Open your library,
//! Pause for…, Recent uploads, Settings and Quit. Recent uploads, Settings and choosing a book for a folder are pages of
//! the flyout, each with Back to the menu. It opens above the tray and closes when it loses focus.

use reelshelf_core::copy::{recent as recent_words, tray as words};
use reelshelf_core::state::{PauseLength, TrayAction, UploaderStateKind};
use rune::{Color, Cursor, El, Icon, ScrollSpec, Tx, col, el, icon, row, text};

use crate::links::Link;
use crate::model::{FlyoutPage, Ui};
use crate::theme::{Look, ty};
use crate::ui::parts::{self, Tone};
use crate::ui::{book_picker, icons, recent, settings};

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

/// The tallest a page's list grows before it scrolls.
const LIST_MAX_HEIGHT: f32 = 340.0;

/// How many recent uploads the flyout lists; the rest are in the library.
const RECENT_ROWS: usize = 8;

pub fn view(s: &Ui, l: &Look) -> El {
    let page = match s.flyout.page {
        FlyoutPage::Home => home(s, l),
        FlyoutPage::Recent => recent_page(s, l),
        FlyoutPage::Settings => settings_page(s, l),
        FlyoutPage::Book => book_page(s, l),
    };
    col()
        .key("flyout")
        .w_full()
        .padding(1.0)
        .fill(l.c.surface)
        .border(1.0, l.c.case_upright)
        .radius(l.radius_flyout)
        .clip_rounded(l.radius_flyout)
        .child(page)
}

fn home(s: &Ui, l: &Look) -> El {
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
            .on_press_tx(move |s: &mut Ui, tx: &mut Tx| state_action(s, tx, action)),
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
            item(l, "recent", &icons::RECENT, words::RECENT_UPLOADS, true)
                .on_press(|s: &mut Ui| go(s, FlyoutPage::Recent)),
        )
        .child(
            item(l, "settings", &icons::SETTINGS, words::SETTINGS, true)
                .on_press(|s: &mut Ui| go(s, FlyoutPage::Settings)),
        )
        .child(el().h(1.0).mx(8.0).my(5.0).fill(l.c.bg_soft).a11y_hidden())
        .child(
            item(l, "quit", &icons::QUIT, words::QUIT, false).on_press_tx(|s: &mut Ui, tx: &mut Tx| {
                tx.close_window();
                s.commands.quit(false);
            }),
        );

    col().key("home").w_full().child(head).child(menu)
}

/// The state's button: Show recent uploads and Choose a book turn the page; the rest act and close the flyout.
fn state_action(s: &mut Ui, tx: &mut Tx, action: TrayAction) {
    match action {
        TrayAction::ShowRecent => go(s, FlyoutPage::Recent),
        TrayAction::ChooseBook => {
            if let Some((folder, _)) = s.model.inputs.needs_book.clone() {
                s.picker.folder = folder;
                s.picker.selected = None;
                go(s, FlyoutPage::Book);
            }
        }
        _ => {
            s.commands.tray_action(action);
            tx.close_window();
        }
    }
}

/// Moves to a page, reading the engine again so it shows what's current.
pub fn go(s: &mut Ui, page: FlyoutPage) {
    s.flyout.page = page;
    s.flyout.pause_open = false;
    s.refresh();
}

/// A page: Back and its title along the top, its body, and an optional bar along the foot.
fn page(l: &Look, key: &str, title: &str, body: El, foot: Option<El>) -> El {
    let back = row()
        .key("back")
        .role("button")
        .label(words::BACK)
        .focusable(true)
        .focus_ring(2.0, 1.0, l.c.accent)
        .cursor(Cursor::Pointer)
        .size(28.0, 28.0)
        .radius(l.radius_lg)
        .center()
        .hover(l.c.fg.alpha(0.06))
        .on_press(|s: &mut Ui| go(s, FlyoutPage::Home))
        .child(icon(&icons::CHEVRON_LEFT, 14.0).color(l.c.fg_soft).a11y_hidden());
    let top = row()
        .key("page-top")
        .w_full()
        .gap(8.0)
        .pl(10.0)
        .pr(18.0)
        .py(12.0)
        .center_y()
        .fill(l.c.bg)
        .border_sides([0.0, 0.0, 1.0, 0.0], l.c.line)
        .child(back)
        .child(
            text(title.to_owned())
                .key("page-title")
                .role("heading")
                .level(1)
                .font(ty::FLYOUT_TITLE)
                .color(l.c.fg)
                .wrap()
                .grow(1.0)
                .min_w(0.0),
        );
    let mut page = col().key(key).w_full().child(top).child(body.w_full().padding(14.0));
    if let Some(foot) = foot {
        page = page.child(
            row()
                .key("page-foot")
                .w_full()
                .gap(8.0)
                .px(14.0)
                .py(10.0)
                .center_y()
                .fill(l.c.bg)
                .border_sides([1.0, 0.0, 0.0, 0.0], l.c.line)
                .child(foot),
        );
    }
    page
}

/// A list that scrolls once it is taller than the page has room for.
fn scrolling(key: &str, list: El) -> El {
    col()
        .key(key)
        .w_full()
        .max_h(LIST_MAX_HEIGHT)
        .scroll(ScrollSpec { stick_bottom: false })
        .child(list)
}

fn recent_page(s: &Ui, l: &Look) -> El {
    let rows = &s.model.recent;
    let body = if rows.is_empty() {
        col().child(
            text(recent_words::EMPTY)
                .key("empty")
                .font(ty::SMALL)
                .color(l.c.fg_soft)
                .wrap(),
        )
    } else {
        let shown = &rows[..rows.len().min(RECENT_ROWS)];
        let mut card = parts::card(l, "uploads").role("list").label(words::RECENT_UPLOADS);
        for (index, upload) in shown.iter().enumerate() {
            card = card.child(recent::upload_row(
                l,
                index,
                upload,
                s.model.current_upload.as_ref(),
                index + 1 == shown.len(),
            ));
        }
        col().child(scrolling("upload-scroll", card))
    };
    let folder = s.model.settings.recordings_folder.clone();
    let foot = row()
        .w_full()
        .center_y()
        .child(
            parts::button(l, "open-folder", recent_words::OPEN_FOLDER, Tone::Link, 6.0, 4.0).on_press(
                move |s: &mut Ui| {
                    if let Some(folder) = &folder {
                        s.commands.open_folder(folder);
                    }
                },
            ),
        )
        .child(rune::spacer())
        .child(
            parts::button(l, "open-library", recent_words::OPEN_LIBRARY, Tone::Link, 6.0, 4.0).on_press_tx(
                |s: &mut Ui, tx: &mut Tx| {
                    s.commands.open_link(Link::Library);
                    tx.close_window();
                },
            ),
        );
    page(l, "recent-page", words::RECENT_UPLOADS, body, Some(foot))
}

fn settings_page(s: &Ui, l: &Look) -> El {
    let (options, signed_in, version) = settings::sections(s, l);
    let body = col().gap(12.0).child(options).child(signed_in);
    page(l, "settings-page", words::SETTINGS, body, Some(version))
}

fn book_page(s: &Ui, l: &Look) -> El {
    let body = col()
        .gap(12.0)
        .child(
            text(book_picker::heading_words(s))
                .key("book-question")
                .font(ty::ROW_STRONG)
                .color(l.c.fg)
                .wrap(),
        )
        .child(scrolling("book-scroll", book_picker::list(s, l)));
    let foot = row()
        .w_full()
        .center_y()
        .child(book_picker::add_game(l))
        .child(rune::spacer())
        .child(book_picker::use_book_button(l));
    page(
        l,
        "book-page",
        reelshelf_core::copy::book_picker::TITLE,
        body,
        Some(foot),
    )
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
