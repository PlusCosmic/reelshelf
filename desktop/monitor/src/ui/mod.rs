//! The window: rune-kit's split view with a sidebar of sections, a toolbar, and the section's page. Colours, type and
//! the kit's components come from the uploader's theme (the site's tokens), light or dark as the system is.

pub mod chart;
pub mod dashboards;
pub mod icons;

use rune::{AppSpec, Chrome, Cx, El, Icon, KeyEvent, Tx, col, text};
use rune_kit::shell::{self, Sidebar, SidebarRow, SidebarToggle, SplitView, Toolbar};
use rune_kit::{ControlSize, Kit};

use reelshelf_app::theme::{self, Look, ty};

use crate::copy;
use crate::state::{Monitor, Section};
use chart::Paint;

pub const SIZE: (f32, f32) = (1280.0, 860.0);

/// The traffic lights centred on the toolbar inside the framed pane, as rune-kit's shell example places them.
pub const CHROME: Chrome = Chrome {
    full_size_content: true,
    traffic_lights: Some((16.0, 25.0)),
    drag_regions: true,
};

pub fn spec() -> AppSpec<Monitor> {
    AppSpec {
        fonts: theme::FONTS,
        families: theme::FAMILIES,
        min_size: Some((720.0, 480.0)),
        on_key: Some(on_key),
        ..AppSpec::new(copy::APP_NAME, SIZE, no_state, view)
    }
}

/// The window's state always comes from `App::init`.
fn no_state() -> Monitor {
    unreachable!("the state comes from App::init")
}

fn on_key(s: &mut Monitor, ev: &KeyEvent, _: &mut Tx) -> bool {
    if shell::is_sidebar_toggle(ev) {
        s.sidebar.toggle();
        return true;
    }
    false
}

pub fn label(section: Section) -> &'static str {
    match section {
        Section::Overview => copy::OVERVIEW,
        Section::Dashboards => copy::DASHBOARDS,
        Section::Storage => copy::STORAGE,
        Section::Transcoding => copy::TRANSCODING,
        Section::AiPipelines => copy::AI_PIPELINES,
        Section::Costs => copy::COSTS,
        Section::Releases => copy::RELEASES,
        Section::Admin => copy::ADMIN,
    }
}

fn icon(section: Section) -> &'static Icon {
    match section {
        Section::Overview => &icons::OVERVIEW,
        Section::Dashboards => &icons::DASHBOARDS,
        Section::Storage => &icons::STORAGE,
        Section::Transcoding => &icons::TRANSCODING,
        Section::AiPipelines => &icons::AI,
        Section::Costs => &icons::COSTS,
        Section::Releases => &icons::RELEASES,
        Section::Admin => &icons::ADMIN,
    }
}

/// The chart colours for an appearance: the theme's text, lines and surface, and its `chart-1`..`chart-8` series.
pub fn paint(dark: bool) -> Paint {
    let l = theme::look(dark);
    let t = theme::theme(dark);
    Paint {
        ink: l.c.fg,
        ink_soft: l.c.fg_soft,
        grid: l.c.line,
        surface: l.c.surface,
        accent: l.c.accent,
        series: (1..=8).map(|i| t.color(&format!("chart-{i}"))).collect(),
        small: rune::TextStyle::new(11.5, 400.0).lh(15.0),
        small_strong: rune::TextStyle::new(11.5, 600.0).lh(15.0),
        big: theme::display(30.0).lh(34.0),
    }
}

fn sidebar(s: &Monitor, k: &Kit) -> El {
    Section::ALL
        .iter()
        .fold(
            Sidebar::new().label(copy::SIDEBAR_LABEL).row_gap(2.0),
            |sb, &section| {
                sb.child(
                    SidebarRow::new(label(section))
                        .icon(icon(section))
                        .height(32.0)
                        .selected(s.section == section)
                        .on_select(move |s: &mut Monitor, _| {
                            s.section = section;
                            s.hover = None;
                        })
                        .build(k)
                        .key(format!("section-{section:?}")),
                )
            },
        )
        .build(k)
}

/// A page with a heading and a line under it, for a section that isn't built and for Grafana not set up.
pub fn message(l: &Look, title: &str, body: &str) -> El {
    col()
        .w_full()
        .padding(32.0)
        .gap(8.0)
        .child(text(title.to_owned()).font(ty::TITLE_SMALL).color(l.c.fg))
        .child(text(body.to_owned()).font(ty::BODY).color(l.c.fg_soft).wrap())
}

pub fn view(s: &Monitor, cx: &mut Cx<Monitor>) -> El {
    let dark = cx.system_dark();
    let l = theme::look(dark);
    let k = &l.kit;
    let (main, toolbar) = match s.section {
        Section::Dashboards => dashboards::page(s, l, &paint(dark)),
        other => (
            message(l, copy::COMING_SOON_TITLE, copy::COMING_SOON),
            Toolbar::new().title(label(other)),
        ),
    };
    let main = col().key("main").w_full().h_full().fill(l.c.bg).child(main);
    SplitView::new(main)
        .sidebar(&s.sidebar, |s: &mut Monitor| &mut s.sidebar, sidebar(s, k))
        .toolbar(toolbar)
        .titlebar_inset(cx.titlebar_inset())
        .frame_inset(8.0)
        .toolbar_in_panel()
        .sidebar_padding(18.0, 10.0)
        .sidebar_toggle(
            SidebarToggle::new()
                .size(ControlSize::Large)
                .past_lights(16.0)
                .edge(18.0)
                .gap(4.0)
                .in_header(),
        )
        .reduced_motion(cx.is_reduced_motion())
        .build(k)
}
