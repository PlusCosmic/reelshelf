//! Every word the monitoring app shows, in one place (as the uploader's `copy.rs`). Grafana's and Reelshelf's own words
//! (dashboard and panel titles, series names, usernames, queue and model names, error messages) come from them and
//! pass through as they are.

use crate::grafana::TimeRange;

pub const APP_NAME: &str = "Reelshelf Monitor";

/// The sidebar's sections, top to bottom.
pub const OVERVIEW: &str = "Overview";
pub const DASHBOARDS: &str = "Dashboards";
pub const STORAGE: &str = "Storage";
pub const TRANSCODING: &str = "Transcoding";
pub const AI_PIPELINES: &str = "AI pipelines";
pub const COSTS: &str = "Costs";
pub const RELEASES: &str = "Releases";
pub const ADMIN: &str = "Admin";
pub const SETTINGS: &str = "Settings";
pub const SIDEBAR_LABEL: &str = "Sections";

/// A section that isn't built yet.
pub const COMING_SOON_TITLE: &str = "Not built yet";
pub const COMING_SOON: &str = "This section comes later.";

/// Dashboards.
pub const ALL_DASHBOARDS: &str = "All dashboards";
pub const NO_DASHBOARDS: &str = "Grafana has no dashboards this token can see.";
pub const LOADING: &str = "Loading…";
pub const REFRESH: &str = "Refresh";
pub const OPEN_IN_GRAFANA: &str = "Open in Grafana";
pub const TIME_RANGE: &str = "Time range";
pub const NO_DATA: &str = "No data";
pub const GENERAL_FOLDER: &str = "General";

/// Grafana isn't set up.
pub const NOT_CONNECTED_TITLE: &str = "Grafana isn't connected";
pub const NOT_CONNECTED: &str = "Add Grafana's address and a Viewer service account token in Settings.";
pub const OPEN_SETTINGS: &str = "Open Settings";

/// Signing in (the ops sections and Settings).
pub const SIGN_IN_TITLE: &str = "Sign in to Reelshelf";
pub const SIGN_IN_INTRO: &str = "The monitor reads Reelshelf's numbers as your account, which must be an Admin. Sign in once and it stays signed in.";
pub const SIGN_IN: &str = "Sign in";
pub const SIGN_IN_AGAIN: &str = "Try again";
pub const CHECKING: &str = "Checking your sign-in…";
pub const STARTING: &str = "Getting a code…";
pub const CODE_LABEL: &str = "Your code";
pub const OPEN_DEVICE_PAGE: &str = "Open the page again";
pub const CANCEL: &str = "Cancel";
pub const EXPIRED: &str = "The code expired before it was approved. Try again for a new one.";
pub const DENIED: &str = "Signing in was turned down on the device page.";
pub const UNREACHABLE: &str = "Couldn't reach Reelshelf. Check the connection and try again.";
pub const REVOKED: &str =
    "This Mac was signed out. It was removed in Settings → Devices, or the account can't sign in.";
pub const UPDATE_TITLE: &str = "Update the monitor";
pub const UPDATE: &str = "This version is older than Reelshelf serves. Build or install the latest one.";

/// "Approve BCDF-GHJK on reelshelf.app/device."
pub fn waiting(device_page: &str) -> String {
    format!("Approve this code on {device_page}. Your browser opened it.")
}

/// Why the ops numbers can't show though signed in.
pub const FLAG_OFF_TITLE: &str = "The ops monitor isn't on for this account";
pub const FLAG_OFF: &str = "Turn on the ops-monitor flag for your account in PostHog, or with FeatureFlags__Overrides__ops-monitor=true on the instance.";
pub const NOT_ADMIN_TITLE: &str = "Only Admins can see this";
pub const NOT_ADMIN: &str = "The account this Mac is signed in as isn't an Admin. Sign in as an Admin, or have one change your role at /admin/accounts.";

/// The toolbar.
pub fn updated(time: &str) -> String {
    format!("Updated {time}")
}
pub fn couldnt_refresh(error: &str) -> String {
    format!("Couldn't refresh: {error}")
}

