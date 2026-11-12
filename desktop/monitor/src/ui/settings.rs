//! Settings: the Reelshelf account this Mac is signed in as (and Sign out), and Grafana's address and token, kept in
//! the Keychain.

use rune::{El, col, row, text};
use rune_kit::{Button, ButtonVariant, ControlSize, TextInput};

use reelshelf_app::theme::{Look, ty};

use super::parts;
use super::sign_in::sign_out_button;
use crate::copy;
use crate::session::Session;
use crate::state::Monitor;

pub fn page(s: &Monitor, l: &Look) -> El {
    parts::page(
        "settings",
        vec![account(s, l), grafana(s, l), parts::note(l, copy::version(&s.version))],
    )
}

fn account(s: &Monitor, l: &Look) -> El {
    let mut children = Vec::new();
    match &s.session {
        Session::SignedIn { user } => {
            let words = match user {
                Some(u) => copy::signed_in_as(&u.username, &s.site),
                None => copy::signed_in_on(&s.site),
            };
            children.push(text(words).font(ty::BODY).color(l.c.fg).wrap());
            children.push(row().child(sign_out_button(l)));
        }
        Session::Checking => children.push(parts::note(l, copy::CHECKING)),
        _ => {
            children.push(text(copy::SIGNED_OUT).font(ty::BODY).color(l.c.fg_soft));
            children.push(
                row().child(
                    Button::new(copy::SIGN_IN)
                        .variant(ButtonVariant::Submit)
                        .control_size(ControlSize::Regular)
                        .on_press(|s: &mut Monitor| {
                            s.section = crate::state::Section::Overview;
                            s.sign_in();
                        })
                        .build(&l.kit)
                        .key("settings-sign-in"),
                ),
            );
        }
    }
    parts::card(l, "account", copy::REELSHELF, children)
}

fn field(l: &Look, label: &str, input: El) -> El {
    col()
        .w_full()
        .max_w(520.0)
        .gap(6.0)
        .child(text(label.to_owned()).font(ty::LABEL).color(l.c.fg_soft))
        .child(input)
}

fn grafana(s: &Monitor, l: &Look) -> El {
    let k = &l.kit;
    let f = &s.grafana_form;
    let mut children = vec![parts::note(l, copy::GRAFANA_INTRO)];
    if f.from_env {
        children.push(parts::note(l, copy::FROM_ENVIRONMENT));
    }
    children.push(field(
        l,
        copy::GRAFANA_URL,
        TextInput::new(&f.url)
            .placeholder(copy::GRAFANA_URL_PLACEHOLDER)
            .label(copy::GRAFANA_URL)
            .on_edit(|s: &mut Monitor| &mut s.grafana_form.url)
            .on_submit(|s: &mut Monitor, _| s.save_grafana())
            .build(k)
            .key("grafana-url"),
    ));
    children.push(field(
        l,
        copy::GRAFANA_TOKEN,
        TextInput::new(&f.token)
            .secure()
            .placeholder(if f.token_kept {
                copy::GRAFANA_TOKEN_KEPT
            } else {
                copy::GRAFANA_TOKEN_PLACEHOLDER
            })
            .label(copy::GRAFANA_TOKEN)
            .on_edit(|s: &mut Monitor| &mut s.grafana_form.token)
            .on_submit(|s: &mut Monitor, _| s.save_grafana())
            .build(k)
            .key("grafana-token"),
    ));
    let mut actions = row().gap(8.0).center_y().child(
        Button::new(copy::SAVE)
            .variant(ButtonVariant::Submit)
            .control_size(ControlSize::Regular)
            .on_press(|s: &mut Monitor| s.save_grafana())
            .build(k)
            .key("grafana-save"),
    );
    if s.connected() && !f.from_env {
        actions = actions.child(
            Button::new(copy::REMOVE)
                .variant(ButtonVariant::Ghost)
                .control_size(ControlSize::Regular)
                .on_press(|s: &mut Monitor| s.remove_grafana())
                .build(k)
                .key("grafana-remove"),
        );
    }
    match &f.note {
        Some(Ok(())) => actions = actions.child(text(copy::SAVED).font(ty::META).color(l.c.fg_soft)),
        Some(Err(e)) => {
            actions = actions.child(
                text(e.clone())
                    .key("grafana-error")
                    .font(ty::META)
                    .color(l.c.danger_ink),
            )
        }
        None => {}
    }
    children.push(actions);
    parts::card(l, "grafana", copy::GRAFANA, children)
}
