//! The tray's state: which of T1–T8 applies, by the priority in screens.html (T8 > T6 > T7 > T5 > T4 > T3 > T2 > T1),
//! with the menu's two lines, its main action and the badge. The engine fills in [`StateInputs`] and
//! [`UploaderState::from_inputs`] alone decides what the tray shows, so the app never works out state itself.

use chrono::{DateTime, TimeZone, Utc};

use crate::copy::tray;

/// The tray states T1–T8 of screens.html.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UploaderStateKind {
    Watching,
    Uploading,
    GameRunning,
    Paused,
    NeedsBook,
    LibraryFull,
    Unreachable,
    UpdateRequired,
}

/// The tray icon's badge: none while watching, green while uploading, amber when it needs you, red when it's stopped.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TrayBadge {
    None,
    Green,
    Amber,
    Red,
}

/// What the tray's main action (the menu's first item) does.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TrayAction {
    ShowRecent,
    UploadNow,
    Resume,
    ChooseBook,
    SeePlans,
    TryNow,
    OpenStore,
}

/// How long a pause lasts (the tray's "Pause for…").
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PauseLength {
    OneHour,
    UntilTomorrow,
    UntilResumed,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PauseInfo {
    pub length: PauseLength,
    pub until: Option<DateTime<Utc>>,
}

/// Why new clips wait: the plan's clip limit, or its limit on clips added in 30 days.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct LibraryFullInfo {
    pub clip_limit: Option<i64>,
    pub monthly_limit: bool,
    pub uploads_from: Option<DateTime<Utc>>,
}

/// Everything the tray's state is worked out from.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct StateInputs {
    pub update_required: bool,
    /// The plan refused a clip; None while it has room.
    pub library_full: Option<LibraryFullInfo>,
    /// When the API will be tried again after it couldn't be reached; None while it can.
    pub unreachable_retry_at: Option<DateTime<Utc>>,
    /// The first folder waiting for a book and how many clips wait in it.
    pub needs_book: Option<(String, i64)>,
    pub paused: Option<PauseInfo>,
    /// A full-screen game is running and uploads wait for it.
    pub game_holds_uploads: bool,
    /// Clips with a book, waiting or uploading.
    pub waiting: i64,
    /// The clip uploading now, and how far it has got.
    pub current: Option<(String, u8)>,
    /// The recordings folder as the tray names it, such as `Videos\NVIDIA`.
    pub folder_name: String,
    pub shelved_today: i64,
}

/// The tray's state, with the menu's two lines.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UploaderState {
    pub kind: UploaderStateKind,
    /// The menu's first line, which the tooltip repeats.
    pub title: String,
    /// The menu's second line: what's happening.
    pub detail: String,
    pub action_label: String,
    pub action: TrayAction,
    pub badge: TrayBadge,
    /// T2's progress bar.
    pub percent: Option<u8>,
}

