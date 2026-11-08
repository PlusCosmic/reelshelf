//! N1–N5 as Windows toasts: the XML `ToastNotificationManager` shows, and the tag that replaces an earlier toast of
//! the same kind. Built here, on every platform, so the tests check it; `win::Toasts` shows it.
//!
//! The toast and each of its buttons open a `reelshelf-app://` link ([`links::app_link`]) with
//! `activationType="protocol"`. The package declares the scheme, so Windows hands the link to the running app (through
//! `rune::single_instance` to `App::on_open_url`), or starts it with the link when a toast is clicked from the
//! notification centre after the app has quit.

use chrono::TimeZone;
use reelshelf_core::notifications::Notification;

use crate::links;

/// Every toast's group, so they can be told apart from any other app's.
pub const GROUP: &str = "reelshelf";

/// A toast as Windows takes it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Toast {
    /// Replaces the shown toast with the same tag: one per kind, and N2 one per folder, so its count updates.
    pub tag: String,
    pub xml: String,
}

/// The toast for a notification, with any date in `zone`.
pub fn toast<Tz: TimeZone>(notification: &Notification, zone: &Tz) -> Toast {
    let words = notification.words(zone);
    let tag = match notification {
        Notification::ClipsShelved { .. } => "shelved".to_owned(),
        Notification::NeedsBook { folder, .. } => format!("book-{}", folder_tag(folder)),
        Notification::LibraryFull(_) => "full".to_owned(),
        Notification::UpdateRequired => "update".to_owned(),
        Notification::SignedOut => "signed-out".to_owned(),
    };
    let mut xml = String::from("<toast");
    // Clicking the toast itself does what its first button does.
    if let Some(first) = words.buttons.first() {
        xml.push_str(&format!(
            r#" launch="{}" activationType="protocol""#,
            escape(&links::app_link(&first.action))
        ));
    }
    xml.push_str(r#"><visual><binding template="ToastGeneric">"#);
    xml.push_str(&format!(
        "<text>{}</text><text>{}</text>",
        escape(&words.title),
        escape(&words.detail)
    ));
    xml.push_str("</binding></visual>");
    if !words.buttons.is_empty() {
        xml.push_str("<actions>");
        for button in &words.buttons {
            xml.push_str(&format!(
                r#"<action content="{}" arguments="{}" activationType="protocol"/>"#,
                escape(&button.label),
                escape(&links::app_link(&button.action))
            ));
        }
        xml.push_str("</actions>");
    }
    xml.push_str("</toast>");
    Toast { tag, xml }
}

/// A toast's tag is at most 64 characters, so a folder is tagged by a hash of its name (FNV-1a, ignoring case, as
/// Windows does for folder names).
fn folder_tag(folder: &str) -> String {
    let mut hash: u64 = 0xcbf2_9ce4_8422_2325;
    for byte in folder.to_lowercase().bytes() {
        hash ^= u64::from(byte);
        hash = hash.wrapping_mul(0x0000_0100_0000_01b3);
    }
    format!("{hash:016x}")
}

fn escape(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    for c in text.chars() {
        match c {
            '&' => out.push_str("&amp;"),
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '"' => out.push_str("&quot;"),
            '\'' => out.push_str("&apos;"),
            c => out.push(c),
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Utc;
    use reelshelf_core::notifications::{NotificationAction, ShelvedBook};

    #[test]
    fn each_button_opens_its_app_link() {
        let needs = toast(
            &Notification::NeedsBook {
                folder: "Tom & Jerry's".into(),
                waiting: 2,
            },
            &Utc,
        );
        let choose = escape(&links::app_link(&NotificationAction::ChooseBook(
            "Tom & Jerry's".into(),
        )));
        assert_eq!(choose, "reelshelf-app://choose-book?folder=Tom%20%26%20Jerry%27s");
        assert_eq!(
            needs.xml,
            format!(
                concat!(
                    r#"<toast launch="{choose}" activationType="protocol"><visual><binding template="ToastGeneric">"#,
                    "<text>Which book do Tom &amp; Jerry&apos;s clips go on?</text>",
                    r"<text>2 clips are waiting in Tom &amp; Jerry&apos;s\</text></binding></visual><actions>",
                    r#"<action content="Choose a book" arguments="{choose}" activationType="protocol"/>"#,
                    r#"<action content="Not now" arguments="reelshelf-app://not-now" activationType="protocol"/>"#,
                    "</actions></toast>"
                ),
                choose = choose
            )
        );
    }

    #[test]
    fn every_notification_has_a_toast_tagged_by_kind() {
        let shelved = toast(
            &Notification::ClipsShelved {
                books: vec![ShelvedBook {
                    book_name: "Apex Legends".into(),
                    clips: 2,
                }],
            },
            &Utc,
        );
        assert_eq!(shelved.tag, "shelved");
        assert!(shelved.xml.contains(r#"arguments="reelshelf-app://open-library""#));
        assert_eq!(toast(&Notification::UpdateRequired, &Utc).tag, "update");
        let signed_out = toast(&Notification::SignedOut, &Utc);
        assert_eq!(signed_out.tag, "signed-out");
        assert!(
            signed_out
                .xml
                .starts_with(r#"<toast launch="reelshelf-app://sign-in" activationType="protocol">"#)
        );

        let tag = |folder: &str| {
            toast(
                &Notification::NeedsBook {
                    folder: folder.into(),
                    waiting: 1,
                },
                &Utc,
            )
            .tag
        };
        assert_eq!(tag("Marvel Rivals"), tag("marvel rivals"));
        assert_ne!(tag("Marvel Rivals"), tag("Valorant"));
        assert_eq!(tag(&"x".repeat(500)).len(), "book-".len() + 16);
    }
}
