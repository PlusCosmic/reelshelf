//! The notifications N1–N5 the engine posts, with their words and buttons, so the app's toasts work nothing out.

use chrono::TimeZone;

use crate::copy::notifications as copy;
use crate::state::LibraryFullInfo;

/// One book's share of a "clips are on the shelf" notification.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShelvedBook {
    pub book_name: String,
    pub clips: i64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Notification {
    /// N1: clips reached the shelf since the last one, most first.
    ClipsShelved { books: Vec<ShelvedBook> },
    /// N2: a folder has no book; `folder` is its name ("" for the recordings folder itself).
    NeedsBook { folder: String, waiting: i64 },
    /// N3: preparing a clip answered that the library is full.
    LibraryFull(LibraryFullInfo),
    /// N4: the API answered 426, or asked for an upload protocol this version can't do.
    UpdateRequired,
    /// N5: the API refused the token.
    SignedOut,
}

/// What a notification's button does. The app turns each into a `reelshelf-app://` link.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NotificationAction {
    OpenLibrary,
    /// S3 for this folder.
    ChooseBook(String),
    NotNow,
    SeePlans,
    OpenStore,
    SignIn,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NotificationButton {
    pub label: String,
    pub action: NotificationAction,
}

/// A notification as it is shown.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NotificationWords {
    pub title: String,
    pub detail: String,
    pub buttons: Vec<NotificationButton>,
}

fn button(label: &str, action: NotificationAction) -> NotificationButton {
    NotificationButton {
        label: label.to_string(),
        action,
    }
}

impl Notification {
    /// The id screens.html gives it, N1 to N5.
    pub fn id(&self) -> &'static str {
        match self {
            Notification::ClipsShelved { .. } => "N1",
            Notification::NeedsBook { .. } => "N2",
            Notification::LibraryFull(_) => "N3",
            Notification::UpdateRequired => "N4",
            Notification::SignedOut => "N5",
        }
    }

    /// Its words, with any date in `zone`.
    pub fn words<Tz: TimeZone>(&self, zone: &Tz) -> NotificationWords {
        match self {
            Notification::ClipsShelved { books } => NotificationWords {
                title: copy::shelved_title(books.iter().map(|book| book.clips).sum()),
                detail: copy::shelved_books(books.iter().map(|book| (book.book_name.as_str(), book.clips))),
                buttons: vec![button(copy::OPEN_LIBRARY, NotificationAction::OpenLibrary)],
            },
            Notification::NeedsBook { folder, waiting } => NotificationWords {
                title: if folder.is_empty() {
                    copy::NEEDS_BOOK_ROOT_TITLE.to_string()
                } else {
                    copy::needs_book_title(folder)
                },
                detail: if folder.is_empty() {
                    copy::needs_book_root_detail(*waiting)
                } else {
                    copy::needs_book_detail(*waiting, &crate::copy::folder(folder))
                },
                buttons: vec![
                    button(copy::CHOOSE_BOOK, NotificationAction::ChooseBook(folder.clone())),
                    button(copy::NOT_NOW, NotificationAction::NotNow),
                ],
            },
            Notification::LibraryFull(full) => NotificationWords {
                title: copy::LIBRARY_FULL_TITLE.to_string(),
                detail: if full.monthly_limit {
                    let from = full.uploads_from.map(|from| from.with_timezone(zone));
                    copy::monthly_limit_detail(from.as_ref())
                } else {
                    copy::library_full_detail(full.clip_limit)
                },
                buttons: vec![button(copy::SEE_PLANS, NotificationAction::SeePlans)],
            },
            Notification::UpdateRequired => NotificationWords {
                title: copy::UPDATE_TITLE.to_string(),
                detail: copy::UPDATE_DETAIL.to_string(),
                buttons: vec![button(copy::OPEN_STORE, NotificationAction::OpenStore)],
            },
            Notification::SignedOut => NotificationWords {
                title: copy::SIGNED_OUT_TITLE.to_string(),
                detail: copy::SIGNED_OUT_DETAIL.to_string(),
                buttons: vec![button(copy::SIGN_IN, NotificationAction::SignIn)],
            },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Utc;

    #[test]
    fn each_notification_has_the_words_from_the_screens() {
        let shelved = Notification::ClipsShelved {
            books: vec![
                ShelvedBook {
                    book_name: "Apex Legends".into(),
                    clips: 2,
                },
                ShelvedBook {
                    book_name: "Valorant".into(),
                    clips: 1,
                },
            ],
        }
        .words(&Utc);
        assert_eq!(
            (
                shelved.title.as_str(),
                shelved.detail.as_str(),
                shelved.buttons[0].label.as_str()
            ),
            (
                "3 clips are on the shelf",
                "Apex Legends (2), Valorant",
                "Open your library"
            )
        );

        let needs = Notification::NeedsBook {
            folder: "Marvel Rivals".into(),
            waiting: 2,
        }
        .words(&Utc);
        assert_eq!(needs.title, "Which book do Marvel Rivals clips go on?");
        assert_eq!(needs.detail, "2 clips are waiting in Marvel Rivals\\");
        assert_eq!(
            needs.buttons.iter().map(|b| b.label.as_str()).collect::<Vec<_>>(),
            ["Choose a book", "Not now"]
        );
        let root = Notification::NeedsBook {
            folder: String::new(),
            waiting: 1,
        }
        .words(&Utc);
        assert_eq!(
            (root.title.as_str(), root.detail.as_str()),
            (
                "Which book do clips in your recordings folder go on?",
                "1 clip is waiting"
            )
        );

        let full = Notification::LibraryFull(LibraryFullInfo {
            clip_limit: Some(100),
            monthly_limit: false,
            uploads_from: None,
        })
        .words(&Utc);
        assert_eq!(
            full.detail,
            "Your plan keeps 100 clips, so new clips are waiting. They'll upload once there's room."
        );
        let monthly = Notification::LibraryFull(LibraryFullInfo {
            clip_limit: Some(1000),
            monthly_limit: true,
            uploads_from: Some(Utc.with_ymd_and_hms(2026, 11, 20, 9, 0, 0).unwrap()),
        })
        .words(&Utc);
        assert_eq!(
            monthly.detail,
            "You've added a lot of clips this month, so new clips are waiting. They'll upload from 20 Nov 2026."
        );

        let update = Notification::UpdateRequired.words(&Utc);
        assert_eq!(update.title, "Update Reelshelf to keep going");
        assert_eq!(update.buttons[0].label, "Open the Store");
        let signed_out = Notification::SignedOut.words(&Utc);
        assert_eq!(
            (
                signed_out.title.as_str(),
                signed_out.detail.as_str(),
                signed_out.buttons[0].label.as_str()
            ),
            (
                "This computer was signed out, so clips aren't uploading.",
                "Sign in again to carry on.",
                "Sign in"
            )
        );
    }
}
