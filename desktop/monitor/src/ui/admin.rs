//! Admin: the site's `/admin/accounts` and `/admin/reports` from the Mac, in two tabs. Each is a list on the left and
//! the one picked on the right; every change asks for confirmation in a dialog that names what it changes, says what
//! happens and takes the reason or note the API records, and shows the API's words when it refuses.

use rune::{El, Tx, col, el, row, spacer, text};
use rune_kit::shell::Toolbar;
use rune_kit::{
    Badge, BadgeVariant, Button, ButtonVariant, ControlSize, Dialog, SegmentedControl, Select, SelectAppearance, State,
    TextInput,
};

use reelshelf_api::{AdminAccountDetail, AdminContentReport};
use reelshelf_app::theme::{Look, ty};

use super::parts::{self, Cell, Tone, column};
use crate::admin::{self, AdminTab, Change, GRANTS, REASONS, ROLES, StatusFilter, is_urgent};
use crate::copy;
use crate::state::{Load, Monitor};
use crate::units::{date, when};

pub fn page(s: &Monitor, l: &Look, layer: &mut Vec<El>) -> (El, Toolbar) {
    let toolbar = toolbar(s, l);
    if let Some(gate) = super::sign_in::gate_session(s, l) {
        return (gate, toolbar);
    }
    if s.admin.forbidden {
        return (
            super::message(l, copy::NOT_ADMIN_TITLE, copy::NOT_ADMIN).key("admin-forbidden"),
            toolbar,
        );
    }
    let body = match s.admin.tab {
        AdminTab::Accounts => accounts(s, l),
        AdminTab::Reports => reports(s, l, layer),
    };
    confirm(s, l, layer);
    (body, toolbar)
}

fn toolbar(s: &Monitor, l: &Look) -> Toolbar {
    let mut actions = row().gap(10.0).center_y();
    if s.session.signed_in() && !s.admin.forbidden {
        let tabs = [AdminTab::Accounts, AdminTab::Reports];
        let picked = tabs.iter().position(|t| *t == s.admin.tab);
        actions = actions
            .child(
                SegmentedControl::new(&[copy::ACCOUNTS, copy::REPORTS], picked)
                    .label(copy::ADMIN_VIEW)
                    .width(Some(200.0))
                    .on_pick(move |s: &mut Monitor, i: usize, _: &mut Tx| s.admin_tab(tabs[i]))
                    .build(&l.kit)
                    .key("admin-tab"),
            )
            .child(
                Button::new(copy::REFRESH)
                    .variant(ButtonVariant::Ghost)
                    .control_size(ControlSize::Regular)
                    .on_press(|s: &mut Monitor| s.poll())
                    .build(&l.kit)
                    .key("refresh"),
            );
    }
    Toolbar::new().title(copy::ADMIN).trailing(actions)
}

/// The list on the left and the picked item on the right, scrolling together.
fn split(key: &str, list: Vec<El>, detail: Vec<El>) -> El {
    let left = list.into_iter().fold(col().w(420.0).gap(12.0), El::child);
    let right = detail
        .into_iter()
        .fold(col().grow(1.0).min_w(320.0).gap(16.0), El::child);
    parts::page(
        key,
        vec![row().w_full().gap(16.0).items_start().child(left).child(right)],
    )
}

/// A row in a list that opens what it names.
fn pick_row(l: &Look, key: String, label: String, picked: bool, urgent: bool, lines: Vec<El>) -> El {
    let r = lines.into_iter().fold(
        col()
            .key(key)
            .role("button")
            .label(label)
            .focusable(true)
            .focus_ring(2.0, 1.0, l.c.accent)
            .w_full()
            .gap(4.0)
            .px(12.0)
            .py(10.0)
            .radius(l.radius)
            .fill(if picked { l.c.selected } else { l.c.surface })
            .hover(l.c.selected)
            .border(1.0, if urgent { l.c.danger } else { l.c.line }),
        El::child,
    );
    if urgent {
        r.border_sides([1.0, 1.0, 1.0, 4.0], l.c.danger)
    } else {
        r
    }
}

