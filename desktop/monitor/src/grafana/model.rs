//! What the app reads from Grafana's HTTP API: the dashboard list (`/api/search`), one dashboard
//! (`/api/dashboards/uid/{uid}`), parsed from JSON with
//! everything the app doesn't use left out.

use serde::Deserialize;
use serde_json::Value;

/// One dashboard in `/api/search?type=dash-db`.
#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct SearchHit {
    pub uid: String,
    pub title: String,
    /// The dashboard's path under Grafana's root, like `/d/reelshelf-api/reelshelf-api`.
    #[serde(default)]
    pub url: String,
    #[serde(default, rename = "folderTitle")]
    pub folder_title: Option<String>,
}

/// Parses `/api/search`'s answer, keeping only dashboards (folders come back too without `type=dash-db`).
pub fn parse_search(json: &str) -> Result<Vec<SearchHit>, serde_json::Error> {
    #[derive(Deserialize)]
    struct Raw {
        #[serde(flatten)]
        hit: SearchHit,
        #[serde(default, rename = "type")]
        kind: String,
    }
    let raw: Vec<Raw> = serde_json::from_str(json)?;
    Ok(raw
        .into_iter()
        .filter(|r| r.kind.is_empty() || r.kind == "dash-db")
        .map(|r| r.hit)
        .collect())
}

/// A data source reference as dashboards write it: `{ "type": "prometheus", "uid": "prometheus" }`, where the uid
/// may be a variable (`${gcm}`).
#[derive(Clone, Debug, Default, Deserialize, PartialEq)]
pub struct DsRef {
    #[serde(default, rename = "type")]
    pub kind: Option<String>,
    #[serde(default)]
    pub uid: Option<String>,
}

/// A panel's place on the dashboard's 24-column grid, in grid units.
#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq)]
pub struct GridPos {
    pub x: u32,
    pub y: u32,
    pub w: u32,
    pub h: u32,
}

/// What kind of panel it is, as far as the app draws it.
#[derive(Clone, Debug, PartialEq)]
pub enum PanelKind {
    TimeSeries,
    Stat,
    /// A row's heading across the grid.
    Row,
    /// Anything else (tables, bar gauges, text...), drawn as a card that links to Grafana.
    Other(String),
}

impl PanelKind {
    fn from_type(kind: &str) -> PanelKind {
        match kind {
            "timeseries" | "graph" => PanelKind::TimeSeries,
            "stat" | "singlestat" => PanelKind::Stat,
            "row" => PanelKind::Row,
            other => PanelKind::Other(other.to_owned()),
        }
    }
}

/// One panel, rows included, with its targets kept as Grafana wrote them (each data source has its own shape).
#[derive(Clone, Debug, PartialEq)]
pub struct Panel {
    pub id: u32,
    pub kind: PanelKind,
    pub title: String,
    pub grid: GridPos,
    pub datasource: Option<DsRef>,
    pub targets: Vec<Value>,
    /// `fieldConfig.defaults.unit`, like `reqps`, `s` or `bytes`.
    pub unit: Option<String>,
    pub decimals: Option<u32>,
    /// `fieldConfig.defaults.min`, for charts that should start somewhere other than their data.
    pub min: Option<f64>,
    pub max: Option<f64>,
    /// `options.reduceOptions.calcs[0]` for a stat (`lastNotNull` when unset).
    pub reduce: String,
}

/// A template variable and the value the dashboard was saved with.
#[derive(Clone, Debug, PartialEq)]
pub struct Variable {
    pub name: String,
    /// `query`, `datasource`, `custom`, `textbox`, `constant`, `interval`...
    pub kind: String,
    /// The saved values; `$__all` stands for every value.
    pub current: Vec<String>,
    pub include_all: bool,
    /// What "All" means in a query, when the dashboard says (`.*`).
    pub all_value: Option<String>,
}

/// A dashboard, flattened: panels inside collapsed rows come after their row, as if it were open.
#[derive(Clone, Debug, PartialEq)]
pub struct Dashboard {
    pub uid: String,
    pub title: String,
    /// Grafana's path to it (`/d/{uid}/{slug}`), for "Open in Grafana".
    pub url: String,
    /// The saved time range, like `now-6h`.
    pub time_from: String,
    pub panels: Vec<Panel>,
    pub variables: Vec<Variable>,
}

impl Dashboard {
    pub fn variable(&self, name: &str) -> Option<&Variable> {
        self.variables.iter().find(|v| v.name == name)
    }

    /// How many grid rows the panels take.
    pub fn grid_height(&self) -> u32 {
        self.panels.iter().map(|p| p.grid.y + p.grid.h).max().unwrap_or(0)
    }
}

