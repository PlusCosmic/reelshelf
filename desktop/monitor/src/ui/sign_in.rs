//! What an ops section shows instead of its numbers: signing in (the code to approve on the device page while it
//! waits), update required, and why a signed-in account can't see them (the flag off, not an Admin).

use rune::{El, Tx, col, row, text};
use rune_kit::{Button, ButtonVariant, ControlSize};

use reelshelf_app::theme::{Look, ty};

use crate::copy;
use crate::links;
use crate::session::{Refusal, Session, SignedOutReason};
use crate::state::Monitor;

/// The page in place of an ops section's, when there is one.
pub fn gate(s: &Monitor, l: &Look) -> Option<El> {
    match (&s.session, s.refusal) {
        (Session::SignedIn { .. }, None) => None,
        (Session::SignedIn { .. }, Some(Refusal::FlagOff)) => {
            Some(super::message(l, copy::FLAG_OFF_TITLE, copy::FLAG_OFF).key("flag-off"))
        }
        (Session::SignedIn { .. }, Some(Refusal::NotAdmin)) => Some(
            super::message(l, copy::NOT_ADMIN_TITLE, copy::NOT_ADMIN)
                .key("not-admin")
                .child(row().px(32.0).child(sign_out_button(l))),
        ),
        (session, _) => Some(sign_in(s, session, l)),
    }
}

pub fn sign_out_button(l: &Look) -> El {
    Button::new(copy::SIGN_OUT)
        .variant(ButtonVariant::Outline)
        .control_size(ControlSize::Regular)
        .on_press(|s: &mut Monitor| s.sign_out())
        .build(&l.kit)
        .key("sign-out")
}

fn sign_in_button(l: &Look, words: &str) -> El {
    Button::new(words)
        .variant(ButtonVariant::Submit)
        .control_size(ControlSize::Large)
        .on_press(|s: &mut Monitor| s.sign_in())
        .build(&l.kit)
        .key("sign-in")
}

fn panel(l: &Look, children: Vec<El>) -> El {
    let card = children.into_iter().fold(
        col()
            .key("sign-in-card")
            .w_full()
            .max_w(460.0)
            .padding(28.0)
            .gap(16.0)
            .fill(l.c.surface)
            .border(1.0, l.c.line)
            .radius(l.radius_lg),
        El::child,
    );
    col().w_full().h_full().center().padding(24.0).child(card)
}

fn sign_in(s: &Monitor, session: &Session, l: &Look) -> El {
    let title = |words: &str| text(words.to_owned()).font(ty::TITLE).color(l.c.fg);
    let body = |words: String| text(words).font(ty::BODY).color(l.c.fg_soft).wrap();
    match session {
        Session::Checking => panel(l, vec![body(copy::CHECKING.to_owned())]),
        Session::UpdateRequired => panel(l, vec![title(copy::UPDATE_TITLE), body(copy::UPDATE.to_owned())]),
        Session::Starting => panel(
            l,
            vec![
                title(copy::SIGN_IN_TITLE),
                body(copy::SIGN_IN_INTRO.to_owned()),
                code_box(l, None),
            ],
        ),
        Session::Waiting(pending) => {
            let url = pending.verification_uri_complete.clone();
            panel(
                l,
                vec![
                    title(copy::SIGN_IN_TITLE),
                    body(copy::SIGN_IN_INTRO.to_owned()),
                    code_box(l, Some(pending.shown_code())),
                    body(copy::waiting(&links::device_page(&s.site))),
                    row()
                        .gap(8.0)
                        .child(
                            Button::new(copy::OPEN_DEVICE_PAGE)
                                .variant(ButtonVariant::Outline)
                                .control_size(ControlSize::Regular)
                                .on_press_tx(move |_: &mut Monitor, tx: &mut Tx| {
                                    let _ = tx.open_url(&url);
                                })
                                .build(&l.kit)
                                .key("open-device-page"),
                        )
                        .child(
                            Button::new(copy::CANCEL)
                                .variant(ButtonVariant::Ghost)
                                .control_size(ControlSize::Regular)
                                .on_press(|s: &mut Monitor| s.cancel_sign_in())
                                .build(&l.kit)
                                .key("cancel-sign-in"),
                        ),
                ],
            )
        }
        Session::SignedOut(reason) => {
            let mut children = vec![title(copy::SIGN_IN_TITLE), body(copy::SIGN_IN_INTRO.to_owned())];
            let words = match reason {
                None => None,
                Some(SignedOutReason::Expired) => Some(copy::EXPIRED),
                Some(SignedOutReason::Denied) => Some(copy::DENIED),
                Some(SignedOutReason::Unreachable) => Some(copy::UNREACHABLE),
                Some(SignedOutReason::Revoked) => Some(copy::REVOKED),
            };
            if let Some(words) = words {
                children.push(
                    text(words)
                        .key("sign-in-notice")
                        .role("alert")
                        .font(ty::BODY)
                        .color(l.c.danger_ink)
                        .wrap(),
                );
            }
            let again = matches!(
                reason,
                Some(SignedOutReason::Expired | SignedOutReason::Denied | SignedOutReason::Unreachable)
            );
            children.push(row().child(sign_in_button(
                l,
                if again { copy::SIGN_IN_AGAIN } else { copy::SIGN_IN },
            )));
            panel(l, children)
        }
        Session::SignedIn { .. } => panel(l, Vec::new()),
    }
}

/// The code, large, in a box; "Getting a code…" until there is one.
fn code_box(l: &Look, code: Option<String>) -> El {
    let inner = match code {
        Some(code) => text(code).key("code").font(ty::CODE).color(l.c.fg).selectable(true),
        None => text(copy::STARTING).key("code").font(ty::BODY).color(l.c.fg_soft),
    };
    col()
        .key("code-box")
        .w_full()
        .min_h(80.0)
        .center()
        .gap(4.0)
        .padding(14.0)
        .fill(l.c.bg)
        .border(1.0, l.c.line)
        .radius(l.radius_lg)
        .child(text(copy::CODE_LABEL).font(ty::LABEL).color(l.c.fg_soft))
        .child(inner)
}
