//! One row of S4, recent uploads: the clip's title, where it is, and its book, length, size and time.

use chrono::{DateTime, TimeZone, Utc};
use uuid::Uuid;

use crate::copy::recent as copy;
use crate::ledger::{LedgerEntry, LedgerState};
use crate::matching::Book;

#[derive(Debug, Clone, PartialEq)]
pub struct RecentUpload {
    pub entry: LedgerEntry,
    pub title: String,
    pub status: String,
    /// "Apex Legends · 0:41 · 74 MB", with the time once it is on the shelf.
    pub meta: String,
    pub cloth_color: Option<String>,
    /// A file that didn't upload gets "Try again".
    pub can_try_again: bool,
    pub book_slug: Option<String>,
}

impl RecentUpload {
    /// Clicking a clip on the shelf opens it on reelshelf.app.
    pub fn can_open(&self) -> bool {
        self.entry.state == LedgerState::Shelved && self.entry.clip_id.is_some() && self.book_slug.is_some()
    }

    pub fn clip_id(&self) -> Option<Uuid> {
        self.entry.clip_id
    }

    /// `current_percent` is how far the file uploading now has got; `zone` is where the time is shown.
    pub fn from_entry<Tz: TimeZone>(
        entry: &LedgerEntry,
        books: &[Book],
        now: DateTime<Utc>,
        current_percent: Option<u8>,
        zone: &Tz,
    ) -> RecentUpload {
        let book = books.iter().find(|book| Some(book.id) == entry.category_id);
        let retrying = entry.attempts > 0 && entry.next_attempt_at.is_some_and(|next| next > now);
        let status = match entry.state {
            LedgerState::Shelved => copy::ON_SHELF.to_string(),
            LedgerState::Duplicate => copy::DUPLICATE.to_string(),
            LedgerState::TooLong => copy::TOO_LONG.to_string(),
            LedgerState::Failed => copy::FAILED.to_string(),
            LedgerState::NeedsBook => copy::NEEDS_BOOK.to_string(),
            LedgerState::PlanFull => copy::LIBRARY_FULL.to_string(),
            _ if retrying => copy::RETRYING_SOON.to_string(),
            LedgerState::Uploading | LedgerState::Waiting if current_percent.is_some() => {
                copy::uploading(current_percent.unwrap_or(0))
            }
            _ => copy::WAITING.to_string(),
        };
        let shelved_at = entry
            .shelved_at
            .filter(|_| entry.state == LedgerState::Shelved)
            .map(|at| at.with_timezone(zone));
        RecentUpload {
            entry: entry.clone(),
            title: entry.title().to_string(),
            status,
            meta: copy::meta(
                book.map_or(copy::NO_BOOK, |book| book.name.as_str()),
                entry.length_seconds,
                entry.size,
                shelved_at.as_ref(),
            ),
            cloth_color: book.and_then(|book| book.cloth_color.clone()),
            can_try_again: entry.state == LedgerState::Failed,
            book_slug: book.and_then(|book| book.slug.clone()),
        }
    }
}
