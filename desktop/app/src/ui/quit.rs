//! Quitting while clips upload: asks first, since quitting stops them until the app next starts.

use reelshelf_core::copy::{self, tray as words};
use rune::{El, Tx, col, row, text};

use crate::model::Ui;
use crate::theme::{Look, ty};
use crate::ui::parts::{self, Tone};

pub fn view(s: &Ui, l: &Look) -> El {
    let uploads = s.model.uploads_in_progress.max(1) as i64;
    let body = col()
        .key("content")
        .gap(16.0)
        .px(28.0)
        .pt(24.0)
        .pb(22.0)
        .child(
            text(words::quit_while_uploading(uploads))
                .key("question")
                .role("heading")
                .level(1)
                .font(ty::FLYOUT_TITLE)
                .color(l.c.fg)
                .wrap(),
        )
        .child(rune::spacer())
        .child(
            row()
                .key("actions")
                .w_full()
                .gap(8.0)
                .justify_end()
                .child(
                    parts::button(l, "cancel", words::CANCEL, Tone::Secondary, 9.0, 13.0)
                        .on_press_tx(|_: &mut Ui, tx: &mut Tx| tx.close_window()),
                )
                .child(
                    parts::button(l, "quit", words::QUIT_CONFIRM, Tone::Primary, 9.0, 15.0)
                        .default_button()
                        .on_press(|s: &mut Ui| s.commands.quit(true)),
                ),
        );
    parts::window(l, copy::APP_NAME, body, None)
}