/// Overview.
pub const ALL_CLEAR: &str = "Nothing needs attention.";
pub const OPEN_SECTION: &str = "Open";
pub const CLIPS: &str = "Clips";
pub const STORED: &str = "Stored";
pub const ACCOUNTS: &str = "Accounts";
pub const WAITING: &str = "Waiting";
pub const RUNNING: &str = "Running";
pub const DONE_LAST_HOUR: &str = "Done in the last hour";
pub const SPEND_24H: &str = "Spend, 24 h";

/// What needs attention.
pub fn failed_last_hour(n: i64, what: &str) -> String {
    format!("{n} {what} failed in the last hour")
}
pub fn stale(n: i64, what: &str) -> String {
    format!("{n} {what} running with no worker")
}
pub fn oldest_waiting(what: &str, age: &str) -> String {
    format!("Oldest {what} has waited {age}")
}
pub const NO_STORE: &str = "No object store is configured";

/// Storage.
pub const TOTALS: &str = "Totals";
pub const IN_THE_BIN: &str = "In the bin";
pub const EXPORTS: &str = "Exports";
pub const EXPORTS_HELD: &str = "Held";
pub const EXPORTS_HELD_BYTES: &str = "Held size";
pub const PENDING: &str = "Pending";
pub const BY_TIER: &str = "By tier";
pub const TIER: &str = "Tier";
pub const TOP_ACCOUNTS: &str = "Top accounts";
pub const ACCOUNT: &str = "Account";
pub const OBJECT_STORE: &str = "Object store";
pub const STORE_KIND: &str = "Kind";
pub const STORE_REGION: &str = "Region";
pub const STORE_ENDPOINT: &str = "Endpoint";
pub const STORE_BUCKET: &str = "Bucket";
pub const STORE_MEDIA: &str = "Media";
pub const NOT_CONFIGURED: &str = "Not configured";

pub fn store_kind(kind: &str) -> &str {
    match kind {
        "s3" => "AWS S3",
        "s3-compatible" => "S3-compatible",
        other => other,
    }
}

pub fn tier(tier: &str) -> &str {
    match tier {
        "free" => "Free",
        "plus" => "Plus",
        "pro" => "Pro",
        "unlimited" => "Unlimited",
        other => other,
    }
}

/// Transcoding.
pub const QUEUE: &str = "Queue";
pub const JOB_TYPE: &str = "Type";
pub const RETRYING: &str = "Retrying";
pub const STALE: &str = "No worker";
pub const OLDEST: &str = "Oldest";
pub const LAST_HOUR: &str = "Last hour";
pub const LAST_DAY: &str = "Last 24 h";
pub const SUCCEEDED: &str = "Done";
pub const FAILED: &str = "Failed";
pub const EXHAUSTED: &str = "Out of tries";
pub const OVERFLOW: &str = "Overflow pools";
pub const OVERFLOW_OFF: &str = "Off on this instance";
pub const POOL: &str = "Pool";
pub const TASKS: &str = "Tasks";
pub const NOT_COUNTED: &str = "Not counted";
pub const AAC_BACKFILL: &str = "AAC backfill";
pub const ON: &str = "On";
pub const OFF: &str = "Off";
pub const REMAINING: &str = "Clips left";
pub const LAST_FAILURES: &str = "Last failures";
pub const NO_FAILURES: &str = "No failures.";
pub const NO_JOBS: &str = "No jobs.";
pub const CLIP: &str = "Clip";
pub const COPY: &str = "Copy";
pub const COPIED: &str = "Copied";
pub const NO_ERROR: &str = "No error recorded";
pub const NOT_FINISHED: &str = "–";

pub fn copy_clip_id(id: &str) -> String {
    format!("Copy clip id {id}")
}
pub fn tasks_of(running: &str, max: i32) -> String {
    format!("{running} of {max}")
}
pub fn attempts(n: i32) -> String {
    if n == 1 {
        "1 try".to_owned()
    } else {
        format!("{n} tries")
    }
}

