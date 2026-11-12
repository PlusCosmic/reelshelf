//! Every word the monitoring app shows, in one place (as the uploader's `copy.rs`). Grafana's, Reelshelf's and GitHub's
//! own words (dashboard and panel titles, series names, usernames, queue and model names, commit and pull request
//! titles, workflow names, tags, error messages) come from them and pass through as they are.

use crate::admin::Change;
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
pub fn open_section(section: &str) -> String {
    format!("Open {section}")
}
pub const CLIPS: &str = "Clips";
pub const STORED: &str = "Stored";
pub const ACCOUNTS: &str = "Accounts";
pub const WAITING: &str = "Waiting";
pub const RUNNING: &str = "Running";
pub const DONE_LAST_HOUR: &str = "Done in the last hour";
pub const SPEND_24H: &str = "Spend, 24 h";
pub const PROD_BEHIND: &str = "Prod behind main";
pub const OPEN_PRS: &str = "Open PRs";
pub const MAIN_CHECKS: &str = "Main";

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
pub fn red_on_main(workflow: &str) -> String {
    format!("{workflow} failed on main")
}
pub fn bump_failing(title: &str) -> String {
    format!("Checks failing on {title}")
}

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
pub const LAST_DAY: &str = "24 h done / failed";
pub const ALL_POOLS: &str = "All pools";
pub const STATUS: &str = "Status";
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
pub const RUNS_DAY: &str = "Runs 24 h";
pub const COST_DAY: &str = "Cost 24 h";
pub const RUNS_MONTH: &str = "Runs 30 d";
pub const TOKENS_MONTH: &str = "Tokens in / out";
pub const COST_MONTH: &str = "Cost 30 d";
pub const NO_SPEND: &str = "No runs in the last 30 days.";
pub const NO_COST: &str = "–";
pub const UNDER_A_CENT: &str = "<$0.01";

pub fn unpriced(n: &str) -> String {
    format!("{n} runs in the last 30 days have no known cost and are left out of it.")
}
pub fn done_failed(done: &str, failed: &str) -> String {
    format!("{done} / {failed}")
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

/// Costs.
pub const THIS_MONTH_TITLE: &str = "This month";
pub const SPEND_THIS_MONTH: &str = "Spend this month";
pub const REVENUE_THIS_MONTH: &str = "Net revenue this month";
pub const SPEND_TOTAL_NOTE: &str = "Every provider that reports in US dollars, added up; revenue isn't taken off. The API reads each one hourly, AWS every 6 hours.";
pub const PROVIDER: &str = "Provider";
pub const THIS_MONTH: &str = "This month";
pub const LAST_MONTH: &str = "Last month";
pub const CREDIT_LEFT: &str = "Credit left";
pub const GROSS_THIS_MONTH: &str = "Gross this month";
pub const NET_THIS_MONTH: &str = "Net this month";
pub const GROSS_LAST_MONTH: &str = "Gross last month";
pub const NET_LAST_MONTH: &str = "Net last month";
pub const LAST_30_DAYS: &str = "Last 30 days";
pub const NOT_KNOWN: &str = "–";
pub const COST_OK: &str = "Read";
pub const COST_PENDING: &str = "Not read yet";
pub const COST_FAILED: &str = "Failed";
pub const COST_NOT_SET_UP: &str = "Not set up";
pub const PENDING_NOTE: &str = "The API hasn't read this yet; it does within a few minutes of starting.";
pub const NO_DAILY: &str = "No daily figures from this provider.";
pub const NOT_SET_UP_TITLE: &str = "Not set up";
pub const NOT_SET_UP_INTRO: &str =
    "These have no credentials on the API. They go in its configuration, never in this app:";
pub const OPENROUTER_HISTORY: &str = "OpenRouter keeps 30 days of activity, so last month isn't known. Without a management key only this month's total and today's spend are.";
pub const REVENUE_NOTE: &str =
    "Revenue, not a cost: gross is what customers paid; net is after Stripe's fees, refunds and disputes.";

/// A provider's name.
pub fn provider(name: &str) -> &str {
    match name {
        "aws" => "AWS",
        "openrouter" => "OpenRouter",
        "openai" => "OpenAI",
        "anthropic" => "Anthropic",
        "stripe" => "Stripe",
        other => other,
    }
}

/// What a provider's breakdown is by.
pub fn breakdown(provider: &str) -> &'static str {
    match provider {
        "aws" => "Last 30 days by service",
        "openai" => "Last 30 days by line item",
        _ => "Last 30 days by model",
    }
}

