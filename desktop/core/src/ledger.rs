//! The uploader's local record of every file it has seen (SQLite, `ledger.db`), so a rescan never hashes or uploads a
//! file twice: a file whose path, size and last-write time match a row is left alone. It also keeps each upload's
//! parts and their ETags, so an upload resumes after a restart. It stays on the device.
//!
//! The schema, and how each column is written, are the .NET build's, so an installed copy keeps its ledger: times are
//! Unix milliseconds, ids lowercase hyphenated UUIDs, and `parts` a JSON object of ETags by part number.

use std::collections::BTreeMap;
use std::path::Path;
use std::sync::Mutex;

use chrono::{DateTime, Utc};
use rusqlite::types::Value;
use rusqlite::{Connection, OptionalExtension, Row, params, params_from_iter};
use uuid::Uuid;

use crate::files::{self, ScannedFile};

/// Where a file is in the uploader's hands.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum LedgerState {
    /// Already in the folder when it was chosen; never uploaded.
    Baseline,
    /// Settled, with a book, waiting for its turn (or for a retry).
    Waiting,
    /// Settled, but its folder has no book yet (S3).
    NeedsBook,
    /// Prepared on the API and part way through its upload.
    Uploading,
    /// On the shelf.
    Shelved,
    /// The library already has it (the API's MD5 check).
    Duplicate,
    /// Longer than the plan keeps.
    TooLong,
    /// The plan had no room for it; uploads pause until it has.
    PlanFull,
    /// Out of attempts, or refused for good.
    Failed,
}

impl LedgerState {
    pub fn as_str(self) -> &'static str {
        match self {
            LedgerState::Baseline => "baseline",
            LedgerState::Waiting => "waiting",
            LedgerState::NeedsBook => "needs_book",
            LedgerState::Uploading => "uploading",
            LedgerState::Shelved => "shelved",
            LedgerState::Duplicate => "duplicate",
            LedgerState::TooLong => "too_long",
            LedgerState::PlanFull => "plan_full",
            LedgerState::Failed => "failed",
        }
    }

    /// A state as the ledger stores it; anything unknown reads as `failed`.
    pub fn parse(value: &str) -> LedgerState {
        match value {
            "baseline" => LedgerState::Baseline,
            "waiting" => LedgerState::Waiting,
            "needs_book" => LedgerState::NeedsBook,
            "uploading" => LedgerState::Uploading,
            "shelved" => LedgerState::Shelved,
            "duplicate" => LedgerState::Duplicate,
            "too_long" => LedgerState::TooLong,
            "plan_full" => LedgerState::PlanFull,
            _ => LedgerState::Failed,
        }
    }
}

/// One file the uploader has seen, keyed by its recordings folder and its path below it.
#[derive(Debug, Clone, PartialEq)]
pub struct LedgerEntry {
    pub root: String,
    pub path: String,
    pub size: u64,
    pub last_write: DateTime<Utc>,
    pub state: LedgerState,
    pub clip_id: Option<Uuid>,
    pub md5: Option<String>,
    pub category_id: Option<Uuid>,
    pub upload_id: Option<String>,
    pub part_size: Option<u64>,
    pub part_count: Option<u32>,
    /// ETags of the parts storage has, by part number.
    pub parts: BTreeMap<u32, String>,
    pub attempts: u32,
    pub last_error: Option<String>,
    pub length_seconds: Option<f64>,
    /// When a waiting file may be tried again after a failure.
    pub next_attempt_at: Option<DateTime<Utc>>,
    pub progress: u8,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
    pub shelved_at: Option<DateTime<Utc>>,
}

