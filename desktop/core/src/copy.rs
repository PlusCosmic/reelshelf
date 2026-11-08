//! Every word the uploader shows, worded as in `reelshelf-docs/desktop-uploader/screens.html` and following
//! `frontend/src/copy/README.md`: British English, sentence case, contractions, straight quotes, "·" between facts and
//! "…" for anything still happening or a button that opens another step. Errors say what didn't happen, then what to
//! do. Counts go through [`plural`]. Nothing the app shows is written anywhere else.

use std::time::Duration;

use chrono::{DateTime, Datelike, TimeZone, Timelike};

pub const APP_NAME: &str = "Reelshelf";

/// A count as people read it, with a comma between thousands: "1,000".
pub fn count(value: i64) -> String {
    let digits = value.unsigned_abs().to_string();
    let mut grouped = String::with_capacity(digits.len() + digits.len() / 3);
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            grouped.push(',');
        }
        grouped.push(digit);
    }
    if value < 0 { format!("-{grouped}") } else { grouped }
}

/// "1 clip", "3 clips": the count followed by the word that fits it.
pub fn plural(value: i64, one: &str, many: &str) -> String {
    format!("{} {}", count(value), if value == 1 { one } else { many })
}

pub fn clips(value: i64) -> String {
    plural(value, "clip", "clips")
}

/// "2:14", a clip's length (rounded to the nearest second, halves to even).
pub fn length(seconds: f64) -> String {
    let whole = seconds.round_ties_even() as i64;
    if whole >= 3600 {
        format!("{}:{:02}:{:02}", whole / 3600, whole / 60 % 60, whole % 60)
    } else {
        format!("{}:{:02}", whole / 60, whole % 60)
    }
}

/// "74 MB", a file's size.
pub fn size(bytes: u64) -> String {
    const MB: f64 = 1024.0 * 1024.0;
    let bytes = bytes as f64;
    if bytes >= 1024.0 * MB {
        let tenths = (bytes / (1024.0 * MB) * 10.0).round() / 10.0;
        format!("{tenths:.1} GB")
    } else {
        format!("{} MB", ((bytes / MB).round_ties_even() as i64).max(1))
    }
}

/// "21:47", a time today.
pub fn time<Tz: TimeZone>(local: &DateTime<Tz>) -> String {
    format!("{:02}:{:02}", local.hour(), local.minute())
}

/// "6 Nov 2026".
pub fn date<Tz: TimeZone>(local: &DateTime<Tz>) -> String {
    const MONTHS: [&str; 12] = [
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec",
    ];
    format!("{} {} {}", local.day(), MONTHS[local.month0() as usize], local.year())
}

/// A folder as the screens name it, with Windows' separator: "Marvel Rivals\".
pub fn folder(name: &str) -> String {
    if name.is_empty() {
        String::new()
    } else {
        format!("{name}\\")
    }
}

/// "in 2 minutes", "in 30 seconds".
pub fn in_time(wait: Duration) -> String {
    let seconds = wait.as_secs_f64();
    if seconds < 60.0 {
        format!("in {}", plural((seconds.ceil() as i64).max(1), "second", "seconds"))
    } else {
        format!("in {}", plural((seconds / 60.0).ceil() as i64, "minute", "minutes"))
    }
}

/// S1: signing in.
pub mod sign_in {
    pub const TITLE: &str = "Put your clips on the shelf while you play";
    pub const INTRO: &str =
        "Sign in once and Reelshelf puts new clips from your recordings folder on their books in the background.";

    pub fn waiting(site: &str) -> String {
        format!("Waiting for you to approve this code at {site}…")
    }

