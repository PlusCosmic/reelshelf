//! The tray: its icon, which shows the state's badge, its flat menu, and where the flyout opens beside it.

use reelshelf_core::copy::tray as words;
use reelshelf_core::state::TrayBadge;
use rune::tray::{TrayIcon, TrayItem};

use crate::model::{Commands, FlyoutPage, Show};

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
        ids::RECENT => commands.show(Show::Flyout(FlyoutPage::Recent)),
        ids::LIBRARY => commands.open_link(crate::links::Link::Library),
        ids::SETTINGS => commands.show(Show::Flyout(FlyoutPage::Settings)),
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

/// A rectangle in physical pixels, as Win32 gives them: `right` and `bottom` are one past the last pixel.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Rect {
    pub left: i32,
    pub top: i32,
    pub right: i32,
    pub bottom: i32,
}

impl Rect {
    fn center(&self) -> (i32, i32) {
        ((self.left + self.right) / 2, (self.top + self.bottom) / 2)
    }
}

/// What the flyout is placed from: the tray icon (or where it was clicked), its display and the display's work area
/// (the display less the taskbar), in physical pixels, with the display's pixels per point.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Placement {
    pub anchor: Rect,
    pub monitor: Rect,
    pub work: Rect,
    pub scale: f64,
}

impl Placement {
    /// Without the tray's place (Linux, or Windows when it can't be found): a display's bottom right corner, above a
    /// 48-point taskbar. `display` is (x, y, width, height) in physical pixels.
    pub fn bottom_right(display: (i32, i32, u32, u32), scale: f64) -> Placement {
        let (x, y, w, h) = display;
        let (right, bottom) = (x + w as i32, y + h as i32);
        let monitor = Rect {
            left: x,
            top: y,
            right,
            bottom,
        };
        let taskbar = (48.0 * scale).round() as i32;
        Placement {
            anchor: Rect {
                left: right - 1,
                top: bottom - 1,
                right,
                bottom,
            },
            monitor,
            work: Rect {
                bottom: bottom - taskbar,
                ..monitor
            },
            scale,
        }
    }

    /// A size in points, in this display's pixels.
    pub fn pixels(&self, size: (f32, f32)) -> (u32, u32) {
        let scale = |points: f32| (f64::from(points) * self.scale).round().max(0.0) as u32;
        (scale(size.0), scale(size.1))
    }
}

/// The edge of the display the taskbar is on.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Edge {
    Bottom,
    Top,
    Left,
    Right,
}

fn taskbar_edge(placement: &Placement) -> Edge {
    let (monitor, work) = (placement.monitor, placement.work);
    if work.bottom < monitor.bottom {
        Edge::Bottom
    } else if work.top > monitor.top {
        Edge::Top
    } else if work.left > monitor.left {
        Edge::Left
    } else if work.right < monitor.right {
        Edge::Right
    } else {
        // An auto-hidden taskbar takes no work area: it is on the edge nearest the icon.
        let (x, y) = placement.anchor.center();
        [
            (monitor.bottom - y, Edge::Bottom),
            (y - monitor.top, Edge::Top),
            (x - monitor.left, Edge::Left),
            (monitor.right - x, Edge::Right),
        ]
        .into_iter()
        .min_by_key(|(distance, _)| *distance)
        .map_or(Edge::Bottom, |(_, edge)| edge)
    }
}

/// Where the flyout's top-left goes, in physical pixels, for its `size` in pixels: a margin from the taskbar, centred
/// on the tray icon and kept inside the work area, so it sits above a taskbar at the bottom, below one at the top,
/// and beside one at either side.
pub fn flyout_position(placement: &Placement, size: (u32, u32)) -> (i32, i32) {
    let margin = (12.0 * placement.scale).round() as i32;
    let (w, h) = (size.0 as i32, size.1 as i32);
    let work = placement.work;
    let (cx, cy) = placement.anchor.center();
    let (x, y) = match taskbar_edge(placement) {
        Edge::Bottom => (cx - w / 2, work.bottom - margin - h),
        Edge::Top => (cx - w / 2, work.top + margin),
        Edge::Left => (work.left + margin, cy - h / 2),
        Edge::Right => (work.right - margin - w, cy - h / 2),
    };
    let fit = |at: i32, low: i32, high: i32| if high < low { low } else { at.clamp(low, high) };
    (
        fit(x, work.left + margin, work.right - margin - w),
        fit(y, work.top + margin, work.bottom - margin - h),
    )
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
    fn without_the_tray_the_flyout_opens_above_the_taskbar_at_the_right() {
        let placement = Placement::bottom_right((0, 0, 1920, 1080), 1.0);
        let (x, y) = flyout_position(&placement, placement.pixels((388.0, 400.0)));
        assert_eq!((x, y), (1920 - 400, 1080 - 460));
        let placement = Placement::bottom_right((1920, 0, 2560, 1440), 2.0);
        let (x, _) = flyout_position(&placement, placement.pixels((388.0, 400.0)));
        assert_eq!(x, 1920 + 2560 - 800);
    }

    fn rect(left: i32, top: i32, right: i32, bottom: i32) -> Rect {
        Rect {
            left,
            top,
            right,
            bottom,
        }
    }

    #[test]
    fn the_flyout_opens_beside_the_tray_icon_whichever_edge_the_taskbar_is_on() {
        let monitor = rect(0, 0, 1920, 1080);
        let at = |anchor, work| {
            let placement = Placement {
                anchor,
                monitor,
                work,
                scale: 1.0,
            };
            flyout_position(&placement, (388, 400))
        };
        // Bottom: centred over the icon, above the taskbar.
        assert_eq!(
            at(rect(1000, 1040, 1024, 1064), rect(0, 0, 1920, 1032)),
            (1012 - 194, 1032 - 12 - 400)
        );
        // An icon near the corner keeps the flyout on screen.
        assert_eq!(
            at(rect(1880, 1040, 1904, 1064), rect(0, 0, 1920, 1032)),
            (1920 - 12 - 388, 620)
        );
        // Top: below the taskbar.
        assert_eq!(at(rect(1800, 10, 1824, 34), rect(0, 48, 1920, 1080)).1, 48 + 12);
        // Either side: beside it, level with the icon but inside the work area.
        assert_eq!(
            at(rect(10, 1000, 34, 1024), rect(62, 0, 1920, 1080)),
            (62 + 12, 1080 - 12 - 400)
        );
        assert_eq!(
            at(rect(1890, 500, 1914, 524), rect(0, 0, 1858, 1080)),
            (1858 - 12 - 388, 512 - 200)
        );
        // An auto-hidden taskbar: the edge nearest the icon.
        assert_eq!(at(rect(1000, 1060, 1024, 1080), monitor).1, 1080 - 12 - 400);
    }
}
