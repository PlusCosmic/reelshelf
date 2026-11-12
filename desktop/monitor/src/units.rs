//! Numbers as Grafana shows them for a panel's unit (`reqps`, `s`, `bytes`, `percentunit`...), and the axes' ticks:
//! "nice" values for the y-axis and round times for the x-axis.

use chrono::{DateTime, FixedOffset, TimeZone};

/// `value` in `unit`, with `decimals` when the panel fixes them.
pub fn format(value: f64, unit: Option<&str>, decimals: Option<u32>) -> String {
    if !value.is_finite() {
        return "–".to_owned();
    }
    let unit = unit.unwrap_or("short");
    match unit {
        "percentunit" => format!("{}%", number(value * 100.0, decimals)),
        "percent" => format!("{}%", number(value, decimals)),
        "s" => duration(value, decimals),
        "ms" => duration(value / 1000.0, decimals),
        "bytes" => scaled(value, 1024.0, &["B", "KiB", "MiB", "GiB", "TiB", "PiB"], decimals),
        "decbytes" => scaled(value, 1000.0, &["B", "kB", "MB", "GB", "TB", "PB"], decimals),
        "Bps" | "binBps" => scaled(value, 1024.0, &["B/s", "KiB/s", "MiB/s", "GiB/s", "TiB/s"], decimals),
        "decBps" => scaled(value, 1000.0, &["B/s", "kB/s", "MB/s", "GB/s", "TB/s"], decimals),
        "reqps" => format!("{} req/s", short(value, decimals)),
        "ops" => format!("{} ops/s", short(value, decimals)),
        "currencyUSD" => format!("${}", short(value, decimals.or(Some(2)))),
        "currencyGBP" => format!("£{}", short(value, decimals.or(Some(2)))),
        "currencyEUR" => format!("€{}", short(value, decimals.or(Some(2)))),
        "none" => number(value, decimals),
        _ => short(value, decimals),
    }
}

/// A plain number: whole numbers as they are, others to about three significant figures.
fn number(value: f64, decimals: Option<u32>) -> String {
    let places = decimals.unwrap_or_else(|| {
        let a = value.abs();
        if a == 0.0 || a.fract() == 0.0 || a >= 100.0 {
            0
        } else if a >= 10.0 {
            1
        } else if a >= 1.0 {
            2
        } else {
            // Enough places for three significant figures, up to six.
            ((-a.log10()).floor() as u32 + 3).min(6)
        }
    });
    let text = format!("{value:.*}", places as usize);
    if decimals.is_none() && text.contains('.') {
        text.trim_end_matches('0').trim_end_matches('.').to_owned()
    } else {
        text
    }
}

/// Thousands, millions and billions as K, M and B.
fn short(value: f64, decimals: Option<u32>) -> String {
    let a = value.abs();
    let (div, suffix) = if a >= 1e12 {
        (1e12, " T")
    } else if a >= 1e9 {
        (1e9, " B")
    } else if a >= 1e6 {
        (1e6, " M")
    } else if a >= 1e3 {
        (1e3, " K")
    } else {
        (1.0, "")
    };
    format!("{}{suffix}", number(value / div, decimals))
}

fn scaled(value: f64, step: f64, units: &[&str], decimals: Option<u32>) -> String {
    let mut v = value;
    let mut at = 0;
    while v.abs() >= step && at + 1 < units.len() {
        v /= step;
        at += 1;
    }
    format!("{} {}", number(v, decimals), units[at])
}

fn duration(seconds: f64, decimals: Option<u32>) -> String {
    let a = seconds.abs();
    if a == 0.0 {
        "0 s".to_owned()
    } else if a < 1e-3 {
        format!("{} µs", number(seconds * 1e6, decimals))
    } else if a < 1.0 {
        format!("{} ms", number(seconds * 1e3, decimals))
    } else if a < 60.0 {
        format!("{} s", number(seconds, decimals))
    } else if a < 3600.0 {
        format!("{} min", number(seconds / 60.0, decimals))
    } else if a < 86_400.0 {
        format!("{} hour", number(seconds / 3600.0, decimals))
    } else {
        format!("{} day", number(seconds / 86_400.0, decimals))
    }
}

