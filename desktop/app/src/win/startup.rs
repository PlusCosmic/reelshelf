//! Start with Windows, through the package's `windows.startupTask` (`desktop/package/Package.appxmanifest`, task id
//! [`STARTUP_TASK_ID`], the same as the .NET build's). It is off until the person turns it on, and they can turn it off
//! in Task Manager, after which only they can turn it on there.

use reelshelf_core::platform::{Startup, StartupState};
use windows::ApplicationModel::{StartupTask as Task, StartupTaskState};
use windows::core::HSTRING;

use super::off_the_ui_thread;

pub const STARTUP_TASK_ID: &str = "ReelshelfUploaderStartup";

pub struct StartupTask;

fn task() -> windows::core::Result<Task> {
    Task::GetAsync(&HSTRING::from(STARTUP_TASK_ID))?.join()
}

fn map(state: StartupTaskState) -> StartupState {
    match state {
        StartupTaskState::Enabled | StartupTaskState::EnabledByPolicy => StartupState::Enabled,
        StartupTaskState::DisabledByUser | StartupTaskState::DisabledByPolicy => StartupState::DisabledByUser,
        _ => StartupState::Disabled,
    }
}

/// An unpackaged build has no startup task, so it stays off.
fn or_off(result: windows::core::Result<StartupState>) -> StartupState {
    result.unwrap_or_else(|error| {
        tracing::info!("The startup task is unavailable: {error}");
        StartupState::Disabled
    })
}

impl Startup for StartupTask {
    fn state(&self) -> StartupState {
        or_off(off_the_ui_thread(|| task()?.State().map(map)))
    }

    fn set_enabled(&self, enabled: bool) -> StartupState {
        or_off(off_the_ui_thread(move || {
            let task = task()?;
            if enabled {
                task.RequestEnableAsync()?.join().map(map)
            } else {
                task.Disable()?;
                task.State().map(map)
            }
        }))
    }
}
