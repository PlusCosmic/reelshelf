//! The canvas's icons as SVG bodies. Icons draw in one colour, the element's.

use rune::Icon;

pub static FOLDER: Icon = Icon {
    name: "reelshelf-folder",
    view: 16.0,
    body: r#"<path d="M1.5 4.5a1 1 0 0 1 1-1h3.6l1.4 1.5h6a1 1 0 0 1 1 1v6.5a1 1 0 0 1-1 1h-11a1 1 0 0 1-1-1z" fill="none" stroke="currentColor" stroke-width="1.3"/>"#,
};

pub static CHEVRON_DOWN: Icon = Icon {
    name: "reelshelf-chevron-down",
    view: 12.0,
    body: r#"<path d="M2.5 4.5L6 8l3.5-3.5" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>"#,
};

pub static CHEVRON_LEFT: Icon = Icon {
    name: "reelshelf-chevron-left",
    view: 12.0,
    body: r#"<path d="M7.5 2.5L4 6l3.5 3.5" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>"#,
};

/// Recent uploads in the flyout's menu: a clock.
pub static RECENT: Icon = Icon {
    name: "reelshelf-recent",
    view: 16.0,
    body: r#"<circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M8 4.8V8l2.2 1.5" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>"#,
};

pub static CHEVRON_RIGHT: Icon = Icon {
    name: "reelshelf-chevron-right",
    view: 12.0,
    body: r#"<path d="M4.5 2.5L8 6l-3.5 3.5" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>"#,
};

/// A row's tick in S3.
pub static CHECK: Icon = Icon {
    name: "reelshelf-check",
    view: 16.0,
    body: r#"<path d="M3 8.5l3 3 7-7" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>"#,
};

/// "On the shelf" in S4.
pub static CHECK_SMALL: Icon = Icon {
    name: "reelshelf-check-small",
    view: 13.0,
    body: r#"<path d="M2.5 7l2.7 2.7 5.3-6" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>"#,
};

pub static PLUS: Icon = Icon {
    name: "reelshelf-plus",
    view: 13.0,
    body: r#"<path d="M6.5 2v9M2 6.5h9" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>"#,
};

/// S1's notice that something didn't work.
pub static ALERT: Icon = Icon {
    name: "reelshelf-alert",
    view: 18.0,
    body: r#"<circle cx="9" cy="9" r="7.5" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M9 5v4.5" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/><circle cx="9" cy="12.6" r="1" fill="currentColor"/>"#,
};

/// S1's waiting spinner: its track and its arc.
pub static SPINNER_TRACK: Icon = Icon {
    name: "reelshelf-spinner-track",
    view: 16.0,
    body: r#"<circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="2"/>"#,
};

pub static SPINNER_ARC: Icon = Icon {
    name: "reelshelf-spinner-arc",
    view: 16.0,
    body: r#"<path d="M8 2a6 6 0 0 1 6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>"#,
};

/// The flyout's rows.
pub static LIBRARY: Icon = Icon {
    name: "reelshelf-library",
    view: 16.0,
    body: r#"<rect x="2" y="3" width="2.6" height="10" rx="0.6" fill="none" stroke="currentColor" stroke-width="1.2"/><rect x="6.4" y="2" width="2.6" height="11" rx="0.6" fill="none" stroke="currentColor" stroke-width="1.2"/><path d="M10.8 4.2l2.3-0.6 2.4 9-2.3 0.6z" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/>"#,
};

pub static PAUSE: Icon = Icon {
    name: "reelshelf-pause",
    view: 16.0,
    body: r#"<path d="M5.5 3.5v9M10.5 3.5v9" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>"#,
};

pub static SETTINGS: Icon = Icon {
    name: "reelshelf-settings",
    view: 16.0,
    body: r#"<circle cx="8" cy="8" r="2.2" fill="none" stroke="currentColor" stroke-width="1.2"/><path d="M8 1.8v2M8 12.2v2M1.8 8h2M12.2 8h2M3.6 3.6l1.4 1.4M11 11l1.4 1.4M3.6 12.4L5 11M11 5l1.4-1.4" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/>"#,
};

pub static QUIT: Icon = Icon {
    name: "reelshelf-quit",
    view: 16.0,
    body: r#"<path d="M8 2v5.5M4.6 4.2a5 5 0 1 0 6.8 0" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>"#,
};
