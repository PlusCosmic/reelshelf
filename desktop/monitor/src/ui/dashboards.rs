//! The Dashboards section: Grafana's dashboards by folder, and an open one with its panels placed by their grid
//! positions (24 columns, 30-point rows with 8 points between, as Grafana lays them out), each drawn natively.

use rune::{El, ScrollSpec, Tx, col, el, row, text};
use rune_kit::shell::Toolbar;
use rune_kit::{Button, ButtonVariant, ControlSize, SegmentedControl};

use reelshelf_app::theme::{Look, ty};

use super::chart::{self, Chart, Paint};
use crate::copy;
use crate::grafana::{Dashboard, Panel, PanelKind, SearchHit, TimeRange, query};
use crate::state::{Hover, Load, Monitor, Opened};

const COLUMNS: f32 = 24.0;
const CELL: f32 = 30.0;
const GAP: f32 = 8.0;
const PAD: f32 = 10.0;
const TITLE: f32 = 18.0;

/// The page and its toolbar.
pub fn page(s: &Monitor, l: &Look, p: &Paint) -> (El, Toolbar) {
    if !s.connected() {
        return (
            super::message(l, copy::NOT_CONNECTED_TITLE, copy::NOT_CONNECTED),
            Toolbar::new().title(copy::DASHBOARDS),
        );
    }
    match &s.opened {
        None => (list(s, l), toolbar(s, l, copy::DASHBOARDS, false)),
        Some(o) => {
            let title = match &o.dashboard {
                Load::Ready(d) => d.title.as_str(),
                _ => copy::DASHBOARDS,
            };
            (opened(s, o, l, p), toolbar(s, l, title, true))
        }
    }
}

fn toolbar(s: &Monitor, l: &Look, title: &str, open: bool) -> Toolbar {
    let k = &l.kit;
    let mut actions = row().gap(8.0).center_y();
    if open {
        let labels: Vec<&str> = TimeRange::ALL.iter().map(|r| copy::range(*r)).collect();
        let picked = TimeRange::ALL.iter().position(|r| *r == s.range);
        actions = actions.child(
            SegmentedControl::new(&labels, picked)
                .label(copy::TIME_RANGE)
                .width(Some(192.0))
                .on_pick(|s: &mut Monitor, i: usize, _: &mut Tx| s.reload(TimeRange::ALL[i]))
                .build(k)
                .key("range"),
        );
    }
    actions = actions.child(
        Button::new(copy::REFRESH)
            .variant(ButtonVariant::Ghost)
            .control_size(ControlSize::Regular)
            .on_press(|s: &mut Monitor| s.reload(s.range))
            .build(k)
            .key("refresh"),
    );
    if open && let Some(url) = s.grafana_link(None) {
        actions = actions.child(
            Button::new(copy::OPEN_IN_GRAFANA)
                .variant(ButtonVariant::Outline)
                .control_size(ControlSize::Regular)
                .on_press_tx(move |_: &mut Monitor, tx: &mut Tx| {
                    let _ = tx.open_url(&url);
                })
                .build(k)
                .key("open-dashboard"),
        );
    }
    let mut t = Toolbar::new().title(title).trailing(actions);
    if open {
        t = t.leading(
            Button::new(copy::ALL_DASHBOARDS)
                .variant(ButtonVariant::Ghost)
                .control_size(ControlSize::Regular)
                .on_press(|s: &mut Monitor| s.close())
                .build(k)
                .key("back"),
        );
    }
    t
}

fn status(l: &Look, words: String, danger: bool) -> El {
    let ink = if danger { l.c.danger_ink } else { l.c.fg_soft };
    col()
        .w_full()
        .padding(32.0)
        .child(text(words).font(ty::BODY).color(ink).wrap())
}

fn list(s: &Monitor, l: &Look) -> El {
    let hits = match &s.list {
        Load::Loading => return status(l, copy::LOADING.to_owned(), false),
        Load::Failed(e) => return status(l, copy::failed(e), true),
        Load::Ready(hits) if hits.is_empty() => return status(l, copy::NO_DASHBOARDS.to_owned(), false),
        Load::Ready(hits) => hits,
    };
    let mut folders: Vec<(&str, Vec<&SearchHit>)> = Vec::new();
    for h in hits {
        let folder = h.folder_title.as_deref().unwrap_or(copy::GENERAL_FOLDER);
        match folders.iter_mut().find(|(f, _)| *f == folder) {
            Some((_, list)) => list.push(h),
            None => folders.push((folder, vec![h])),
        }
    }
    let mut page = col().w_full().padding(24.0).gap(20.0);
    for (folder, hits) in folders {
        let mut group = col()
            .w_full()
            .max_w(720.0)
            .gap(6.0)
            .child(text(folder.to_owned()).font(ty::LABEL).color(l.c.fg_soft));
        for h in hits {
            let uid = h.uid.clone();
            group = group.child(
                row()
                    .key(format!("dashboard-{}", h.uid))
                    .w_full()
                    .h(44.0)
                    .px(14.0)
                    .center_y()
                    .fill(l.c.surface)
                    .border(1.0, l.c.line)
                    .radius(l.radius_lg)
                    .hover(l.c.selected)
                    .role("button")
                    .label(h.title.clone())
                    .on_press(move |s: &mut Monitor| s.open(uid.clone()))
                    .child(text(h.title.clone()).font(ty::ROW_TITLE).color(l.c.fg)),
            );
        }
        page = page.child(group);
    }
    col()
        .key("list")
        .w_full()
        .h_full()
        .scroll(ScrollSpec { stick_bottom: false })
        .child(page)
}

