//! The uploader's log: one file a day, `logs\uploader-YYYYMMDD.log`, kept for [`KEEP_DAYS`] days, written through
//! `tracing`. Callers log what happened, never a token, a sign-in code or a file's contents; [`redact`] takes out
//! anything shaped like a device token in case one slips through. A panic hook writes the panic and its backtrace there
//! before the process ends.

use std::borrow::Cow;
use std::fs::{self, OpenOptions};
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::sync::{Arc, LazyLock, Mutex};

use chrono::{DateTime, Days, FixedOffset, Local, NaiveDate};
use regex::Regex;
use tracing_subscriber::fmt::MakeWriter;
use tracing_subscriber::fmt::format::Writer;
use tracing_subscriber::fmt::time::FormatTime;

/// How many days of files are kept, today's included.
pub const KEEP_DAYS: u64 = 14;

static TOKEN: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"rsd_[A-Za-z0-9_\-]+").expect("the token pattern is valid"));

/// The message with every device token (`rsd_…`) replaced.
pub fn redact(message: &str) -> Cow<'_, str> {
    TOKEN.replace_all(message, "rsd_[redacted]")
}

type Now = Box<dyn Fn() -> DateTime<FixedOffset> + Send + Sync>;

struct Inner {
    directory: PathBuf,
    now: Now,
    cleaned_on: Mutex<Option<NaiveDate>>,
}

/// The daily log files. Cheap to clone; every clone writes to the same files.
#[derive(Clone)]
pub struct DailyFileLog {
    inner: Arc<Inner>,
}

impl DailyFileLog {
    pub fn new(directory: impl Into<PathBuf>) -> Self {
        Self::with_clock(directory, Box::new(|| Local::now().fixed_offset()))
    }

    /// A log whose days follow `now`, for tests.
    pub fn with_clock(directory: impl Into<PathBuf>, now: Now) -> Self {
        DailyFileLog {
            inner: Arc::new(Inner {
                directory: directory.into(),
                now,
                cleaned_on: Mutex::new(None),
            }),
        }
    }

    pub fn directory(&self) -> &Path {
        &self.inner.directory
    }

    pub fn path_for(&self, day: NaiveDate) -> PathBuf {
        self.inner
            .directory
            .join(format!("uploader-{}.log", day.format("%Y%m%d")))
    }

    /// Writes one line, with the local time in front.
    pub fn write(&self, message: &str) {
        let now = (self.inner.now)();
        self.append(&format!("{} {message}\n", now.format("%Y-%m-%d %H:%M:%S%.3f %:z")));
    }

    /// Appends text (already a whole line) to today's file, redacted. Logging never stops the uploader, so a file that
    /// can't be written is skipped.
    fn append(&self, text: &str) {
        let now = (self.inner.now)();
        let today = now.date_naive();
        let mut cleaned_on = self
            .inner
            .cleaned_on
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if fs::create_dir_all(&self.inner.directory).is_err() {
            return;
        }
        let written = OpenOptions::new()
            .create(true)
            .append(true)
            .open(self.path_for(today))
            .and_then(|mut file| file.write_all(redact(text).as_bytes()));
        if written.is_ok() && *cleaned_on != Some(today) {
            *cleaned_on = Some(today);
            self.delete_old(today);
        }
    }

    /// Deletes the daily files older than [`KEEP_DAYS`].
    pub fn delete_old(&self, today: NaiveDate) {
        let Some(oldest_kept) = today.checked_sub_days(Days::new(KEEP_DAYS - 1)) else {
            return;
        };
        let Ok(entries) = fs::read_dir(&self.inner.directory) else {
            return;
        };
        for entry in entries.flatten() {
            let name = entry.file_name();
            let Some(day) = name
                .to_str()
                .and_then(|name| name.strip_prefix("uploader-"))
                .and_then(|rest| rest.strip_suffix(".log"))
                .filter(|digits| digits.len() == 8 && digits.bytes().all(|b| b.is_ascii_digit()))
                .and_then(|digits| NaiveDate::parse_from_str(digits, "%Y%m%d").ok())
            else {
                continue;
            };
            if day < oldest_kept {
                let _ = fs::remove_file(entry.path());
            }
        }
    }
}