fn badge(l: &Look, words: &str, accent: bool) -> El {
    Badge::new(words)
        .variant(if accent {
            BadgeVariant::Accent
        } else {
            BadgeVariant::Neutral
        })
        .build(&l.kit)
}

/// A red badge: suspended, an urgent reason.
fn danger_badge(l: &Look, words: &str) -> El {
    row()
        .role("status")
        .label(words.to_owned())
        .h(16.0)
        .px(4.0)
        .radius(2.0)
        .center()
        .border(1.0, l.c.danger)
        .child(
            text(words.to_owned())
                .font(rune::TextStyle::new(11.0, 600.0).lh(11.0))
                .color(l.c.danger_ink)
                .a11y_hidden(),
        )
}

fn badges(items: Vec<El>) -> El {
    items.into_iter().fold(row().gap(6.0).center_y().flex_wrap(), El::child)
}

fn error_line(l: &Look, words: String) -> El {
    text(words).font(ty::META).color(l.c.danger_ink).wrap()
}

fn done_line(s: &Monitor, l: &Look) -> Option<El> {
    s.admin.done.as_ref().map(|d| {
        text(d.clone())
            .key("admin-done")
            .font(ty::ROW_STRONG)
            .color(l.c.accent)
            .wrap()
    })
}

// Accounts.

fn accounts(s: &Monitor, l: &Look) -> El {
    let a = &s.admin;
    let mut list = vec![
        col()
            .w_full()
            .gap(6.0)
            .child(text(copy::FIND_ACCOUNT).font(ty::LABEL).color(l.c.fg_soft))
            .child(
                TextInput::new(&a.query)
                    .placeholder(copy::FIND_ACCOUNT_PLACEHOLDER)
                    .width(None)
                    .label(copy::FIND_ACCOUNT)
                    .on_edit(|s: &mut Monitor| &mut s.admin.query)
                    .on_change(|s: &mut Monitor, _: &mut Tx| s.search_accounts())
                    .on_submit(|s: &mut Monitor, _| s.search_accounts())
                    .build(&l.kit)
                    .key("account-search"),
            ),
    ];
    match &a.results {
        None => list.push(parts::note(l, copy::FIND_ACCOUNT_HINT)),
        Some(Load::Loading) => list.push(parts::note(l, copy::SEARCHING)),
        Some(Load::Failed(e)) => list.push(error_line(l, copy::couldnt_load(e))),
        Some(Load::Ready(found)) if found.is_empty() => list.push(parts::note(l, copy::NO_MATCH)),
        Some(Load::Ready(found)) => {
            for (i, account) in found.iter().enumerate() {
                let id = account.id;
                let name = admin::display_name(account).to_owned();
                let mut tags = vec![badge(l, copy::role(&account.role), account.role == "admin")];
                if let Some(t) = &account.granted_tier {
                    tags.push(badge(l, &copy::granted(copy::tier(t)), true));
                }
                if account.suspended_at.is_some() {
                    tags.push(danger_badge(l, copy::SUSPENDED));
                }
                let sign_ins = account
                    .identities
                    .iter()
                    .map(|i| copy::sign_in_short(&i.provider, &i.username))
                    .collect::<Vec<_>>()
                    .join(copy::SEPARATOR);
                let mut lines = vec![text(name.clone()).font(ty::ROW_STRONG).color(l.c.fg).truncate()];
                if !sign_ins.is_empty() {
                    lines.push(text(sign_ins).font(ty::META).color(l.c.fg_soft).truncate());
                }
                lines.push(badges(tags));
                list.push(
                    pick_row(l, format!("account-{i}"), name, a.account == Some(id), false, lines)
                        .on_press(move |s: &mut Monitor| s.open_account(id)),
                );
            }
        }
    }
    let detail = match &a.detail {
        None => vec![parts::note(l, copy::PICK_ACCOUNT)],
        Some(Load::Loading) => vec![parts::note(l, copy::LOADING)],
        Some(Load::Failed(e)) => vec![error_line(l, copy::couldnt_load(e))],
        Some(Load::Ready(d)) => account(s, l, d),
    };
    split("admin-accounts", list, detail)
}