/// Where a provider's credentials go on the API.
pub fn cost_setting(provider: &str) -> &'static str {
    match provider {
        "aws" => "AWS: Ops:Costs:Aws:AccessKeyId and SecretAccessKey, a key allowed only ce:GetCostAndUsage.",
        "openrouter" => "OpenRouter: Ops:Costs:OpenRouter:ManagementKey, or any OpenRouter API key.",
        "openai" => "OpenAI: Ops:Costs:OpenAi:AdminKey, an organization admin key.",
        "anthropic" => "Anthropic: Ops:Costs:Anthropic:AdminKey, an Admin API key.",
        "stripe" => "Stripe: Billing:SecretKey, with Read on balance transactions.",
        _ => "Its key under Ops:Costs.",
    }
}

pub fn couldnt_read(error: &str) -> String {
    format!("Couldn't read it: {error}")
}
pub fn showing_from(time: &str) -> String {
    format!("Showing what was read {time}.")
}
pub fn last_read(time: &str) -> String {
    format!("Read {time}")
}
pub fn costs_unreadable(provider: &str) -> String {
    format!("{provider} couldn't be read")
}

/// An amount in its currency: `$1,234.50`, `£12.00`, `-$3.10`, `12.00 (CHF)`.
pub fn money(amount: f64, currency: &str) -> String {
    let digits = crate::units::two_places(amount.abs());
    let negative = amount < -0.004;
    match (currency.to_uppercase().as_str(), negative) {
        ("USD", false) => format!("${digits}"),
        ("USD", true) => format!("-${digits}"),
        ("GBP", false) => format!("£{digits}"),
        ("GBP", true) => format!("-£{digits}"),
        ("EUR", false) => format!("€{digits}"),
        ("EUR", true) => format!("-€{digits}"),
        (code, false) => format!("{digits} ({code})"),
        (code, true) => format!("-{digits} ({code})"),
    }
}

/// Releases.
pub const GITHUB_NOT_CONNECTED_TITLE: &str = "GitHub isn't connected";
pub const GITHUB_NOT_CONNECTED: &str = "Add a fine-grained, read-only GitHub token in Settings: Contents, Pull requests and Actions, read, on PlusCosmic/reelshelf and PlusCosmic/infrastructure.";
pub const DEPLOYED: &str = "Deployed";
pub const DEPLOYED_INTRO: &str =
    "The image tags in infrastructure's compose files, and the commits main has that each doesn't.";
pub const PROD: &str = "Prod";
pub const DEV: &str = "Dev";
pub const UP_TO_DATE: &str = "Up to date";
pub const NOT_A_COMMIT: &str = "Not built from a commit";
pub const HIDE: &str = "Hide";
pub const COMING: &str = "Coming";
pub const IMAGE_BUMP: &str = "Image bump";
pub const NO_BUMP: &str = "No image bump is waiting in infrastructure.";
pub const ON_MAIN: &str = "On main";
pub const NO_RUNS: &str = "No runs yet";
pub const UPLOADER: &str = "Uploader";
pub const LATEST_TAG: &str = "Latest tag";
pub const NO_UPLOADER_TAG: &str = "No uploader tag yet";
pub const LAST_PUBLISH: &str = "Last publish";
pub const OPEN_PULLS: &str = "Open pull requests";
pub const NO_PULLS: &str = "No open pull requests.";
pub const DRAFT: &str = "Draft";
pub const PASSING: &str = "Passing";
pub const FAILING: &str = "Failing";
pub const CANCELLED: &str = "Cancelled";
pub const NO_CHECKS: &str = "No checks";
pub const CHECKS_UNKNOWN: &str = "Checks unknown";

