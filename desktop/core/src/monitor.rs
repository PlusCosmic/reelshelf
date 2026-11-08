//! Turns what is in the recordings folder into ledger rows. Files there when the folder is chosen become `baseline`,
//! and every other file the ledger doesn't know (by path, size and last-write time) is watched until it settles, then
//! added as `waiting` on its book, or `needs_book`. It runs no timers itself: the engine calls [`FolderMonitor::rescan`]
//! every 60 s (and at start, and after the watcher errs or overflows) and [`FolderMonitor::settle`] every 2 s.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::{DateTime, Utc};

use crate::files::{self, ScannedFile};
use crate::ledger::{Ledger, LedgerEntry, LedgerState};
use crate::matching::{self, Book};
use crate::platform::Clock;
use crate::settings::UploaderSettings;

/// How long a file's size and last-write time must hold before it counts as written.
pub const STABLE_FOR: Duration = Duration::from_secs(5);

/// How often settling files are looked at.
pub const CHECK_EVERY: Duration = Duration::from_secs(2);

/// Whether a file opens for reading while others may only read it ([`files::can_open_for_reading`] in the app).
pub type CanOpen = Arc<dyn Fn(&Path) -> bool + Send + Sync>;

struct Tracked {
    last: Option<ScannedFile>,
    stable_since: DateTime<Utc>,
}

/// Waits for files a recorder may still be writing. A tracked file is ready once its size and last-write time have held
/// for [`STABLE_FOR`] and it opens for reading without a sharing violation.
pub struct FileSettler {
    root: PathBuf,
    clock: Arc<dyn Clock>,
    can_open: CanOpen,
    tracked: BTreeMap<String, Tracked>,
}

impl FileSettler {
    pub fn new(root: PathBuf, clock: Arc<dyn Clock>, can_open: CanOpen) -> Self {
        FileSettler {
            root,
            clock,
            can_open,
            tracked: BTreeMap::new(),
        }
    }

    pub fn count(&self) -> usize {
        self.tracked.len()
    }

    pub fn is_tracking(&self, relative_path: &str) -> bool {
        self.tracked.contains_key(&files::normalize(relative_path))
    }

    /// Starts watching a file, or carries on watching it.
    pub fn track(&mut self, relative_path: &str) {
        let now = self.clock.now();
        self.tracked.entry(files::normalize(relative_path)).or_insert(Tracked {
            last: None,
            stable_since: now,
        });
    }

    /// Looks at every tracked file once: answers the ones that have settled, and stops tracking them and any that are
    /// gone.
    pub fn check(&mut self) -> Vec<ScannedFile> {
        let now = self.clock.now();
        let mut ready = Vec::new();
        let stable_for = chrono::TimeDelta::from_std(STABLE_FOR).unwrap_or_default();
        self.tracked.retain(|path, tracked| {
            let Some(current) = files::stat(&self.root, path) else {
                return false;
            };

            let unchanged = tracked
                .last
                .as_ref()
                .is_some_and(|last| last.size == current.size && last.last_write == current.last_write);
            if !unchanged {
                tracked.last = Some(current);
                tracked.stable_since = now;
                return true;
            }

            if now - tracked.stable_since < stable_for || !(self.can_open)(&files::full_path(&self.root, path)) {
                return true;
            }

            ready.push(current);
            false
        });
        ready
    }
}

struct Watched {
    root: String,
    settler: FileSettler,
}

/// The recordings folder's files on their way into the ledger.
pub struct FolderMonitor {
    ledger: Arc<Ledger>,
    clock: Arc<dyn Clock>,
    can_open: CanOpen,
    watched: Mutex<Option<Watched>>,
}

/// The folder as the ledger keys it: absolute, as Windows' `GetFullPathName` gives it.
pub fn root_key(folder: &str) -> String {
    std::path::absolute(folder)
        .map(|path| path.to_string_lossy().into_owned())
        .unwrap_or_else(|_| folder.to_string())
}