    pub const BROWSER_OPENED: &str = "Your browser should have opened it.";
    pub const COPY_CODE: &str = "Copy code";
    pub const OPEN_AGAIN: &str = "Open the page again";
    pub const STARTING: &str = "Getting a code…";
    pub const EXPIRED: &str = "That code expired before it was approved. Get a new code to try again.";
    pub const GET_NEW_CODE: &str = "Get a new code";
    pub const DENIED: &str = "Sign-in was cancelled on reelshelf.app, so this computer isn't signed in.";
    pub const TRY_AGAIN: &str = "Try again";
    pub const UNREACHABLE: &str =
        "Couldn't reach Reelshelf, so this computer isn't signed in. Check your connection and try again.";
}

/// S2: choosing the recordings folder.
pub mod setup {
    pub const TITLE: &str = "Where does your recorder save clips?";
    pub const FOLDER_LABEL: &str = "Recordings folder";
    pub const CHANGE_FOLDER: &str = "Change…";
    pub const CHOOSE_FOLDER: &str = "Choose…";
    pub const FOLDER_PICKER_TITLE: &str = "Choose your recordings folder";
    pub const MATCHED_HEADING: &str = "We matched these folders to your books";
    pub const NO_BOOK: &str = "No book";

    /// Between a folder and its book.
    pub const MATCH_ARROW: &str = "→";

    pub const CHOOSE_BOOK: &str = "Choose…";
    pub const NO_SUBFOLDERS: &str = "Clips saved straight into this folder go to the book below.";
    pub const DEFAULT_BOOK_LABEL: &str = "Clips that don't match a book go to";
    pub const ASK_EACH_TIME: &str = "Ask me each time";
    pub const BASELINE: &str = "Clips already in this folder stay where they are. Only new clips are uploaded.";
    pub const EMPTY_SHELF: &str = "Your shelf is empty. Add a game on reelshelf.app first.";
    pub const ADD_GAME: &str = "Add a game on reelshelf.app";
    pub const ALSO_START_WITH_WINDOWS: &str = "Also start with Windows";
    pub const START_WATCHING: &str = "Start watching";
    pub const BOOKS_FAILED: &str = "Your books didn't load. Check your connection and try again.";
    pub const TRY_AGAIN: &str = "Try again";
}

/// T1–T8 and the tray menu.
pub mod tray {
    use chrono::{DateTime, TimeZone};
    use std::time::Duration;

    use super::{clips, plural};

    pub mod watching {
        use super::clips;

        pub const TITLE: &str = "Watching for clips";

        pub fn detail(folder: &str, shelved_today: i64) -> String {
            format!("{folder} · {} on the shelf today", clips(shelved_today))
        }

        pub const ACTION: &str = "Show recent uploads";
    }

    pub mod uploading {
        use super::clips;

        pub fn title(count: i64) -> String {
            format!("Uploading {}…", clips(count))
        }

        pub fn detail(title: &str, percent: u8) -> String {
            format!("{title} · {percent}%")
        }

        pub const ACTION: &str = "Show recent uploads";
    }

    pub mod game_running {
        use super::clips;

        pub const TITLE: &str = "Waiting for you to finish playing…";

        pub fn detail(waiting: i64) -> String {
            format!("{} will upload when you leave the game", clips(waiting))
        }

        pub const ACTION: &str = "Upload now";
    }

    pub mod paused {
        use super::{DateTime, TimeZone};

        pub fn until<Tz: TimeZone>(local: &DateTime<Tz>) -> String {
            format!("Paused until {}", super::super::time(local))
        }

        pub const UNTIL_TOMORROW: &str = "Paused until tomorrow";
        pub const UNTIL_RESUMED: &str = "Paused";
        pub const DETAIL: &str = "New clips will upload when you resume";
        pub const ACTION: &str = "Resume";
    }

    pub mod needs_book {
        use super::clips;

        pub fn title(folder: &str) -> String {
            format!("{folder} needs a book")
        }

        /// Clips saved straight into the recordings folder, with no default book.
        pub const ROOT_TITLE: &str = "Your recordings folder needs a book";

        pub fn detail(waiting: i64) -> String {
            if waiting == 1 {
                "1 clip is waiting".to_string()
            } else {
                format!("{} are waiting", clips(waiting))
            }
        }

