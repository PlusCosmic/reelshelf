//! Which files the uploader looks at, and walking the recordings folder for them: videos in containers the site takes
//! (`.mp4`, `.mov`, `.mkv`, `.webm`, ignoring case), not hidden or system files, and nothing under a folder whose name
//! starts with `.`.

use std::fs::{self, Metadata};
use std::path::{Path, PathBuf};

use chrono::{DateTime, Utc};

const EXTENSIONS: [&str; 4] = ["mp4", "mov", "mkv", "webm"];

/// A video file under the recordings folder as a scan saw it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ScannedFile {
    /// Below the recordings folder, with `/` between folders.
    pub relative_path: String,
    pub size: u64,
    pub last_write: DateTime<Utc>,
}

impl ScannedFile {
    /// The folder directly below the recordings folder the file is in, or "" for the folder itself.
    pub fn folder(&self) -> &str {
        folder_of(&self.relative_path)
    }

    /// The file's name without its extension, which becomes the clip's title.
    pub fn title(&self) -> &str {
        title_of(&self.relative_path)
    }
}

/// A path below the root as the ledger keys it: `/` between folders.
pub fn normalize(relative_path: &str) -> String {
    relative_path.replace('\\', "/")
}

/// The first folder of a path below the root, or "" for a file in the root.
pub fn folder_of(relative_path: &str) -> &str {
    match relative_path.find(['/', '\\']) {
        Some(slash) => &relative_path[..slash],
        None => "",
    }
}

/// A file's name without its extension: the clip's title.
pub fn title_of(relative_path: &str) -> &str {
    let name = relative_path.rsplit(['/', '\\']).next().unwrap_or(relative_path);
    match name.rfind('.') {
        Some(dot) if dot > 0 => &name[..dot],
        _ => name,
    }
}

fn extension_of(path: &str) -> Option<&str> {
    let name = path.rsplit(['/', '\\']).next().unwrap_or(path);
    name.rfind('.').map(|dot| &name[dot + 1..])
}

pub fn has_video_extension(path: &str) -> bool {
    extension_of(path).is_some_and(|extension| EXTENSIONS.iter().any(|known| known.eq_ignore_ascii_case(extension)))
}

/// MP4 and MOV keep their length in an `mvhd` box the app can read before uploading.
pub fn has_readable_length(path: &str) -> bool {
    extension_of(path)
        .is_some_and(|extension| extension.eq_ignore_ascii_case("mp4") || extension.eq_ignore_ascii_case("mov"))
}

/// Whether a path below the root may be a clip: a video by its extension, under no folder whose name starts with `.`,
/// and not itself hidden by name. Attributes are checked by [`is_hidden`].
pub fn is_candidate(relative_path: &str) -> bool {
    let normalized = normalize(relative_path);
    let segments: Vec<&str> = normalized.split('/').filter(|segment| !segment.is_empty()).collect();
    match segments.last() {
        None => false,
        Some(last) => !segments.iter().any(|segment| segment.starts_with('.')) && has_video_extension(last),
    }
}

/// Hidden or system by its attributes (Windows); elsewhere a name starting with `.` is what hides a file.
#[cfg(windows)]
pub fn is_hidden(metadata: &Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    const HIDDEN: u32 = 0x2;
    const SYSTEM: u32 = 0x4;
    metadata.file_attributes() & (HIDDEN | SYSTEM) != 0
}

#[cfg(not(windows))]
pub fn is_hidden(_metadata: &Metadata) -> bool {
    false
}

/// Opens the file for reading while sharing it only with readers. A recorder still writing holds it without sharing,
/// so on Windows the open fails with a sharing violation until it lets go.
pub fn can_open_for_reading(path: &Path) -> bool {
    let mut options = fs::OpenOptions::new();
    options.read(true);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        const FILE_SHARE_READ: u32 = 0x1;
        options.share_mode(FILE_SHARE_READ);
    }
    options.open(path).is_ok()
}

/// A file's full path from the root and its path below it.
pub fn full_path(root: &Path, relative_path: &str) -> PathBuf {
    let mut path = root.to_path_buf();
    for segment in normalize(relative_path)
        .split('/')
        .filter(|segment| !segment.is_empty())
    {
        path.push(segment);
    }
    path
}

fn modified(metadata: &Metadata) -> Option<DateTime<Utc>> {
    metadata.modified().ok().map(DateTime::<Utc>::from)
}

/// Walks the recordings folder for candidate videos, skipping hidden and system files and dot folders.
pub fn scan(root: &Path) -> Vec<ScannedFile> {
    let mut files = Vec::new();
    if root.is_dir() {
        walk(root, root, &mut files);
    }
    files
}

/// One file by its path below the root, or None when it isn't (or is no longer) a candidate.
pub fn stat(root: &Path, relative_path: &str) -> Option<ScannedFile> {
    if !is_candidate(relative_path) {
        return None;
    }

    let metadata = fs::metadata(full_path(root, relative_path)).ok()?;
    if !metadata.is_file() || is_hidden(&metadata) {
        return None;
    }

    Some(ScannedFile {
        relative_path: normalize(relative_path),
        size: metadata.len(),
        last_write: modified(&metadata)?,
    })
}

/// The folders directly below the root that hold a video somewhere inside, by name, ignoring case.
pub fn folders_with_videos(root: &Path) -> Vec<String> {
    let mut folders: Vec<String> = Vec::new();
    for file in scan(root) {
        let folder = file.folder();
        if !folder.is_empty()
            && !folders
                .iter()
                .any(|known| known.to_lowercase() == folder.to_lowercase())
        {
            folders.push(folder.to_string());
        }
    }
    folders.sort_by_key(|folder| folder.to_lowercase());
    folders
}

fn walk(directory: &Path, root: &Path, files: &mut Vec<ScannedFile>) {
    let Ok(entries) = fs::read_dir(directory) else {
        return;
    };

    for entry in entries.flatten() {
        let name = entry.file_name();
        let Some(name) = name.to_str() else {
            continue;
        };
        if name.starts_with('.') {
            continue;
        }

        let Ok(file_type) = entry.file_type() else {
            continue;
        };
        let path = entry.path();
        // A link could loop back on itself; recorders don't save through them.
        if file_type.is_dir() {
            if entry.metadata().is_ok_and(|metadata| !is_hidden(&metadata)) {
                walk(&path, root, files);
            }
            continue;
        }

        let Ok(metadata) = fs::metadata(&path) else {
            continue;
        };
        if !metadata.is_file() || is_hidden(&metadata) || !has_video_extension(name) {
            continue;
        }

        let Ok(relative) = path.strip_prefix(root) else {
            continue;
        };
        let Some(relative) = relative.to_str() else {
            continue;
        };
        if let Some(last_write) = modified(&metadata) {
            files.push(ScannedFile {
                relative_path: normalize(relative),
                size: metadata.len(),
                last_write,
            });
        }
    }
}