fn account(s: &Monitor, l: &Look, d: &AdminAccountDetail) -> Vec<El> {
    let a = &d.account;
    let name = admin::display_name(a).to_owned();
    let is_self = s.me() == Some(a.id);
    let mut about = vec![
        text(name.clone())
            .key("account-name")
            .font(ty::TITLE_SMALL)
            .color(l.c.fg),
    ];
    if a.global_name.is_some() {
        about.push(parts::note(l, a.username.clone()));
    }
    about.push(parts::pair(
        l,
        copy::PLAN_NOW,
        copy::on_tier(copy::tier(&d.tier), a.granted_tier.is_some()),
    ));
    about.push(
        row()
            .w_full()
            .gap(12.0)
            .child(text(copy::ACCOUNT_ID).font(ty::ROW).color(l.c.fg_soft).w(110.0))
            .child(
                text(a.id.to_string())
                    .font(parts::NUMBER)
                    .color(l.c.fg)
                    .selectable(true),
            ),
    );
    for (i, identity) in a.identities.iter().enumerate() {
        about.push(
            parts::pair(
                l,
                if i == 0 { copy::SIGN_INS } else { "" },
                copy::identity(&identity.provider, &identity.username, &identity.provider_user_id),
            )
            .key(format!("identity-{i}")),
        );
    }
    if let Some(at) = a.suspended_at {
        about.push(parts::alert(l, copy::suspended_since(&date(at, &s.tz))));
    }
    if let Some(done) = done_line(s, l) {
        about.push(done);
    }
    let mut cards = vec![parts::card(l, "account-card", copy::ACCOUNT, about)];
    cards.push(access(l, d, &name, is_self));
    cards.push(changes(s, l, d));
    cards
}

/// Role, granted plan and suspension, each changed through a confirmation.
fn access(l: &Look, d: &AdminAccountDetail, name: &str, is_self: bool) -> El {
    let a = &d.account;
    let id = a.id;
    let k = &l.kit;
    let role_labels: Vec<&str> = ROLES.iter().map(|r| copy::role(r)).collect();
    let mut roles = SegmentedControl::new(&role_labels, ROLES.iter().position(|r| *r == a.role))
        .label(copy::ROLE)
        .width(Some(270.0));
    if is_self {
        for i in 0..ROLES.len() {
            roles = roles.segment_state(i, State::Disabled);
        }
    } else {
        let (from, who) = (a.role.clone(), name.to_owned());
        roles = roles.on_pick(move |s: &mut Monitor, i: usize, _: &mut Tx| {
            if ROLES[i] != from {
                s.ask(Change::Role {
                    account: id,
                    name: who.clone(),
                    from: from.clone(),
                    to: ROLES[i].to_owned(),
                });
            }
        });
    }
    let grant_labels: Vec<&str> = GRANTS.iter().map(|g| g.map_or(copy::NO_GRANT, copy::tier)).collect();
    let (from, who) = (a.granted_tier.clone(), name.to_owned());
    let grants = SegmentedControl::new(
        &grant_labels,
        GRANTS.iter().position(|g| g.map(str::to_owned) == a.granted_tier),
    )
    .label(copy::GRANTED_PLAN)
    .width(Some(340.0))
    .on_pick(move |s: &mut Monitor, i: usize, _: &mut Tx| {
        let to = GRANTS[i].map(str::to_owned);
        if to != from {
            s.ask(Change::GrantedTier {
                account: id,
                name: who.clone(),
                from: from.clone(),
                to,
            });
        }
    });
    let suspended = a.suspended_at.is_some();
    let admin = a.role == "admin";
    let who = name.to_owned();
    let suspension = Button::new(if suspended {
        copy::LIFT_SUSPENSION
    } else {
        copy::SUSPEND
    })
    .variant(if suspended {
        ButtonVariant::Outline
    } else {
        ButtonVariant::Danger
    })
    .control_size(ControlSize::Regular)
    .disabled(is_self || (admin && !suspended))
    .on_press(move |s: &mut Monitor| {
        let name = who.clone();
        s.ask(if suspended {
            Change::LiftSuspension { account: id, name }
        } else {
            Change::Suspend { account: id, name }
        })
    })
    .build(k)
    .key("suspension");
    let line = |label: &str, control: El| {
        row()
            .w_full()
            .gap(12.0)
            .center_y()
            .child(text(label.to_owned()).font(ty::ROW).color(l.c.fg_soft).w(110.0))
            .child(control)
    };
    let mut children = vec![
        line(copy::ROLE, el().key("role").child(roles.build(k))),
        line(copy::GRANTED_PLAN, el().key("granted").child(grants.build(k))),
        line(
            copy::SUSPENSION,
            row().gap(12.0).center_y().child(suspension).child(parts::note(
                l,
                if suspended {
                    copy::SUSPENDED
                } else {
                    copy::NOT_SUSPENDED
                },
            )),
        ),
    ];
    if is_self {
        children.push(parts::note(l, copy::CANT_CHANGE_SELF).key("cant-self"));
    } else if admin && !suspended {
        children.push(parts::note(l, copy::CANT_SUSPEND_ADMIN).key("cant-admin"));
    }
    parts::card(l, "access", copy::ACCESS, children)
}