        pub const ACTION: &str = "Choose a book…";
    }

    pub mod library_full {
        use super::{DateTime, TimeZone, clips};

        pub const TITLE: &str = "Your library is full";

        pub fn detail(clip_limit: Option<i64>, waiting: i64) -> String {
            match clip_limit {
                Some(limit) => format!("Your plan keeps {} · {} waiting", clips(limit), clips(waiting)),
                None => format!("{} waiting", clips(waiting)),
            }
        }

        pub const MONTHLY_TITLE: &str = "You've added a lot of clips this month";

        pub fn monthly_detail<Tz: TimeZone>(uploads_from: Option<&DateTime<Tz>>, waiting: i64) -> String {
            match uploads_from {
                Some(from) => format!(
                    "New clips upload from {} · {} waiting",
                    super::super::date(from),
                    clips(waiting)
                ),
                None => format!("{} waiting", clips(waiting)),
            }
        }

        pub const ACTION: &str = "See plans";
    }

    pub mod unreachable {
        use super::{Duration, clips};

        pub const TITLE: &str = "Couldn't reach Reelshelf";

        pub fn detail(retry_in: Duration, waiting: i64) -> String {
            format!(
                "Trying again {} · {} waiting",
                super::super::in_time(retry_in),
                clips(waiting)
            )
        }

        pub const ACTION: &str = "Try now";
    }

    pub mod update_required {
        pub const TITLE: &str = "Update Reelshelf";
        pub const DETAIL: &str = "This version doesn't work with Reelshelf any more";
        pub const ACTION: &str = "Open the Store";
    }

    pub const OPEN_LIBRARY: &str = "Open your library";
    pub const PAUSE_FOR: &str = "Pause for…";
    pub const PAUSE_HOUR: &str = "1 hour";
    pub const PAUSE_TOMORROW: &str = "Until tomorrow";
    pub const PAUSE_UNTIL_RESUMED: &str = "Until I resume";
    pub const SETTINGS: &str = "Settings";
    pub const QUIT: &str = "Quit";

    pub fn quit_while_uploading(uploads: i64) -> String {
        format!(
            "Quit and stop {}? They'll carry on next time.",
            plural(uploads, "upload", "uploads")
        )
    }

    pub const QUIT_CONFIRM: &str = "Quit";
    pub const CANCEL: &str = "Cancel";
}

/// S3: a folder with no book.
pub mod book_picker {
    pub const WINDOW_TITLE: &str = "Choose a book";

    pub fn heading(folder: &str) -> String {
        format!("Clips from {folder} go on")
    }

    pub const ROOT_HEADING: &str = "Clips saved straight into your recordings folder go on";
    pub const ADD_GAME: &str = "Add a game on reelshelf.app";
    pub const USE_BOOK: &str = "Use this book";
}

/// S4: recent uploads.
pub mod recent {
    use chrono::{DateTime, TimeZone};

    pub const WINDOW_TITLE: &str = "Recent uploads";
    pub const WAITING: &str = "Waiting";

    pub fn uploading(percent: u8) -> String {
        format!("Uploading… {percent}%")
    }

    pub const ON_SHELF: &str = "On the shelf";
    pub const DUPLICATE: &str = "Already on the shelf";
    pub const TOO_LONG: &str = "Too long for your plan";
    pub const RETRYING_SOON: &str = "Didn't upload, trying again soon";
    pub const FAILED: &str = "Didn't upload";
    pub const TRY_AGAIN: &str = "Try again";
    pub const NEEDS_BOOK: &str = "Waiting for a book";
    pub const LIBRARY_FULL: &str = "Waiting for room in your library";
    pub const NO_BOOK: &str = "No book";

