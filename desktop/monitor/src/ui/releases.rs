//! Releases, from GitHub: what is deployed (each image tag in infrastructure's compose files, its commit and the
//! commits main has that it doesn't), what is coming (the image-bump PR and main's workflows), the uploader's latest
//! tag and publish run, and the open pull requests with their checks. Every title opens on GitHub in the browser.

use rune::{El, Link, LinkPaint, Tx, col, el, row, spacer, text};
use rune_kit::{Button, ButtonVariant, ControlSize};

use reelshelf_app::theme::{Look, ty};

use super::parts;
use crate::copy;
use crate::github::model::{Commit, short};
use crate::github::releases::{Behind, PullStatus, Uploader, WorkflowStatus};
use crate::github::{Checks, Deployed, Environment, Releases, Run};
use crate::state::{Monitor, Section};
use crate::units::{count, when};

/// Commits listed under an open row; the rest are a link to the comparison.
const LISTED: usize = 20;

pub fn page(s: &Monitor, l: &Look) -> El {
    if !s.github {
        let open = Button::new(copy::OPEN_SETTINGS)
            .variant(ButtonVariant::Submit)
            .control_size(ControlSize::Regular)
            .on_press(|s: &mut Monitor| s.select(Section::Settings))
            .build(&l.kit)
            .key("open-settings");
        return super::message(l, copy::GITHUB_NOT_CONNECTED_TITLE, copy::GITHUB_NOT_CONNECTED)
            .child(row().px(32.0).child(open));
    }
    let Some(r) = &s.releases.value else {
        return super::loading(s, l);
    };
    parts::page(
        "releases",
        vec![deployed(s, r, l), coming(s, r, l), uploader(s, r, l), pulls(s, r, l)],
    )
}

/// Text that opens `url` in the browser.
pub fn link(l: &Look, key: &str, words: String, url: &str, font: rune::TextStyle) -> El {
    let len = words.len();
    text(words)
        .key(key.to_owned())
        .font(font)
        .color(l.c.fg)
        .truncate()
        .link(Link::new(0..len, url.to_owned()).hover(LinkPaint::new().underline(l.c.fg)))
        .on_link(|_: &mut Monitor, url: &str, tx: &mut Tx| {
            let _ = tx.open_url(url);
        })
}

/// A dot and a word for how checks went: red failing, amber running, green passing.
pub fn checks(l: &Look, c: Option<Checks>) -> El {
    let (words, dot, ink) = match c {
        Some(Checks::Failing) => (copy::FAILING, l.c.danger, l.c.danger_ink),
        Some(Checks::Running) => (copy::RUNNING, l.c.warning, l.c.warn_ink),
        Some(Checks::Passing) => (copy::PASSING, l.c.accent, l.c.fg),
        Some(Checks::Cancelled) => (copy::CANCELLED, l.c.line, l.c.fg_soft),
        Some(Checks::None) => (copy::NO_CHECKS, l.c.line, l.c.fg_soft),
        None => (copy::CHECKS_UNKNOWN, l.c.line, l.c.fg_soft),
    };
    row()
        .gap(6.0)
        .center_y()
        .child(el().size(7.0, 7.0).radius(3.5).fill(dot).a11y_hidden())
        .child(text(words).font(ty::STATUS).color(ink))
}

/// A part GitHub couldn't read.
fn failed(l: &Look, error: &str) -> El {
    text(copy::couldnt_refresh(error))
        .font(ty::META)
        .color(l.c.danger_ink)
        .wrap()
}

fn meta(l: &Look, words: String) -> El {
    text(words).font(ty::META).color(l.c.fg_soft).truncate()
}

fn commit_meta(s: &Monitor, c: &Commit) -> String {
    copy::commit_meta(
        c.short_sha(),
        c.author().unwrap_or_default(),
        &c.date().map(|d| when(d, s.now_ms, &s.tz)).unwrap_or_default(),
    )
}

/// How long ago, "3 h 5 min".
fn age(s: &Monitor, at: chrono::DateTime<chrono::Utc>) -> String {
    copy::age((s.now_ms - at.timestamp_millis()) as f64 / 1000.0)
}

/// What a row deploys: "App", or "Overflow workers (ec2, ecs, batch)".
fn what(d: &Deployed) -> String {
    if d.image.pools.is_empty() {
        copy::image(&d.image.image).to_owned()
    } else {
        copy::overflow_workers(&d.image.pools.join(", "))
    }
}

fn deployed(s: &Monitor, r: &Releases, l: &Look) -> El {
    let mut children = vec![parts::note(l, copy::DEPLOYED_INTRO)];
    match &r.deployed {
        Err(e) => children.push(failed(l, e)),
        Ok(rows) => {
            for (i, d) in rows.iter().enumerate() {
                children.push(deployed_row(s, l, i, d));
            }
        }
    }
    parts::card(l, "deployed", copy::DEPLOYED, children)
}

