//! The platform parts the engine and the windows need, behind the core's traits ([`CredentialStore`], [`GameActivity`],
//! [`Notifier`], [`Startup`]) and the app's own [`Shell`].
//!
//! These are the stand-ins: the token in memory, never a game, notifications written to the log, no starting with
//! the system, and links and folders opened with the desktop's opener. They let the app run, and be tested, on Linux.
//! The Windows build swaps in Credential Manager, `SHQueryUserNotificationState`, toasts and MSIX's startup task in
//! [`Platform::native`], behind `cfg(windows)`, and nothing else in the app changes.

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
    /// The platform the app runs on. Every platform gets the stand-ins until the Windows integrations land.
    pub fn native() -> Platform {
        Platform::stand_ins()
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
}
