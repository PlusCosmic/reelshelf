//! Every word the monitoring app shows, in one place (as the uploader's `copy.rs`). Grafana's own words (dashboard
//! and panel titles, series names, its error messages) come from Grafana and pass through as they are.

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
pub const SIDEBAR_LABEL: &str = "Sections";

/// A section that isn't built yet.
pub const COMING_SOON_TITLE: &str = "Not built yet";
pub const COMING_SOON: &str = "This section comes later. Dashboards works now.";

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
pub const NOT_CONNECTED: &str =
    "Start the app with GRAFANA_URL and GRAFANA_TOKEN set (a Viewer service account token).";

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