/// AI pipelines.
pub const TRIGGER: &str = "Trigger";
pub const SPEND: &str = "Spend by model";
pub const MODEL: &str = "Model";
pub const RUNS: &str = "Runs";
pub const TOKENS: &str = "Tokens in / out";
pub const COST: &str = "Cost";
pub const DAY: &str = "24 h";
pub const MONTH: &str = "30 days";
pub const NO_SPEND: &str = "No runs in the last 30 days.";
pub const NO_COST: &str = "–";
pub const UNDER_A_CENT: &str = "<$0.01";

pub fn unpriced(n: i64) -> String {
    format!("+{n} unpriced")
}
pub fn tokens(input: &str, output: &str) -> String {
    format!("{input} / {output}")
}

/// A run queue's name.
pub fn queue(name: &str) -> &str {
    match name {
        "legend_detection" => "Legend detection",
        "clip_transcription" => "Transcription",
        "clip_summary" => "Summaries",
        other => other,
    }
}

/// Settings.
pub const REELSHELF: &str = "Reelshelf";
pub const SIGN_OUT: &str = "Sign out";
pub const SIGNED_OUT: &str = "Not signed in.";
pub fn signed_in_as(username: &str, site: &str) -> String {
    format!("Signed in as {username} on {site}.")
}
pub fn signed_in_on(site: &str) -> String {
    format!("Signed in on {site}.")
}
pub const GRAFANA: &str = "Grafana";
pub const GRAFANA_INTRO: &str =
    "Grafana's address on the tailnet and a Viewer service account token. Both are kept in the Keychain.";
pub const GRAFANA_URL: &str = "Address";
pub const GRAFANA_URL_PLACEHOLDER: &str = "http://grafana:3020";
pub const GRAFANA_TOKEN: &str = "Token";
pub const GRAFANA_TOKEN_PLACEHOLDER: &str = "glsa_…";
pub const GRAFANA_TOKEN_KEPT: &str = "Kept in the Keychain; enter a new one to replace it";
pub const SAVE: &str = "Save";
pub const REMOVE: &str = "Remove";
pub const SAVED: &str = "Saved.";
pub const BAD_URL: &str = "Enter an address starting http:// or https://.";
pub const NO_TOKEN: &str = "Enter the token.";
pub const FROM_ENVIRONMENT: &str = "Using GRAFANA_URL and GRAFANA_TOKEN from the environment.";
pub fn couldnt_keep(error: &str) -> String {
    format!("Couldn't keep it in the Keychain: {error}")
}
pub fn version(version: &str) -> String {
    format!("Version {version}")
}

/// How long something has waited: "45 s", "12 min", "3 h 5 min", "2 d 4 h".
pub fn age(seconds: f64) -> String {
    let s = seconds.max(0.0).round() as i64;
    match s {
        0..60 => format!("{s} s"),
        60..3600 => format!("{} min", s / 60),
        3600..86_400 => format!("{} h {} min", s / 3600, s % 3600 / 60),
        _ => format!("{} d {} h", s / 86_400, s % 86_400 / 3600),
    }
}

/// The picker's choices.
pub fn range(r: TimeRange) -> &'static str {
    match r {
        TimeRange::LastHour => "1h",
        TimeRange::Last6Hours => "6h",
        TimeRange::Last24Hours => "24h",
        TimeRange::Last7Days => "7d",
    }
}

/// The card for a panel the app doesn't draw.
pub fn not_drawn(kind: &str) -> String {
    format!("This {kind} panel isn't drawn here yet.")
}

/// More series than the legend shows.
pub fn more_series(n: usize) -> String {
    format!("+{n} more")
}

/// A panel's query failed.
pub fn failed(error: &str) -> String {
    format!("Couldn't load: {error}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ages_read_at_a_glance() {
        assert_eq!(age(12.4), "12 s");
        assert_eq!(age(754.0), "12 min");
        assert_eq!(age(3.0 * 3600.0 + 300.0), "3 h 5 min");
        assert_eq!(age(2.0 * 86_400.0 + 4.0 * 3600.0), "2 d 4 h");
    }
}
