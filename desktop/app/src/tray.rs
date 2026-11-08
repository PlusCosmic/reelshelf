//! The tray: its icon, which shows the state's badge, its flat menu, and where the flyout opens above it.

use reelshelf_core::copy::tray as words;
use reelshelf_core::state::TrayBadge;
use rune::tray::{TrayIcon, TrayItem};

use crate::model::{Commands, Show};

/// The icon's side in pixels.
pub const ICON_SIZE: u32 = 32;

/// The flat menu's ids.
pub mod ids {
    pub const RECENT: &str = "recent";
    pub const LIBRARY: &str = "library";
    pub const SETTINGS: &str = "settings";
    pub const QUIT: &str = "quit";
}

/// The secondary click's menu: Show recent uploads, Open your library, Settings and Quit.
pub fn menu() -> Vec<TrayItem> {
    vec![
        TrayItem::action(ids::RECENT, words::watching::ACTION),
        TrayItem::action(ids::LIBRARY, words::OPEN_LIBRARY),
        TrayItem::action(ids::SETTINGS, words::SETTINGS),
        TrayItem::Separator,
        TrayItem::action(ids::QUIT, words::QUIT),
    ]
}

/// Does what a menu item says.
pub fn on_menu(id: &str, commands: &dyn Commands) {
    match id {
        ids::RECENT => commands.show(Show::Recent),
        ids::LIBRARY => commands.open_link(crate::links::Link::Library),
        ids::SETTINGS => commands.show(Show::Settings),
        ids::QUIT => commands.quit(false),
        other => tracing::warn!("Unknown tray menu item {other:?}"),
    }
}

/// The badge's colour as `[r, g, b]`: the theme's fill, warning and danger.
fn badge_rgb(badge: TrayBadge) -> Option<[u8; 3]> {
    let look = crate::theme::look(false);
    let color = match badge {
        TrayBadge::None => return None,
        TrayBadge::Green => look.c.fill,
        TrayBadge::Amber => look.c.warning,
        TrayBadge::Red => look.c.danger,
    };
    Some(rgb(color))
}

fn rgb(color: rune::Color) -> [u8; 3] {
    let hex = color.hex();
    let byte = |at: usize| u8::from_str_radix(hex.get(at..at + 2).unwrap_or("00"), 16).unwrap_or(0);
    [byte(1), byte(3), byte(5)]
}

/// The mark at 32 × 32 (the logo's three spines at twice the canvas's 16), with the state's badge in the corner.
pub fn icon(badge: TrayBadge) -> TrayIcon {
    let look = crate::theme::look(false);
    let spines = look.logo.map(rgb);
    let size = ICON_SIZE as usize;
    let mut pixels = vec![0u8; size * size * 4];
    // Each spine as (x, y, w, h) in the logo's 16-point box; the third leans 8° about its centre.
    let rects = [(1.0, 4.0, 3.5, 11.0), (5.5, 2.0, 3.5, 13.0), (10.0, 5.0, 3.5, 10.0)];
    let lean = (-8f32).to_radians();
    let samples = 4;
    for py in 0..size {
        for px in 0..size {
            let mut cover = [0u32; 3];
            for sy in 0..samples {
                for sx in 0..samples {
                    let x = (px as f32 + (sx as f32 + 0.5) / samples as f32) / 2.0;
                    let y = (py as f32 + (sy as f32 + 0.5) / samples as f32) / 2.0;
                    for (index, &(rx, ry, rw, rh)) in rects.iter().enumerate() {
                        let (mut qx, mut qy) = (x, y);
                        if index == 2 {
                            let (cx, cy) = (rx + rw / 2.0, ry + rh / 2.0);
                            let (dx, dy) = (x - cx, y - cy);
                            qx = cx + dx * lean.cos() + dy * lean.sin();
                            qy = cy - dx * lean.sin() + dy * lean.cos();
                        }
                        if qx >= rx && qx < rx + rw && qy >= ry && qy < ry + rh {
                            cover[index] += 1;
                            break;
                        }
                    }
                }
            }
            let total = (samples * samples) as u32;
            let at = (py * size + px) * 4;
            if let Some(index) = (0..3).max_by_key(|&i| cover[i]).filter(|&i| cover[i] > 0) {
                let alpha: u32 = cover.iter().sum::<u32>().min(total) * 255 / total;
                pixels[at..at + 3].copy_from_slice(&spines[index]);
                pixels[at + 3] = alpha as u8;
            }
        }
    }
    if let Some(color) = badge_rgb(badge) {
        // A ring cut out of the mark, then the dot, bottom right.
        let (cx, cy) = (24.5f32, 24.5f32);
        for py in 0..size {
            for px in 0..size {
                let distance = ((px as f32 + 0.5 - cx).powi(2) + (py as f32 + 0.5 - cy).powi(2)).sqrt();
                let at = (py * size + px) * 4;
                if distance <= 6.5 {
                    pixels[at..at + 3].copy_from_slice(&color);
                    pixels[at + 3] = 255;
                } else if distance <= 8.5 {
                    pixels[at + 3] = 0;
                }
            }
        }
    }
    TrayIcon::rgba(ICON_SIZE, ICON_SIZE, pixels)
}

/// Where the flyout's top-left goes, in physical pixels: above the taskbar at the bottom right of the display, where
/// the tray is. `display` is (x, y, width, height) in physical pixels and `scale` its pixels per point.
pub fn flyout_position(display: (i32, i32, u32, u32), scale: f64, size: (f32, f32)) -> (i32, i32) {
    const MARGIN: f64 = 12.0;
    const TASKBAR: f64 = 48.0;
    let (x, y, w, h) = display;
    let right = f64::from(x) + f64::from(w) - (f64::from(size.0) + MARGIN) * scale;
    let top = f64::from(y) + f64::from(h) - (f64::from(size.1) + TASKBAR + MARGIN) * scale;
    (right.round() as i32, top.round() as i32)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_icon_is_the_mark_with_the_states_badge() {
        let plain = icon(TrayBadge::None);
        let red = icon(TrayBadge::Red);
        assert_ne!(plain, red);
        assert_ne!(icon(TrayBadge::Green), red);
        assert_ne!(icon(TrayBadge::Amber), red);
        assert_eq!(menu().len(), 5);
    }

    #[test]
    fn the_flyout_opens_above_the_taskbar_at_the_right() {
        let (x, y) = flyout_position((0, 0, 1920, 1080), 1.0, (388.0, 400.0));
        assert_eq!((x, y), (1920 - 400, 1080 - 460));
        let (x, _) = flyout_position((1920, 0, 2560, 1440), 2.0, (388.0, 400.0));
        assert_eq!(x, 1920 + 2560 - 800);
    }
}
