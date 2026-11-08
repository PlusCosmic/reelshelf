//! S3: a folder with no book. The account's books, one picked, and Use this book.

use reelshelf_core::copy::{self, book_picker as words};
use rune::{Cursor, El, ScrollSpec, Shadow, Span, Tx, col, icon, rich, row, text};

use crate::links::Link;
use crate::model::Ui;
use crate::theme::{Look, ty};
use crate::ui::icons;
use crate::ui::parts::{self, Tone};

pub fn view(s: &Ui, l: &Look) -> El {
    let folder = &s.picker.folder;
    let heading = if folder.is_empty() {
        words::ROOT_HEADING.to_owned()
    } else {
        words::heading(&copy::folder(folder))
    };
    let mut list = parts::card(l, "books")
        .role("listbox")
        .label(words::WINDOW_TITLE)
        .focus_group();
    let books = &s.model.books;
    for (index, book) in books.iter().enumerate() {
        let chosen = s.picker.selected == Some(book.id);
        let id = book.id;
        let mut option = parts::card_row(l, index + 1 == books.len())
            .key(format!("book-{index}"))
            .role("option")
            .label(book.name.clone())
            .checked(chosen)
            .focusable(true)
            .focus_selected(chosen)
            .cursor(Cursor::Pointer)
            .gap(12.0)
            .px(14.0)
            .py(10.0)
            .hover(l.c.fg.alpha(0.03))
            .on_press(move |s: &mut Ui| s.picker.selected = Some(id))
            .child(parts::spine(
                l,
                Some(parts::cloth(l, book.cloth_color.as_deref())),
                11.0,
                26.0,
            ))
            .child(
                text(book.name.clone())
                    .font(ty::ROW_TITLE)
                    .color(l.c.fg)
                    .truncate()
                    .grow(1.0)
                    .min_w(0.0),
            );
        if chosen {
            option = option
                .fill(l.c.selected)
                .box_shadow(Shadow {
                    x: 3.0,
                    y: 0.0,
                    blur: 0.0,
                    spread: 0.0,
                    color: l.c.fill,
                    inset: true,
                })
                .child(icon(&icons::CHECK, 16.0).color(l.c.accent).a11y_hidden());
        }
        list = list.child(option);
    }
    let add = row()
        .key("add-game")
        .role("link")
        .label(words::ADD_GAME)
        .focusable(true)
        .cursor(Cursor::Pointer)
        .self_start()
        .gap(6.0)
        .center_y()
        .on_press(|s: &mut Ui| s.commands.open_link(Link::AddGame))
        .child(icon(&icons::PLUS, 13.0).color(l.c.accent).a11y_hidden())
        .child(
            rich(
                words::ADD_GAME,
                vec![Span::new(0, words::ADD_GAME.len()).underline(l.c.accent)],
            )
            .font(ty::BUTTON)
            .color(l.c.accent),
        );
    let body = col()
        .key("content")
        .gap(16.0)
        .px(26.0)
        .pt(24.0)
        .pb(20.0)
        .child(parts::heading(l, heading, ty::TITLE_SMALL))
        .child(
            col()
                .key("book-scroll")
                .w_full()
                .shrink()
                .min_h(0.0)
                .scroll(ScrollSpec { stick_bottom: false })
                .child(list),
        )
        .child(add);
    let footer = parts::footer(l, 26.0).justify_end().child(
        parts::button(l, "use-book", words::USE_BOOK, Tone::Primary, 9.0, 15.0)
            .default_button()
            .on_press_tx(use_book),
    );
    parts::window(l, words::WINDOW_TITLE, body, Some(footer))
}

/// Saves the book for the folder and closes S3. Nothing happens until a book is picked.
pub fn use_book(s: &mut Ui, tx: &mut Tx) {
    let Some(book) = s.picker.selected else {
        return;
    };
    let folder = s.picker.folder.clone();
    s.commands.choose_book(&folder, book);
    s.refresh();
    tx.close_window();
}
