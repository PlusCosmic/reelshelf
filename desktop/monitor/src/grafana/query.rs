//! Turning a panel's targets into one `POST /api/ds/query` body: the time range as epoch milliseconds, each target's
//! data source, and the dashboard's template variables written into every string (Grafana's own frontend does this
//! before it asks; the backend only fills in `$__interval`, `$__rate_interval` and `$__range`).

use serde_json::{Value, json};

use super::model::{Dashboard, DsRef, Panel, PanelKind, Variable};

/// The ranges the picker offers, back from now.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TimeRange {
    LastHour,
    Last6Hours,
    Last24Hours,
    Last7Days,
}

impl TimeRange {
    pub const ALL: [TimeRange; 4] = [
        TimeRange::LastHour,
        TimeRange::Last6Hours,
        TimeRange::Last24Hours,
        TimeRange::Last7Days,
    ];

    pub fn millis(self) -> i64 {
        const HOUR: i64 = 3_600_000;
        match self {
            TimeRange::LastHour => HOUR,
            TimeRange::Last6Hours => 6 * HOUR,
            TimeRange::Last24Hours => 24 * HOUR,
            TimeRange::Last7Days => 7 * 24 * HOUR,
        }
    }

    /// Grafana's spelling, for links (`now-6h`).
    pub fn grafana(self) -> &'static str {
        match self {
            TimeRange::LastHour => "now-1h",
            TimeRange::Last6Hours => "now-6h",
            TimeRange::Last24Hours => "now-24h",
            TimeRange::Last7Days => "now-7d",
        }
    }

    /// The picker's choice for a dashboard's saved range: the same one when offered, else six hours.
    pub fn from_grafana(from: &str) -> TimeRange {
        match from {
            "now-1h" | "now-60m" => TimeRange::LastHour,
            "now-24h" | "now-1d" => TimeRange::Last24Hours,
            "now-7d" | "now-1w" => TimeRange::Last7Days,
            _ => TimeRange::Last6Hours,
        }
    }

    /// `(from, to)` in epoch milliseconds, ending at `now`.
    pub fn span(self, now: i64) -> (i64, i64) {
        (now - self.millis(), now)
    }
}

/// How many points a series asks for: about one per two points of a wide panel.
pub const MAX_DATA_POINTS: i64 = 600;

/// Data sources the app draws. Everything else (Cloud Monitoring, BigQuery, Infinity) gets the "Open in Grafana" card.
const DRAWN_SOURCES: &[&str] = &["prometheus", "__expr__"];

/// The data source a target asks, its own or else the panel's.
fn source_of<'a>(panel: &'a Panel, target: &'a Value) -> Option<DsRef> {
    match &target["datasource"] {
        Value::Object(_) => serde_json::from_value(target["datasource"].clone()).ok(),
        _ => panel.datasource.clone(),
    }
}

/// Whether the app draws this panel itself: a time series or stat whose every target asks a data source it knows.
pub fn is_drawn(panel: &Panel) -> bool {
    matches!(panel.kind, PanelKind::TimeSeries | PanelKind::Stat)
        && !panel.targets.is_empty()
        && panel.targets.iter().all(|t| {
            source_of(panel, t)
                .and_then(|ds| ds.kind)
                .is_some_and(|kind| DRAWN_SOURCES.contains(&kind.as_str()))
        })
}

/// The body for `POST /api/ds/query` running every target of `panel` that isn't hidden, over `(from, to)`.
pub fn query_body(panel: &Panel, dashboard: &Dashboard, (from, to): (i64, i64)) -> Value {
    let interval_ms = ((to - from) / MAX_DATA_POINTS).max(1_000);
    let queries: Vec<Value> = panel
        .targets
        .iter()
        .filter(|t| !t["hide"].as_bool().unwrap_or(false))
        .map(|t| {
            let mut q = interpolate_json(t, &dashboard.variables, (from, to));
            if let Some(ds) = source_of(panel, t) {
                q["datasource"] = json!({ "type": ds.kind, "uid": ds.uid });
            }
            q["intervalMs"] = json!(interval_ms);
            q["maxDataPoints"] = json!(MAX_DATA_POINTS);
            q
        })
        .collect();
    json!({ "from": from.to_string(), "to": to.to_string(), "queries": queries })
}

fn interpolate_json(value: &Value, vars: &[Variable], range: (i64, i64)) -> Value {
    match value {
        Value::String(s) => Value::String(interpolate(s, vars, range)),
        Value::Array(items) => Value::Array(items.iter().map(|v| interpolate_json(v, vars, range)).collect()),
        Value::Object(map) => Value::Object(
            map.iter()
                .map(|(k, v)| (k.clone(), interpolate_json(v, vars, range)))
                .collect(),
        ),
        other => other.clone(),
    }
}