fn deployed_row(s: &Monitor, l: &Look, i: usize, d: &Deployed) -> El {
    let key = format!("deployed-{i}");
    let what = what(d);
    let env = match d.environment {
        Environment::Prod => copy::PROD,
        Environment::Dev => copy::DEV,
    };
    let open = s.release_open.as_deref() == Some(d.key().as_str());
    let (commit, status): (El, El) = match &d.at {
        Err(e) => (
            col()
                .grow(1.0)
                .min_w(0.0)
                .gap(2.0)
                .child(meta(l, copy::tag(&d.image.tag)))
                .child(failed(l, e)),
            el(),
        ),
        Ok(None) => (
            col().grow(1.0).min_w(0.0).child(meta(l, copy::tag(&d.image.tag))),
            text(copy::NOT_A_COMMIT).font(ty::META).color(l.c.fg_soft),
        ),
        Ok(Some(b)) => (
            col()
                .grow(1.0)
                .min_w(0.0)
                .gap(2.0)
                .child(link(
                    l,
                    &format!("{key}-commit"),
                    b.commit.title().to_owned(),
                    &b.commit.html_url,
                    ty::ROW,
                ))
                .child(meta(l, commit_meta(s, &b.commit))),
            behind_button(l, &key, d, b, open, &what),
        ),
    };
    let top = row()
        .w_full()
        .gap(12.0)
        .center_y()
        .child(
            text(env)
                .font(ty::LABEL)
                .color(if d.environment == Environment::Prod {
                    l.c.fg
                } else {
                    l.c.fg_soft
                })
                .w(40.0),
        )
        .child(
            text(what.clone())
                .font(ty::ROW_STRONG)
                .color(l.c.fg)
                .truncate()
                .w(230.0),
        )
        .child(commit)
        .child(row().w(150.0).justify_end().child(status));
    let mut out = col()
        .key(key.clone())
        .w_full()
        .gap(8.0)
        .py(8.0)
        .border_sides([1.0, 0.0, 0.0, 0.0], l.c.line)
        .child(top);
    if open && let Some(b) = d.behind() {
        out = out.child(behind_list(s, l, &key, b));
    }
    out
}

fn behind_button(l: &Look, key: &str, d: &Deployed, b: &Behind, open: bool, what: &str) -> El {
    if b.ahead_by == 0 {
        return text(copy::UP_TO_DATE).font(ty::META).color(l.c.fg_soft);
    }
    let n = count(b.ahead_by);
    let row_key = d.key();
    Button::new(if open { copy::HIDE.to_owned() } else { copy::behind(&n) })
        .variant(if open {
            ButtonVariant::Ghost
        } else {
            ButtonVariant::Outline
        })
        .control_size(ControlSize::Small)
        .on_press(move |s: &mut Monitor| s.toggle_release(row_key.clone()))
        .build(&l.kit)
        .label(if open {
            copy::hide_behind(what)
        } else {
            copy::show_behind(&n, what)
        })
        .key(format!("{key}-behind"))
}

/// The commits main has that a deployed image doesn't, newest first.
fn behind_list(s: &Monitor, l: &Look, key: &str, b: &Behind) -> El {
    let mut list = col()
        .key(format!("{key}-commits"))
        .w_full()
        .gap(6.0)
        .padding(12.0)
        .fill(l.c.bg_soft)
        .radius(l.radius_lg);
    for (i, c) in b.commits.iter().take(LISTED).enumerate() {
        list = list.child(
            row()
                .w_full()
                .gap(12.0)
                .center_y()
                .child(col().grow(1.0).min_w(0.0).child(link(
                    l,
                    &format!("{key}-commit-{i}"),
                    c.title().to_owned(),
                    &c.html_url,
                    ty::ROW,
                )))
                .child(meta(l, commit_meta(s, c)).max_w(320.0)),
        );
    }
    let rest = b.ahead_by - b.commits.len().min(LISTED) as i64;
    if rest > 0 {
        list = list.child(link(
            l,
            &format!("{key}-more"),
            copy::more_commits(&count(rest)),
            &b.compare_url,
            ty::META,
        ));
    }
    list
}

fn coming(s: &Monitor, r: &Releases, l: &Look) -> El {
    let mut children = vec![text(copy::IMAGE_BUMP).font(ty::LABEL).color(l.c.fg_soft)];
    match &r.bump {
        Err(e) => children.push(failed(l, e)),
        Ok(None) => children.push(parts::note(l, copy::NO_BUMP)),
        Ok(Some(p)) => children.push(pull_row(s, l, "bump", p)),
    }
    children.push(text(copy::ON_MAIN).font(ty::LABEL).color(l.c.fg_soft));
    match &r.main {
        Err(e) => children.push(failed(l, e)),
        Ok(workflows) => {
            for (i, w) in workflows.iter().enumerate() {
                children.push(workflow_row(s, l, &format!("main-{i}"), w));
            }
        }
    }
    parts::card(l, "coming", copy::COMING, children)
}

