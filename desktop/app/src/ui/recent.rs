//! S4: recent uploads, newest first, each with its book, where it is, and Try again for a file that didn't upload.

use reelshelf_core::copy::recent as words;
use reelshelf_core::ledger::LedgerState;
use reelshelf_core::recent::RecentUpload;
use rune::{Color, Cursor, El, ScrollSpec, Span, col, icon, rich, row, text};

use crate::links::Link;
use crate::model::Ui;
use crate::theme::{Look, ty};
use crate::ui::icons;
use crate::ui::parts::{self, Tone};

pub fn view(s: &Ui, l: &Look) -> El {
    let rows = &s.model.recent;
    let list = if rows.is_empty() {
        text(words::EMPTY).key("empty").font(ty::BODY).color(l.c.fg_soft).wrap()
    } else {
        let mut card = parts::card(l, "uploads").role("list").label(words::WINDOW_TITLE);
        for (index, upload) in rows.iter().enumerate() {
            card = card.child(upload_row(
                l,
                index,
                upload,
                s.model.current_upload.as_ref(),
                index + 1 == rows.len(),
            ));
        }
        col()
            .key("upload-scroll")
            .w_full()
            .shrink()
            .min_h(0.0)
            .scroll(ScrollSpec { stick_bottom: false })
            .child(card)
    };
    let body = col()
        .key("content")
        .gap(14.0)
        .px(26.0)
        .pt(24.0)
        .pb(18.0)
        .child(parts::heading(l, words::WINDOW_TITLE, ty::TITLE))
        .child(list);
    let folder = s.model.settings.recordings_folder.clone();
    let footer = parts::footer(l, 26.0)
        .gap(8.0)
        .child(
            parts::button(l, "open-folder", words::OPEN_FOLDER, Tone::Link, 9.0, 4.0).on_press(move |s: &mut Ui| {
                if let Some(folder) = &folder {
                    s.commands.open_folder(folder);
                }
            }),
        )
        .child(rune::spacer())
        .child(
            parts::button(l, "open-library", words::OPEN_LIBRARY, Tone::Primary, 9.0, 15.0)
                .on_press(|s: &mut Ui| s.commands.open_link(Link::Library)),
        );
    parts::window(l, words::WINDOW_TITLE, body, Some(footer))
}

/// What each row's status says, in which colour, and what goes with it.
fn status(l: &Look, index: usize, upload: &RecentUpload, current: Option<&(String, u8)>) -> El {
    let label = |color: Color| text(upload.status.clone()).font(ty::STATUS).color(color);
    match upload.entry.state {
        LedgerState::Shelved => row()
            .gap(6.0)
            .center_y()
            .child(icon(&icons::CHECK_SMALL, 13.0).color(l.c.accent).a11y_hidden())
            .child(label(l.c.accent)),
        LedgerState::Failed => {
            let entry = upload.entry.clone();
            row().gap(4.0).center_y().child(label(l.c.danger_ink)).child(
                row()
                    .key(format!("try-again-{index}"))
                    .role("button")
                    .label(words::TRY_AGAIN)
                    .focusable(true)
                    .cursor(Cursor::Pointer)
                    .px(4.0)
                    .py(4.0)
                    .on_press(move |s: &mut Ui| {
                        s.commands.try_again(&entry);
                        s.refresh();
                    })
                    .child(
                        rich(
                            words::TRY_AGAIN,
                            vec![Span::new(0, words::TRY_AGAIN.len()).underline(l.c.accent)],
                        )
                        .font(ty::STATUS)
                        .color(l.c.accent),
                    ),
            )
        }
        LedgerState::NeedsBook | LedgerState::PlanFull | LedgerState::TooLong => label(l.c.warn_ink),
        LedgerState::Duplicate => label(l.c.fg_soft),
        LedgerState::Waiting | LedgerState::Uploading => match uploading_percent(upload, current) {
            Some(percent) => col()
                .gap(5.0)
                .items_end()
                .child(label(l.c.accent))
                .child(parts::progress(
                    l,
                    &format!("progress-{index}"),
                    percent,
                    Some(96.0),
                    4.0,
                )),
            None => label(l.c.fg_soft),
        },
        LedgerState::Baseline => label(l.c.fg_soft),
    }
}

/// How far the file uploading now has got, when this row is it.
fn uploading_percent(upload: &RecentUpload, current: Option<&(String, u8)>) -> Option<u8> {
    current
        .filter(|(path, _)| *path == upload.entry.path)
        .map(|&(_, percent)| percent)
}

fn upload_row(l: &Look, index: usize, upload: &RecentUpload, current: Option<&(String, u8)>, last: bool) -> El {
    let cloth = upload.cloth_color.as_deref().map(|color| parts::cloth(l, Some(color)));
    let has_book = upload.entry.category_id.is_some();
    let spine = parts::spine(
        l,
        if has_book {
            Some(cloth.unwrap_or(l.c.plank_front))
        } else {
            None
        },
        11.0,
        30.0,
    );
    let title = if upload.can_open() {
        let link = Link::Clip {
            book_slug: upload.book_slug.clone().unwrap_or_default(),
            clip_id: upload.clip_id().unwrap_or_default(),
        };
        rich(
            upload.title.clone(),
            vec![Span::new(0, upload.title.len()).underline(l.c.track_bg)],
        )
        .key(format!("title-{index}"))
        .role("link")
        .label(upload.title.clone())
        .focusable(true)
        .cursor(Cursor::Pointer)
        .on_press(move |s: &mut Ui| s.commands.open_link(link.clone()))
    } else {
        text(upload.title.clone()).key(format!("title-{index}"))
    };
    parts::card_row(l, last)
        .key(format!("upload-{index}"))
        .role("listitem")
        .gap(12.0)
        .px(14.0)
        .py(11.0)
        .child(spine)
        .child(
            col()
                .grow(1.0)
                .min_w(0.0)
                .gap(3.0)
                .child(title.font(ty::ROW_TITLE).color(l.c.fg).truncate())
                .child(text(upload.meta.clone()).font(ty::META).color(l.c.fg_soft).truncate()),
        )
        .child(status(l, index, upload, current))
}