impl UploaderState {
    /// The screen id, T1 to T8.
    pub fn id(&self) -> &'static str {
        match self.kind {
            UploaderStateKind::Watching => "T1",
            UploaderStateKind::Uploading => "T2",
            UploaderStateKind::GameRunning => "T3",
            UploaderStateKind::Paused => "T4",
            UploaderStateKind::NeedsBook => "T5",
            UploaderStateKind::LibraryFull => "T6",
            UploaderStateKind::Unreachable => "T7",
            UploaderStateKind::UpdateRequired => "T8",
        }
    }

    /// The tooltip repeats the menu's first line.
    pub fn tooltip(&self) -> &str {
        &self.title
    }

    fn new(
        kind: UploaderStateKind,
        title: impl Into<String>,
        detail: impl Into<String>,
        action_label: &str,
        action: TrayAction,
        badge: TrayBadge,
    ) -> Self {
        UploaderState {
            kind,
            title: title.into(),
            detail: detail.into(),
            action_label: action_label.to_string(),
            action,
            badge,
            percent: None,
        }
    }

    /// Times and dates are shown in `zone`, the computer's time zone in the app.
    pub fn from_inputs<Tz: TimeZone>(inputs: &StateInputs, now: DateTime<Utc>, zone: &Tz) -> UploaderState {
        use UploaderStateKind as Kind;

        if inputs.update_required {
            return UploaderState::new(
                Kind::UpdateRequired,
                tray::update_required::TITLE,
                tray::update_required::DETAIL,
                tray::update_required::ACTION,
                TrayAction::OpenStore,
                TrayBadge::Red,
            );
        }

        if let Some(full) = &inputs.library_full {
            let (title, detail) = if full.monthly_limit {
                let from = full.uploads_from.map(|from| from.with_timezone(zone));
                (
                    tray::library_full::MONTHLY_TITLE,
                    tray::library_full::monthly_detail(from.as_ref(), inputs.waiting),
                )
            } else {
                (
                    tray::library_full::TITLE,
                    tray::library_full::detail(full.clip_limit, inputs.waiting),
                )
            };
            return UploaderState::new(
                Kind::LibraryFull,
                title,
                detail,
                tray::library_full::ACTION,
                TrayAction::SeePlans,
                TrayBadge::Amber,
            );
        }

        if let Some(retry_at) = inputs.unreachable_retry_at {
            let wait = (retry_at - now).to_std().unwrap_or_default();
            return UploaderState::new(
                Kind::Unreachable,
                tray::unreachable::TITLE,
                tray::unreachable::detail(wait, inputs.waiting),
                tray::unreachable::ACTION,
                TrayAction::TryNow,
                TrayBadge::Red,
            );
        }

        if let Some((folder, waiting)) = &inputs.needs_book {
            let title = if folder.is_empty() {
                tray::needs_book::ROOT_TITLE.to_string()
            } else {
                tray::needs_book::title(folder)
            };
            return UploaderState::new(
                Kind::NeedsBook,
                title,
                tray::needs_book::detail(*waiting),
                tray::needs_book::ACTION,
                TrayAction::ChooseBook,
                TrayBadge::Amber,
            );
        }

        if let Some(paused) = &inputs.paused {
            let title = match paused.length {
                PauseLength::UntilResumed => tray::paused::UNTIL_RESUMED.to_string(),
                PauseLength::UntilTomorrow => tray::paused::UNTIL_TOMORROW.to_string(),
                PauseLength::OneHour => tray::paused::until(&paused.until.unwrap_or(now).with_timezone(zone)),
            };
            return UploaderState::new(
                Kind::Paused,
                title,
                tray::paused::DETAIL,
                tray::paused::ACTION,
                TrayAction::Resume,
                TrayBadge::Amber,
            );
        }

        if inputs.game_holds_uploads && inputs.waiting > 0 {
            return UploaderState::new(
                Kind::GameRunning,
                tray::game_running::TITLE,
                tray::game_running::detail(inputs.waiting),
                tray::game_running::ACTION,
                TrayAction::UploadNow,
                TrayBadge::Green,
            );
        }

        if let Some((current, percent)) = &inputs.current {
            let mut state = UploaderState::new(
                Kind::Uploading,
                tray::uploading::title(inputs.waiting.max(1)),
                tray::uploading::detail(current, *percent),
                tray::uploading::ACTION,
                TrayAction::ShowRecent,
                TrayBadge::Green,
            );
            state.percent = Some(*percent);
            return state;
        }

        UploaderState::new(
            Kind::Watching,
            tray::watching::TITLE,
            tray::watching::detail(&inputs.folder_name, inputs.shelved_today),
            tray::watching::ACTION,
            TrayAction::ShowRecent,
            TrayBadge::None,
        )
    }
}

/// The recordings folder as the tray names it: its last two folders, `Videos\NVIDIA`.
pub fn short_folder_name(path: Option<&str>) -> String {
    let Some(path) = path.filter(|path| !path.trim().is_empty()) else {
        return String::new();
    };
    let parts: Vec<&str> = path.split(['\\', '/']).filter(|part| !part.is_empty()).collect();
    parts[parts.len().saturating_sub(2)..].join("\\")
}
