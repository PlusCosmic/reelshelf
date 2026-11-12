//! The window's state: which section shows, signing in to Reelshelf and the ops numbers it reads, Grafana's
//! dashboard list, the open dashboard and each panel's data, and the Settings form. Loading goes through
//! [`Fetcher`](crate::fetch::Fetcher); without one (headless scenes) the state only changes by hand.

use std::collections::HashMap;
use std::sync::Arc;

use chrono::FixedOffset;
use reelshelf_api::{OpsAi, OpsCosts, OpsStorage, OpsTranscoding};
use rune::TextEdit;
use rune_kit::shell::PaneState;
use uuid::Uuid;

use crate::copy;
use crate::fetch::Fetcher;
use crate::grafana::{Dashboard, PanelData, SearchHit, TimeRange};
use crate::session::{Refusal, Session};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Section {
    Overview,
    Dashboards,
    Storage,
    Transcoding,
    AiPipelines,
    Costs,
    Releases,
    Admin,
    Settings,
}

impl Section {
    /// A section by its name in `REELSHELF_MONITOR_SECTION` (`overview`, `storage`, `ai-pipelines`...).
    pub fn named(name: &str) -> Option<Section> {
        Section::ALL
            .into_iter()
            .find(|s| crate::ui::label(*s).to_lowercase().replace(' ', "-") == name.trim().to_lowercase())
    }

    pub const ALL: [Section; 9] = [
        Section::Overview,
        Section::Dashboards,
        Section::Storage,
        Section::Transcoding,
        Section::AiPipelines,
        Section::Costs,
        Section::Releases,
        Section::Admin,
        Section::Settings,
    ];

    /// The ops answers the section shows, which it polls while it shows.
    pub fn ops(self) -> &'static [OpsKind] {
        match self {
            Section::Overview => &[OpsKind::Storage, OpsKind::Transcoding, OpsKind::Ai, OpsKind::Costs],
            Section::Storage => &[OpsKind::Storage],
            Section::Transcoding => &[OpsKind::Transcoding],
            Section::AiPipelines => &[OpsKind::Ai],
            Section::Costs => &[OpsKind::Costs],
            _ => &[],
        }
    }
}

/// One of the `/api/admin/ops/*` answers.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum OpsKind {
    Storage,
    Transcoding,
    Ai,
    Costs,
}

/// How often the section showing is read again.
pub const POLL_EVERY: std::time::Duration = std::time::Duration::from_secs(30);

/// Something on its way from Grafana.
#[derive(Clone, Debug, PartialEq)]
pub enum Load<T> {
    Loading,
    Ready(T),
    Failed(String),
}

/// An ops answer: the last one read, and why the latest read failed, if it did (the last answer stays on screen).
#[derive(Clone, Debug, PartialEq)]
pub struct Fetched<T> {
    pub value: Option<T>,
    pub error: Option<String>,
}

impl<T> Default for Fetched<T> {
    fn default() -> Self {
        Fetched {
            value: None,
            error: None,
        }
    }
}

impl<T> Fetched<T> {
    pub fn ready(value: T) -> Self {
        Fetched {
            value: Some(value),
            error: None,
        }
    }
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

/// Settings' Grafana form.
pub struct GrafanaForm {
    pub url: TextEdit,
    /// Empty until a new token is typed; the kept one is never read back into the field.
    pub token: TextEdit,
    /// A token is kept in the Keychain.
    pub token_kept: bool,
    /// Grafana came from `GRAFANA_URL`/`GRAFANA_TOKEN` (debug builds), not the Keychain.
    pub from_env: bool,
    /// What the last Save or Remove said.
    pub note: Option<Result<(), String>>,
}

impl Default for GrafanaForm {
    fn default() -> Self {
        GrafanaForm {
            url: TextEdit::new(""),
            token: TextEdit::new("").secure(),
            token_kept: false,
            from_env: false,
            note: None,
        }
    }
}

pub struct Monitor {
    pub section: Section,
    pub sidebar: PaneState,
    /// Signing in to Reelshelf.
    pub session: Session,
    /// The site the app signs in to and reads (`https://reelshelf.app`, or a debug build's `REELSHELF_API_BASE_URL`).
    pub site: String,
    pub version: String,
    /// Why the ops answers are refused (the flag off, not an Admin), until one is read.
    pub refusal: Option<Refusal>,
    pub storage: Fetched<OpsStorage>,
    pub transcoding: Fetched<OpsTranscoding>,
    pub ai: Fetched<OpsAi>,
    pub costs: Fetched<OpsCosts>,
    /// The Costs page's width, as last laid out, for its charts.
    pub costs_width: f32,
    /// The clip id last copied, for its button's "Copied".
    pub copied: Option<Uuid>,
    /// "Now" for the pages' times, epoch milliseconds: when the latest answer arrived.
    pub now_ms: i64,
    pub list: Load<Vec<SearchHit>>,
    pub opened: Option<Opened>,
    pub range: TimeRange,
    /// The dashboard grid's width, as last laid out.
    pub grid_width: f32,
    pub hover: Option<Hover>,
    /// Grafana's address, for "Open in Grafana"; None when it isn't set up.
    pub grafana: Option<String>,
    pub grafana_form: GrafanaForm,
    /// The time zone times are shown in.
    pub tz: FixedOffset,
    pub fetch: Option<Arc<Fetcher>>,
    generation: u64,
}

impl Monitor {
    pub fn new(fetch: Option<Arc<Fetcher>>, grafana: Option<String>, tz: FixedOffset) -> Monitor {
        let mut grafana_form = GrafanaForm::default();
        if let Some(url) = &grafana {
            grafana_form.url = TextEdit::new(url.clone());
            grafana_form.token_kept = true;
        }
        let mut s = Monitor {
            section: Section::Overview,
            sidebar: PaneState::new(232.0).range(180.0, 360.0),
            session: Session::SignedOut(None),
            site: reelshelf_app::links::PRODUCTION_SITE.to_owned(),
            version: crate::links::version().to_owned(),
            refusal: None,
            storage: Fetched::default(),
            transcoding: Fetched::default(),
            ai: Fetched::default(),
            costs: Fetched::default(),
            costs_width: 1040.0,
            copied: None,
            now_ms: chrono::Utc::now().timestamp_millis(),
            list: Load::Loading,
            opened: None,
            range: TimeRange::Last6Hours,
            grid_width: 960.0,
            hover: None,
            grafana,
            grafana_form,
            tz,
            fetch,
            generation: 0,
        };
        if let Some(f) = &s.fetch {
            s.site = f.site().to_owned();
            s.session = Session::Checking;
            f.restore();
            if s.grafana.is_some() {
                f.list();
            }
        }
        if s.grafana.is_none() {
            s.list = Load::Ready(Vec::new());
        }
        s
    }