impl LedgerEntry {
    /// A new row for a file, in `state`.
    pub fn new(root: &str, file: &ScannedFile, state: LedgerState, now: DateTime<Utc>) -> Self {
        LedgerEntry {
            root: root.to_string(),
            path: file.relative_path.clone(),
            size: file.size,
            last_write: file.last_write,
            state,
            clip_id: None,
            md5: None,
            category_id: None,
            upload_id: None,
            part_size: None,
            part_count: None,
            parts: BTreeMap::new(),
            attempts: 0,
            last_error: None,
            length_seconds: None,
            next_attempt_at: None,
            progress: 0,
            created_at: now,
            updated_at: now,
            shelved_at: None,
        }
    }

    /// The folder below the root it is in ("" for the root), whose book it goes on.
    pub fn folder(&self) -> &str {
        files::folder_of(&self.path)
    }

    /// The clip's title: the file name without its extension.
    pub fn title(&self) -> &str {
        files::title_of(&self.path)
    }

    /// The same file: its size and last-write time, to the millisecond the ledger keeps, are unchanged.
    pub fn matches(&self, file: &ScannedFile) -> bool {
        file.size == self.size && file.last_write.timestamp_millis() == self.last_write.timestamp_millis()
    }

    /// Forgets an upload in progress, so the file is prepared afresh.
    pub fn forget_upload(&mut self) {
        self.clip_id = None;
        self.upload_id = None;
        self.part_size = None;
        self.part_count = None;
        self.parts.clear();
        self.progress = 0;
    }
}

/// The ledger. Every call is a short statement under one connection, so it is safe to share between threads.
pub struct Ledger {
    connection: Mutex<Connection>,
}

pub const RECENT_COUNT: u32 = 50;

const COLUMNS: &str = "root, path, size, last_write, state, clip_id, md5, category_id, upload_id, part_size, \
    part_count, parts, attempts, last_error, length_seconds, next_attempt_at, progress, created_at, updated_at, \
    shelved_at";

const SCHEMA: &str = "
    PRAGMA journal_mode = WAL;
    CREATE TABLE IF NOT EXISTS file (
        root TEXT NOT NULL,
        path TEXT NOT NULL,
        size INTEGER NOT NULL,
        last_write INTEGER NOT NULL,
        state TEXT NOT NULL,
        clip_id TEXT,
        md5 TEXT,
        category_id TEXT,
        upload_id TEXT,
        part_size INTEGER,
        part_count INTEGER,
        parts TEXT,
        attempts INTEGER NOT NULL DEFAULT 0,
        last_error TEXT,
        length_seconds REAL,
        next_attempt_at INTEGER,
        progress INTEGER NOT NULL DEFAULT 0,
        created_at INTEGER NOT NULL,
        updated_at INTEGER NOT NULL,
        shelved_at INTEGER,
        PRIMARY KEY (root, path)
    );
    CREATE INDEX IF NOT EXISTS file_state_idx ON file (state, created_at);
";

fn millis(at: DateTime<Utc>) -> i64 {
    at.timestamp_millis()
}

fn from_millis(value: i64) -> DateTime<Utc> {
    DateTime::from_timestamp_millis(value).unwrap_or(DateTime::UNIX_EPOCH)
}

fn parse_uuid(value: Option<String>) -> Option<Uuid> {
    value.and_then(|text| Uuid::parse_str(&text).ok())
}

fn state_list(states: &[LedgerState]) -> String {
    states
        .iter()
        .map(|state| format!("'{}'", state.as_str()))
        .collect::<Vec<_>>()
        .join(", ")
}