fn changes(s: &Monitor, l: &Look, d: &AdminAccountDetail) -> El {
    if d.changes.is_empty() {
        return parts::card(l, "changes", copy::CHANGES, vec![parts::note(l, copy::NO_CHANGES)]);
    }
    let describe = |field: &str, value: Option<&str>| -> String {
        match value {
            None => copy::NONE.to_owned(),
            Some(v) if field == "role" => copy::role(v).to_owned(),
            Some(v) => copy::tier(v).to_owned(),
        }
    };
    let rows = d
        .changes
        .iter()
        .map(|c| {
            let what = match c.field.as_str() {
                "suspended" if c.new_value.is_some() => copy::SUSPENDED.to_owned(),
                "suspended" => copy::SUSPENSION_LIFTED.to_owned(),
                field => copy::changed(
                    if field == "role" {
                        copy::ROLE
                    } else {
                        copy::GRANTED_PLAN
                    },
                    &describe(field, c.old_value.as_deref()),
                    &describe(field, c.new_value.as_deref()),
                ),
            };
            vec![
                Cell::new(c.reason.clone()),
                Cell::new(what).tone(Tone::Strong),
                Cell::new(c.changed_by_name.clone().unwrap_or_else(|| copy::NOBODY.to_owned())).tone(Tone::Soft),
                Cell::new(when(c.changed_at, s.now_ms, &s.tz)).tone(Tone::Soft),
            ]
        })
        .collect();
    parts::card(
        l,
        "changes",
        copy::CHANGES,
        vec![parts::table(
            l,
            "changes-table",
            &[
                column(copy::REASON, 0.0),
                column(copy::CHANGE, 190.0),
                column(copy::BY, 110.0),
                column(copy::WHEN, 130.0),
            ],
            rows,
        )],
    )
}

// Reports.

fn status_label(f: StatusFilter) -> &'static str {
    copy::report_status(f.status())
}

