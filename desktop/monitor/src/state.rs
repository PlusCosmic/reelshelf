//! The window's state: which section shows, the dashboard list, the open dashboard and each panel's data, and where
//! the pointer rests on a chart. Loading goes through [`Fetcher`](crate::fetch::Fetcher); without one (headless
//! scenes, or Grafana not set up) the state only changes by hand.

use std::collections::HashMap;
use std::sync::Arc;

use chrono::FixedOffset;
use rune_kit::shell::PaneState;

use crate::fetch::Fetcher;
use crate::grafana::{Dashboard, PanelData, SearchHit, TimeRange};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Section {
    Overview,
    Dashboards,
    Storage,
    Transcoding,
    AiPipelines,
    Costs,
    Releases,
    Admin,
}

impl Section {
    pub const ALL: [Section; 8] = [
        Section::Overview,
        Section::Dashboards,
        Section::Storage,
        Section::Transcoding,
        Section::AiPipelines,
        Section::Costs,
        Section::Releases,
        Section::Admin,
    ];
}

/// Something on its way from Grafana.
#[derive(Clone, Debug, PartialEq)]
pub enum Load<T> {
    Loading,
    Ready(T),
    Failed(String),
}

/// The pointer over a chart: the panel, and its x in the chart's own points.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Hover {
    pub panel: u32,
    pub x: f32,
}

/// The open dashboard.
#[derive(Clone, Debug)]
pub struct Opened {
    pub uid: String,
    pub dashboard: Load<Dashboard>,
    /// Each drawn panel's data, by panel id; a refresh keeps the last data until the new arrives.
    pub data: HashMap<u32, Load<PanelData>>,
    /// The range the data covers, epoch milliseconds.
    pub span: (i64, i64),
    /// Bumped by every load, so answers to an older one are dropped.
    pub generation: u64,
}

pub struct Monitor {
    pub section: Section,
    pub sidebar: PaneState,
    pub list: Load<Vec<SearchHit>>,
    pub opened: Option<Opened>,
    pub range: TimeRange,
    /// The dashboard grid's width, as last laid out.
    pub grid_width: f32,
    pub hover: Option<Hover>,
    /// Grafana's address, for "Open in Grafana"; None when it isn't set up.
    pub grafana: Option<String>,
    /// The time zone the axes are labelled in.
    pub tz: FixedOffset,
    pub fetch: Option<Arc<Fetcher>>,
    generation: u64,
}

impl Monitor {
    pub fn new(fetch: Option<Arc<Fetcher>>, grafana: Option<String>, tz: FixedOffset) -> Monitor {
        let mut s = Monitor {
            section: Section::Dashboards,
            sidebar: PaneState::new(232.0).range(180.0, 360.0),
            list: Load::Loading,
            opened: None,
            range: TimeRange::Last6Hours,
            grid_width: 960.0,
            hover: None,
            grafana,
            tz,
            fetch,
            generation: 0,
        };
        if let Some(f) = &s.fetch {
            f.list();
        } else if s.grafana.is_none() {
            s.list = Load::Ready(Vec::new());
        }
        s
    }

    pub fn connected(&self) -> bool {
        self.grafana.is_some()
    }

    fn next_generation(&mut self) -> u64 {
        self.generation += 1;
        self.generation
    }

    /// Opens a dashboard at its own saved range.
    pub fn open(&mut self, uid: String) {
        let generation = self.next_generation();
        self.hover = None;
        self.opened = Some(Opened {
            uid: uid.clone(),
            dashboard: Load::Loading,
            data: HashMap::new(),
            span: self.range.span(chrono::Utc::now().timestamp_millis()),
            generation,
        });
        if let Some(f) = &self.fetch {
            f.open(uid, None, generation);
        }
    }

    /// Back to the list.
    pub fn close(&mut self) {
        self.opened = None;
        self.hover = None;
    }

    /// Runs the open dashboard's queries again, over `range`.
    pub fn reload(&mut self, range: TimeRange) {
        self.range = range;
        let generation = self.next_generation();
        let Some(opened) = &mut self.opened else {
            if let Some(f) = &self.fetch {
                self.list = Load::Loading;
                f.list();
            }
            return;
        };
        opened.generation = generation;
        if let Some(f) = &self.fetch {
            f.open(opened.uid.clone(), Some(range), generation);
        }
    }

    /// The open dashboard when it's still the load `generation` started.
    pub fn current(&mut self, generation: u64) -> Option<&mut Opened> {
        self.opened.as_mut().filter(|o| o.generation == generation)
    }

    /// A link to the open dashboard (and one panel of it) in Grafana, at the picked range.
    pub fn grafana_link(&self, panel: Option<u32>) -> Option<String> {
        let base = self.grafana.as_ref()?;
        let Some(Opened {
            dashboard: Load::Ready(d),
            ..
        }) = &self.opened
        else {
            return Some(base.clone());
        };
        let mut url = format!("{base}{}?from={}&to=now", d.url, self.range.grafana());
        if let Some(id) = panel {
            url.push_str(&format!("&viewPanel=panel-{id}"));
        }
        Some(url)
    }
}
