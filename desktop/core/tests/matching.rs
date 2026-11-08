//! Which book a clip goes on, and when it was made: the frontend's `assignGame` and `deriveCreatedAt` cases
//! (`frontend/src/hooks/bulkUploadQueue.test.ts`), then the uploader's own rules. Ported from the .NET core's
//! `MatchingTests`.

use chrono::{DateTime, FixedOffset, TimeZone, Timelike, Utc};
use reelshelf_core::dates::{derive_created_at, parse_timestamp_from_source_path};
use reelshelf_core::matching::{
    Book, BookMatch, BookSource, assign_game, compact_for_match, normalize_for_match, resolve,
};
use uuid::Uuid;

struct Shelf {
    apex: Book,
    valorant: Book,
}

impl Shelf {
    fn new() -> Self {
        Shelf {
            apex: Book::new(Uuid::new_v4(), "Apex Legends", Some("apex-legends")),
            valorant: Book::new(Uuid::new_v4(), "Valorant", Some("valorant")),
        }
    }

    fn books(&self) -> Vec<Book> {
        vec![self.apex.clone(), self.valorant.clone()]
    }

    fn id_of(&self, name: Option<&str>) -> Option<Uuid> {
        name.and_then(|name| {
            self.books()
                .into_iter()
                .find(|book| book.name == name)
                .map(|book| book.id)
        })
    }
}

// Ported from "builds valid queue rows from files and rejects invalid files".
#[test]
fn assign_game_matches_a_game_in_the_file_name() {
    let shelf = Shelf::new();
    assert_eq!(
        assign_game("apex-legends_2026-05-10_2147.mp4", &shelf.books(), None),
        BookMatch::new(shelf.apex.id, BookSource::SourcePath)
    );
}

// Ported from "uses source path game assignment before game-page fallback context".
#[test]
fn assign_game_uses_the_source_path_before_the_fallback() {
    let shelf = Shelf::new();
    assert_eq!(
        assign_game(
            "captures/Apex Legends/clip.mp4",
            &shelf.books(),
            Some(shelf.valorant.id)
        ),
        BookMatch::new(shelf.apex.id, BookSource::SourcePath)
    );
    assert_eq!(
        assign_game("captures/unknown/clip.mp4", &shelf.books(), Some(shelf.valorant.id)),
        BookMatch::new(shelf.valorant.id, BookSource::Fallback)
    );
}

// Ported from "groups rows by game assignment ..." and "supports selection ...", whose rows are filed by path.
#[test]
fn assign_game_files_rows_by_their_path() {
    let shelf = Shelf::new();
    for (path, book) in [
        ("apex-legends/one.mp4", Some("Apex Legends")),
        ("valorant/two.mp4", Some("Valorant")),
        ("apex-one.mp4", None),
    ] {
        assert_eq!(
            assign_game(path, &shelf.books(), None).category_id,
            shelf.id_of(book),
            "{path}"
        );
    }
}

#[test]
fn assign_game_with_nothing_to_go_on_has_no_book() {
    let shelf = Shelf::new();
    assert_eq!(
        assign_game("captures/unknown/clip.mp4", &shelf.books(), None),
        BookMatch::NONE
    );
}

#[test]
fn a_folder_matches_a_book_ignoring_case_and_punctuation() {
    let shelf = Shelf::new();
    for (folder, book) in [
        ("Apex Legends", Some("Apex Legends")),
        ("VALORANT", Some("Valorant")),
        ("apex_legends", Some("Apex Legends")),
        ("ApexLegends", Some("Apex Legends")),
        ("Apex Legends (ranked)", Some("Apex Legends")),
        ("Marvel Rivals", None),
    ] {
        assert_eq!(
            resolve(folder, &shelf.books(), None, None).category_id,
            shelf.id_of(book),
            "{folder}"
        );
    }
}

#[test]
fn the_longest_matching_name_wins() {
    let shelf = Shelf::new();
    let apex_mobile = Book::new(Uuid::new_v4(), "Apex Legends Mobile", Some("apex-legends-mobile"));

    assert_eq!(
        resolve(
            "Apex Legends Mobile",
            &[shelf.apex.clone(), apex_mobile.clone()],
            None,
            None
        )
        .category_id,
        Some(apex_mobile.id)
    );
}