fn reports(s: &Monitor, l: &Look, layer: &mut Vec<El>) -> El {
    let a = &s.admin;
    let k = &l.kit;
    let labels: Vec<&str> = StatusFilter::ALL.iter().map(|f| status_label(*f)).collect();
    let statuses = SegmentedControl::new(&labels, StatusFilter::ALL.iter().position(|f| *f == a.status))
        .label(copy::STATUS_FILTER)
        .width(Some(420.0))
        .on_pick(|s: &mut Monitor, i: usize, _: &mut Tx| s.filter_status(StatusFilter::ALL[i]))
        .build(k);
    let reason_labels: Vec<&str> = std::iter::once(copy::ALL_REASONS)
        .chain(REASONS.iter().map(|r| copy::report_reason(r)))
        .collect();
    let current = a.reason.as_deref().map_or(copy::ALL_REASONS, copy::report_reason);
    let reasons = Select::new("reason-filter", &reason_labels)
        .current(Some(current))
        .label(copy::REASON_FILTER)
        .appearance(SelectAppearance::Field)
        .width(420.0)
        .open(a.reason_menu)
        .on_toggle(|s: &mut Monitor| s.admin.reason_menu = !s.admin.reason_menu)
        .on_close(|s: &mut Monitor| s.admin.reason_menu = false)
        .on_choose(|s: &mut Monitor, picked: &str, _: &mut Tx| {
            s.admin.reason = REASONS
                .iter()
                .find(|r| copy::report_reason(r) == picked)
                .map(|r| (*r).to_owned());
            s.admin.reason_menu = false;
        })
        .build(k, layer);
    let mut list = vec![
        el().key("status-filter").child(statuses),
        el().key("reason-filter-field").child(reasons),
    ];
    let shown = a.reports();
    let fetched = a.list();
    if let Some(e) = &fetched.error {
        list.push(error_line(l, copy::couldnt_refresh(e)));
    }
    if fetched.value.is_none() {
        if fetched.error.is_none() {
            list.push(parts::note(l, copy::LOADING));
        }
    } else if shown.is_empty() {
        list.push(parts::note(
            l,
            if a.status == StatusFilter::Open && a.reason.is_none() {
                copy::NO_OPEN_REPORTS
            } else {
                copy::NO_REPORTS
            },
        ));
    }
    for (i, r) in shown.iter().enumerate() {
        let id = r.id;
        let urgent = is_urgent(&r.reason) && r.status == "open";
        let title = r.clip_title.clone().unwrap_or_else(|| copy::UNTITLED.to_owned());
        let mut tags = vec![if is_urgent(&r.reason) {
            danger_badge(l, copy::report_reason(&r.reason))
        } else {
            badge(l, copy::report_reason(&r.reason), false)
        }];
        if r.open_reports_on_clip > 1 && r.status == "open" {
            tags.push(badge(l, &copy::open_on_clip(r.open_reports_on_clip), false));
        }
        let mut meta = copy::reported(&when(r.created_at, s.now_ms, &s.tz));
        if let Some(owner) = &r.owner_name {
            meta = copy::owner_and(owner, &meta);
        }
        list.push(
            pick_row(
                l,
                format!("report-{i}"),
                title.clone(),
                a.report == Some(id),
                urgent,
                vec![
                    badges(tags),
                    text(title).font(ty::ROW_STRONG).color(l.c.fg).truncate(),
                    text(meta).font(ty::META).color(l.c.fg_soft).truncate(),
                ],
            )
            .on_press(move |s: &mut Monitor| s.open_report(id)),
        );
    }
    let detail = match a.selected_report() {
        Some(r) => report(s, l, r),
        None => vec![parts::note(l, copy::PICK_REPORT)],
    };
    split("admin-reports", list, detail)
}

/// What has become of the clip's sharing.
fn share_state(r: &AdminContentReport) -> &'static str {
    if r.status == "removed" {
        copy::SHARE_REMOVED
    } else if r.clip_id.is_none() {
        copy::SHARE_DELETED
    } else if r.sharing_blocked {
        copy::SHARE_BLOCKED
    } else if r.share_hidden {
        copy::SHARE_HIDDEN
    } else if r.share_url.is_some() {
        copy::SHARE_LIVE
    } else {
        copy::SHARE_NO_LINK
    }
}