    pub fn meta<Tz: TimeZone>(
        book: &str,
        length_seconds: Option<f64>,
        size: u64,
        shelved_at: Option<&DateTime<Tz>>,
    ) -> String {
        let mut facts = vec![book.to_string()];
        if let Some(length) = length_seconds {
            facts.push(super::length(length));
        }
        facts.push(super::size(size));
        if let Some(at) = shelved_at {
            facts.push(super::time(at));
        }
        facts.join(" · ")
    }

    pub const EMPTY: &str = "Nothing yet. New clips from your recordings folder show up here.";
    pub const OPEN_FOLDER: &str = "Open the recordings folder";
    pub const OPEN_LIBRARY: &str = "Open your library";
}

/// S5: settings.
pub mod settings {
    use super::plural;

    pub const WINDOW_TITLE: &str = "Reelshelf settings";
    pub const FOLDER_LABEL: &str = "Recordings folder";

    pub fn folder_detail(path: &str, matched: i64) -> String {
        format!("{path} · {} matched", plural(matched, "folder", "folders"))
    }

    pub const CHANGE: &str = "Change…";
    pub const DEFAULT_BOOK_LABEL: &str = "Clips that don't match a book";
    pub const CHANGE_DEFAULT: &str = "Change";
    pub const ASK_EACH_TIME: &str = "Ask me each time";
    pub const FULL_SCREEN_LABEL: &str = "While a game is full screen";
    pub const FULL_SCREEN_WAIT: &str = "Uploads wait until you leave the game";
    pub const FULL_SCREEN_SLOW: &str = "Uploads carry on at the speed limit, or 5 Mbit/s without one";
    pub const WAIT: &str = "Wait";
    pub const UPLOAD_SLOWLY: &str = "Upload slowly";
    pub const SPEED_LABEL: &str = "Upload speed limit";
    pub const SPEED_DETAIL: &str = "Applies to every upload";
    pub const NO_LIMIT: &str = "No limit";

    pub fn mbps(mbps: u32) -> String {
        format!("{mbps} Mbit/s")
    }

    pub const START_WITH_WINDOWS: &str = "Start with Windows";
    pub const START_WITH_WINDOWS_BLOCKED: &str =
        "Windows has turned this off. Turn it on in Task Manager's startup apps.";
    pub const NOTIFY_SHELVED: &str = "Tell me when clips are on the shelf";
    pub const SIGNED_IN: &str = "Signed in";

    pub fn device_name(name: &str) -> String {
        format!("This PC is \"{name}\" on reelshelf.app")
    }

    pub const SIGN_OUT: &str = "Sign out";

    pub fn version(version: &str) -> String {
        format!("Version {version}")
    }

    pub const OPEN_LOG_FOLDER: &str = "Open log folder";
}

/// N1–N5.
pub mod notifications {
    use chrono::{DateTime, TimeZone};

    use super::clips;

    pub fn shelved_title(count: i64) -> String {
        if count == 1 {
            "1 clip is on the shelf".to_string()
        } else {
            format!("{} are on the shelf", clips(count))
        }
    }

