//! The platform parts the engine and the windows need, behind the core's traits ([`CredentialStore`], [`GameActivity`],
//! [`Notifier`], [`Startup`]) and the app's own [`Shell`].
//!
//! On Windows, [`Platform::native`] is Credential Manager, `SHQueryUserNotificationState`, toasts and MSIX's startup
//! task (`crate::win`). Everywhere else it is the stand-ins here: the token in memory, never a game, notifications
//! written to the log and no starting with the system. They let the app run, and be tested, on Linux. Both open links
//! and folders with the desktop's opener.

use std::process::Command;
use std::sync::{Arc, Mutex};

use reelshelf_core::notifications::Notification;
use reelshelf_core::platform::{CredentialStore, GameActivity, Notifier, Startup, StartupState};

/// Opens links in the browser and folders in the file manager.
pub trait Shell: Send + Sync {
    fn open(&self, target: &str);
}

/// Everything platform-specific the app is built from.
#[derive(Clone)]
pub struct Platform {
    pub credentials: Arc<dyn CredentialStore>,
    pub game: Arc<dyn GameActivity>,
    pub notifier: Arc<dyn Notifier>,
    pub startup: Arc<dyn Startup>,
    pub shell: Arc<dyn Shell>,
}

impl Platform {
    /// The platform the app runs on: Windows' own parts on Windows, the stand-ins elsewhere.
    pub fn native() -> Platform {
        #[cfg(windows)]
        {
            crate::win::platform()
        }
        #[cfg(not(windows))]
        {
            Platform::stand_ins()
        }
    }

    pub fn stand_ins() -> Platform {
        Platform {
            credentials: Arc::new(MemoryCredentials::default()),
            game: Arc::new(NeverInAGame),
            notifier: Arc::new(LoggedNotifier),
            startup: Arc::new(NoStartup),
            shell: Arc::new(SystemShell),
        }
    }
}

/// The token in memory, gone when the app quits.
#[derive(Default)]
pub struct MemoryCredentials(Mutex<Option<String>>);

impl MemoryCredentials {
    pub fn with_token(token: &str) -> Self {
        MemoryCredentials(Mutex::new(Some(token.to_owned())))
    }
}

impl CredentialStore for MemoryCredentials {
    fn read_token(&self) -> Option<String> {
        self.0.lock().unwrap_or_else(|poisoned| poisoned.into_inner()).clone()
    }

    fn write_token(&self, token: &str) {
        *self.0.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(token.to_owned());
    }

    fn delete_token(&self) {
        *self.0.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = None;
    }
}

/// No full-screen game, ever.
pub struct NeverInAGame;

impl GameActivity for NeverInAGame {
    fn is_game_running(&self) -> bool {
        false
    }
}

/// Writes each notification's id and title to the log instead of showing it.
pub struct LoggedNotifier;

impl Notifier for LoggedNotifier {
    fn post(&self, notification: Notification) {
        let words = notification.words(&chrono::Local);
        tracing::info!("Notification {}: {}", notification.id(), words.title);
    }
}

/// Starting with the system isn't available: it stays off.
pub struct NoStartup;

impl Startup for NoStartup {
    fn state(&self) -> StartupState {
        StartupState::Disabled
    }

    fn set_enabled(&self, _enabled: bool) -> StartupState {
        StartupState::Disabled
    }
}

/// The desktop's own opener: `explorer` on Windows, `open` on macOS, `xdg-open` elsewhere.
pub struct SystemShell;

impl Shell for SystemShell {
    fn open(&self, target: &str) {
        let program = if cfg!(windows) {
            "explorer"
        } else if cfg!(target_os = "macos") {
            "open"
        } else {
            "xdg-open"
        };
        if let Err(error) = Command::new(program).arg(target).spawn() {
            tracing::warn!("Couldn't open a link or folder: {error}");
        }
    }
}

/// The generic credential Credential Manager keeps the token under, the same one the .NET build used, so its sign-in
/// carries over.
pub const CREDENTIAL_TARGET: &str = "Reelshelf";

/// The token as the credential's blob: UTF-16LE with no terminator, as the .NET build wrote it
/// (`Encoding.Unicode.GetBytes`).
pub fn credential_blob(token: &str) -> Vec<u8> {
    token.encode_utf16().flat_map(u16::to_le_bytes).collect()
}

/// The token in a credential's blob; None when it is empty.
pub fn token_from_blob(blob: &[u8]) -> Option<String> {
    let units: Vec<u16> = blob
        .as_chunks::<2>()
        .0
        .iter()
        .map(|pair| u16::from_le_bytes(*pair))
        .collect();
    Some(String::from_utf16_lossy(&units)).filter(|token| !token.is_empty())
}

/// The computer's name, which S1 signs in as and S5 shows.
pub fn device_name() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .ok()
        .or_else(|| std::fs::read_to_string("/etc/hostname").ok())
        .map(|name| name.trim().to_owned())
        .filter(|name| !name.is_empty())
        .unwrap_or_else(|| "This PC".to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_stand_ins_keep_a_token_and_nothing_else() {
        let platform = Platform::stand_ins();
        assert_eq!(platform.credentials.read_token(), None);
        platform.credentials.write_token("rsd_token");
        assert_eq!(platform.credentials.read_token().as_deref(), Some("rsd_token"));
        platform.credentials.delete_token();
        assert_eq!(platform.credentials.read_token(), None);
        assert!(!platform.game.is_game_running());
        assert_eq!(platform.startup.set_enabled(true), StartupState::Disabled);
        platform.notifier.post(Notification::SignedOut);
        assert!(!device_name().is_empty());
    }

    #[test]
    fn the_credential_blob_is_the_tokens_utf16() {
        // What .NET's Encoding.Unicode.GetBytes("rsd_é") gives.
        let blob = credential_blob("rsd_é");
        assert_eq!(blob, [0x72, 0, 0x73, 0, 0x64, 0, 0x5f, 0, 0xe9, 0]);
        assert_eq!(token_from_blob(&blob).as_deref(), Some("rsd_é"));
        assert_eq!(token_from_blob(&[]), None);
    }
}
