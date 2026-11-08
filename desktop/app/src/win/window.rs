//! The windows' native parts Rune leaves to the app: where the flyout opens, from the tray icon's own rectangle and
//! its display's work area, and S3's and S5's caption buttons (Close only).

use std::ffi::c_void;

use rune::raw_window_handle::RawWindowHandle;
use windows::Win32::Foundation::{HWND, POINT, RECT};
use windows::Win32::Graphics::Gdi::{GetMonitorInfoW, MONITOR_DEFAULTTONEAREST, MONITORINFO, MonitorFromPoint};
use windows::Win32::System::Threading::GetCurrentProcessId;
use windows::Win32::UI::HiDpi::{GetDpiForMonitor, MDT_EFFECTIVE_DPI};
use windows::Win32::UI::Shell::{NOTIFYICONIDENTIFIER, Shell_NotifyIconGetRect};
use windows::Win32::UI::WindowsAndMessaging::{
    FindWindowExW, GWL_STYLE, GetCursorPos, GetWindowLongPtrW, GetWindowThreadProcessId, HWND_MESSAGE,
    SWP_FRAMECHANGED, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE, SWP_NOZORDER, SetWindowLongPtrW, SetWindowPos,
    WS_MAXIMIZEBOX, WS_MINIMIZEBOX,
};
use windows::core::{PCWSTR, w};

use crate::tray::{Placement, Rect};

fn rect(from: RECT) -> Rect {
    Rect {
        left: from.left,
        top: from.top,
        right: from.right,
        bottom: from.bottom,
    }
}

/// The tray icon's rectangle on screen. Rune's tray is a message-only window of class `RuneTray` holding icon id 1
/// (`rune/src/tray/win32.rs`), so this finds this process's one and asks the shell where its icon is. None while
/// the icon is hidden in the overflow, or when it can't be found.
fn tray_icon_rect() -> Option<Rect> {
    // SAFETY: plain Win32 calls; the window handles are only passed back to Win32.
    unsafe {
        let process = GetCurrentProcessId();
        let mut after = None;
        let tray = loop {
            let found = FindWindowExW(Some(HWND_MESSAGE), after, w!("RuneTray"), PCWSTR::null()).ok()?;
            let mut owner = 0u32;
            GetWindowThreadProcessId(found, Some(&mut owner as *mut u32));
            if owner == process {
                break found;
            }
            after = Some(found);
        };
        let identifier = NOTIFYICONIDENTIFIER {
            cbSize: size_of::<NOTIFYICONIDENTIFIER>() as u32,
            hWnd: tray,
            uID: 1,
            ..NOTIFYICONIDENTIFIER::default()
        };
        Shell_NotifyIconGetRect(&identifier).ok().map(rect)
    }
}

/// Where the pointer is, which is on the icon just after a click.
fn pointer() -> Option<Rect> {
    let mut at = POINT::default();
    // SAFETY: writes one point.
    unsafe { GetCursorPos(&mut at) }.ok()?;
    Some(Rect {
        left: at.x,
        top: at.y,
        right: at.x + 1,
        bottom: at.y + 1,
    })
}

/// What the flyout is placed from: the tray icon (or the pointer), and its display's bounds, work area and scale.
pub fn flyout_placement() -> Option<Placement> {
    let anchor = tray_icon_rect().or_else(pointer)?;
    let center = POINT {
        x: (anchor.left + anchor.right) / 2,
        y: (anchor.top + anchor.bottom) / 2,
    };
    // SAFETY: plain Win32 calls writing into the structs given.
    unsafe {
        let monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        let mut info = MONITORINFO {
            cbSize: size_of::<MONITORINFO>() as u32,
            ..MONITORINFO::default()
        };
        if !GetMonitorInfoW(monitor, &mut info).as_bool() {
            return None;
        }
        let (mut dpi_x, mut dpi_y) = (96, 96);
        let _ = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &mut dpi_x, &mut dpi_y);
        Some(Placement {
            anchor,
            monitor: rect(info.rcMonitor),
            work: rect(info.rcWork),
            scale: f64::from(dpi_x.max(1)) / 96.0,
        })
    }
}

/// Takes the minimize and maximize boxes off a window, so its title bar has only Close (Rune draws the caption
/// buttons the window's style has).
pub fn close_button_only(window: &rune::WindowHandle) {
    let result = window.with_raw_handle(|handle| {
        let RawWindowHandle::Win32(win32) = handle.as_raw() else {
            return;
        };
        let hwnd = HWND(win32.hwnd.get() as *mut c_void);
        // SAFETY: a live window this thread owns, borrowed for the call.
        unsafe {
            let style = GetWindowLongPtrW(hwnd, GWL_STYLE);
            let boxes = (WS_MINIMIZEBOX.0 | WS_MAXIMIZEBOX.0) as isize;
            if style & boxes != 0 {
                SetWindowLongPtrW(hwnd, GWL_STYLE, style & !boxes);
                let _ = SetWindowPos(
                    hwnd,
                    None,
                    0,
                    0,
                    0,
                    0,
                    SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE,
                );
            }
        }
    });
    if let Err(error) = result {
        tracing::debug!("Couldn't set a window's caption buttons: {error}");
    }
}