fn read_entry(row: &Row<'_>) -> rusqlite::Result<LedgerEntry> {
    let parts: Option<String> = row.get(11)?;
    Ok(LedgerEntry {
        root: row.get(0)?,
        path: row.get(1)?,
        size: row.get::<_, i64>(2)?.max(0) as u64,
        last_write: from_millis(row.get(3)?),
        state: LedgerState::parse(&row.get::<_, String>(4)?),
        clip_id: parse_uuid(row.get(5)?),
        md5: row.get(6)?,
        category_id: parse_uuid(row.get(7)?),
        upload_id: row.get(8)?,
        part_size: row.get::<_, Option<i64>>(9)?.map(|value| value.max(0) as u64),
        part_count: row.get::<_, Option<i64>>(10)?.map(|value| value.max(0) as u32),
        parts: parts
            .and_then(|json| serde_json::from_str(&json).ok())
            .unwrap_or_default(),
        attempts: row.get::<_, i64>(12)?.max(0) as u32,
        last_error: row.get(13)?,
        length_seconds: row.get(14)?,
        next_attempt_at: row.get::<_, Option<i64>>(15)?.map(from_millis),
        progress: row.get::<_, i64>(16)?.clamp(0, 100) as u8,
        created_at: from_millis(row.get(17)?),
        updated_at: from_millis(row.get(18)?),
        shelved_at: row.get::<_, Option<i64>>(19)?.map(from_millis),
    })
}

impl Ledger {
    pub fn open(path: &Path) -> rusqlite::Result<Ledger> {
        if let Some(directory) = path.parent().filter(|directory| !directory.as_os_str().is_empty()) {
            let _ = std::fs::create_dir_all(directory);
        }

        let connection = Connection::open(path)?;
        connection.execute_batch(SCHEMA)?;
        Ok(Ledger {
            connection: Mutex::new(connection),
        })
    }

    fn connection(&self) -> std::sync::MutexGuard<'_, Connection> {
        self.connection.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn query(&self, sql: &str, parameters: Vec<Value>) -> Vec<LedgerEntry> {
        let connection = self.connection();
        let Ok(mut statement) = connection.prepare_cached(sql) else {
            return Vec::new();
        };
        statement
            .query_map(params_from_iter(parameters), read_entry)
            .map(|rows| rows.filter_map(Result::ok).collect())
            .unwrap_or_default()
    }

    pub fn find(&self, root: &str, path: &str) -> Option<LedgerEntry> {
        let connection = self.connection();
        connection
            .query_row(
                &format!("SELECT {COLUMNS} FROM file WHERE root = ?1 AND path = ?2"),
                params![root, files::normalize(path)],
                read_entry,
            )
            .optional()
            .ok()
            .flatten()
    }

    /// Whether the ledger has this exact file (path, size and last-write time).
    pub fn knows(&self, root: &str, file: &ScannedFile) -> bool {
        self.find(root, &file.relative_path)
            .is_some_and(|entry| entry.matches(file))
    }

    /// Records files as already there, so they are never uploaded; files it has are left as they are.
    pub fn add_baseline(&self, root: &str, files: &[ScannedFile], now: DateTime<Utc>) -> usize {
        let mut connection = self.connection();
        let Ok(transaction) = connection.transaction() else {
            return 0;
        };
        let mut added = 0;
        {
            let Ok(mut statement) = transaction.prepare(
                "INSERT OR IGNORE INTO file (root, path, size, last_write, state, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, 'baseline', ?5, ?5)",
            ) else {
                return 0;
            };
            for file in files {
                added += statement
                    .execute(params![
                        root,
                        file.relative_path,
                        file.size as i64,
                        millis(file.last_write),
                        millis(now)
                    ])
                    .unwrap_or(0);
            }
        }
        if transaction.commit().is_err() {
            return 0;
        }
        added
    }