/// Writes the dashboard's variables into `text`: `$name`, `${name}` and `${name:format}` (`regex`, `pipe`, `csv`,
/// `raw`), plus `${__from}`/`${__to}` as milliseconds. Grafana's own `$__…` macros and variables the dashboard doesn't
/// have stay as written.
pub fn interpolate(text: &str, vars: &[Variable], (from, to): (i64, i64)) -> String {
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(at) = rest.find('$') {
        out.push_str(&rest[..at]);
        let after = &rest[at + 1..];
        let (name, format, used) = if let Some(braced) = after.strip_prefix('{') {
            match braced.find('}') {
                Some(end) => {
                    let inner = &braced[..end];
                    let (name, format) = inner.split_once(':').map_or((inner, None), |(n, f)| (n, Some(f)));
                    (name, format, end + 2)
                }
                None => ("", None, 0),
            }
        } else {
            let len = after
                .find(|c: char| !(c.is_ascii_alphanumeric() || c == '_'))
                .unwrap_or(after.len());
            (&after[..len], None, len)
        };
        let value = match name {
            "__from" => Some(from.to_string()),
            "__to" => Some(to.to_string()),
            _ => vars.iter().find(|v| v.name == name).map(|v| value_of(v, format)),
        };
        match value {
            Some(value) => {
                out.push_str(&value);
                rest = &after[used..];
            }
            None => {
                out.push('$');
                rest = after;
            }
        }
    }
    out.push_str(rest);
    out
}

fn value_of(v: &Variable, format: Option<&str>) -> String {
    let all = v.current.iter().any(|c| c == "$__all");
    if all && let Some(value) = &v.all_value {
        return value.clone();
    }
    let values: Vec<String> = if all { vec![".*".to_owned()] } else { v.current.clone() };
    match (format, values.as_slice()) {
        (Some("csv"), _) => values.join(","),
        (Some("pipe"), _) => values.join("|"),
        (Some("raw"), _) | (None, [_]) => values.join(","),
        (_, []) => String::new(),
        // Prometheus's default for several values, and `:regex`: an alternation.
        (_, _) if all => values.join("|"),
        _ => format!(
            "({})",
            values.iter().map(|v| regex_escape(v)).collect::<Vec<_>>().join("|")
        ),
    }
}

fn regex_escape(s: &str) -> String {
    s.chars()
        .flat_map(|c| {
            let special = "\\.+*?()|[]{}^$".contains(c);
            special.then_some('\\').into_iter().chain([c])
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::grafana::model::parse_dashboard;

    fn var(name: &str, current: &[&str], all_value: Option<&str>) -> Variable {
        Variable {
            name: name.into(),
            kind: "query".into(),
            current: current.iter().map(|c| c.to_string()).collect(),
            include_all: true,
            all_value: all_value.map(str::to_owned),
        }
    }

    #[test]
    fn variables_are_written_in_and_macros_left_for_grafana() {
        let vars = [var("env", &["Production"], Some(".*"))];
        let text = r#"rate(x{env=~"$env"}[$__rate_interval]) and ${env} and ${missing} $5"#;
        assert_eq!(
            interpolate(text, &vars, (1, 2)),
            r#"rate(x{env=~"Production"}[$__rate_interval]) and Production and ${missing} $5"#
        );
    }

    #[test]
    fn all_and_several_values_and_formats() {
        let all = [var("env", &["$__all"], Some(".*"))];
        assert_eq!(interpolate("$env", &all, (0, 0)), ".*");
        let many = [var("b", &["a.b", "c"], None)];
        assert_eq!(interpolate("$b", &many, (0, 0)), r"(a\.b|c)");
        assert_eq!(interpolate("${b:csv}", &many, (0, 0)), "a.b,c");
        assert_eq!(interpolate("${b:pipe}", &many, (0, 0)), "a.b|c");
        assert_eq!(interpolate("${__from}-${__to}", &many, (10, 20)), "10-20");
    }

    #[test]
    fn a_panel_becomes_one_query_body() {
        let d = parse_dashboard(include_str!("../../tests/fixtures/dashboard-api.json")).unwrap();
        let latency = d.panels.iter().find(|p| p.title == "Latency").unwrap();
        assert!(is_drawn(latency));
        let body = query_body(latency, &d, (1_000_000, 1_000_000 + 21_600_000));
        assert_eq!(body["from"], "1000000");
        assert_eq!(body["to"], "22600000");
        let queries = body["queries"].as_array().unwrap();
        assert_eq!(queries.len(), 3);
        assert_eq!(queries[0]["refId"], "A");
        assert_eq!(queries[0]["datasource"]["uid"], "prometheus");
        assert_eq!(queries[0]["intervalMs"], 36_000);
        let expr = queries[0]["expr"].as_str().unwrap();
        assert!(expr.contains(r#"deployment_environment_name=~"Production""#), "{expr}");
        assert!(expr.contains("$__rate_interval"), "{expr}");
    }

    #[test]
    fn only_prometheus_panels_are_drawn() {
        let d = parse_dashboard(include_str!("../../tests/fixtures/dashboard-pipeline.json")).unwrap();
        let by = |title: &str| d.panels.iter().find(|p| p.title == title).unwrap();
        assert!(is_drawn(by("Waiting to be claimed")));
        assert!(is_drawn(by("Queue by job type")));
        assert!(!is_drawn(by("Worker VMs")), "Cloud Monitoring gets the card");
        assert!(!is_drawn(by("Spend by SKU (time range)")), "tables get the card");
        assert!(!is_drawn(by("Transcoding")), "rows are headings");
    }

    #[test]
    fn the_picker_follows_the_saved_range() {
        assert_eq!(TimeRange::from_grafana("now-7d"), TimeRange::Last7Days);
        assert_eq!(TimeRange::from_grafana("now-30d"), TimeRange::Last6Hours);
        assert_eq!(TimeRange::LastHour.span(10_000_000), (6_400_000, 10_000_000));
    }
}