    /// "Apex Legends (2), Valorant".
    pub fn shelved_books<'a>(books: impl IntoIterator<Item = (&'a str, i64)>) -> String {
        books
            .into_iter()
            .map(|(book, count)| {
                if count > 1 {
                    format!("{book} ({count})")
                } else {
                    book.to_string()
                }
            })
            .collect::<Vec<_>>()
            .join(", ")
    }

    pub const OPEN_LIBRARY: &str = "Open your library";

    pub fn needs_book_title(folder_name: &str) -> String {
        format!("Which book do {folder_name} clips go on?")
    }

    pub const NEEDS_BOOK_ROOT_TITLE: &str = "Which book do clips in your recordings folder go on?";

    pub fn needs_book_detail(waiting: i64, folder: &str) -> String {
        if waiting == 1 {
            format!("1 clip is waiting in {folder}")
        } else {
            format!("{} are waiting in {folder}", clips(waiting))
        }
    }

    pub fn needs_book_root_detail(waiting: i64) -> String {
        if waiting == 1 {
            "1 clip is waiting".to_string()
        } else {
            format!("{} are waiting", clips(waiting))
        }
    }

    pub const CHOOSE_BOOK: &str = "Choose a book";
    pub const NOT_NOW: &str = "Not now";

    pub const LIBRARY_FULL_TITLE: &str = "Your library is full";

    pub fn library_full_detail(clip_limit: Option<i64>) -> String {
        match clip_limit {
            Some(limit) => format!(
                "Your plan keeps {}, so new clips are waiting. They'll upload once there's room.",
                clips(limit)
            ),
            None => "New clips are waiting. They'll upload once there's room.".to_string(),
        }
    }

    pub fn monthly_limit_detail<Tz: TimeZone>(uploads_from: Option<&DateTime<Tz>>) -> String {
        match uploads_from {
            Some(from) => format!(
                "You've added a lot of clips this month, so new clips are waiting. They'll upload from {}.",
                super::date(from)
            ),
            None => {
                "You've added a lot of clips this month, so new clips are waiting. They'll upload once there's room."
                    .to_string()
            }
        }
    }

    pub const SEE_PLANS: &str = "See plans";
    pub const UPDATE_TITLE: &str = "Update Reelshelf to keep going";
    pub const UPDATE_DETAIL: &str =
        "This version doesn't work with Reelshelf any more, so clips aren't uploading. Get the latest from the Store.";
    pub const OPEN_STORE: &str = "Open the Store";
    pub const SIGNED_OUT_TITLE: &str = "This computer was signed out, so clips aren't uploading.";
    pub const SIGNED_OUT_DETAIL: &str = "Sign in again to carry on.";
    pub const SIGN_IN: &str = "Sign in";
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::{FixedOffset, Utc};

    #[test]
    fn counts_and_formats_as_the_copy_guide_says() {
        assert_eq!(clips(1), "1 clip");
        assert_eq!(clips(1000), "1,000 clips");
        assert_eq!(clips(1_234_567), "1,234,567 clips");
        assert_eq!(length(41.2), "0:41");
        assert_eq!(length(134.0), "2:14");
        assert_eq!(length(3725.0), "1:02:05");
        assert_eq!(size(77_600_000), "74 MB");
        assert_eq!(size(1_342_177_280), "1.3 GB");
        assert_eq!(size(10), "1 MB");
        assert_eq!(
            recent::meta::<Utc>("Apex Legends", Some(41.0), 77_600_000, None),
            "Apex Legends · 0:41 · 74 MB"
        );
        assert_eq!(notifications::shelved_title(3), "3 clips are on the shelf");
        assert_eq!(
            notifications::shelved_books([("Apex Legends", 2), ("Valorant", 1)]),
            "Apex Legends (2), Valorant"
        );
        assert_eq!(
            notifications::needs_book_title("Marvel Rivals"),
            "Which book do Marvel Rivals clips go on?"
        );
        assert_eq!(
            notifications::needs_book_detail(2, &folder("Marvel Rivals")),
            "2 clips are waiting in Marvel Rivals\\"
        );
        assert_eq!(
            tray::quit_while_uploading(2),
            "Quit and stop 2 uploads? They'll carry on next time."
        );
    }

    #[test]
    fn times_dates_and_waits() {
        let zone = FixedOffset::east_opt(3600).unwrap();
        let at = zone.with_ymd_and_hms(2026, 9, 6, 9, 5, 0).unwrap();
        assert_eq!(time(&at), "09:05");
        assert_eq!(date(&at), "6 Sept 2026");
        assert_eq!(in_time(Duration::from_secs(15)), "in 15 seconds");
        assert_eq!(in_time(Duration::from_millis(200)), "in 1 second");
        assert_eq!(in_time(Duration::from_secs(61)), "in 2 minutes");
        assert_eq!(folder(""), "");
    }
}
