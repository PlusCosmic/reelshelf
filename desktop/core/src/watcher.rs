//! The recordings folder's watcher (`notify`, `ReadDirectoryChangesW` on Windows), recursive. Its events only mark
//! paths dirty for the next settle check. It can drop events when its buffer overflows, so it asks for a full rescan
//! then, after an error, and when a folder appears (its files raise no events of their own). The 60-second rescan
//! catches anything else missed.

use std::collections::BTreeSet;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Weak};

use notify::{Event, EventKind, RecommendedWatcher, RecursiveMode, Watcher};

use crate::files;

/// What the watcher has noticed since the engine last looked.
#[derive(Default)]
pub struct WatchSignals {
    dirty: Mutex<BTreeSet<String>>,
    rescan: AtomicBool,
}

impl WatchSignals {
    /// Paths below the root, with `/` between folders, that may have changed.
    pub fn take_dirty(&self) -> Vec<String> {
        std::mem::take(&mut *self.dirty.lock().unwrap_or_else(|poisoned| poisoned.into_inner()))
            .into_iter()
            .collect()
    }

    /// Whether a full rescan was asked for, clearing the request.
    pub fn take_rescan(&self) -> bool {
        self.rescan.swap(false, Ordering::SeqCst)
    }

    pub fn request_rescan(&self) {
        self.rescan.store(true, Ordering::SeqCst);
    }

    fn mark(&self, relative: String) {
        self.dirty
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .insert(relative);
    }
}

/// A running watcher; dropping it stops watching.
pub struct FolderWatcher {
    _watcher: RecommendedWatcher,
}

impl FolderWatcher {
    /// Watches `root` and everything below it, reporting to `signals`. The engine falls back on its rescans when this
    /// fails.
    pub fn start(root: &Path, signals: &Arc<WatchSignals>) -> notify::Result<FolderWatcher> {
        let root_path = root.to_path_buf();
        let weak: Weak<WatchSignals> = Arc::downgrade(signals);
        let mut watcher = notify::recommended_watcher(move |result: notify::Result<Event>| {
            if let Some(signals) = weak.upgrade() {
                on_event(&root_path, &signals, result);
            }
        })?;
        watcher.watch(root, RecursiveMode::Recursive)?;
        Ok(FolderWatcher { _watcher: watcher })
    }
}

fn on_event(root: &Path, signals: &WatchSignals, result: notify::Result<Event>) {
    let event = match result {
        Ok(event) => event,
        Err(_) => {
            signals.request_rescan();
            return;
        }
    };

    if event.need_rescan() || matches!(event.kind, EventKind::Other) {
        signals.request_rescan();
        return;
    }
    if matches!(event.kind, EventKind::Access(_)) {
        return;
    }

    for path in &event.paths {
        if path.is_dir() {
            signals.request_rescan();
            continue;
        }
        if let Some(relative) = relative_to(root, path)
            && files::is_candidate(&relative)
        {
            signals.mark(relative);
        }
    }
}

fn relative_to(root: &Path, path: &Path) -> Option<String> {
    let relative: PathBuf = path.strip_prefix(root).ok()?.to_path_buf();
    relative.to_str().map(files::normalize)
}