    pub fn connected(&self) -> bool {
        self.grafana.is_some()
    }

    /// Shows `section`, reading what it shows at once.
    pub fn select(&mut self, section: Section) {
        self.section = section;
        self.hover = None;
        self.poll();
    }

    /// Reads again the ops answers the section showing needs (every [`POLL_EVERY`], and on Refresh).
    pub fn poll(&mut self) {
        if !self.session.signed_in() {
            return;
        }
        if let Some(f) = &self.fetch {
            for kind in self.section.ops() {
                f.ops(*kind);
            }
        }
    }

    /// The session changed: signed in, read the section; signed out, forget what was read.
    pub fn set_session(&mut self, session: Session) {
        let signed_in = session.signed_in();
        self.session = session;
        if signed_in {
            self.poll();
        } else {
            self.refusal = None;
            self.storage = Fetched::default();
            self.transcoding = Fetched::default();
            self.ai = Fetched::default();
            self.costs = Fetched::default();
        }
    }

    pub fn sign_in(&mut self) {
        if let Some(f) = &self.fetch {
            self.session = Session::Starting;
            f.start_sign_in();
        }
    }

    pub fn cancel_sign_in(&mut self) {
        if let Some(f) = &self.fetch {
            f.cancel_sign_in();
        }
        self.session = Session::SignedOut(None);
    }

    pub fn sign_out(&mut self) {
        if let Some(f) = &self.fetch {
            f.sign_out();
        }
        self.set_session(Session::Checking);
    }

    /// When the ops answers on screen were read, the newest of them.
    pub fn updated_at(&self) -> Option<chrono::DateTime<chrono::Utc>> {
        self.section
            .ops()
            .iter()
            .filter_map(|kind| match kind {
                OpsKind::Storage => self.storage.value.as_ref().map(|v| v.generated_at),
                OpsKind::Transcoding => self.transcoding.value.as_ref().map(|v| v.generated_at),
                OpsKind::Ai => self.ai.value.as_ref().map(|v| v.generated_at),
                OpsKind::Costs => self.costs.value.as_ref().map(|v| v.generated_at),
            })
            .max()
    }

    /// Why the latest read of an answer on screen failed, if one did.
    pub fn ops_error(&self) -> Option<&str> {
        self.section.ops().iter().find_map(|kind| match kind {
            OpsKind::Storage => self.storage.error.as_deref(),
            OpsKind::Transcoding => self.transcoding.error.as_deref(),
            OpsKind::Ai => self.ai.error.as_deref(),
            OpsKind::Costs => self.costs.error.as_deref(),
        })
    }

    /// Saves Settings' Grafana address and token (a blank token keeps the kept one), and loads its dashboards.
    pub fn save_grafana(&mut self) {
        let url = self.grafana_form.url.text.trim().trim_end_matches('/').to_owned();
        let token = self.grafana_form.token.text.trim().to_owned();
        if !(url.starts_with("http://") || url.starts_with("https://")) || url.len() < 10 {
            self.grafana_form.note = Some(Err(copy::BAD_URL.to_owned()));
            return;
        }
        if token.is_empty() && !self.grafana_form.token_kept {
            self.grafana_form.note = Some(Err(copy::NO_TOKEN.to_owned()));
            return;
        }
        let Some(f) = &self.fetch else {
            return;
        };
        match f.save_grafana(&url, (!token.is_empty()).then_some(token.as_str())) {
            Ok(()) => {
                self.grafana_form.token.replace_text("");
                self.grafana_form.token_kept = true;
                self.grafana_form.from_env = false;
                self.grafana_form.note = Some(Ok(()));
                self.grafana = Some(url);
                self.opened = None;
                self.list = Load::Loading;
                f.list();
            }
            Err(error) => self.grafana_form.note = Some(Err(copy::couldnt_keep(&error))),
        }
    }

    /// Forgets Grafana's address and token.
    pub fn remove_grafana(&mut self) {
        if let Some(f) = &self.fetch {
            f.remove_grafana();
        }
        self.grafana_form = GrafanaForm::default();
        self.grafana = None;
        self.opened = None;
        self.list = Load::Ready(Vec::new());
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
