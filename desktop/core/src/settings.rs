//! The uploader's settings (`settings.json`) and where its files live. The file is the .NET build's, key for key, so an
//! installed copy keeps its folder and choices: camelCase names, ids as lowercase hyphenated UUIDs, `fullScreen` as
//! `"Wait"` or `"UploadSlowly"`, and nulls written out.

use std::fmt;
use std::path::{Path, PathBuf};

use serde::de::{self, MapAccess, Visitor};
use serde::ser::SerializeMap;
use serde::{Deserialize, Deserializer, Serialize, Serializer};
use uuid::Uuid;

/// Where the uploader keeps its files: `%LOCALAPPDATA%\Reelshelf\Uploader` on Windows.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UploaderPaths {
    pub root: PathBuf,
}

impl UploaderPaths {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        UploaderPaths { root: root.into() }
    }

    /// `%LOCALAPPDATA%\Reelshelf\Uploader`, or elsewhere where .NET puts local application data (`$XDG_DATA_HOME`,
    /// else `~/.local/share`).
    pub fn default_location() -> Self {
        let local = std::env::var_os("LOCALAPPDATA")
            .filter(|_| cfg!(windows))
            .map(PathBuf::from)
            .or_else(|| std::env::var_os("XDG_DATA_HOME").map(PathBuf::from))
            .or_else(|| std::env::var_os("HOME").map(|home| PathBuf::from(home).join(".local").join("share")))
            .unwrap_or_else(std::env::temp_dir);
        UploaderPaths::new(local.join("Reelshelf").join("Uploader"))
    }

    pub fn settings(&self) -> PathBuf {
        self.root.join("settings.json")
    }

    pub fn ledger(&self) -> PathBuf {
        self.root.join("ledger.db")
    }

    pub fn logs(&self) -> PathBuf {
        self.root.join("logs")
    }
}

/// What the uploader does while a full-screen game is running (S5).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum FullScreenMode {
    /// Clips wait until the game is closed. The default.
    #[default]
    Wait,
    /// Clips upload at the speed limit, or [`SLOW_UPLOAD_MBPS`] without one.
    UploadSlowly,
}

impl Serialize for FullScreenMode {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(match self {
            FullScreenMode::Wait => "Wait",
            FullScreenMode::UploadSlowly => "UploadSlowly",
        })
    }
}

impl<'de> Deserialize<'de> for FullScreenMode {
    /// As .NET's `JsonStringEnumConverter` reads it: the name, ignoring case, or the number.
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        struct ModeVisitor;

        impl Visitor<'_> for ModeVisitor {
            type Value = FullScreenMode;

            fn expecting(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
                formatter.write_str("\"Wait\" or \"UploadSlowly\"")
            }

            fn visit_str<E: de::Error>(self, value: &str) -> Result<FullScreenMode, E> {
                if value.eq_ignore_ascii_case("wait") {
                    Ok(FullScreenMode::Wait)
                } else if value.eq_ignore_ascii_case("uploadslowly") {
                    Ok(FullScreenMode::UploadSlowly)
                } else {
                    Err(E::unknown_variant(value, &["Wait", "UploadSlowly"]))
                }
            }

            fn visit_u64<E: de::Error>(self, value: u64) -> Result<FullScreenMode, E> {
                match value {
                    0 => Ok(FullScreenMode::Wait),
                    1 => Ok(FullScreenMode::UploadSlowly),
                    _ => Err(E::invalid_value(de::Unexpected::Unsigned(value), &self)),
                }
            }

            fn visit_i64<E: de::Error>(self, value: i64) -> Result<FullScreenMode, E> {
                match u64::try_from(value) {
                    Ok(value) => self.visit_u64(value),
                    Err(_) => Err(E::invalid_value(de::Unexpected::Signed(value), &self)),
                }
            }
        }

        deserializer.deserialize_any(ModeVisitor)
    }
}

/// "Upload slowly" without a speed limit.
pub const SLOW_UPLOAD_MBPS: u32 = 5;

/// The speed limits S5 offers, besides none.
pub const SPEED_LIMITS: [u32; 4] = [20, 10, 5, 2];