fn report(s: &Monitor, l: &Look, r: &AdminContentReport) -> Vec<El> {
    let k = &l.kit;
    let urgent = is_urgent(&r.reason);
    let title = r.clip_title.clone().unwrap_or_else(|| copy::UNTITLED.to_owned());
    let mut head = vec![if urgent {
        danger_badge(l, copy::report_reason(&r.reason))
    } else {
        badge(l, copy::report_reason(&r.reason), false)
    }];
    head.push(badge(l, copy::report_status(&r.status), r.status == "open"));
    if r.open_reports_on_clip > 1 && r.status == "open" {
        head.push(badge(l, &copy::open_on_clip(r.open_reports_on_clip), false));
    }
    let mut about = vec![
        badges(head),
        text(title.clone())
            .key("report-title")
            .font(ty::TITLE_SMALL)
            .color(l.c.fg)
            .wrap(),
        parts::note(l, copy::reported(&when(r.created_at, s.now_ms, &s.tz))),
    ];
    if let Some(owner) = &r.owner_name {
        about.push(parts::pair(l, copy::OWNER, owner.clone()));
    }
    if let Some(email) = &r.reporter_email {
        about.push(parts::pair(l, copy::REPORTER, email.clone()));
    }
    about.push(parts::pair(l, copy::SHARING, share_state(r).to_owned()));
    if let Some(clip) = r.clip_id {
        about.push(parts::clip_id(s, l, "report-clip", clip));
    }
    // Admins are never shown a child-safety report's clip (the API sends no link either).
    if r.reason != "child_safety"
        && let Some(path) = &r.share_url
    {
        let url = format!("{}{path}", s.site);
        about.push(
            row().child(
                Button::new(copy::OPEN_SHARED_CLIP)
                    .variant(ButtonVariant::Outline)
                    .control_size(ControlSize::Small)
                    .on_press_tx(move |_: &mut Monitor, tx: &mut Tx| {
                        let _ = tx.open_url(&url);
                    })
                    .build(k)
                    .key("open-shared-clip"),
            ),
        );
    }
    if r.reason == "child_safety" && r.status == "open" {
        about.push(
            parts::alert(
                l,
                copy::child_safety_warning(
                    &r.clip_id
                        .map_or_else(|| copy::CLIP_DELETED.to_owned(), |c| c.to_string()),
                ),
            )
            .key("child-safety-warning"),
        );
    }
    about.push(text(copy::REPORTER_SAID).font(ty::LABEL).color(l.c.fg_soft));
    about.push(if r.details.trim().is_empty() {
        parts::note(l, copy::NOTHING_SAID)
    } else {
        text(r.details.clone())
            .key("report-details")
            .font(ty::BODY)
            .color(l.c.fg)
            .wrap()
            .selectable(true)
    });
    if let Some(done) = done_line(s, l) {
        about.push(done);
    }
    let mut cards = vec![parts::card(l, "report-card", copy::REPORT, about)];
    cards.push(if r.status == "open" {
        actions(l, r, &title)
    } else {
        outcome(s, l, r)
    });
    cards
}

fn actions(l: &Look, r: &AdminContentReport, title: &str) -> El {
    let k = &l.kit;
    let urgent = is_urgent(&r.reason);
    let (id, clip, owner) = (r.id, title.to_owned(), r.owner_name.clone());
    let main = if urgent {
        Button::new(copy::REMOVE_CLIP)
            .variant(ButtonVariant::Danger)
            .control_size(ControlSize::Regular)
            .on_press(move |s: &mut Monitor| {
                s.ask(Change::Remove {
                    report: id,
                    clip: clip.clone(),
                    owner: owner.clone(),
                })
            })
            .build(k)
            .key("remove")
    } else {
        Button::new(copy::TAKE_DOWN)
            .variant(ButtonVariant::Danger)
            .control_size(ControlSize::Regular)
            .on_press(move |s: &mut Monitor| {
                s.ask(Change::TakeDown {
                    report: id,
                    clip: clip.clone(),
                })
            })
            .build(k)
            .key("take-down")
    };
    let clip = title.to_owned();
    let dismiss = Button::new(copy::DISMISS)
        .variant(ButtonVariant::Outline)
        .control_size(ControlSize::Regular)
        .on_press(move |s: &mut Monitor| {
            s.ask(Change::Dismiss {
                report: id,
                clip: clip.clone(),
            })
        })
        .build(k)
        .key("dismiss");
    let mut children = Vec::new();
    if urgent {
        children.push(parts::note(
            l,
            copy::remove_explain(r.owner_name.as_deref().unwrap_or(copy::THE_OWNER)),
        ));
    }
    children.push(row().gap(8.0).center_y().child(main).child(dismiss));
    parts::card(l, "report-actions", copy::DEAL_WITH_IT, children)
}

