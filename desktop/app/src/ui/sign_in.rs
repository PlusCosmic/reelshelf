//! S1: signing in. A shelf over the heading, the code to approve on the device page while it waits, and what to do
//! when the code expired, sign-in was cancelled or Reelshelf couldn't be reached.

use reelshelf_core::copy::{self, sign_in as words};
use rune::{El, Tx, col, el, icon, row, spacer, text};

use crate::links::Link;
use crate::model::{SignInPhase, Ui};
use crate::theme::{Look, ty};
use crate::ui::icons;
use crate::ui::parts::{self, Tone};

/// S1's decorative shelf, as the canvas sets it: each spine's width and height.
const SPINES: [(f32, f32); 10] = [
    (13.0, 44.0),
    (17.0, 52.0),
    (12.0, 40.0),
    (15.0, 48.0),
    (14.0, 46.0),
    (12.0, 38.0),
    (16.0, 50.0),
    (13.0, 42.0),
    (15.0, 47.0),
    (12.0, 41.0),
];

/// The leaning spine.
const LEANING: usize = 4;

pub fn view(s: &Ui, l: &Look) -> El {
    let waiting = matches!(s.sign_in, SignInPhase::Starting | SignInPhase::Waiting { .. });
    let mut content = col()
        .key("content")
        .w_full()
        .gap(16.0)
        .px(28.0)
        .pt(if waiting { 24.0 } else { 26.0 })
        .pb(24.0)
        .child(parts::heading(l, words::TITLE, ty::TITLE))
        .child(text(words::INTRO).key("intro").font(ty::BODY).color(l.c.fg_soft).wrap());
    content = match &s.sign_in {
        SignInPhase::Starting => content.child(code_box(l, None)),
        SignInPhase::Waiting { user_code, .. } => content
            .child(code_box(l, Some(user_code)))
            .child(waiting_line(l, &s.model.device_page)),
        SignInPhase::Expired => content.child(notice(l, words::EXPIRED)),
        SignInPhase::Denied => content.child(notice(l, words::DENIED)),
        SignInPhase::Unreachable => content.child(notice(l, words::UNREACHABLE)),
    };
    content = content.child(spacer()).child(actions(s, l));
    let body = if waiting {
        col().child(shelf(l)).child(content.grow(1.0))
    } else {
        col().child(content.grow(1.0))
    };
    parts::window(l, copy::APP_NAME, body, None)
}

/// The shelf of spines along the top, on its plank.
fn shelf(l: &Look) -> El {
    let mut shelf = row()
        .key("shelf")
        .a11y_hidden()
        .w_full()
        .h(70.0)
        .px(28.0)
        .gap(3.0)
        .items_end()
        .fill(l.c.case_back)
        .border_sides([0.0, 0.0, 6.0, 0.0], l.c.plank_front)
        .pb(6.0);
    for (index, (&(w, h), cloth)) in SPINES.iter().zip(&l.spines).enumerate() {
        let spine = el().size(w, h).corners([1.5, 1.5, 0.0, 0.0]);
        let spine = match cloth {
            Some(cloth) => spine.fill(*cloth),
            None => spine
                .fill(l.c.empty_cloth)
                .border_sides([1.0, 1.0, 0.0, 1.0], l.c.case_upright),
        };
        let spine = if index == LEANING {
            spine.ml(4.0).origin(1.0, 1.0).rotate(-7f32.to_radians())
        } else {
            spine
        };
        shelf = shelf.child(spine);
    }
    shelf
}

/// The code, large, in a card; "Getting a code…" until there is one.
fn code_box(l: &Look, code: Option<&String>) -> El {
    let inner = match code {
        Some(code) => text(code.clone()).key("code").font(ty::CODE).color(l.c.fg),
        None => text(words::STARTING).key("code").font(ty::BODY).color(l.c.fg_soft),
    };
    col()
        .key("code-box")
        .w_full()
        .min_h(75.0)
        .center()
        .padding(16.0)
        .fill(l.c.surface)
        .border(1.0, l.c.line)
        .radius(l.radius_lg)
        .child(inner)
}

fn waiting_line(l: &Look, device_page: &str) -> El {
    let spinner = el()
        .size(16.0, 16.0)
        .mt(2.0)
        .a11y_hidden()
        .child(icon(&icons::SPINNER_TRACK, 16.0).color(l.c.line).absolute(0.0, 0.0))
        .child(icon(&icons::SPINNER_ARC, 16.0).color(l.c.fill).absolute(0.0, 0.0));
    row()
        .key("waiting")
        .w_full()
        .gap(10.0)
        .items_start()
        .child(spinner)
        .child(
            text(format!("{} {}", words::waiting(device_page), words::BROWSER_OPENED))
                .key("waiting-text")
                .font(ty::SMALL)
                .color(l.c.fg_soft)
                .wrap()
                .grow(1.0),
        )
}

/// What didn't work, and what to do.
fn notice(l: &Look, message: &str) -> El {
    row()
        .key("notice")
        .role("alert")
        .w_full()
        .gap(12.0)
        .items_start()
        .px(16.0)
        .py(14.0)
        .fill(l.c.surface)
        .border(1.0, l.c.line)
        .radius(l.radius_lg)
        .child(icon(&icons::ALERT, 18.0).color(l.c.danger_ink).mt(1.0).a11y_hidden())
        .child(
            text(message)
                .key("notice-text")
                .font(ty::BODY)
                .color(l.c.fg)
                .wrap()
                .grow(1.0),
        )
}

fn actions(s: &Ui, l: &Look) -> El {
    let buttons = row().key("actions").w_full().gap(8.0).justify_end();
    match &s.sign_in {
        SignInPhase::Starting => buttons,
        SignInPhase::Waiting {
            user_code,
            verification_uri_complete,
        } => {
            let code = user_code.clone();
            let page = verification_uri_complete.clone();
            buttons
                .child(
                    parts::button(l, "copy-code", words::COPY_CODE, Tone::Secondary, 9.0, 13.0)
                        .on_press_tx(move |_: &mut Ui, tx: &mut Tx| tx.copy(code.clone())),
                )
                .child(
                    parts::button(l, "open-page", words::OPEN_AGAIN, Tone::Primary, 9.0, 13.0)
                        .on_press(move |s: &mut Ui| s.commands.open_link(Link::Url(page.clone()))),
                )
        }
        SignInPhase::Expired => buttons.child(retry(l, words::GET_NEW_CODE)),
        SignInPhase::Denied | SignInPhase::Unreachable => buttons.child(retry(l, words::TRY_AGAIN)),
    }
}

fn retry(l: &Look, label: &str) -> El {
    parts::button(l, "retry", label, Tone::Primary, 9.0, 13.0).on_press(|s: &mut Ui| {
        s.sign_in = SignInPhase::Starting;
        s.commands.start_sign_in();
    })
}
