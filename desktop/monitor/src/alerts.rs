//! What needs attention, for Overview's cards: a queue's failures in the last hour, its running jobs whose worker
//! went quiet, and a pending job that has waited over [`OLDEST_PENDING`]; storage with no object store; a cost provider
//! whose latest read failed; and a workflow whose latest finished run on main failed, or failing checks on the
//! image-bump PR.

use reelshelf_api::{OpsAi, OpsCosts, OpsQueueCounts, OpsStorage, OpsTranscoding};

use crate::copy;
use crate::github::{Checks, Releases};

/// A pending job older than this needs attention.
pub const OLDEST_PENDING: f64 = 10.0 * 60.0;

/// The lines for one queue's counts, naming it `what`.
pub fn queue(counts: &OpsQueueCounts, what: &str) -> Vec<String> {
    let mut out = Vec::new();
    if counts.last_hour.failed > 0 {
        out.push(copy::failed_last_hour(counts.last_hour.failed, what));
    }
    if counts.stale_running > 0 {
        out.push(copy::stale(counts.stale_running, what));
    }
    if counts.oldest_pending_age_seconds > OLDEST_PENDING {
        out.push(copy::oldest_waiting(
            what,
            &copy::age(counts.oldest_pending_age_seconds),
        ));
    }
    out
}

pub fn storage(s: &OpsStorage) -> Vec<String> {
    if s.store.configured {
        Vec::new()
    } else {
        vec![copy::NO_STORE.to_owned()]
    }
}

pub fn transcoding(t: &OpsTranscoding) -> Vec<String> {
    t.types.iter().flat_map(|c| queue(c, &c.name)).collect()
}

pub fn ai(a: &OpsAi) -> Vec<String> {
    a.queues
        .iter()
        .flat_map(|q| {
            q.triggers
                .iter()
                .flat_map(move |c| queue(c, &format!("{} {}", copy::queue(&q.queue).to_lowercase(), c.name)))
        })
        .collect()
}

/// A provider that is set up but couldn't be read (one that isn't set up needs nothing).
pub fn costs(c: &OpsCosts) -> Vec<String> {
    c.providers
        .iter()
        .filter(|p| p.status == "failed")
        .map(|p| copy::costs_unreadable(copy::provider(&p.provider)))
        .collect()
}

pub fn releases(r: &Releases) -> Vec<String> {
    let mut out: Vec<String> = r.red().iter().map(|w| copy::red_on_main(w.name())).collect();
    if let Ok(Some(bump)) = &r.bump
        && bump.checks == Some(Checks::Failing)
    {
        out.push(copy::bump_failing(&bump.pull.title));
    }
    out
}

#[cfg(test)]
mod tests {
    use reelshelf_api::OpsFinishedCounts;

    use super::*;

    fn counts(failed: i64, stale: i64, oldest: f64) -> OpsQueueCounts {
        OpsQueueCounts {
            name: "transcode".to_owned(),
            pending: 1,
            retrying: 0,
            running: 1,
            stale_running: stale,
            oldest_pending_age_seconds: oldest,
            last_hour: OpsFinishedCounts {
                succeeded: 4,
                failed,
                exhausted: 0,
            },
            last_day: OpsFinishedCounts::default(),
        }
    }

    #[test]
    fn a_healthy_queue_needs_nothing() {
        assert!(queue(&counts(0, 0, 599.0), "transcode").is_empty());
    }

    #[test]
    fn failures_stale_jobs_and_old_waits_are_called_out() {
        assert_eq!(
            queue(&counts(2, 1, 754.0), "transcode"),
            vec![
                "2 transcode failed in the last hour",
                "1 transcode running with no worker",
                "Oldest transcode has waited 12 min",
            ]
        );
    }
}