/// How a report was dealt with, and for a removed clip what became of its evidence and owner.
fn outcome(s: &Monitor, l: &Look, r: &AdminContentReport) -> El {
    let status = copy::report_status(&r.status);
    let at = r.resolved_at.map(|t| when(t, s.now_ms, &s.tz)).unwrap_or_default();
    let mut children = vec![
        text(match &r.resolved_by_name {
            Some(name) => copy::resolved_by(status, &at, name),
            None => copy::resolved(status, &at),
        })
        .font(ty::ROW_STRONG)
        .color(l.c.fg),
    ];
    if let Some(note) = &r.resolution_note {
        children.push(text(note.clone()).font(ty::BODY).color(l.c.fg).wrap());
    }
    if r.status == "removed" {
        match &r.evidence {
            None => children.push(parts::note(l, copy::NOTHING_KEPT)),
            Some(e) if e.status == "failed" => children.push(parts::alert(
                l,
                copy::evidence_failed(e.error.as_deref().unwrap_or(copy::NO_ERROR_RECORDED)),
            )),
            Some(e) if e.status == "pending" => {
                children.push(parts::note(l, copy::EVIDENCE_PENDING));
                if let Some(error) = &e.error {
                    children.push(parts::note(l, copy::evidence_retrying(error)));
                }
            }
            Some(e) => children.push(parts::note(l, copy::evidence_kept(&date(e.delete_after, &s.tz)))),
        }
        if r.evidence.as_ref().and_then(|e| e.cdn_purge.as_deref()) == Some("not_configured") {
            children.push(parts::alert(l, copy::NOT_PURGED.to_owned()));
        }
        children.push(parts::note(
            l,
            if r.owner_suspended {
                copy::OWNER_SUSPENDED
            } else {
                copy::OWNER_NOT_SUSPENDED
            },
        ));
    }
    parts::card(l, "report-outcome", status, children)
}

// The confirmation.

fn confirm(s: &Monitor, l: &Look, layer: &mut Vec<El>) {
    let Some(c) = &s.admin.confirm else {
        return;
    };
    let k = &l.kit;
    let (title, what, button) = copy::confirm(&c.change);
    let mut body = col()
        .w_full()
        .px(16.0)
        .pb(16.0)
        .gap(12.0)
        .grow(1.0)
        .child(text(what).key("confirm-what").font(ty::BODY).color(l.c.fg).wrap());
    if matches!(c.change, Change::Remove { .. }) {
        body = body.child(parts::alert(l, copy::CANT_UNDO.to_owned()).key("cant-undo"));
    }
    let required = c.change.note_required();
    let label = if c.change.account().is_some() {
        copy::REASON
    } else {
        copy::NOTE
    };
    body = body.child(
        col()
            .w_full()
            .gap(6.0)
            .child(text(label).font(ty::LABEL).color(l.c.fg_soft))
            .child(
                TextInput::new(&c.note)
                    .placeholder(if required { copy::WHY } else { copy::WHY_OPTIONAL })
                    .width(None)
                    .label(label)
                    .disabled(c.busy)
                    .on_edit(|s: &mut Monitor| &mut s.admin.confirm.as_mut().expect("open").note)
                    .on_submit(|s: &mut Monitor, _| s.confirm_change())
                    .build(k)
                    .key("confirm-note"),
            ),
    );
    if let Some(e) = &c.error {
        body = body.child(error_line(l, e.clone()).key("confirm-error"));
    }
    body = body.child(spacer()).child(
        row()
            .w_full()
            .gap(8.0)
            .justify_end()
            .child(
                Button::new(copy::CANCEL)
                    .variant(ButtonVariant::Outline)
                    .control_size(ControlSize::Regular)
                    .disabled(c.busy)
                    .on_press(|s: &mut Monitor| s.cancel_change())
                    .build(k)
                    .key("confirm-cancel"),
            )
            .child(
                Button::new(if c.busy { copy::SAVING } else { button })
                    .variant(if c.change.destructive() {
                        ButtonVariant::Danger
                    } else {
                        ButtonVariant::Submit
                    })
                    .control_size(ControlSize::Regular)
                    .disabled(!c.can_send())
                    .on_press(|s: &mut Monitor| s.confirm_change())
                    .build(k)
                    .key("confirm-send"),
            ),
    );
    Dialog::new("confirm", title)
        .width(480.0)
        .open(true)
        .on_close(|s: &mut Monitor| s.cancel_change())
        .child(body)
        .build(k, layer);
}