fn opened(s: &Monitor, o: &Opened, l: &Look, p: &Paint) -> El {
    let d = match &o.dashboard {
        Load::Loading => return status(l, copy::LOADING.to_owned(), false),
        Load::Failed(e) => return status(l, copy::failed(e), true),
        Load::Ready(d) => d,
    };
    let width = s.grid_width.max(240.0);
    let column = (width + GAP) / COLUMNS;
    let height = d.grid_height() as f32 * (CELL + GAP);
    let mut grid = el()
        .key("grid")
        .w_full()
        .h(height)
        .on_layout_tx(|s: &mut Monitor, r: [f32; 4], tx: &mut Tx| {
            if (s.grid_width - r[2]).abs() > 0.5 {
                s.grid_width = r[2];
            } else {
                tx.live_only();
            }
        });
    for panel in &d.panels {
        let x = panel.grid.x as f32 * column;
        let y = panel.grid.y as f32 * (CELL + GAP);
        let w = (panel.grid.w as f32 * column - GAP).max(40.0);
        let h = panel.grid.h as f32 * CELL + (panel.grid.h.saturating_sub(1)) as f32 * GAP;
        grid = grid.child(match panel.kind {
            PanelKind::Row => text(panel.title.clone())
                .font(
                    rune::TextStyle::new(17.0, 500.0)
                        .family(reelshelf_app::theme::DISPLAY)
                        .lh(20.0),
                )
                .color(l.c.fg)
                .absolute(x, y + 8.0)
                .key(format!("row-{}", panel.id)),
            _ => card(s, o, d, panel, (w, h), l, p).absolute(x, y),
        });
    }
    col()
        .key("dashboard")
        .w_full()
        .h_full()
        .scroll(ScrollSpec { stick_bottom: false })
        .child(col().w_full().padding(16.0).child(grid))
}

fn card(s: &Monitor, o: &Opened, d: &Dashboard, panel: &Panel, (w, h): (f32, f32), l: &Look, p: &Paint) -> El {
    let body = (w - 2.0 * PAD, (h - 2.0 * PAD - TITLE - 6.0).max(10.0));
    let key = format!("panel-{}", panel.id);
    let inner = if query::is_drawn(panel) {
        match o.data.get(&panel.id) {
            None | Some(Load::Loading) => note(copy::LOADING.to_owned(), l.c.fg_soft),
            Some(Load::Failed(e)) => note(copy::failed(e), l.c.danger_ink),
            Some(Load::Ready(data)) if data.series.is_empty() => match &data.error {
                Some(e) => note(copy::failed(e), l.c.danger_ink),
                None => note(copy::NO_DATA.to_owned(), l.c.fg_soft),
            },
            Some(Load::Ready(data)) => match panel.kind {
                PanelKind::Stat => chart::stat(
                    &format!("{key}-stat"),
                    &data.series,
                    &panel.reduce,
                    panel.unit.as_deref(),
                    panel.decimals,
                    body,
                    p,
                ),
                _ => {
                    let id = panel.id;
                    let hover = s.hover.filter(|h| h.panel == id).map(|h| h.x);
                    chart::time_series(
                        &Chart {
                            key: format!("{key}-chart"),
                            series: &data.series,
                            unit: panel.unit.as_deref(),
                            decimals: panel.decimals,
                            min: panel.min,
                            max: panel.max,
                            span: o.span,
                            size: body,
                            hover,
                            tz: &s.tz,
                        },
                        p,
                        move |s: &mut Monitor, x: Option<f32>| {
                            s.hover = x.map(|x| Hover { panel: id, x });
                        },
                    )
                }
            },
        }
    } else {
        not_drawn(s, d, panel, l)
    };
    col()
        .key(key)
        .size(w, h)
        .padding(PAD)
        .gap(6.0)
        .fill(l.c.surface)
        .border(1.0, l.c.line)
        .radius(l.radius_lg)
        .clip_rounded(l.radius_lg)
        .child(text(panel.title.clone()).font(ty::ROW_STRONG).color(l.c.fg).h(TITLE))
        .child(inner)
}

fn note(words: String, ink: rune::Color) -> El {
    text(words).font(ty::META).color(ink).wrap()
}

/// The card for a panel the app doesn't draw: what it is, and a link to it in Grafana.
fn not_drawn(s: &Monitor, d: &Dashboard, panel: &Panel, l: &Look) -> El {
    let kind = match &panel.kind {
        PanelKind::Other(kind) => kind.clone(),
        _ => panel
            .datasource
            .as_ref()
            .and_then(|ds| ds.kind.clone())
            .unwrap_or_default(),
    };
    let mut out = col().gap(8.0).child(note(copy::not_drawn(&kind), l.c.fg_soft));
    if let Some(url) = s.grafana_link(Some(panel.id)).filter(|_| !d.url.is_empty()) {
        out = out.child(
            Button::new(copy::OPEN_IN_GRAFANA)
                .variant(ButtonVariant::Outline)
                .control_size(ControlSize::Small)
                .on_press_tx(move |_: &mut Monitor, tx: &mut Tx| {
                    let _ = tx.open_url(&url);
                })
                .build(&l.kit)
                .key(format!("open-panel-{}", panel.id)),
        );
    }
    out
}
