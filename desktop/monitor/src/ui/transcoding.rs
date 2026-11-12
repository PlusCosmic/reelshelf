//! Transcoding: the queue by job type, the overflow pools, the AAC backfill and the last failures (each clip id
//! copyable).

use rune::El;

use reelshelf_api::{OpsQueueCounts, OpsTranscoding};
use reelshelf_app::theme::Look;

use super::parts::{self, Cell, Column, Tone, column};
use crate::state::Monitor;
use crate::units::{count, when};
use crate::{alerts, copy};

pub fn page(s: &Monitor, l: &Look) -> El {
    let Some(v) = &s.transcoding.value else {
        return super::loading(s, l);
    };
    let warnings = alerts::transcoding(v);
    let mut queue = vec![queue_table(l, "queue-table", copy::JOB_TYPE, &v.types)];
    queue.extend(warnings.into_iter().map(|w| parts::alert(l, w)));
    parts::page(
        "transcoding",
        vec![
            parts::card(l, "queue", copy::QUEUE, queue),
            overflow(v, l),
            backfill(v, l),
            failures(s, v, l),
        ],
    )
}

const QUEUE_COLUMNS: usize = 9;

fn queue_columns(first: &'static str) -> [Column; QUEUE_COLUMNS] {
    [
        column(first, 0.0),
        column(copy::PENDING, 64.0),
        column(copy::RETRYING, 64.0),
        column(copy::RUNNING, 64.0),
        column(copy::STALE, 72.0),
        column(copy::OLDEST, 92.0),
        column(copy::SUCCEEDED, 64.0),
        column(copy::FAILED, 64.0),
        column(copy::LAST_DAY, 132.0),
    ]
}

/// A queue's counts by type or trigger: waiting, running, gone quiet, the oldest wait, and the last hour's and day's
/// finished runs. Anything that needs attention is red.
pub fn queue_table(l: &Look, key: &str, first: &'static str, counts: &[OpsQueueCounts]) -> El {
    if counts.is_empty() {
        return parts::note(l, copy::NO_JOBS);
    }
    let rows = counts
        .iter()
        .map(|c| {
            let old = c.oldest_pending_age_seconds > alerts::OLDEST_PENDING;
            vec![
                Cell::new(c.name.clone()).tone(Tone::Strong),
                Cell::count(c.pending, false),
                Cell::count(c.retrying, false),
                Cell::count(c.running, false),
                Cell::count(c.stale_running, true),
                if c.oldest_pending_age_seconds > 0.0 {
                    Cell::new(copy::age(c.oldest_pending_age_seconds)).tone(if old {
                        Tone::Danger
                    } else {
                        Tone::Plain
                    })
                } else {
                    Cell::new(copy::NOT_FINISHED).tone(Tone::Soft)
                },
                Cell::count(c.last_hour.succeeded, false),
                Cell::count(c.last_hour.failed, true),
                Cell::new(format_day(c)).tone(Tone::Soft),
            ]
        })
        .collect();
    parts::table(l, key, &queue_columns(first), rows)
}

/// The day's runs: done, then failed.
fn format_day(c: &OpsQueueCounts) -> String {
    copy::done_failed(&count(c.last_day.succeeded), &count(c.last_day.failed))
}

fn overflow(v: &OpsTranscoding, l: &Look) -> El {
    let o = &v.overflow;
    if !o.enabled {
        return parts::card(l, "overflow", copy::OVERFLOW, vec![parts::note(l, copy::OVERFLOW_OFF)]);
    }
    let mut rows: Vec<Vec<Cell>> = o
        .pools
        .iter()
        .map(|p| {
            vec![
                Cell::new(p.name.clone()).tone(Tone::Strong),
                match p.running {
                    Some(n) => Cell::new(copy::tasks_of(&count(n), p.max_tasks)),
                    None => Cell::new(copy::NOT_COUNTED).tone(Tone::Soft),
                },
            ]
        })
        .collect();
    rows.push(vec![
        Cell::new(copy::ALL_POOLS).tone(Tone::Soft),
        match o.running {
            Some(n) => Cell::new(copy::tasks_of(&count(n), o.max_tasks)).tone(Tone::Strong),
            None => Cell::new(copy::NOT_COUNTED).tone(Tone::Soft),
        },
    ]);
    parts::card(
        l,
        "overflow",
        copy::OVERFLOW,
        vec![parts::table(
            l,
            "overflow-table",
            &[column(copy::POOL, 0.0), column(copy::TASKS, 140.0)],
            rows,
        )],
    )
}

fn backfill(v: &OpsTranscoding, l: &Look) -> El {
    let b = &v.aac_backfill;
    parts::card(
        l,
        "aac-backfill",
        copy::AAC_BACKFILL,
        vec![parts::stats(vec![
            parts::stat(l, copy::REMAINING, count(b.remaining), false),
            parts::stat(
                l,
                copy::STATUS,
                (if b.enabled { copy::ON } else { copy::OFF }).to_owned(),
                false,
            ),
        ])],
    )
}

fn failures(s: &Monitor, v: &OpsTranscoding, l: &Look) -> El {
    let mut children = Vec::new();
    if v.recent_failures.is_empty() {
        children.push(parts::note(l, copy::NO_FAILURES));
    }
    for (i, f) in v.recent_failures.iter().enumerate() {
        let finished = f
            .finished_at
            .map(|t| when(t, s.now_ms, &s.tz))
            .unwrap_or_else(|| copy::NOT_FINISHED.to_owned());
        children.push(parts::failure(
            s,
            l,
            &format!("failure-{i}"),
            vec![f.job_type.clone(), copy::attempts(f.attempts), finished],
            f.clip_id,
            f.error.as_deref(),
        ));
    }
    parts::card(l, "failures", copy::LAST_FAILURES, children)
}