    /// Writes the whole row, adding it when it is new.
    pub fn save(&self, entry: &LedgerEntry) {
        let parts = if entry.parts.is_empty() {
            None
        } else {
            serde_json::to_string(&entry.parts).ok()
        };
        let connection = self.connection();
        let result = connection.execute(
            &format!(
                "INSERT INTO file ({COLUMNS})
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20)
                 ON CONFLICT (root, path) DO UPDATE SET
                     size = excluded.size, last_write = excluded.last_write, state = excluded.state,
                     clip_id = excluded.clip_id, md5 = excluded.md5, category_id = excluded.category_id,
                     upload_id = excluded.upload_id, part_size = excluded.part_size, part_count = excluded.part_count,
                     parts = excluded.parts, attempts = excluded.attempts, last_error = excluded.last_error,
                     length_seconds = excluded.length_seconds, next_attempt_at = excluded.next_attempt_at,
                     progress = excluded.progress, created_at = excluded.created_at, updated_at = excluded.updated_at,
                     shelved_at = excluded.shelved_at"
            ),
            params![
                entry.root,
                files::normalize(&entry.path),
                entry.size as i64,
                millis(entry.last_write),
                entry.state.as_str(),
                entry.clip_id.map(|id| id.to_string()),
                entry.md5,
                entry.category_id.map(|id| id.to_string()),
                entry.upload_id,
                entry.part_size.map(|size| size as i64),
                entry.part_count.map(i64::from),
                parts,
                i64::from(entry.attempts),
                entry.last_error,
                entry.length_seconds,
                entry.next_attempt_at.map(millis),
                i64::from(entry.progress),
                millis(entry.created_at),
                millis(entry.updated_at),
                entry.shelved_at.map(millis),
            ],
        );
        if let Err(error) = result {
            tracing::error!("Couldn't save {} in the ledger: {error}", entry.path);
        }
    }

    pub fn in_state(&self, root: &str, states: &[LedgerState]) -> Vec<LedgerEntry> {
        self.query(
            &format!(
                "SELECT {COLUMNS} FROM file WHERE root = ?1 AND state IN ({}) ORDER BY created_at, path",
                state_list(states)
            ),
            vec![Value::Text(root.to_string())],
        )
    }

    pub fn count(&self, root: &str, states: &[LedgerState]) -> usize {
        self.in_state(root, states).len()
    }

    /// The next file to upload: one already uploading first (to resume it), then the oldest waiting one whose retry
    /// time has come.
    pub fn next_to_upload(&self, root: &str, now: DateTime<Utc>) -> Option<LedgerEntry> {
        self.query(
            &format!(
                "SELECT {COLUMNS} FROM file
                 WHERE root = ?1 AND state IN ('uploading', 'waiting') AND
                       (next_attempt_at IS NULL OR next_attempt_at <= ?2)
                 ORDER BY state = 'uploading' DESC, created_at, path
                 LIMIT 1"
            ),
            vec![Value::Text(root.to_string()), Value::Integer(millis(now))],
        )
        .into_iter()
        .next()
    }

    /// When the soonest waiting file may be tried again, if any is waiting on a retry.
    pub fn next_retry_at(&self, root: &str) -> Option<DateTime<Utc>> {
        let connection = self.connection();
        connection
            .query_row(
                "SELECT min(next_attempt_at) FROM file
                 WHERE root = ?1 AND state IN ('waiting', 'uploading') AND next_attempt_at IS NOT NULL",
                params![root],
                |row| row.get::<_, Option<i64>>(0),
            )
            .ok()
            .flatten()
            .map(from_millis)
    }

    /// The last [`RECENT_COUNT`] files that aren't baseline, newest first, from every folder (S4).
    pub fn recent(&self, count: u32) -> Vec<LedgerEntry> {
        self.query(
            &format!("SELECT {COLUMNS} FROM file WHERE state <> 'baseline' ORDER BY created_at DESC, path LIMIT ?1"),
            vec![Value::Integer(i64::from(count))],
        )
    }

    pub fn shelved_since(&self, root: &str, since: DateTime<Utc>) -> usize {
        let connection = self.connection();
        connection
            .query_row(
                "SELECT count(*) FROM file WHERE root = ?1 AND state = 'shelved' AND shelved_at >= ?2",
                params![root, millis(since)],
                |row| row.get::<_, i64>(0),
            )
            .map(|count| count.max(0) as usize)
            .unwrap_or(0)
    }

    /// Gives files that failed one more try, at the app's start: they wait again with one attempt left, and those that
    /// fail again stay failed until the next start.
    pub fn retry_failed_once(&self, root: &str, max_attempts: u32, now: DateTime<Utc>) -> usize {
        let connection = self.connection();
        connection
            .execute(
                "UPDATE file SET state = 'waiting', attempts = ?2, next_attempt_at = NULL, updated_at = ?3
                 WHERE root = ?1 AND state = 'failed'",
                params![root, i64::from(max_attempts.saturating_sub(1)), millis(now)],
            )
            .unwrap_or(0)
    }

    /// Files waiting for a book in a folder get it, and wait for their turn.
    pub fn assign_book(&self, root: &str, folder: &str, category_id: Uuid, now: DateTime<Utc>) -> usize {
        let mut assigned = 0;
        for mut entry in self.in_state(root, &[LedgerState::NeedsBook]) {
            if entry.folder().to_lowercase() != folder.to_lowercase() {
                continue;
            }
            entry.category_id = Some(category_id);
            entry.state = LedgerState::Waiting;
            entry.updated_at = now;
            self.save(&entry);
            assigned += 1;
        }
        assigned
    }

    /// Files that waited on a full library go back in the queue.
    pub fn requeue_plan_full(&self, root: &str, now: DateTime<Utc>) -> usize {
        let connection = self.connection();
        connection
            .execute(
                "UPDATE file SET state = 'waiting', next_attempt_at = NULL, updated_at = ?2
                 WHERE root = ?1 AND state = 'plan_full'",
                params![root, millis(now)],
            )
            .unwrap_or(0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A ledger written by the .NET build reads back: its ids, its parts JSON (with .NET's escaped quotes) and its
    /// times in milliseconds.
    #[test]
    fn a_ledger_the_dotnet_build_wrote_reads_back() {
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("ledger.db");
        let ledger = Ledger::open(&path).unwrap();
        {
            let connection = ledger.connection();
            connection
                .execute(
                    "INSERT INTO file (root, path, size, last_write, state, clip_id, category_id, upload_id,
                         part_size, part_count, parts, attempts, progress, created_at, updated_at)
                     VALUES ('C:\\Users\\you\\Videos\\NVIDIA', 'Apex Legends/clip.mp4', 3000, 1794005220123,
                         'uploading', '6F9619FF-8B86-D011-B42D-00C04FC964FF', '0f8fad5b-d9cb-469f-a165-70867728950e',
                         'upload-1', 1024, 3, '{\"1\":\"\\u0022etag-1-1024\\u0022\"}', 2, 34, 1794005220000,
                         1794005221000)",
                    [],
                )
                .unwrap();
        }

        let entry = ledger
            .find("C:\\Users\\you\\Videos\\NVIDIA", "Apex Legends\\clip.mp4")
            .unwrap();

        assert_eq!(entry.state, LedgerState::Uploading);
        assert_eq!(
            entry.clip_id.unwrap().to_string(),
            "6f9619ff-8b86-d011-b42d-00c04fc964ff"
        );
        assert_eq!(entry.parts.get(&1).map(String::as_str), Some("\"etag-1-1024\""));
        assert_eq!(entry.last_write.timestamp_millis(), 1_794_005_220_123);
        assert_eq!(
            (entry.part_size, entry.part_count, entry.attempts, entry.progress),
            (Some(1024), Some(3), 2, 34)
        );
        assert_eq!(entry.folder(), "Apex Legends");
        assert_eq!(entry.title(), "clip");

        ledger.save(&entry);
        let connection = ledger.connection();
        let (clip_id, parts): (String, String) = connection
            .query_row("SELECT clip_id, parts FROM file", [], |row| {
                Ok((row.get(0)?, row.get(1)?))
            })
            .unwrap();
        assert_eq!(clip_id, "6f9619ff-8b86-d011-b42d-00c04fc964ff");
        assert_eq!(parts, r#"{"1":"\"etag-1-1024\""}"#);
    }
}
