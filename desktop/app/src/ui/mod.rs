//! The windows, drawn with Rune from the theme, with their words from `copy.rs`. One view function serves every
//! window; each window's [`Ui`] says which it is.

pub mod book_picker;
pub mod flyout;
pub mod icons;
pub mod parts;
pub mod quit;
pub mod recent;
pub mod settings;
pub mod setup;
pub mod sign_in;

use reelshelf_core::copy;
use rune::{AppSpec, Cx, El};

use crate::model::{SignInPhase, Ui, WindowKind};
use crate::theme::{self, Look};

/// The flyout's width; its height follows its content.
pub const FLYOUT_WIDTH: f32 = 388.0;

/// The look for the window being built, following the system's light or dark setting.
pub fn look(cx: &Cx<Ui>) -> &'static Look {
    theme::look(cx.system_dark())
}

/// Every window's view.
pub fn view(s: &Ui, cx: &mut Cx<Ui>) -> El {
    let l = look(cx);
    match s.kind {
        WindowKind::SignIn => sign_in::view(s, l),
        WindowKind::Setup => setup::view(s, l),
        WindowKind::Quit => quit::view(s, l),
        WindowKind::Flyout => flyout::view(s, l),
    }
}

/// The window's name in its titlebar and on the taskbar.
pub fn title(kind: WindowKind) -> &'static str {
    match kind {
        WindowKind::SignIn | WindowKind::Setup | WindowKind::Quit | WindowKind::Flyout => copy::APP_NAME,
    }
}

/// The window's size in points, as the canvas draws it. The flyout's height is where it starts; it follows its
/// content.
pub fn size(kind: WindowKind) -> (f32, f32) {
    match kind {
        WindowKind::SignIn => (520.0, 500.0),
        WindowKind::Setup => (580.0, 700.0),
        WindowKind::Quit => (440.0, 210.0),
        WindowKind::Flyout => (FLYOUT_WIDTH, 300.0),
    }
}

/// S1 is shorter once there's no code to show.
pub fn sign_in_size(phase: &SignInPhase) -> (f32, f32) {
    match phase {
        SignInPhase::Starting | SignInPhase::Waiting { .. } => size(WindowKind::SignIn),
        SignInPhase::Expired | SignInPhase::Denied | SignInPhase::Unreachable => (520.0, 400.0),
    }
}

/// The window's spec: its title, size, the theme's fonts and the shared view. Its state comes from the app (`init`).
pub fn spec(kind: WindowKind) -> AppSpec<Ui> {
    AppSpec {
        fonts: theme::FONTS,
        families: theme::FAMILIES,
        min_size: Some((0.0, 0.0)),
        ..AppSpec::new(title(kind), size(kind), no_state, view)
    }
}

/// Windows always start from the app's own state (`App::init`); this is never called.
fn no_state() -> Ui {
    unreachable!("every window's state comes from App::init")
}
