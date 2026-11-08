//! S5: settings. The recordings folder, the default book, what happens during a full-screen game, the speed limit,
//! starting with Windows and N1, then the device's sign-in, and the version with the log folder.

use reelshelf_core::copy::{settings as words, setup as setup_words};
use reelshelf_core::platform::StartupState;
use reelshelf_core::settings::{FullScreenMode, SPEED_LIMITS};
use rune::{El, FileDialogOptions, PopupMenu, Tx, col, row, text};

use crate::model::Ui;
use crate::theme::{Look, ty};
use crate::ui::parts::{self, Tone};
use crate::ui::setup::{book_menu, book_name, picked_book};

/// The menu's id for "No limit".
const NO_LIMIT: &str = "none";

pub fn view(s: &Ui, l: &Look) -> El {
    let model = &s.model;
    let settings = &model.settings;
    let folder = settings.recordings_folder.clone().unwrap_or_default();
    let default_book = book_name(&model.books, settings.default_book_id).unwrap_or(words::ASK_EACH_TIME.to_owned());
    let speed = settings
        .speed_limit_mbps
        .map_or(words::NO_LIMIT.to_owned(), words::mbps);
    let (wait, slowly) = match settings.full_screen {
        FullScreenMode::Wait => (true, false),
        FullScreenMode::UploadSlowly => (false, true),
    };

    let card = parts::card(l, "options")
        .child(
            setting(
                l,
                "folder-row",
                words::FOLDER_LABEL,
                Some(&words::folder_detail(&folder, model.matched_folders as i64)),
                false,
            )
            .child(
                parts::button(l, "change-folder", words::CHANGE, Tone::Secondary, 8.0, 12.0).on_press_tx(change_folder),
            ),
        )
        .child(setting(l, "default-row", words::DEFAULT_BOOK_LABEL, None, false).child(
            parts::select_field(l, "default-book", words::DEFAULT_BOOK_LABEL, &default_book, true).on_press_tx(
                |s: &mut Ui, tx: &mut Tx| {
                    let menu = book_menu(&s.model.books, s.model.settings.default_book_id, words::ASK_EACH_TIME);
                    tx.popup_menu(menu, "default-book", |s: &mut Ui, picked, _tx: &mut Tx| {
                        if let Some(picked) = picked {
                            s.commands.set_default_book(picked_book(picked));
                            s.refresh();
                        }
                    });
                },
            ),
        ))
        .child(
            setting(
                l,
                "full-screen-row",
                words::FULL_SCREEN_LABEL,
                Some(if wait {
                    words::FULL_SCREEN_WAIT
                } else {
                    words::FULL_SCREEN_SLOW
                }),
                false,
            )
            .child(full_screen(l, wait, slowly)),
        )
        .child(
            setting(l, "speed-row", words::SPEED_LABEL, Some(words::SPEED_DETAIL), false)
                .child(parts::select_field(l, "speed-limit", words::SPEED_LABEL, &speed, true).on_press_tx(speed_menu)),
        )
        .child(startup_row(s, l))
        .child(setting(l, "notify-row", words::NOTIFY_SHELVED, None, true).child(
            parts::switch(l, "notify-shelved", words::NOTIFY_SHELVED, settings.notify_shelved).on_press(
                |s: &mut Ui| {
                    let on = !s.model.settings.notify_shelved;
                    s.commands.set_notify_shelved(on);
                    s.refresh();
                },
            ),
        ));

    let device = settings.device_name.clone().unwrap_or_default();
    let signed_in = parts::card(l, "account").child(
        setting(
            l,
            "account-row",
            words::SIGNED_IN,
            Some(&words::device_name(&device)),
            true,
        )
        .child(
            parts::button(l, "sign-out", words::SIGN_OUT, Tone::Secondary, 8.0, 12.0).on_press_tx(
                |s: &mut Ui, tx: &mut Tx| {
                    s.commands.sign_out();
                    tx.close_window();
                },
            ),
        ),
    );

    let foot = row()
        .key("version-row")
        .gap(6.0)
        .center_y()
        .child(
            text(words::version(&model.version))
                .key("version")
                .font(ty::META)
                .color(l.c.fg_soft),
        )
        .child(parts::dot(l.c.fg_soft, 2.5))
        .child(
            parts::button(l, "open-logs", words::OPEN_LOG_FOLDER, Tone::Link, 4.0, 2.0)
                .on_press(|s: &mut Ui| s.commands.open_log_folder()),
        );

    let body = col()
        .key("content")
        .gap(18.0)
        .px(26.0)
        .pt(24.0)
        .pb(20.0)
        .child(parts::heading(l, words::WINDOW_TITLE, ty::TITLE))
        .child(card)
        .child(signed_in)
        .child(rune::spacer())
        .child(foot);
    parts::window(l, words::WINDOW_TITLE, body, None)
}

