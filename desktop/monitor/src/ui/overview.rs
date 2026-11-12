//! Overview: a card per ops section with its headline numbers and, in red, anything that needs attention (failures
//! in the last hour, running jobs whose worker went quiet, a pending job waiting over 10 minutes, no object store, a
//! cost provider that couldn't be read).

use rune::{El, row, spacer, text};
use rune_kit::{Button, ButtonVariant, ControlSize};

use reelshelf_api::OpsQueueCounts;
use reelshelf_app::theme::{Look, ty};

use super::parts;
use crate::state::{Monitor, Section};
use crate::units::{bytes, count, dollars};
use crate::{alerts, copy};

pub fn page(s: &Monitor, l: &Look) -> El {
    parts::page(
        "overview",
        vec![
            section_card(l, Section::Storage, storage(s, l)),
            section_card(l, Section::Transcoding, transcoding(s, l)),
            section_card(l, Section::AiPipelines, ai(s, l)),
            section_card(l, Section::Costs, costs(s, l)),
        ],
    )
}

/// A section's headline numbers and what needs attention, or None until it is read.
type Summary = Option<(Vec<El>, Vec<String>)>;

fn storage(s: &Monitor, l: &Look) -> Summary {
    let v = s.storage.value.as_ref()?;
    let accounts: i64 = v.by_tier.iter().map(|t| t.accounts).sum();
    Some((
        vec![
            parts::stat(l, copy::STORED, bytes(v.total.bytes), false),
            parts::stat(l, copy::CLIPS, count(v.total.clips), false),
            parts::stat(l, copy::ACCOUNTS, count(accounts), false),
        ],
        alerts::storage(v),
    ))
}

/// Waiting (pending and retrying), running and finished in the last hour, over a queue's rows.
fn totals<'a>(counts: impl Iterator<Item = &'a OpsQueueCounts> + Clone) -> (i64, i64, i64, i64) {
    counts.fold((0, 0, 0, 0), |(w, r, d, f), c| {
        (
            w + c.pending + c.retrying,
            r + c.running,
            d + c.last_hour.succeeded,
            f + c.last_hour.failed,
        )
    })
}

fn transcoding(s: &Monitor, l: &Look) -> Summary {
    let v = s.transcoding.value.as_ref()?;
    let (waiting, running, done, failed) = totals(v.types.iter());
    Some((
        vec![
            parts::stat(l, copy::WAITING, count(waiting), false),
            parts::stat(l, copy::RUNNING, count(running), false),
            parts::stat(l, copy::DONE_LAST_HOUR, count(done), false),
            parts::stat(l, copy::FAILED, count(failed), failed > 0),
        ],
        alerts::transcoding(v),
    ))
}

fn ai(s: &Monitor, l: &Look) -> Summary {
    let v = s.ai.value.as_ref()?;
    let (waiting, running, _, failed) = totals(v.queues.iter().flat_map(|q| q.triggers.iter()));
    let spend: f64 = v.spend.iter().filter_map(|m| m.last_day.cost_usd).sum();
    Some((
        vec![
            parts::stat(l, copy::WAITING, count(waiting), false),
            parts::stat(l, copy::RUNNING, count(running), false),
            parts::stat(l, copy::FAILED, count(failed), failed > 0),
            parts::stat(
                l,
                copy::SPEND_24H,
                dollars(spend).unwrap_or_else(|| copy::UNDER_A_CENT.to_owned()),
                false,
            ),
        ],
        alerts::ai(v),
    ))
}

fn costs(s: &Monitor, l: &Look) -> Summary {
    let v = s.costs.value.as_ref()?;
    let mut stats = vec![parts::stat(
        l,
        copy::SPEND_THIS_MONTH,
        copy::money(v.month_to_date_usd, "USD"),
        false,
    )];
    if let Some((net, currency)) = super::costs::net_revenue(v) {
        stats.push(parts::stat(
            l,
            copy::REVENUE_THIS_MONTH,
            copy::money(net, currency),
            false,
        ));
    }
    Some((stats, alerts::costs(v)))
}

fn section_card(l: &Look, section: Section, summary: Summary) -> El {
    let title = super::label(section);
    let key = format!("card-{section:?}");
    let attention = summary.as_ref().is_some_and(|(_, a)| !a.is_empty());
    let head = row()
        .w_full()
        .gap(10.0)
        .center_y()
        .child(
            rune::el()
                .size(9.0, 9.0)
                .radius(4.5)
                .fill(match &summary {
                    None => l.c.line,
                    Some(_) if attention => l.c.danger,
                    Some(_) => l.c.accent,
                })
                .a11y_hidden(),
        )
        .child(text(title).font(ty::ROW_TITLE).color(l.c.fg))
        .child(spacer())
        .child(
            Button::new(copy::OPEN_SECTION)
                .variant(ButtonVariant::Ghost)
                .control_size(ControlSize::Small)
                .on_press(move |s: &mut Monitor| s.select(section))
                .build(&l.kit)
                .label(copy::open_section(title))
                .key(format!("{key}-open")),
        );
    let mut card = rune::col()
        .key(key)
        .w_full()
        .padding(16.0)
        .gap(14.0)
        .fill(l.c.surface)
        .border(1.0, if attention { l.c.danger } else { l.c.line })
        .radius(l.radius_lg)
        .child(head);
    match summary {
        None => card = card.child(parts::note(l, copy::LOADING)),
        Some((stats, attention)) => {
            card = card.child(parts::stats(stats));
            if attention.is_empty() {
                card = card.child(parts::note(l, copy::ALL_CLEAR));
            }
            for line in attention {
                card = card.child(parts::alert(l, line));
            }
        }
    }
    card
}
