//! S2: the recordings folder, the folders below it matched to books, the book for clips that match none, and Start
//! watching.

use reelshelf_core::copy::{self, setup as words};
use reelshelf_core::matching::Book;
use rune::{El, FileDialogOptions, PopupMenu, Tx, col, icon, row, text};
use uuid::Uuid;

use crate::links::Link;
use crate::model::Ui;
use crate::theme::{Look, ty};
use crate::ui::icons;
use crate::ui::parts::{self, Tone};

/// The menu's id for "Ask me each time".
pub const ASK: &str = "ask";

pub fn view(s: &Ui, l: &Look) -> El {
    let draft = &s.setup;
    let mut body = col()
        .key("content")
        .gap(22.0)
        .px(28.0)
        .pt(26.0)
        .pb(22.0)
        .child(parts::heading(l, words::TITLE, ty::TITLE))
        .child(folder_group(s, l));
    if draft.folder.is_some() {
        body = body.child(matches_group(s, l));
    }
    if s.model.books.is_empty() {
        body = body.child(empty_shelf(l));
    } else {
        body = body.child(default_book_group(s, l));
    }
    body = body.child(
        text(words::BASELINE)
            .key("baseline")
            .font(ty::SMALL)
            .color(l.c.fg_soft)
            .wrap(),
    );
    let footer = parts::footer(l, 28.0)
        .child(
            parts::checkbox(
                l,
                "start-with-windows",
                words::ALSO_START_WITH_WINDOWS,
                draft.start_with_windows,
            )
            .on_press(|s: &mut Ui| s.setup.start_with_windows = !s.setup.start_with_windows),
        )
        .child(rune::spacer())
        .child(
            parts::button(l, "start-watching", words::START_WATCHING, Tone::Primary, 9.0, 15.0)
                .default_button()
                .on_press_tx(start_watching),
        );
    parts::window(l, copy::APP_NAME, body, Some(footer))
}

/// Saves the folder and the choices, and closes S2. Nothing happens until a folder is chosen.
pub fn start_watching(s: &mut Ui, tx: &mut Tx) {
    let Some(folder) = s.setup.folder.clone() else {
        choose_folder(s, tx);
        return;
    };
    s.commands
        .start_watching(&folder, s.setup.default_book, s.setup.start_with_windows);
    s.refresh();
    tx.close_window();
}

/// Opens the folder picker; the folder chosen replaces the draft's and its folders are matched again.
pub fn choose_folder(s: &mut Ui, tx: &mut Tx) {
    let options = FileDialogOptions {
        title: words::FOLDER_PICKER_TITLE.to_owned(),
        directory: s.setup.folder.clone().map(Into::into),
        ..FileDialogOptions::default()
    };
    tx.open_folder(options, |s: &mut Ui, picked, _tx: &mut Tx| {
        if let Ok(Some(path)) = picked {
            let folder = path.to_string_lossy().into_owned();
            s.setup.matches = s.commands.folder_matches(&folder);
            s.setup.folder = Some(folder);
        }
    });
}

fn label(l: &Look, words: &str) -> El {
    text(words).font(ty::LABEL).color(l.c.fg_soft)
}

fn folder_group(s: &Ui, l: &Look) -> El {
    let folder = s.setup.folder.clone().unwrap_or_default();
    let field = row()
        .key("folder")
        .role("textbox")
        .label(words::FOLDER_LABEL)
        .a11y_value(folder.clone())
        .grow(1.0)
        .min_w(0.0)
        .h(42.0)
        .px(12.0)
        .gap(9.0)
        .center_y()
        .fill(l.c.bg_soft)
        .border(1.0, l.c.line)
        .radius(l.radius)
        .child(icon(&icons::FOLDER, 16.0).color(l.c.fg_soft).a11y_hidden())
        .child(
            text(folder)
                .key("folder-path")
                .font(ty::FIELD)
                .color(l.c.fg)
                .truncate()
                .grow(1.0),
        );
    let change = if s.setup.folder.is_some() {
        words::CHANGE_FOLDER
    } else {
        words::CHOOSE_FOLDER
    };
    col()
        .key("folder-group")
        .w_full()
        .gap(7.0)
        .child(label(l, words::FOLDER_LABEL))
        .child(
            row().w_full().gap(8.0).child(field).child(
                parts::button(l, "change-folder", change, Tone::Secondary, 9.0, 13.0)
                    .h(42.0)
                    .on_press_tx(choose_folder),
            ),
        )
}

fn matches_group(s: &Ui, l: &Look) -> El {
    let matches = &s.setup.matches;
    let mut group = col()
        .key("matches")
        .w_full()
        .gap(8.0)
        .child(label(l, words::MATCHED_HEADING));
    if matches.is_empty() {
        return group.child(
            text(words::NO_SUBFOLDERS)
                .key("no-subfolders")
                .font(ty::SMALL)
                .color(l.c.fg_soft)
                .wrap(),
        );
    }
    let mut card = parts::card(l, "match-list");
    for (index, (folder, book)) in matches.iter().enumerate() {
        card = card.child(match_row(l, index, folder, book.as_ref(), index + 1 == matches.len()));
    }
    group = group.child(card);
    group
}