/// The book chosen for each folder below the recordings folder, by its name ("" for the recordings folder itself),
/// matched ignoring case. Kept in the order they were chosen, as the .NET build writes them.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct FolderBooks(Vec<(String, Uuid)>);

impl FolderBooks {
    pub fn get(&self, folder: &str) -> Option<Uuid> {
        let folder = folder.to_lowercase();
        self.0
            .iter()
            .find(|(name, _)| name.to_lowercase() == folder)
            .map(|&(_, book)| book)
    }

    /// Saves a folder's book, replacing a choice saved under any casing of its name.
    pub fn insert(&mut self, folder: &str, book: Uuid) {
        let lower = folder.to_lowercase();
        match self.0.iter_mut().find(|(name, _)| name.to_lowercase() == lower) {
            Some(existing) => existing.1 = book,
            None => self.0.push((folder.to_string(), book)),
        }
    }

    pub fn remove(&mut self, folder: &str) {
        let lower = folder.to_lowercase();
        self.0.retain(|(name, _)| name.to_lowercase() != lower);
    }

    pub fn iter(&self) -> impl Iterator<Item = (&str, Uuid)> {
        self.0.iter().map(|(name, book)| (name.as_str(), *book))
    }

    pub fn len(&self) -> usize {
        self.0.len()
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

impl Serialize for FolderBooks {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        let mut map = serializer.serialize_map(Some(self.0.len()))?;
        for (folder, book) in &self.0 {
            map.serialize_entry(folder, book)?;
        }
        map.end()
    }
}

impl<'de> Deserialize<'de> for FolderBooks {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        struct BooksVisitor;

        impl<'de> Visitor<'de> for BooksVisitor {
            type Value = FolderBooks;

            fn expecting(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
                formatter.write_str("an object of book ids by folder")
            }

            fn visit_unit<E: de::Error>(self) -> Result<FolderBooks, E> {
                Ok(FolderBooks::default())
            }

            fn visit_map<M: MapAccess<'de>>(self, mut map: M) -> Result<FolderBooks, M::Error> {
                let mut books = FolderBooks::default();
                while let Some((folder, book)) = map.next_entry::<String, Uuid>()? {
                    books.insert(&folder, book);
                }
                Ok(books)
            }
        }

        deserializer.deserialize_any(BooksVisitor)
    }
}

/// The uploader's settings: the recordings folder, which book each of its folders goes on, the default book, and the
/// options in S5. Everything stays on the device.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct UploaderSettings {
    pub recordings_folder: Option<String>,
    /// A choice here wins over matching by name.
    pub folder_books: FolderBooks,
    /// Where clips that match no book go; None asks each time (S3).
    pub default_book_id: Option<Uuid>,
    pub full_screen: FullScreenMode,
    /// Megabits a second for every upload; None for no limit.
    pub speed_limit_mbps: Option<u32>,
    /// Whether N1 ("clips are on the shelf") is shown.
    pub notify_shelved: bool,
    /// The name the device signed in with, shown in S5.
    pub device_name: Option<String>,
}

impl Default for UploaderSettings {
    fn default() -> Self {
        UploaderSettings {
            recordings_folder: None,
            folder_books: FolderBooks::default(),
            default_book_id: None,
            full_screen: FullScreenMode::Wait,
            speed_limit_mbps: None,
            notify_shelved: true,
            device_name: None,
        }
    }
}

impl UploaderSettings {
    /// The book saved for a folder, if one was chosen.
    pub fn saved_book_for(&self, folder: &str) -> Option<Uuid> {
        self.folder_books.get(folder)
    }
}

/// Reads and writes [`UploaderSettings`], writing through a temporary file so a crash never leaves half a file.
#[derive(Debug, Clone)]
pub struct SettingsStore {
    path: PathBuf,
}