#[test]
fn a_saved_choice_wins_over_the_name_and_the_default_catches_the_rest() {
    let shelf = Shelf::new();
    let books = shelf.books();
    assert_eq!(
        resolve("Apex Legends", &books, Some(shelf.valorant.id), None),
        BookMatch::new(shelf.valorant.id, BookSource::Saved)
    );
    assert_eq!(
        resolve("Marvel Rivals", &books, None, Some(shelf.apex.id)),
        BookMatch::new(shelf.apex.id, BookSource::Fallback)
    );
    assert_eq!(resolve("Marvel Rivals", &books, None, None), BookMatch::NONE);
}

#[test]
fn a_file_in_the_recordings_folder_itself_goes_to_the_default_book_even_if_its_name_matches() {
    let shelf = Shelf::new();
    let books = shelf.books();
    assert_eq!(
        resolve("", &books, None, Some(shelf.valorant.id)),
        BookMatch::new(shelf.valorant.id, BookSource::Fallback)
    );
    assert_eq!(resolve("", &books, None, None), BookMatch::NONE);
}

#[test]
fn a_book_no_longer_on_the_shelf_counts_as_none() {
    let shelf = Shelf::new();
    let gone = Uuid::new_v4();

    assert_eq!(
        resolve("Apex Legends", &shelf.books(), Some(gone), None).category_id,
        Some(shelf.apex.id)
    );
    assert_eq!(
        resolve("Marvel Rivals", &shelf.books(), None, Some(gone)).category_id,
        None
    );
}

#[test]
fn normalize_for_match_is_the_frontends_rule() {
    assert_eq!(normalize_for_match("  Apex_Legends -- 2026!"), " apex legends 2026 ");
    assert_eq!(compact_for_match("Apex_Legends -- 2026!"), "apexlegends2026");
}

// Ported from "derives timestamps from metadata first and filename fallback second".
#[test]
fn derive_created_at_uses_the_last_write_time_first_and_the_file_name_second() {
    let zone = FixedOffset::east_opt(3600).unwrap();
    let metadata = zone.with_ymd_and_hms(2026, 5, 11, 6, 0, 0).unwrap();

    let from_metadata = derive_created_at(metadata.with_timezone(&Utc), "20260510_214700.mp4", &zone);
    let from_name = derive_created_at(DateTime::UNIX_EPOCH, "session_2026-05-10_2147.mp4", &zone);

    assert_eq!(from_metadata.with_timezone(&zone).hour(), 6);
    assert_eq!(from_name, zone.with_ymd_and_hms(2026, 5, 10, 21, 47, 0).unwrap());
}

#[test]
fn the_file_name_patterns_are_read_as_local_time() {
    let zone = FixedOffset::west_opt(5 * 3600).unwrap();
    for (name, (year, month, day, hour, minute, second)) in [
        ("apex-legends_2026-05-10_2147.mp4", (2026, 5, 10, 21, 47, 0)),
        ("clip 2026-05-10 21-47.mp4", (2026, 5, 10, 21, 47, 0)),
        ("20260510_214755.mp4", (2026, 5, 10, 21, 47, 55)),
        ("20260510214755.mp4", (2026, 5, 10, 21, 47, 55)),
    ] {
        assert_eq!(
            parse_timestamp_from_source_path(name, &zone),
            Some(
                zone.with_ymd_and_hms(year, month, day, hour, minute, second)
                    .unwrap()
                    .with_timezone(&Utc)
            ),
            "{name}"
        );
    }
}

#[test]
fn a_name_with_no_real_timestamp_falls_back_to_the_epoch() {
    for name in ["clip.mp4", "2026-02-31_2147.mp4", "2026-05-10_2547.mp4"] {
        assert_eq!(
            derive_created_at(DateTime::UNIX_EPOCH, name, &Utc),
            DateTime::UNIX_EPOCH,
            "{name}"
        );
    }
}
