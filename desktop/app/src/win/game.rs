//! Whether a full-screen game is in the foreground, from `SHQueryUserNotificationState`, polled every 10 seconds:
//! `QUNS_RUNNING_D3D_FULL_SCREEN` (a Direct3D game in exclusive full screen) and `QUNS_BUSY` (a full-screen app, which
//! includes borderless games) count as a game.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use reelshelf_core::platform::GameActivity;
use windows::Win32::UI::Shell::{QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, SHQueryUserNotificationState};

pub const POLL_EVERY: Duration = Duration::from_secs(10);

pub struct FullScreenCheck {
    running: Arc<AtomicBool>,
}

impl FullScreenCheck {
    /// Checks now, then every [`POLL_EVERY`] on a thread of its own for as long as the app runs.
    pub fn start() -> Self {
        let running = Arc::new(AtomicBool::new(query()));
        let polled = running.clone();
        let spawned = std::thread::Builder::new()
            .name("reelshelf-full-screen".into())
            .spawn(move || {
                loop {
                    std::thread::sleep(POLL_EVERY);
                    polled.store(query(), Ordering::Relaxed);
                }
            });
        if let Err(error) = spawned {
            tracing::warn!("Couldn't start the full-screen check: {error}");
        }
        FullScreenCheck { running }
    }
}

impl GameActivity for FullScreenCheck {
    fn is_game_running(&self) -> bool {
        self.running.load(Ordering::Relaxed)
    }
}

fn query() -> bool {
    // SAFETY: a plain call that writes one enum value.
    unsafe { SHQueryUserNotificationState() }
        .is_ok_and(|state| state == QUNS_BUSY || state == QUNS_RUNNING_D3D_FULL_SCREEN)
}