impl SettingsStore {
    pub fn new(path: impl Into<PathBuf>) -> Self {
        SettingsStore { path: path.into() }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    /// The saved settings, or the defaults when there are none or the file can't be read.
    pub fn load(&self) -> UploaderSettings {
        std::fs::read_to_string(&self.path)
            .ok()
            .and_then(|text| serde_json::from_str::<Option<UploaderSettings>>(&text).ok().flatten())
            .unwrap_or_default()
    }

    pub fn save(&self, settings: &UploaderSettings) -> std::io::Result<()> {
        if let Some(directory) = self.path.parent() {
            std::fs::create_dir_all(directory)?;
        }
        let mut temporary = self.path.clone().into_os_string();
        temporary.push(".tmp");
        let temporary = PathBuf::from(temporary);
        let json = serde_json::to_string_pretty(settings).map_err(std::io::Error::other)?;
        std::fs::write(&temporary, json)?;
        std::fs::rename(&temporary, &self.path)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn settings_round_trip_and_match_folders_ignoring_case() {
        let directory = tempfile::tempdir().unwrap();
        let store = SettingsStore::new(directory.path().join("settings.json"));
        let book = Uuid::new_v4();
        let mut settings = UploaderSettings {
            recordings_folder: Some("C:\\Users\\you\\Videos\\NVIDIA".to_string()),
            default_book_id: Some(book),
            full_screen: FullScreenMode::UploadSlowly,
            speed_limit_mbps: Some(10),
            ..UploaderSettings::default()
        };
        settings.folder_books.insert("Marvel Rivals", book);
        store.save(&settings).unwrap();

        let loaded = store.load();

        assert_eq!(loaded, settings);
        assert_eq!(loaded.saved_book_for("marvel rivals"), Some(book));
        let json = std::fs::read_to_string(store.path()).unwrap();
        assert!(json.contains("\"fullScreen\": \"UploadSlowly\""), "{json}");
    }

    /// The file exactly as the .NET build writes it (System.Text.Json, indented, camelCase, its own escapes).
    #[test]
    fn a_settings_file_the_dotnet_build_wrote_reads_back() {
        let directory = tempfile::tempdir().unwrap();
        let store = SettingsStore::new(directory.path().join("settings.json"));
        std::fs::write(
            store.path(),
            r#"{
  "recordingsFolder": "C:\\Users\\you\\Videos\\NVIDIA",
  "folderBooks": {
    "Marvel Rivals": "0f8fad5b-d9cb-469f-a165-70867728950e",
    "Pok\u00E9mon": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
  },
  "defaultBookId": null,
  "fullScreen": "UploadSlowly",
  "speedLimitMbps": 5,
  "notifyShelved": false,
  "deviceName": "Gaming PC"
}"#,
        )
        .unwrap();

        let settings = store.load();

        assert_eq!(
            settings.recordings_folder.as_deref(),
            Some("C:\\Users\\you\\Videos\\NVIDIA")
        );
        assert_eq!(
            settings
                .saved_book_for("MARVEL RIVALS")
                .map(|id| id.to_string())
                .as_deref(),
            Some("0f8fad5b-d9cb-469f-a165-70867728950e")
        );
        assert!(settings.saved_book_for("Pokémon").is_some());
        assert_eq!(settings.default_book_id, None);
        assert_eq!(settings.full_screen, FullScreenMode::UploadSlowly);
        assert_eq!(settings.speed_limit_mbps, Some(5));
        assert!(!settings.notify_shelved);
        assert_eq!(settings.device_name.as_deref(), Some("Gaming PC"));

        let written = serde_json::to_value(&settings).unwrap();
        let keys: Vec<&str> = written.as_object().unwrap().keys().map(String::as_str).collect();
        assert_eq!(
            keys,
            [
                "defaultBookId",
                "deviceName",
                "folderBooks",
                "fullScreen",
                "notifyShelved",
                "recordingsFolder",
                "speedLimitMbps"
            ]
        );
        assert_eq!(written["defaultBookId"], serde_json::Value::Null);
    }

    #[test]
    fn a_missing_or_broken_file_gives_the_defaults() {
        let directory = tempfile::tempdir().unwrap();
        let store = SettingsStore::new(directory.path().join("settings.json"));
        assert_eq!(store.load(), UploaderSettings::default());
        std::fs::write(store.path(), "{ not json").unwrap();
        assert_eq!(store.load(), UploaderSettings::default());
        std::fs::write(store.path(), "{}").unwrap();
        assert!(store.load().notify_shelved);
        std::fs::write(store.path(), r#"{"fullScreen": 1, "folderBooks": null}"#).unwrap();
        assert_eq!(store.load().full_screen, FullScreenMode::UploadSlowly);
    }
}
