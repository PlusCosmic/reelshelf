//! What the headless checks share: a runtime over a scene with the app's fonts, and the window specs as the app
//! builds them.

#![allow(dead_code)]

use reelshelf_app::fixtures::Scene;
use reelshelf_app::model::{Ui, WindowKind};
use reelshelf_app::{theme, ui};
use rune::text::TextSystem;
use rune::{App, Env, Runtime, Titlebar, WindowOptions};

/// A runtime for `ui` at `size`, light or dark, laid out and settled.
pub fn runtime(ui: Ui, size: (f32, f32), dark: bool) -> Runtime<Ui> {
    let text = TextSystem::new(theme::FONTS, theme::FAMILIES);
    let mut rt = Runtime::with_text(ui, ui::view, size, Env::new(dark, 1.0), text);
    rt.frame(0.0);
    settle(&mut rt);
    rt
}

/// Lets springs land.
pub fn settle(rt: &mut Runtime<Ui>) {
    for _ in 0..90 {
        rt.frame(1.0 / 60.0);
    }
}

/// A scene's runtime. The flyout takes its content's height, as its window does.
pub fn scene_runtime(scene: &Scene, dark: bool) -> Runtime<Ui> {
    let (ui, _) = scene.ui();
    let mut rt = runtime(ui, scene.size, dark);
    if scene.kind == WindowKind::Flyout {
        let height = flyout_height(&rt);
        rt.set_viewport((scene.size.0, height), 1.0);
        settle(&mut rt);
    }
    rt
}

/// The flyout's content height: the bottom of its menu, inside the card's 1-point border.
pub fn flyout_height(rt: &Runtime<Ui>) -> f32 {
    let menu = rt.presented("menu").expect("the flyout has its menu").0;
    (menu[1] + menu[3] + 1.0).ceil()
}

/// The scene's window as the app builds it, reading Rune's flags from `args` (`--snap`, `--tree`, `--theme`).
pub fn scene_app(scene: &Scene, args: Vec<String>) -> App<Ui> {
    let mut spec = ui::spec(scene.kind);
    spec.size = scene.size;
    if scene.kind == WindowKind::Flyout {
        // A headless snapshot keeps the size it starts at, so start it at the content's height.
        let rt = scene_runtime(scene, args.iter().any(|arg| arg == "dark"));
        spec.size.1 = flyout_height(&rt);
    }
    let (ui, _) = scene.ui();
    let app = App::new(spec).init(move || ui).args(args);
    match scene.kind {
        WindowKind::Flyout => app
            .window_options(WindowOptions {
                borderless: true,
                transparent: true,
                always_on_top: true,
                resizable: false,
                min_size: Some((0.0, 0.0)),
                ..WindowOptions::default()
            })
            .fit_content_height(),
        _ => app.titlebar(Titlebar::new(36.0)),
    }
}
