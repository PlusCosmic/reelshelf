//! The Windows integrations, behind the core's platform traits: the token in Credential Manager, the full-screen
//! check, N1–N5 as toasts, MSIX's startup task, and the windows' native parts Rune leaves to the app (where the flyout
//! opens, and which caption buttons S3 and S5 have).
//!
//! The toasts and the startup task need the package's identity, so an unpackaged build (`cargo run`) logs that they
//! are unavailable and carries on without them.

mod credentials;
mod game;
mod startup;
mod toasts;
mod window;

use std::sync::Arc;

pub use credentials::CredentialManager;
pub use game::FullScreenCheck;
pub use startup::{STARTUP_TASK_ID, StartupTask};
pub use toasts::Toasts;
pub use window::{close_button_only, flyout_placement};

use crate::platform::{Platform, SystemShell};

/// Windows' own parts.
pub fn platform() -> Platform {
    Platform {
        credentials: Arc::new(CredentialManager),
        game: Arc::new(FullScreenCheck::start()),
        notifier: Arc::new(Toasts),
        startup: Arc::new(StartupTask),
        shell: Arc::new(SystemShell),
    }
}

/// Runs a blocking WinRT call on a thread of its own, so it never waits on the UI thread's apartment.
fn off_the_ui_thread<T: Send>(call: impl FnOnce() -> T + Send) -> T {
    std::thread::scope(|scope| scope.spawn(call).join()).unwrap_or_else(|panic| std::panic::resume_unwind(panic))
}
