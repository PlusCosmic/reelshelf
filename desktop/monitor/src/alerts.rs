//! What needs attention, for Overview's cards: a queue's failures in the last hour, its running jobs whose worker
//! went quiet, and a pending job that has waited over [`OLDEST_PENDING`]; storage with no object store, CloudFront
//! answering more than [`SERVER_ERROR_LIMIT`] of requests with a 5xx, a bucket-to-database difference growing faster
//! than [`gap_growing`] allows, or CloudWatch unreadable; a cost provider whose latest read failed; and a workflow
//! whose latest finished run on main failed, or failing checks on the image-bump PR.

use reelshelf_api::{OpsAi, OpsCosts, OpsQueueCounts, OpsStorage, OpsStorageGap, OpsTranscoding};

use crate::copy;
use crate::github::{Checks, Releases};
use crate::units::{self, bytes};

/// CloudFront's 5xx rate over the last 24 hours, in percent of requests, above which it needs attention.
pub const SERVER_ERROR_LIMIT: f64 = 1.0;

/// How much the bucket's difference from the database may grow in a week: this share of what the database accounts
/// for, and at least [`GAP_GROWTH_FLOOR`]. New clips' thumbnails and previews grow it a little on their own.
pub const GAP_GROWTH_SHARE: f64 = 0.005;
pub const GAP_GROWTH_FLOOR: i64 = 1 << 30;

/// Whether the difference grew by more than a week's allowance.
pub fn gap_growing(gap: &OpsStorageGap) -> bool {
    let allowed = (gap.database_bytes as f64 * GAP_GROWTH_SHARE).max(GAP_GROWTH_FLOOR as f64);
    gap.growth_last_week_bytes.is_some_and(|growth| growth as f64 > allowed)
}

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
    if !s.store.configured {
        return vec![copy::NO_STORE.to_owned()];
    }
    let mut out = Vec::new();
    if let Some(bucket) = &s.bucket {
        if matches!(bucket.status.as_str(), "denied" | "failed") {
            out.push(copy::BUCKET_UNREADABLE.to_owned());
        }
        if let Some(gap) = bucket.gap.as_ref().filter(|g| gap_growing(g)) {
            out.push(copy::gap_growing(&bytes(gap.growth_last_week_bytes.unwrap_or(0))));
        }
    }
    if let Some(cdn) = &s.cdn {
        if matches!(cdn.status.as_str(), "denied" | "failed") {
            out.push(copy::CDN_UNREADABLE.to_owned());
        }
        if let Some(rate) = cdn.server_error_rate_last_day.filter(|r| *r > SERVER_ERROR_LIMIT) {
            out.push(copy::server_errors_high(&units::format(rate, Some("percent"), Some(2))));
        }
    }
    out
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

    fn gap(database: i64, growth: Option<i64>) -> OpsStorageGap {
        OpsStorageGap {
            database_bytes: database,
            bucket_bytes: database + (40 << 30),
            bucket_day: chrono::NaiveDate::from_ymd_opt(2024, 11, 11).unwrap(),
            difference_bytes: 40 << 30,
            growth_last_week_bytes: growth,
            days: Vec::new(),
        }
    }

    #[test]
    fn the_gap_grows_too_much_past_half_a_percent_or_a_gib() {
        const TIB: i64 = 1 << 40;
        // Half a percent of 2 TiB is about 10 GiB.
        assert!(!gap_growing(&gap(2 * TIB, Some(10 << 30))));
        assert!(gap_growing(&gap(2 * TIB, Some(11 << 30))));
        // A small bucket still gets a GiB.
        assert!(!gap_growing(&gap(10 << 30, Some(1 << 30))));
        assert!(gap_growing(&gap(10 << 30, Some((1 << 30) + 1))));
        assert!(!gap_growing(&gap(2 * TIB, None)));
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