/// About `count` round values covering `[lo, hi]`, and the range they span (which contains it).
pub fn nice_ticks(lo: f64, hi: f64, count: usize) -> (f64, f64, Vec<f64>) {
    // A flat line still gets an axis: from zero to the value (or to one, for zero).
    let (lo, hi) = if (hi - lo).abs() < f64::EPSILON {
        if lo == 0.0 {
            (0.0, 1.0)
        } else {
            (lo.min(0.0), lo.max(0.0))
        }
    } else {
        (lo, hi)
    };
    let raw = (hi - lo) / count.max(1) as f64;
    let mag = 10f64.powf(raw.log10().floor());
    let step = [1.0, 2.0, 2.5, 5.0, 10.0]
        .iter()
        .map(|m| m * mag)
        .find(|s| *s >= raw)
        .unwrap_or(10.0 * mag);
    let start = (lo / step).floor() * step;
    let end = (hi / step).ceil() * step;
    let n = ((end - start) / step).round() as usize;
    let ticks = (0..=n).map(|i| start + step * i as f64).collect();
    (start, end, ticks)
}

/// Round times across `[from, to]` (epoch ms) about `count` apart, each with its label in `tz`: hours and minutes
/// within a day, else the day.
pub fn time_ticks(from: i64, to: i64, count: usize, tz: &FixedOffset) -> Vec<(i64, String)> {
    const MIN: i64 = 60_000;
    const HOUR: i64 = 60 * MIN;
    const DAY: i64 = 24 * HOUR;
    let steps = [
        MIN,
        2 * MIN,
        5 * MIN,
        10 * MIN,
        15 * MIN,
        30 * MIN,
        HOUR,
        2 * HOUR,
        3 * HOUR,
        6 * HOUR,
        12 * HOUR,
        DAY,
        2 * DAY,
        7 * DAY,
    ];
    let span = (to - from).max(1);
    let raw = span / count.max(1) as i64;
    let step = steps.iter().copied().find(|s| *s >= raw).unwrap_or(7 * DAY);
    // Round in local time, so hours land on the hour where the person is.
    let offset = i64::from(tz.local_minus_utc()) * 1000;
    let first = ((from + offset) / step + 1) * step - offset;
    let mut out = Vec::new();
    let mut t = first;
    while t <= to {
        let at: DateTime<FixedOffset> = tz.timestamp_millis_opt(t).single().unwrap_or_default();
        let label = if step >= DAY || (span > DAY && (t + offset) % DAY == 0) {
            at.format("%a %-d").to_string()
        } else {
            at.format("%H:%M").to_string()
        };
        out.push((t, label));
        t += step;
    }
    out
}

/// A time for the hover readout: the day when the range is longer than one.
pub fn readout_time(t: i64, span: i64, tz: &FixedOffset) -> String {
    let at = tz.timestamp_millis_opt(t).single().unwrap_or_default();
    if span > 24 * 3_600_000 {
        at.format("%a %-d %b, %H:%M").to_string()
    } else {
        at.format("%H:%M:%S").to_string()
    }
}

/// A count with thousands grouped: `12,345`.
pub fn count(n: i64) -> String {
    let digits = n.unsigned_abs().to_string();
    let mut out = String::new();
    for (i, c) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i).is_multiple_of(3) {
            out.push(',');
        }
        out.push(c);
    }
    if n < 0 { format!("-{out}") } else { out }
}

/// A non-negative amount to two places with thousands grouped: `1,234.50`.
pub fn two_places(v: f64) -> String {
    let cents = (v * 100.0).round() as i64;
    format!("{}.{:02}", count(cents / 100), cents % 100)
}

