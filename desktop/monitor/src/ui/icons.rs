//! The sidebar's icons, 16-point line drawings in `currentColor`.

use rune::Icon;

const fn icon(name: &'static str, body: &'static str) -> Icon {
    Icon { name, view: 16.0, body }
}

pub static OVERVIEW: Icon = icon(
    "monitor-overview",
    r#"<rect x="2.5" y="2.5" width="4.5" height="4.5" rx="1" stroke="currentColor" stroke-width="1.2" fill="none"/><rect x="9" y="2.5" width="4.5" height="4.5" rx="1" stroke="currentColor" stroke-width="1.2" fill="none"/><rect x="2.5" y="9" width="4.5" height="4.5" rx="1" stroke="currentColor" stroke-width="1.2" fill="none"/><rect x="9" y="9" width="4.5" height="4.5" rx="1" stroke="currentColor" stroke-width="1.2" fill="none"/>"#,
);
pub static DASHBOARDS: Icon = icon(
    "monitor-dashboards",
    r#"<path d="M2.5 12.5L6 8.5L8.5 10.5L13.5 4.5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/><path d="M2.5 2.5V13.5H13.5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" fill="none"/>"#,
);
pub static STORAGE: Icon = icon(
    "monitor-storage",
    r#"<ellipse cx="8" cy="4" rx="5" ry="1.8" stroke="currentColor" stroke-width="1.2" fill="none"/><path d="M3 4V12C3 13 5.2 13.8 8 13.8C10.8 13.8 13 13 13 12V4M3 8C3 9 5.2 9.8 8 9.8C10.8 9.8 13 9 13 8" stroke="currentColor" stroke-width="1.2" fill="none"/>"#,
);
pub static TRANSCODING: Icon = icon(
    "monitor-transcoding",
    r#"<rect x="2.5" y="3.5" width="11" height="9" rx="1.5" stroke="currentColor" stroke-width="1.2" fill="none"/><path d="M6.5 6L10 8L6.5 10Z" fill="currentColor"/>"#,
);
pub static AI: Icon = icon(
    "monitor-ai",
    r#"<path d="M8 2.5L9.3 6.7L13.5 8L9.3 9.3L8 13.5L6.7 9.3L2.5 8L6.7 6.7Z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round" fill="none"/>"#,
);
pub static COSTS: Icon = icon(
    "monitor-costs",
    r#"<circle cx="8" cy="8" r="5.5" stroke="currentColor" stroke-width="1.2" fill="none"/><path d="M9.8 6.2C9.5 5.5 8.8 5.1 8 5.1C7 5.1 6.2 5.7 6.2 6.5C6.2 8.3 9.8 7.5 9.8 9.5C9.8 10.3 9 10.9 8 10.9C7.1 10.9 6.4 10.5 6.1 9.8M8 4V5.1M8 10.9V12" stroke="currentColor" stroke-width="1.1" stroke-linecap="round" fill="none"/>"#,
);
pub static RELEASES: Icon = icon(
    "monitor-releases",
    r#"<path d="M8 2.5V10M5 7L8 10L11 7" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/><path d="M3 11.5V13.5H13V11.5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>"#,
);
pub static ADMIN: Icon = icon(
    "monitor-admin",
    r#"<path d="M8 2L13 4V8C13 11 10.8 13.2 8 14C5.2 13.2 3 11 3 8V4Z" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round" fill="none"/>"#,
);
pub static SETTINGS: Icon = icon(
    "monitor-settings",
    r#"<circle cx="8" cy="8" r="2.2" stroke="currentColor" stroke-width="1.2" fill="none"/><path d="M8 1.8V3.6M8 12.4V14.2M1.8 8H3.6M12.4 8H14.2M3.6 3.6L4.9 4.9M11.1 11.1L12.4 12.4M3.6 12.4L4.9 11.1M11.1 4.9L12.4 3.6" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" fill="none"/>"#,
);