/// One setting's row: its name, an optional line under it, and the control the caller adds.
fn setting(l: &Look, key: &str, name: &str, detail: Option<&str>, last: bool) -> El {
    let mut words = col()
        .grow(1.0)
        .min_w(0.0)
        .gap(3.0)
        .child(text(name.to_owned()).font(ty::ROW_TITLE).color(l.c.fg).wrap());
    if let Some(detail) = detail {
        words = words.child(text(detail.to_owned()).font(ty::META).color(l.c.fg_soft).wrap());
    }
    parts::card_row(l, last)
        .key(key)
        .min_h(51.0)
        .gap(16.0)
        .px(16.0)
        .py(14.0)
        .child(words)
}

fn full_screen(l: &Look, wait: bool, slowly: bool) -> El {
    parts::segmented(
        l,
        "full-screen",
        words::FULL_SCREEN_LABEL,
        &[(words::WAIT, wait), (words::UPLOAD_SLOWLY, slowly)],
        |s: &mut Ui, index| {
            let mode = if index == 0 {
                FullScreenMode::Wait
            } else {
                FullScreenMode::UploadSlowly
            };
            s.commands.set_full_screen(mode);
            s.refresh();
        },
    )
}

fn startup_row(s: &Ui, l: &Look) -> El {
    let blocked = s.model.startup == StartupState::DisabledByUser;
    let on = s.model.startup == StartupState::Enabled;
    let detail = blocked.then_some(words::START_WITH_WINDOWS_BLOCKED);
    setting(l, "startup-row", words::START_WITH_WINDOWS, detail, false).child(
        parts::switch(l, "start-with-windows", words::START_WITH_WINDOWS, on).on_press(|s: &mut Ui| {
            let on = s.model.startup != StartupState::Enabled;
            s.commands.set_start_with_windows(on);
            s.refresh();
        }),
    )
}

fn change_folder(s: &mut Ui, tx: &mut Tx) {
    let options = FileDialogOptions {
        title: setup_words::FOLDER_PICKER_TITLE.to_owned(),
        directory: s.model.settings.recordings_folder.clone().map(Into::into),
        ..FileDialogOptions::default()
    };
    tx.open_folder(options, |s: &mut Ui, picked, _tx: &mut Tx| {
        if let Ok(Some(path)) = picked {
            let folder = path.to_string_lossy().into_owned();
            let starts = s.model.startup == StartupState::Enabled;
            s.commands
                .start_watching(&folder, s.model.settings.default_book_id, starts);
            s.refresh();
        }
    });
}

fn speed_menu(s: &mut Ui, tx: &mut Tx) {
    let current = s
        .model
        .settings
        .speed_limit_mbps
        .map_or(NO_LIMIT.to_owned(), |mbps| mbps.to_string());
    let options = std::iter::once((NO_LIMIT.to_owned(), words::NO_LIMIT.to_owned()))
        .chain(SPEED_LIMITS.iter().map(|mbps| (mbps.to_string(), words::mbps(*mbps))));
    tx.popup_menu(
        PopupMenu::picker(options, &current),
        "speed-limit",
        |s: &mut Ui, picked, _tx: &mut Tx| {
            if let Some(picked) = picked {
                s.commands.set_speed_limit(picked.parse().ok());
                s.refresh();
            }
        },
    );
}