/// Bytes in binary units: `1.21 TiB`.
pub fn bytes(n: i64) -> String {
    format(n as f64, Some("bytes"), None)
}

/// Dollars: two places, or "under a cent" as None for the caller's words.
pub fn dollars(v: f64) -> Option<String> {
    (v == 0.0 || v >= 0.005).then(|| format!("${v:.2}"))
}

/// When something happened: the time on the day of `now`, else the day and time (`Tue 12 Nov, 09:48`).
pub fn when(t: DateTime<chrono::Utc>, now_ms: i64, tz: &FixedOffset) -> String {
    let at = t.with_timezone(tz);
    let now = tz.timestamp_millis_opt(now_ms).single().unwrap_or(at);
    if at.date_naive() == now.date_naive() {
        at.format("%H:%M").to_string()
    } else {
        at.format("%a %-d %b, %H:%M").to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn counts_bytes_money_and_times() {
        assert_eq!(count(0), "0");
        assert_eq!(count(1234567), "1,234,567");
        assert_eq!(count(-1200), "-1,200");
        assert_eq!(bytes(3 * 1024 * 1024), "3 MiB");
        assert_eq!(dollars(12.345).as_deref(), Some("$12.35"));
        assert_eq!(dollars(0.001), None);
        let tz = FixedOffset::east_opt(0).unwrap();
        let now = 1_731_420_000_000; // Tue 12 Nov 2024, 14:00 UTC
        let t = chrono::DateTime::from_timestamp_millis(now - 3_600_000).unwrap();
        assert_eq!(when(t, now, &tz), "13:00");
        let t = chrono::DateTime::from_timestamp_millis(now - 86_400_000).unwrap();
        assert_eq!(when(t, now, &tz), "Mon 11 Nov, 14:00");
    }

    #[test]
    fn units_read_as_grafana_shows_them() {
        assert_eq!(format(12.345, Some("reqps"), None), "12.3 req/s");
        assert_eq!(format(0.0123, Some("percentunit"), None), "1.23%");
        assert_eq!(format(0.25, Some("s"), None), "250 ms");
        assert_eq!(format(90.0, Some("s"), None), "1.5 min");
        assert_eq!(format(3.0 * 1024.0 * 1024.0, Some("bytes"), None), "3 MiB");
        assert_eq!(format(1_500_000.0, Some("decbytes"), None), "1.5 MB");
        assert_eq!(format(42.0, None, None), "42");
        assert_eq!(format(4200.0, Some("short"), None), "4.2 K");
        assert_eq!(format(2.0, Some("short"), Some(1)), "2.0");
        assert_eq!(format(f64::NAN, None, None), "–");
    }

    #[test]
    fn nice_ticks_are_round() {
        let (lo, hi, ticks) = nice_ticks(0.0, 0.93, 4);
        assert_eq!((lo, hi), (0.0, 1.0));
        assert_eq!(ticks, vec![0.0, 0.25, 0.5, 0.75, 1.0]);
        let (_, _, ticks) = nice_ticks(0.0, 0.0, 4);
        assert_eq!(ticks.first(), Some(&0.0));
        assert!(ticks.len() >= 2);
    }

    #[test]
    fn time_ticks_land_on_round_local_times() {
        let tz = FixedOffset::east_opt(3600).unwrap();
        // 6 hours from 10:07 local time.
        let from = 1_731_402_420_000 - 3_600_000;
        let to = from + 6 * 3_600_000;
        let ticks = time_ticks(from, to, 6, &tz);
        assert!(ticks.len() >= 5, "{ticks:?}");
        assert!(ticks.iter().all(|(_, label)| label.ends_with(":00")), "{ticks:?}");
        let week = time_ticks(from, from + 7 * 24 * 3_600_000, 6, &tz);
        assert!(week.iter().all(|(_, label)| !label.contains(':')), "{week:?}");
    }
}