/// A workflow's latest run: its name, the run's title and when, and how it went (red when main is red).
fn workflow_row(s: &Monitor, l: &Look, key: &str, w: &WorkflowStatus) -> El {
    let state = w.latest.as_ref().map(Run::checks);
    let name = w.name().to_owned();
    let body = match &w.latest {
        Some(run) => col()
            .grow(1.0)
            .min_w(0.0)
            .gap(2.0)
            .child(link(l, &format!("{key}-run"), name, &run.html_url, ty::ROW_STRONG))
            .child(meta(l, run_line(s, run))),
        None => col()
            .grow(1.0)
            .min_w(0.0)
            .gap(2.0)
            .child(text(name).font(ty::ROW_STRONG).color(l.c.fg))
            .child(meta(l, copy::NO_RUNS.to_owned())),
    };
    let mut status = row().gap(10.0).center_y().child(checks(l, state));
    if w.red && state != Some(Checks::Failing) {
        // Going again on top of a failure.
        status = status.child(checks(l, Some(Checks::Failing)));
    }
    row()
        .key(key.to_owned())
        .w_full()
        .gap(12.0)
        .center_y()
        .py(6.0)
        .border_sides([1.0, 0.0, 0.0, 0.0], l.c.line)
        .child(body)
        .child(status)
}

/// A run's title and when it started: "Merge pull request #389 … · 17:44".
fn run_line(s: &Monitor, run: &Run) -> String {
    let title = run
        .display_title
        .clone()
        .unwrap_or_else(|| short(&run.head_sha).to_owned());
    copy::run_meta(&title, &when(run.created_at, s.now_ms, &s.tz))
}

fn pull_row(s: &Monitor, l: &Look, key: &str, p: &PullStatus) -> El {
    let author = p.pull.user.as_ref().map(|u| u.login.as_str()).unwrap_or_default();
    let mut head = row().w_full().gap(8.0).center_y().child(link(
        l,
        &format!("{key}-title"),
        p.pull.title.clone(),
        &p.pull.html_url,
        ty::ROW_STRONG,
    ));
    if p.pull.draft {
        head = head.child(text(copy::DRAFT).font(ty::LABEL).color(l.c.fg_soft));
    }
    row()
        .key(key.to_owned())
        .w_full()
        .gap(12.0)
        .center_y()
        .py(6.0)
        .border_sides([1.0, 0.0, 0.0, 0.0], l.c.line)
        .child(col().grow(1.0).min_w(0.0).gap(2.0).child(head).child(meta(
            l,
            copy::pull_meta(p.pull.number, &age(s, p.pull.created_at), author),
        )))
        .child(checks(l, p.checks))
}

fn uploader(s: &Monitor, r: &Releases, l: &Look) -> El {
    let children = match &r.uploader {
        Err(e) => vec![failed(l, e)],
        Ok(Uploader { tag, run }) => vec![
            parts::pair(
                l,
                copy::LATEST_TAG,
                tag.clone().unwrap_or_else(|| copy::NO_UPLOADER_TAG.to_owned()),
            ),
            row()
                .key("uploader-run")
                .w_full()
                .gap(12.0)
                .center_y()
                .child(text(copy::LAST_PUBLISH).font(ty::ROW).color(l.c.fg_soft).w(110.0))
                .child(match run {
                    Some(run) => col().grow(1.0).min_w(0.0).child(link(
                        l,
                        "uploader-run-link",
                        run_line(s, run),
                        &run.html_url,
                        ty::ROW,
                    )),
                    None => col().grow(1.0).child(meta(l, copy::NO_RUNS.to_owned())),
                })
                .child(match run {
                    Some(run) => checks(l, Some(run.checks())),
                    None => spacer(),
                }),
        ],
    };
    parts::card(l, "uploader", copy::UPLOADER, children)
}

fn pulls(s: &Monitor, r: &Releases, l: &Look) -> El {
    let mut children = Vec::new();
    match &r.pulls {
        Err(e) => children.push(failed(l, e)),
        Ok(pulls) if pulls.is_empty() => children.push(parts::note(l, copy::NO_PULLS)),
        Ok(pulls) => {
            for (i, p) in pulls.iter().enumerate() {
                children.push(pull_row(s, l, &format!("pull-{i}"), p));
            }
        }
    }
    parts::card(l, "pulls", copy::OPEN_PULLS, children)
}