impl FolderMonitor {
    pub fn new(ledger: Arc<Ledger>, clock: Arc<dyn Clock>, can_open: CanOpen) -> Self {
        FolderMonitor {
            ledger,
            clock,
            can_open,
            watched: Mutex::new(None),
        }
    }

    fn watched(&self) -> std::sync::MutexGuard<'_, Option<Watched>> {
        self.watched.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    pub fn root(&self) -> Option<String> {
        self.watched().as_ref().map(|watched| watched.root.clone())
    }

    /// How many files are waiting to settle.
    pub fn settling(&self) -> usize {
        self.watched().as_ref().map_or(0, |watched| watched.settler.count())
    }

    /// Starts watching a folder. Its files are baselined only when `baseline` is set (S2). Answers how many were.
    pub fn use_folder(&self, folder: &str, baseline: bool) -> usize {
        let root = root_key(folder);
        let settler = FileSettler::new(PathBuf::from(&root), self.clock.clone(), self.can_open.clone());
        let added = if baseline {
            self.ledger
                .add_baseline(&root, &files::scan(Path::new(&root)), self.clock.now())
        } else {
            0
        };
        *self.watched() = Some(Watched { root, settler });
        added
    }

    /// Looks over the whole folder for files the ledger doesn't have.
    pub fn rescan(&self) {
        let mut watched = self.watched();
        let Some(watched) = watched.as_mut() else {
            return;
        };
        for file in files::scan(Path::new(&watched.root)) {
            if !self.ledger.knows(&watched.root, &file) {
                watched.settler.track(&file.relative_path);
            }
        }
    }

    /// A watcher event: the file is looked at on the next [`settle`](Self::settle) unless the ledger has it.
    pub fn mark_dirty(&self, relative_path: &str) {
        let mut watched = self.watched();
        let Some(watched) = watched.as_mut() else {
            return;
        };
        if let Some(file) = files::stat(Path::new(&watched.root), relative_path)
            && !self.ledger.knows(&watched.root, &file)
        {
            watched.settler.track(&file.relative_path);
        }
    }

    /// Adds the files that have settled to the ledger, each on its book, and answers them.
    pub fn settle(&self, books: &[Book], settings: &UploaderSettings) -> Vec<LedgerEntry> {
        let mut watched = self.watched();
        let Some(watched) = watched.as_mut() else {
            return Vec::new();
        };

        let now = self.clock.now();
        let mut added = Vec::new();
        for file in watched.settler.check() {
            if self.ledger.knows(&watched.root, &file) {
                continue;
            }

            let found = matching::resolve(
                file.folder(),
                books,
                settings.saved_book_for(file.folder()),
                settings.default_book_id,
            );
            let state = if found.category_id.is_some() {
                LedgerState::Waiting
            } else {
                LedgerState::NeedsBook
            };
            let mut entry = LedgerEntry::new(&watched.root, &file, state, now);
            entry.category_id = found.category_id;
            self.ledger.save(&entry);
            added.push(entry);
        }
        added
    }

    /// Files waiting for a book whose folder now has one (a choice was saved, a default was set, or a game was added on
    /// the site and its name matches) go in the queue. Answers how many did.
    pub fn rematch(&self, books: &[Book], settings: &UploaderSettings) -> usize {
        let Some(root) = self.root() else {
            return 0;
        };

        let mut matched = 0;
        for mut entry in self.ledger.in_state(&root, &[LedgerState::NeedsBook]) {
            let found = matching::resolve(
                entry.folder(),
                books,
                settings.saved_book_for(entry.folder()),
                settings.default_book_id,
            );
            let Some(book) = found.category_id else {
                continue;
            };
            entry.category_id = Some(book);
            entry.state = LedgerState::Waiting;
            entry.updated_at = self.clock.now();
            self.ledger.save(&entry);
            matched += 1;
        }
        matched
    }
}
