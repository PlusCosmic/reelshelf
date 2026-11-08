//! Which book a clip goes on. A port of the frontend's `assignGame` (`frontend/src/hooks/bulkUploadQueue.ts`) with its
//! `normalizeForMatch` rule: lower case, anything but letters and digits as one space, matched as a whole phrase or,
//! with the spaces taken out, anywhere; the longest matching name wins. The uploader matches the first folder below
//! the recordings folder ([`resolve`]).

use uuid::Uuid;

/// A book on the account's shelf, as matching and the windows need it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Book {
    pub id: Uuid,
    pub name: String,
    pub slug: Option<String>,
    /// `#rrggbb`, the book's cloth on the shelf.
    pub cloth_color: Option<String>,
}

impl Book {
    pub fn new(id: Uuid, name: impl Into<String>, slug: Option<&str>) -> Self {
        Book {
            id,
            name: name.into(),
            slug: slug.map(str::to_string),
            cloth_color: None,
        }
    }
}

/// How a file's book was found, as the frontend's `GameAssignmentSource`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BookSource {
    /// A name in the path matched the book.
    SourcePath,
    /// Nothing matched, so the fallback (the default book) was used.
    Fallback,
    /// The person chose the book for this folder.
    Saved,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BookMatch {
    pub category_id: Option<Uuid>,
    pub source: Option<BookSource>,
}

impl BookMatch {
    pub const NONE: BookMatch = BookMatch {
        category_id: None,
        source: None,
    };

    pub fn new(category_id: Uuid, source: BookSource) -> Self {
        BookMatch {
            category_id: Some(category_id),
            source: Some(source),
        }
    }
}

pub fn assign_game(source_path: &str, books: &[Book], fallback_category_id: Option<Uuid>) -> BookMatch {
    let normalized_path = normalize_for_match(source_path);
    let path_compact = compact_for_match(source_path);

    let mut best: Option<(&Book, usize)> = None;
    for book in books {
        let mut score = 0;
        for value in [Some(book.name.as_str()), book.slug.as_deref()].into_iter().flatten() {
            if value.is_empty() {
                continue;
            }

            let normalized_value = normalize_for_match(value);
            let compact_value = compact_for_match(value);
            let matched = contains_phrase(&normalized_path, &normalized_value)
                || (!compact_value.is_empty() && path_compact.contains(&compact_value));
            if matched {
                score = score.max(compact_value.len());
            }
        }

        if score > 0 && best.is_none_or(|(_, best_score)| score > best_score) {
            best = Some((book, score));
        }
    }

    if let Some((book, _)) = best {
        return BookMatch::new(book.id, BookSource::SourcePath);
    }

    match fallback_category_id {
        Some(fallback) => BookMatch::new(fallback, BookSource::Fallback),
        None => BookMatch::NONE,
    }
}

/// The book for a file in `folder` (the first folder below the recordings folder, "" for the folder itself): the book
/// saved for that folder, else the book its name matches, else the default book, else none, and the file waits as
/// `needs_book`. A saved or default book no longer on the shelf counts as none.
pub fn resolve(folder: &str, books: &[Book], saved_book: Option<Uuid>, default_book: Option<Uuid>) -> BookMatch {
    let on_shelf = |id: Uuid| books.iter().any(|book| book.id == id);

    if let Some(saved) = saved_book.filter(|&id| on_shelf(id)) {
        return BookMatch::new(saved, BookSource::Saved);
    }

    let fallback = default_book.filter(|&id| on_shelf(id));
    if folder.is_empty() {
        return match fallback {
            Some(book) => BookMatch::new(book, BookSource::Fallback),
            None => BookMatch::NONE,
        };
    }

    assign_game(folder, books, fallback)
}

/// The frontend's `normalizeForMatch`: lower case, every run of anything but `a-z0-9` as one space, trimmed, with a
/// space either side.
pub fn normalize_for_match(value: &str) -> String {
    let lower = value.to_lowercase();
    let mut normalized = String::with_capacity(lower.len() + 2);
    let mut in_gap = false;
    for c in lower.chars() {
        if c.is_ascii_lowercase() || c.is_ascii_digit() {
            if in_gap && !normalized.is_empty() {
                normalized.push(' ');
            }
            in_gap = false;
            normalized.push(c);
        } else {
            in_gap = true;
        }
    }
    format!(" {normalized} ")
}

/// The frontend's `compactForMatch`: lower case, only `a-z0-9`.
pub fn compact_for_match(value: &str) -> String {
    value
        .to_lowercase()
        .chars()
        .filter(|c| c.is_ascii_lowercase() || c.is_ascii_digit())
        .collect()
}

fn contains_phrase(normalized_path: &str, normalized_value: &str) -> bool {
    !normalized_value.trim().is_empty() && normalized_path.contains(normalized_value)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalize_for_match_is_the_frontends_rule() {
        assert_eq!(normalize_for_match("  Apex_Legends -- 2026!"), " apex legends 2026 ");
        assert_eq!(compact_for_match("Apex_Legends -- 2026!"), "apexlegends2026");
        assert_eq!(normalize_for_match("!!!"), "  ");
        assert_eq!(normalize_for_match("Pokémon Unite"), " pok mon unite ");
    }
}
