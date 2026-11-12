//! AI pipelines: each run queue's counts by trigger and its last failures, and spend by model over 24 hours and 30
//! days.

use rune::El;

use reelshelf_api::{OpsAi, OpsAiQueue, OpsAiUsage};
use reelshelf_app::theme::Look;

use super::parts::{self, Cell, Tone, column};
use super::transcoding::queue_table;
use crate::state::Monitor;
use crate::units::{dollars, when};
use crate::{alerts, copy};

/// Failures shown per queue; the API sends up to 50.
const FAILURES_SHOWN: usize = 5;

pub fn page(s: &Monitor, l: &Look) -> El {
    let Some(v) = &s.ai.value else {
        return super::loading(s, l);
    };
    let mut cards: Vec<El> = v.queues.iter().map(|q| queue(s, q, l)).collect();
    cards.push(spend(v, l));
    parts::page("ai", cards)
}

fn queue(s: &Monitor, q: &OpsAiQueue, l: &Look) -> El {
    let key = format!("queue-{}", q.queue);
    let mut children = vec![queue_table(l, &format!("{key}-table"), copy::TRIGGER, &q.triggers)];
    let name = copy::queue(&q.queue).to_lowercase();
    children.extend(
        q.triggers
            .iter()
            .flat_map(|c| alerts::queue(c, &format!("{name} {}", c.name)))
            .map(|w| parts::alert(l, w)),
    );
    for (i, f) in q.recent_failures.iter().take(FAILURES_SHOWN).enumerate() {
        let finished = f
            .completed_at
            .map(|t| when(t, s.now_ms, &s.tz))
            .unwrap_or_else(|| copy::NOT_FINISHED.to_owned());
        children.push(parts::failure(
            s,
            l,
            &format!("{key}-failure-{i}"),
            vec![f.trigger.clone(), f.model.clone(), copy::attempts(f.attempts), finished],
            f.clip_id,
            f.error.as_deref(),
        ));
    }
    parts::card(l, &key, copy::queue(&q.queue), children)
}

/// A cost in dollars: "–" when none is known, "<$0.01" for less than a cent.
fn cost(u: &OpsAiUsage) -> Cell {
    match u.cost_usd {
        None => Cell::new(copy::NO_COST).tone(Tone::Soft),
        Some(v) => Cell::new(dollars(v).unwrap_or_else(|| copy::UNDER_A_CENT.to_owned())),
    }
}

fn spend(v: &OpsAi, l: &Look) -> El {
    if v.spend.is_empty() {
        return parts::card(l, "spend", copy::SPEND, vec![parts::note(l, copy::NO_SPEND)]);
    }
    let rows = v
        .spend
        .iter()
        .map(|m| {
            vec![
                Cell::new(m.model.clone()).tone(Tone::Strong),
                Cell::new(copy::queue(&m.queue)).tone(Tone::Soft),
                Cell::count(m.last_day.runs, false),
                cost(&m.last_day),
                Cell::count(m.last_month.runs, false),
                Cell::new(copy::tokens(
                    &crate::units::format(m.last_month.input_tokens as f64, None, None),
                    &crate::units::format(m.last_month.output_tokens as f64, None, None),
                ))
                .tone(Tone::Soft),
                cost(&m.last_month),
            ]
        })
        .collect();
    let mut children = vec![parts::table(
        l,
        "spend-table",
        &[
            column(copy::MODEL, 0.0),
            column(copy::QUEUE, 120.0),
            column(copy::RUNS_DAY, 72.0),
            column(copy::COST_DAY, 72.0),
            column(copy::RUNS_MONTH, 80.0),
            column(copy::TOKENS_MONTH, 120.0),
            column(copy::COST_MONTH, 80.0),
        ],
        rows,
    )];
    let unpriced: i64 = v.spend.iter().map(|m| m.last_month.unpriced_runs).sum();
    if unpriced > 0 {
        children.push(parts::note(l, copy::unpriced(&crate::units::count(unpriced))));
    }
    parts::card(l, "spend", copy::SPEND, children)
}
