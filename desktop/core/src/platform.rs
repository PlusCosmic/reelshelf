//! The parts of the uploader that are Windows: the app implements these traits (Credential Manager, the full-screen
//! check, toasts, the startup task and the real clock), and tests fake them.

use std::time::Duration;

use chrono::{DateTime, FixedOffset, Local, Offset, TimeZone, Utc};

use crate::BoxFuture;
use crate::notifications::Notification;

/// Where the device token is kept: Windows Credential Manager in the app, memory in tests.
pub trait CredentialStore: Send + Sync {
    fn read_token(&self) -> Option<String>;

    fn write_token(&self, token: &str);

    fn delete_token(&self);
}

/// Whether a full-screen game is in the foreground (`SHQueryUserNotificationState` in the app).
pub trait GameActivity: Send + Sync {
    fn is_game_running(&self) -> bool;
}

/// Shows the notifications N1–N5 (Windows toasts in the app). Their words come from [`Notification::words`].
pub trait Notifier: Send + Sync {
    fn post(&self, notification: Notification);
}

/// A notifier that shows nothing.
pub struct SilentNotifier;

impl Notifier for SilentNotifier {
    fn post(&self, _notification: Notification) {}
}

/// Whether the app starts with Windows (MSIX's startup task in the app).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StartupState {
    Enabled,
    Disabled,
    /// Turned off in Task Manager's startup apps, which only the person can undo (S5 says so).
    DisabledByUser,
}

/// "Start with Windows" (S2's checkbox and S5's toggle).
pub trait Startup: Send + Sync {
    fn state(&self) -> StartupState;

    /// Turns starting with Windows on or off, and answers how it now is.
    fn set_enabled(&self, enabled: bool) -> StartupState;
}

/// The clock, so waits and timestamps can be driven by tests.
pub trait Clock: Send + Sync {
    fn now(&self) -> DateTime<Utc>;

    /// The local time zone's offset at an instant, for the times and dates the tray shows.
    fn local_offset(&self, at: DateTime<Utc>) -> FixedOffset;

    fn sleep(&self, duration: Duration) -> BoxFuture<'static, ()>;
}

/// The real clock, in the computer's time zone.
pub struct SystemClock;

impl Clock for SystemClock {
    fn now(&self) -> DateTime<Utc> {
        Utc::now()
    }

    fn local_offset(&self, at: DateTime<Utc>) -> FixedOffset {
        Local.offset_from_utc_datetime(&at.naive_utc()).fix()
    }

    fn sleep(&self, duration: Duration) -> BoxFuture<'static, ()> {
        Box::pin(async move {
            if !duration.is_zero() {
                tokio::time::sleep(duration).await;
            }
        })
    }
}