/// Parses `/api/dashboards/uid/{uid}`'s answer (`{ dashboard, meta }`).
pub fn parse_dashboard(json: &str) -> Result<Dashboard, serde_json::Error> {
    let root: Value = serde_json::from_str(json)?;
    let d = &root["dashboard"];
    let str_of = |v: &Value| v.as_str().unwrap_or_default().to_owned();
    let mut panels = Vec::new();
    for p in d["panels"].as_array().into_iter().flatten() {
        panels.push(panel(p)?);
        // A collapsed row holds its panels; they keep their own (absolute) grid places.
        for inner in p["panels"].as_array().into_iter().flatten() {
            panels.push(panel(inner)?);
        }
    }
    panels.sort_by_key(|p| (p.grid.y, p.grid.x));
    let variables = d["templating"]["list"]
        .as_array()
        .into_iter()
        .flatten()
        .map(variable)
        .collect();
    Ok(Dashboard {
        uid: str_of(&d["uid"]),
        title: str_of(&d["title"]),
        url: str_of(&root["meta"]["url"]),
        time_from: d["time"]["from"].as_str().unwrap_or("now-6h").to_owned(),
        panels,
        variables,
    })
}

fn panel(p: &Value) -> Result<Panel, serde_json::Error> {
    let defaults = &p["fieldConfig"]["defaults"];
    let datasource = match &p["datasource"] {
        Value::Null => None,
        // Older dashboards name the data source as a string.
        Value::String(name) => Some(DsRef {
            kind: None,
            uid: Some(name.clone()),
        }),
        other => Some(serde_json::from_value(other.clone())?),
    };
    Ok(Panel {
        id: p["id"].as_u64().unwrap_or_default() as u32,
        kind: PanelKind::from_type(p["type"].as_str().unwrap_or_default()),
        title: p["title"].as_str().unwrap_or_default().to_owned(),
        grid: serde_json::from_value(p["gridPos"].clone()).unwrap_or_default(),
        datasource,
        targets: p["targets"].as_array().cloned().unwrap_or_default(),
        unit: defaults["unit"].as_str().map(str::to_owned),
        decimals: defaults["decimals"].as_u64().map(|d| d as u32),
        min: defaults["min"].as_f64(),
        max: defaults["max"].as_f64(),
        reduce: p["options"]["reduceOptions"]["calcs"][0]
            .as_str()
            .unwrap_or("lastNotNull")
            .to_owned(),
    })
}

fn variable(v: &Value) -> Variable {
    let current = match &v["current"]["value"] {
        Value::String(s) => vec![s.clone()],
        Value::Array(values) => values.iter().filter_map(|x| x.as_str().map(str::to_owned)).collect(),
        _ => Vec::new(),
    };
    Variable {
        name: v["name"].as_str().unwrap_or_default().to_owned(),
        kind: v["type"].as_str().unwrap_or_default().to_owned(),
        current,
        include_all: v["includeAll"].as_bool().unwrap_or(false),
        all_value: v["allValue"].as_str().map(str::to_owned),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const API: &str = include_str!("../../tests/fixtures/dashboard-api.json");
    const PIPELINE: &str = include_str!("../../tests/fixtures/dashboard-pipeline.json");

    #[test]
    fn search_keeps_dashboards_only() {
        let hits = parse_search(include_str!("../../tests/fixtures/search.json")).unwrap();
        assert_eq!(hits.len(), 3);
        assert_eq!(hits[0].uid, "reelshelf-api");
        assert_eq!(hits[0].folder_title, None);
        assert!(hits.iter().all(|h| !h.title.is_empty()));
    }

    #[test]
    fn a_dashboard_reads_its_panels_variables_and_time() {
        let d = parse_dashboard(API).unwrap();
        assert_eq!(d.uid, "reelshelf-api");
        assert_eq!(d.url, "/d/reelshelf-api/reelshelf-api");
        assert_eq!(d.time_from, "now-6h");
        let env = d.variable("env").unwrap();
        assert_eq!(env.current, vec!["Production"]);
        assert_eq!(env.all_value.as_deref(), Some(".*"));
        let stat = d.panels.iter().find(|p| p.title == "Requests per second").unwrap();
        assert_eq!(stat.kind, PanelKind::Stat);
        assert_eq!(stat.unit.as_deref(), Some("reqps"));
        assert_eq!(stat.grid, GridPos { x: 0, y: 1, w: 6, h: 4 });
        let latency = d.panels.iter().find(|p| p.title == "Latency").unwrap();
        assert_eq!(latency.kind, PanelKind::TimeSeries);
        assert_eq!(latency.targets.len(), 3);
        assert!(d.panels.iter().any(|p| p.kind == PanelKind::Row));
        assert!(d.grid_height() >= 13);
    }

    #[test]
    fn datasource_variables_and_other_panel_types_parse() {
        let d = parse_dashboard(PIPELINE).unwrap();
        let gcm = d.variable("gcm").unwrap();
        assert_eq!(gcm.kind, "datasource");
        assert!(gcm.current.is_empty());
        assert!(d.panels.iter().any(|p| p.kind == PanelKind::Other("table".into())));
        let vms = d.panels.iter().find(|p| p.title == "Worker VMs").unwrap();
        assert_eq!(vms.datasource.as_ref().unwrap().uid.as_deref(), Some("${gcm}"));
    }
}
