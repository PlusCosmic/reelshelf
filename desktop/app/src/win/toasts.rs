//! N1–N5 as toasts through `ToastNotificationManager`, built by [`crate::toast`]. The notifier is the package's own
//! (its AUMID, as the .NET build's was), which a packaged app gets without registering anything. Windows itself holds
//! toasts back while a game is full screen.

use std::sync::atomic::{AtomicBool, Ordering};

use reelshelf_core::notifications::Notification;
use reelshelf_core::platform::Notifier;
use windows::Data::Xml::Dom::XmlDocument;
use windows::UI::Notifications::{ToastNotification, ToastNotificationManager};
use windows::core::HSTRING;

use crate::toast::{GROUP, toast};

pub struct Toasts;

/// Said once, so an unpackaged build doesn't log it for every notification.
static UNAVAILABLE: AtomicBool = AtomicBool::new(false);

fn show(notification: &Notification) -> windows::core::Result<()> {
    let toast = toast(notification, &chrono::Local);
    let document = XmlDocument::new()?;
    document.LoadXml(&HSTRING::from(toast.xml))?;
    let shown = ToastNotification::CreateToastNotification(&document)?;
    shown.SetTag(&HSTRING::from(toast.tag))?;
    shown.SetGroup(&HSTRING::from(GROUP))?;
    ToastNotificationManager::CreateToastNotifier()?.Show(&shown)
}

impl Notifier for Toasts {
    fn post(&self, notification: Notification) {
        if let Err(error) = show(&notification) {
            if !UNAVAILABLE.swap(true, Ordering::Relaxed) {
                tracing::warn!("Notification {} couldn't be shown: {error}", notification.id());
            } else {
                tracing::debug!("Notification {} couldn't be shown: {error}", notification.id());
            }
        }
    }
}