/// One event's text, written to the file as a whole when `tracing` is done with it.
pub struct LogLine {
    log: DailyFileLog,
    buffer: Vec<u8>,
}

impl Write for LogLine {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        self.buffer.extend_from_slice(bytes);
        Ok(bytes.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        if !self.buffer.is_empty() {
            let text = String::from_utf8_lossy(&self.buffer).into_owned();
            self.buffer.clear();
            self.log.append(&text);
        }
        Ok(())
    }
}

impl Drop for LogLine {
    fn drop(&mut self) {
        let _ = self.flush();
    }
}

impl<'a> MakeWriter<'a> for DailyFileLog {
    type Writer = LogLine;

    fn make_writer(&'a self) -> LogLine {
        LogLine {
            log: self.clone(),
            buffer: Vec::new(),
        }
    }
}

/// Local time, to the millisecond, with its offset.
struct LocalTime;

impl FormatTime for LocalTime {
    fn format_time(&self, writer: &mut Writer<'_>) -> std::fmt::Result {
        write!(writer, "{}", Local::now().format("%Y-%m-%d %H:%M:%S%.3f %:z"))
    }
}

/// Sends `tracing` to the daily files in `directory` and writes panics there. Call once, at start.
pub fn init(directory: impl Into<PathBuf>) -> DailyFileLog {
    let log = DailyFileLog::new(directory);
    let _ = tracing_subscriber::fmt()
        .with_writer(log.clone())
        .with_ansi(false)
        .with_timer(LocalTime)
        .with_target(false)
        .try_init();
    install_panic_hook(log.clone());
    log
}

/// Writes a panic and its backtrace to the log, then lets the default hook report it as usual.
pub fn install_panic_hook(log: DailyFileLog) {
    let previous = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let backtrace = std::backtrace::Backtrace::force_capture();
        log.write(&format!("PANIC {info}\n{backtrace}"));
        previous(info);
    }));
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    #[test]
    fn the_log_writes_a_daily_file_keeps_fourteen_days_and_never_a_token() {
        let directory = tempfile::tempdir().unwrap();
        let now = FixedOffset::east_opt(0)
            .unwrap()
            .with_ymd_and_hms(2026, 11, 20, 9, 30, 0)
            .unwrap();
        let log = DailyFileLog::with_clock(directory.path(), Box::new(move || now));
        fs::write(directory.path().join("uploader-20261106.log"), "old").unwrap();
        fs::write(directory.path().join("uploader-20261107.log"), "kept").unwrap();
        fs::write(directory.path().join("notes.txt"), "not ours").unwrap();

        log.write("Signed in with rsd_AbC-123_xyz");

        let today = fs::read_to_string(directory.path().join("uploader-20261120.log")).unwrap();
        assert!(today.contains("Signed in with rsd_[redacted]"), "{today}");
        assert!(!today.contains("AbC-123"));
        assert!(today.starts_with("2026-11-20 09:30:00.000 +00:00 "));
        assert!(!directory.path().join("uploader-20261106.log").exists());
        assert!(directory.path().join("uploader-20261107.log").exists());
        assert!(directory.path().join("notes.txt").exists());
    }

    #[test]
    fn tracing_events_go_to_the_file_redacted() {
        let directory = tempfile::tempdir().unwrap();
        let log = DailyFileLog::new(directory.path());
        let subscriber = tracing_subscriber::fmt()
            .with_writer(log.clone())
            .with_ansi(false)
            .with_timer(LocalTime)
            .finish();

        tracing::subscriber::with_default(subscriber, || {
            tracing::info!(token = "rsd_secretValue", "Signed in");
        });

        let today = fs::read_to_string(log.path_for(Local::now().date_naive())).unwrap();
        assert!(today.contains("Signed in"), "{today}");
        assert!(today.contains("rsd_[redacted]"), "{today}");
        assert!(!today.contains("secretValue"));
    }
}
