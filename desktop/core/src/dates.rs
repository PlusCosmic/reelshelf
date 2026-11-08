//! When a clip was made, as the frontend's `deriveCreatedAt` and `parseTimestampFromSourcePath`
//! (`frontend/src/hooks/bulkUploadQueue.ts`) work it out: the file's last-write time, else a timestamp in its name,
//! read as local time, else the epoch.

use std::sync::LazyLock;

use chrono::{DateTime, LocalResult, NaiveDate, TimeZone, Utc};
use regex::Regex;

static PATTERNS: LazyLock<[Regex; 3]> = LazyLock::new(|| {
    [
        r"(?<year>[0-9]{4})-(?<month>[0-9]{2})-(?<day>[0-9]{2})[_\-\s](?<hour>[0-9]{2})(?<minute>[0-9]{2})",
        r"(?<year>[0-9]{4})-(?<month>[0-9]{2})-(?<day>[0-9]{2})[_\-\s](?<hour>[0-9]{2})-(?<minute>[0-9]{2})",
        r"(?<year>[0-9]{4})(?<month>[0-9]{2})(?<day>[0-9]{2})[_\-\s]?(?<hour>[0-9]{2})(?<minute>[0-9]{2})(?<second>[0-9]{2})",
    ]
    .map(|pattern| Regex::new(pattern).expect("the timestamp patterns are valid"))
});

pub fn derive_created_at<Tz: TimeZone>(last_write: DateTime<Utc>, source_path: &str, zone: &Tz) -> DateTime<Utc> {
    if last_write > DateTime::UNIX_EPOCH {
        return last_write;
    }

    parse_timestamp_from_source_path(source_path, zone).unwrap_or(DateTime::UNIX_EPOCH)
}

/// The first timestamp in the path that is a real local time in `zone`. The frontend builds a `Date` and checks it
/// didn't roll over (a 31 February, a 25th hour, or a time the clocks skipped); here that is a range check.
pub fn parse_timestamp_from_source_path<Tz: TimeZone>(source_path: &str, zone: &Tz) -> Option<DateTime<Utc>> {
    for pattern in PATTERNS.iter() {
        let Some(found) = pattern.captures(source_path) else {
            continue;
        };

        let number = |name: &str| {
            found
                .name(name)
                .map_or(0, |value| value.as_str().parse::<u32>().unwrap_or(0))
        };
        let year = number("year") as i32;
        let Some(local) = NaiveDate::from_ymd_opt(year, number("month"), number("day"))
            .and_then(|day| day.and_hms_opt(number("hour"), number("minute"), number("second")))
        else {
            continue;
        };

        match zone.from_local_datetime(&local) {
            LocalResult::Single(at) => return Some(at.with_timezone(&Utc)),
            // A time the clocks went back over happens twice; standard time is the later one.
            LocalResult::Ambiguous(_, later) => return Some(later.with_timezone(&Utc)),
            LocalResult::None => continue,
        }
    }

    None
}