fn match_row(l: &Look, index: usize, folder: &str, book: Option<&Book>, last: bool) -> El {
    let (spine, name, ink) = match book {
        Some(book) => (
            parts::spine(l, Some(parts::cloth(l, book.cloth_color.as_deref())), 9.0, 20.0),
            book.name.clone(),
            l.c.fg,
        ),
        None => (
            parts::spine(l, None, 9.0, 20.0),
            words::NO_BOOK.to_owned(),
            l.c.warn_ink,
        ),
    };
    let owned = folder.to_owned();
    parts::card_row(l, last)
        .key(format!("match-{index}"))
        .gap(8.0)
        .px(14.0)
        .py(11.0)
        .child(
            text(copy::folder(folder))
                .font(ty::ROW)
                .color(l.c.fg_soft)
                .truncate()
                .grow(1.0)
                .min_w(0.0),
        )
        .child(
            text(words::MATCH_ARROW)
                .font(ty::ROW)
                .color(l.c.plank_front)
                .w(20.0)
                .a11y_hidden(),
        )
        .child(
            row()
                .grow(1.2)
                .min_w(0.0)
                .gap(9.0)
                .center_y()
                .child(spine)
                .child(text(name).font(ty::ROW_STRONG).color(ink).truncate().min_w(0.0)),
        )
        .child(
            parts::button(l, &format!("choose-{index}"), words::CHOOSE_BOOK, Tone::Link, 6.0, 4.0).on_press_tx(
                move |s: &mut Ui, tx: &mut Tx| {
                    let folder = owned.clone();
                    let current = s
                        .setup
                        .matches
                        .iter()
                        .find(|(name, _)| *name == folder)
                        .and_then(|(_, book)| book.as_ref())
                        .map_or(String::new(), |book| book.id.to_string());
                    let options = s
                        .model
                        .books
                        .iter()
                        .map(|book| (book.id.to_string(), book.name.clone()));
                    tx.popup_menu(
                        PopupMenu::picker(options, &current),
                        format!("choose-{index}"),
                        move |s: &mut Ui, picked, _tx: &mut Tx| {
                            if let Some(book) = picked.and_then(picked_book) {
                                s.commands.choose_book(&folder, book);
                            }
                        },
                    );
                },
            ),
        )
}

fn default_book_group(s: &Ui, l: &Look) -> El {
    let current = book_name(&s.model.books, s.setup.default_book).unwrap_or(words::ASK_EACH_TIME.to_owned());
    col()
        .key("default-group")
        .w_full()
        .gap(7.0)
        .child(label(l, words::DEFAULT_BOOK_LABEL))
        .child(
            parts::select_field(l, "default-book", words::DEFAULT_BOOK_LABEL, &current, false)
                .w_full()
                .on_press_tx(|s: &mut Ui, tx: &mut Tx| {
                    let menu = book_menu(&s.model.books, s.setup.default_book, words::ASK_EACH_TIME);
                    tx.popup_menu(menu, "default-book", |s: &mut Ui, picked, _tx: &mut Tx| {
                        if let Some(picked) = picked {
                            s.setup.default_book = picked_book(picked);
                        }
                    });
                }),
        )
}

fn empty_shelf(l: &Look) -> El {
    col()
        .key("empty-shelf")
        .w_full()
        .gap(8.0)
        .child(text(words::EMPTY_SHELF).font(ty::SMALL).color(l.c.warn_ink).wrap())
        .child(
            parts::button(l, "add-game", words::ADD_GAME, Tone::Link, 4.0, 0.0)
                .self_start()
                .on_press(|s: &mut Ui| s.commands.open_link(Link::AddGame)),
        )
}

/// A book's name by id.
pub fn book_name(books: &[Book], id: Option<Uuid>) -> Option<String> {
    let id = id?;
    books.iter().find(|book| book.id == id).map(|book| book.name.clone())
}

/// "Ask me each time" and every book, the current one checked.
pub fn book_menu(books: &[Book], current: Option<Uuid>, ask: &str) -> PopupMenu {
    let selected = current.map_or(ASK.to_owned(), |id| id.to_string());
    let options = std::iter::once((ASK.to_owned(), ask.to_owned()))
        .chain(books.iter().map(|book| (book.id.to_string(), book.name.clone())));
    PopupMenu::picker(options, &selected)
}

/// The book a menu pick names; None for "Ask me each time".
pub fn picked_book(picked: &str) -> Option<Uuid> {
    Uuid::parse_str(picked).ok()
}