/// A deployed image's name.
pub fn image(name: &str) -> &str {
    match name {
        "reelshelf" => "App",
        "reelshelf-migrations" => "Migrations",
        "reelshelf-worker" => "Worker",
        "reelshelf-dev-base" => "Dev runner",
        other => other,
    }
}
/// The worker image handed to the overflow pools ("ec2, ecs, batch").
pub fn overflow_workers(pools: &str) -> String {
    format!("Overflow workers ({pools})")
}
pub fn tag(tag: &str) -> String {
    format!("Tag {tag}")
}
pub fn behind(n: &str) -> String {
    format!("{n} behind")
}
pub fn show_behind(n: &str, what: &str) -> String {
    format!("Show the {n} commits on main not in {what}")
}
pub fn hide_behind(what: &str) -> String {
    format!("Hide the commits not in {what}")
}
pub fn more_commits(n: &str) -> String {
    format!("{n} more on GitHub")
}
/// "3f8936c · PlusCosmic · 18:53".
pub fn commit_meta(sha: &str, author: &str, when: &str) -> String {
    format!("{sha} · {author} · {when}")
}
pub fn pull_meta(number: u64, age: &str, author: &str) -> String {
    format!("#{number} · opened {age} ago by {author}")
}
pub fn run_meta(title: &str, when: &str) -> String {
    format!("{title} · {when}")
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
pub const TOKEN_KEPT: &str = "Kept in the Keychain";
pub const SAVE: &str = "Save";
pub const REMOVE: &str = "Remove";
pub const SAVED: &str = "Saved.";
pub const BAD_URL: &str = "Enter an address starting http:// or https://.";
pub const NO_TOKEN: &str = "Enter the token.";
pub const FROM_ENVIRONMENT: &str = "Using GRAFANA_URL and GRAFANA_TOKEN from the environment.";
pub const GITHUB: &str = "GitHub";
pub const GITHUB_INTRO: &str = "A fine-grained, read-only token for Releases: Contents, Pull requests and Actions, read, on PlusCosmic/reelshelf and PlusCosmic/infrastructure. It is kept in the Keychain.";
pub const GITHUB_TOKEN_PLACEHOLDER: &str = "github_pat_…";
pub fn save_token(service: &str) -> String {
    format!("Save the {service} token")
}
pub fn remove_token(service: &str) -> String {
    format!("Remove the {service} token")
}
pub const GITHUB_FROM_ENVIRONMENT: &str = "Using GITHUB_TOKEN from the environment.";
pub fn couldnt_keep(error: &str) -> String {
    format!("Couldn't keep it in the Keychain: {error}")
}
pub fn version(version: &str) -> String {
    format!("Version {version}")
}

/// Admin.
pub const ADMIN_VIEW: &str = "Admin view";
pub const REPORTS: &str = "Reports";
pub const REPORT: &str = "Report";
pub const ACCESS: &str = "Access";
pub const DEAL_WITH_IT: &str = "Deal with it";
pub const SEPARATOR: &str = " · ";
pub fn sign_in_short(provider: &str, username: &str) -> String {
    format!("{provider}: {username}")
}
pub fn owner_and(owner: &str, reported: &str) -> String {
    format!("by {owner} · {reported}")
}

/// Overview's Admin card.
pub const OPEN_REPORTS: &str = "Open reports";
pub const URGENT: &str = "Urgent";
pub const OPEN_ADMIN: &str = "Open Admin";
pub fn urgent_open(n: usize) -> String {
    if n == 1 {
        format!("{n} urgent report open")
    } else {
        format!("{n} urgent reports open")
    }
}

/// Admin → Accounts.
pub const FIND_ACCOUNT: &str = "Find an account";
pub const FIND_ACCOUNT_PLACEHOLDER: &str = "Username, display name or sign-in id";
pub const FIND_ACCOUNT_HINT: &str =
    "Type at least two characters of a username, a display name or an exact Discord, Twitch or Google id.";
pub const SEARCHING: &str = "Searching…";
pub const NO_MATCH: &str = "No account matches.";
pub const PICK_ACCOUNT: &str = "Pick an account to see or change its role, granted plan and suspension.";
pub const SUSPENDED: &str = "Suspended";
pub const ACCOUNT_ID: &str = "Account ID";
pub const SIGN_INS: &str = "Sign-ins";
pub const PLAN_NOW: &str = "Plan now";
pub const ROLE: &str = "Role";
pub const GRANTED_PLAN: &str = "Granted plan";
pub const NO_GRANT: &str = "None";
pub const SUSPENSION: &str = "Suspension";
pub const NOT_SUSPENDED: &str = "Not suspended: it can sign in and its share links work.";
pub const SUSPEND: &str = "Suspend…";
pub const LIFT_SUSPENSION: &str = "Lift suspension…";
pub const CANT_CHANGE_SELF: &str = "You can't change your own role or suspend yourself.";
pub const CANT_SUSPEND_ADMIN: &str = "Admins can't be suspended; change the role first.";
pub const CHANGES: &str = "Changes";
pub const NO_CHANGES: &str = "No changes recorded.";
pub const REASON: &str = "Reason";
pub const SUSPENSION_LIFTED: &str = "Suspension lifted";
pub const NONE: &str = "none";
pub const NOBODY: &str = "–";
pub fn granted(tier: &str) -> String {
    format!("{tier} granted")
}
pub fn on_tier(tier: &str, granted: bool) -> String {
    if granted {
        format!("{tier}, by a grant")
    } else {
        format!("{tier}, paid for or Free")
    }
}
pub fn suspended_since(date: &str) -> String {
    format!("Suspended since {date}: it can't sign in and its share links don't work. Its clips are kept.")
}
pub fn identity(provider: &str, username: &str, id: &str) -> String {
    format!("{provider}: {username} ({id})")
}
pub fn by_when(by: &str, when: &str) -> String {
    format!("{by} · {when}")
}
pub fn changed(field: &str, from: &str, to: &str) -> String {
    format!("{field}: {from} → {to}")
}
pub fn couldnt_load(error: &str) -> String {
    format!("Couldn't load: {error}")
}

pub fn role(role: &str) -> &str {
    match role {
        "viewer" => "Viewer",
        "editor" => "Editor",
        "admin" => "Admin",
        other => other,
    }
}

/// Admin → Reports.
pub const STATUS_FILTER: &str = "Status";
pub const REASON_FILTER: &str = "Reason";
pub const ALL_REASONS: &str = "All reasons";
pub const OPEN: &str = "Open";
pub const TAKEN_DOWN: &str = "Taken down";
pub const DISMISSED: &str = "Dismissed";
pub const REMOVED: &str = "Removed";
pub const NO_OPEN_REPORTS: &str = "No open reports.";
pub const NO_REPORTS: &str = "No reports match.";
pub const PICK_REPORT: &str = "Pick a report to see it and deal with it.";
pub const UNTITLED: &str = "Untitled clip";
pub const OPEN_SHARED_CLIP: &str = "Open the shared clip";
pub const REPORTER_SAID: &str = "What the reporter said";
pub const NOTHING_SAID: &str = "The reporter added nothing.";
pub const OWNER: &str = "Owner";
pub const REPORTER: &str = "Reporter";
pub const SHARING: &str = "Sharing";
pub const TAKE_DOWN: &str = "Take down…";
pub const DISMISS: &str = "Dismiss…";
pub const REMOVE_CLIP: &str = "Remove clip…";
pub const SHARE_REMOVED: &str = "Removed from Reelshelf.";
pub const SHARE_DELETED: &str = "The clip has since been deleted.";
pub const SHARE_BLOCKED: &str = "Taken down: it can't be shared.";
pub const SHARE_HIDDEN: &str = "Its share link is hidden while a child-safety or terrorism report is open.";
pub const SHARE_NO_LINK: &str = "It has no live share link.";
pub const SHARE_LIVE: &str = "Its share link works.";
pub const NOTHING_KEPT: &str = "Nothing was kept: the clip had already been deleted.";
pub const EVIDENCE_PENDING: &str = "Quarantining the clip off public access and keeping a private copy…";
pub const NOT_PURGED: &str = "The media domain wasn't purged, so it may still serve cached copies. Purge the clip's files from the media domain's CDN by hand.";
pub const OWNER_SUSPENDED: &str = "The owner's account is suspended.";
pub const OWNER_NOT_SUSPENDED: &str = "The owner's account isn't suspended (Admins can't be).";
pub fn reported(when: &str) -> String {
    format!("Reported {when}")
}
pub fn by_owner(name: &str) -> String {
    format!("by {name}")
}
pub fn open_on_clip(n: i32) -> String {
    if n == 1 {
        format!("{n} open report on this clip")
    } else {
        format!("{n} open reports on this clip")
    }
}
pub fn child_safety_warning(clip_id: &str) -> String {
    format!(
        "Don't open or download this clip. If the report looks genuine, remove it, then report it to the IWF (UK) or NCMEC's CyberTipline (US) with the clip's id ({clip_id}). Its share link stays hidden until you decide."
    )
}
pub const CLIP_DELETED: &str = "deleted";
pub fn resolved(status: &str, when: &str) -> String {
    format!("{status} {when}")
}
pub fn resolved_by(status: &str, when: &str, name: &str) -> String {
    format!("{status} {when} by {name}")
}
pub fn evidence_failed(error: &str) -> String {
    format!(
        "Needs attention: the private evidence copy still isn't made ({error}). It is still being retried, and nothing is deleted until it works. The video is quarantined, off public access, until the copy is made."
    )
}
pub const NO_ERROR_RECORDED: &str = "no error was recorded";
pub fn evidence_retrying(error: &str) -> String {
    format!("Retrying after: {error}")
}
pub fn evidence_kept(date: &str) -> String {
    format!("Kept privately as evidence until {date}, and gone from public storage.")
}
pub fn remove_explain(owner: &str) -> String {
    format!(
        "Removing it deletes the clip from Reelshelf, keeps a private copy for a year for the authorities, and suspends {owner}. They aren't emailed."
    )
}
pub const THE_OWNER: &str = "the owner";

/// A report's reason, as the site's report form words it.
pub fn report_reason(reason: &str) -> &str {
    match reason {
        "child_safety" => "Child sexual abuse or exploitation",
        "terrorism" => "Terrorism or violent extremism",
        "sexual" => "Nudity or sexual content",
        "violence" => "Graphic violence or gore",
        "hate_or_harassment" => "Hate or harassment",
        "copyright" => "Copyright",
        "other" => "Something else that breaks the terms",
        other => other,
    }
}

pub fn report_status(status: &str) -> &str {
    match status {
        "open" => OPEN,
        "taken_down" => TAKEN_DOWN,
        "dismissed" => DISMISSED,
        "removed" => REMOVED,
        other => other,
    }
}

/// The confirmation.
pub const NOTE: &str = "Note";
pub const WHY: &str = "Why, for the record";
pub const WHY_OPTIONAL: &str = "Why, for the record (optional)";
pub const NOTE_NEEDED: &str = "Say why, for the record.";
pub const SAVING: &str = "Saving…";
pub const CANT_UNDO: &str = "This can't be undone.";

/// The confirmation's title, what happens, and its button.
pub fn confirm(change: &Change) -> (String, String, &'static str) {
    match change {
        Change::Role { name, to, .. } => (
            format!("Make {name} {}?", role(to)),
            "Their permissions change at once. The change and its reason are recorded.".to_owned(),
            "Change role",
        ),
        Change::GrantedTier { name, to: Some(t), .. } => (
            format!("Grant {} to {name}?", tier(t)),
            format!(
                "They get {}'s limits for as long as the grant lasts, whatever they pay for.",
                tier(t)
            ),
            "Grant",
        ),
        Change::GrantedTier { name, to: None, .. } => (
            format!("Take {name}'s granted plan away?"),
            "They go back to the plan they pay for, or Free.".to_owned(),
            "Take it away",
        ),
        Change::Suspend { name, .. } => (
            format!("Suspend {name}?"),
            "They can't sign in and their share links stop working. Their clips are kept, and the suspension can be lifted later.".to_owned(),
            "Suspend",
        ),
        Change::LiftSuspension { name, .. } => (
            format!("Lift {name}'s suspension?"),
            "They can sign in again and their share links work again.".to_owned(),
            "Lift suspension",
        ),
        Change::TakeDown { clip, .. } => (
            format!("Take down “{clip}”?"),
            "Its share links are revoked and it can't be shared again; the clip's other open reports are resolved too. Its owner is emailed that it broke the terms, never who reported it. The clip stays in their library.".to_owned(),
            "Take down",
        ),
        Change::Dismiss { clip, .. } => (
            format!("Dismiss the report on “{clip}”?"),
            "The clip is left as it is.".to_owned(),
            "Dismiss",
        ),
        Change::Remove { clip, owner, .. } => (
            format!("Remove “{clip}”?"),
            format!(
                "This deletes the clip from Reelshelf for everyone, keeps a private copy for a year for the authorities, and suspends {}. They aren't emailed.",
                owner.as_deref().unwrap_or(THE_OWNER)
            ),
            "Remove clip",
        ),
    }
}

/// What a confirmed change did.
pub fn done(change: &Change) -> String {
    match change {
        Change::Role { name, to, .. } => format!("{name} is now {}.", role(to)),
        Change::GrantedTier { name, to: Some(t), .. } => format!("{name} has {} granted.", tier(t)),
        Change::GrantedTier { name, to: None, .. } => format!("{name}'s granted plan was taken away."),
        Change::Suspend { name, .. } => format!("{name} is suspended."),
        Change::LiftSuspension { name, .. } => format!("{name}'s suspension was lifted."),
        Change::TakeDown { .. } => "Taken down.".to_owned(),
        Change::Dismiss { .. } => "Dismissed.".to_owned(),
        Change::Remove { .. } => "Removed.".to_owned(),
    }
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
    fn money_reads_in_its_currency() {
        assert_eq!(money(1234.5, "USD"), "$1,234.50");
        assert_eq!(money(12.0, "gbp"), "£12.00");
        assert_eq!(money(-3.1, "USD"), "-$3.10");
        assert_eq!(money(-0.001, "USD"), "$0.00");
        assert_eq!(money(9.999, "CHF"), "10.00 (CHF)");
    }

    #[test]
    fn ages_read_at_a_glance() {
        assert_eq!(age(12.4), "12 s");
        assert_eq!(age(754.0), "12 min");
        assert_eq!(age(3.0 * 3600.0 + 300.0), "3 h 5 min");
        assert_eq!(age(2.0 * 86_400.0 + 4.0 * 3600.0), "2 d 4 h");
    }
}
