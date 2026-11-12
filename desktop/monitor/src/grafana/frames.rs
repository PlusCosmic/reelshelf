//! Reading `/api/ds/query`'s answer: Grafana's data frames (a schema of fields and their values column by column)
//! turned into named time series, one per number field beside a time field.

use serde_json::Value;

/// One line on a chart: its name as Grafana would show it, and its points in epoch milliseconds. A missing value
/// (null, NaN or infinite) breaks the line.
#[derive(Clone, Debug, PartialEq)]
pub struct Series {
    pub name: String,
    pub points: Vec<(i64, Option<f64>)>,
}

impl Series {
    pub fn last_value(&self) -> Option<f64> {
        self.points.iter().rev().find_map(|p| p.1)
    }

    /// Reduces the series to one number, as a stat panel's `calcs` says.
    pub fn reduce(&self, calc: &str) -> Option<f64> {
        let mut values = self.points.iter().filter_map(|p| p.1);
        match calc {
            "mean" => {
                let (sum, n) = values.fold((0.0, 0usize), |(s, n), v| (s + v, n + 1));
                (n > 0).then(|| sum / n as f64)
            }
            "max" => values.reduce(f64::max),
            "min" => values.reduce(f64::min),
            "sum" => values.reduce(|a, b| a + b),
            "first" | "firstNotNull" => values.next(),
            _ => self.last_value(),
        }
    }
}

/// What came back for a panel: every series, and the first error any query reported.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PanelData {
    pub series: Vec<Series>,
    pub error: Option<String>,
}

/// Parses a `/api/ds/query` answer (`{ results: { refId: { frames, error } } }`), queries in refId order.
pub fn parse_response(json: &str) -> Result<PanelData, serde_json::Error> {
    let root: Value = serde_json::from_str(json)?;
    let mut data = PanelData::default();
    let Some(results) = root["results"].as_object() else {
        data.error = root["message"].as_str().map(str::to_owned);
        return Ok(data);
    };
    let mut refs: Vec<&String> = results.keys().collect();
    refs.sort();
    for r in refs {
        let result = &results[r];
        if let Some(error) = result["error"].as_str().filter(|e| !e.is_empty()) {
            data.error.get_or_insert_with(|| error.to_owned());
        }
        for frame in result["frames"].as_array().into_iter().flatten() {
            data.series.extend(frame_series(frame));
        }
    }
    Ok(data)
}

fn frame_series(frame: &Value) -> Vec<Series> {
    let fields = frame["schema"]["fields"].as_array().cloned().unwrap_or_default();
    let values = frame["data"]["values"].as_array().cloned().unwrap_or_default();
    let Some(time_at) = fields.iter().position(|f| f["type"] == "time") else {
        return Vec::new();
    };
    let times: Vec<Option<i64>> = column(&values, time_at)
        .iter()
        .map(|t| t.as_i64().or_else(|| t.as_f64().map(|f| f as i64)))
        .collect();
    let mut out = Vec::new();
    for (at, field) in fields.iter().enumerate() {
        if field["type"] != "number" {
            continue;
        }
        let column = column(&values, at);
        let bad = special_indexes(&frame["data"]["entities"], at);
        let points = times
            .iter()
            .zip(column.iter())
            .enumerate()
            .filter_map(|(i, (t, v))| {
                let value = if bad.contains(&i) {
                    None
                } else {
                    v.as_f64().filter(|v| v.is_finite())
                };
                t.map(|t| (t, value))
            })
            .collect();
        out.push(Series {
            name: display_name(field, &frame["schema"]["name"]),
            points,
        });
    }
    out
}

fn column(values: &[Value], at: usize) -> Vec<Value> {
    values.get(at).and_then(Value::as_array).cloned().unwrap_or_default()
}

/// The rows Grafana sends as null but meant as NaN or ±Inf (`data.entities[field]`).
fn special_indexes(entities: &Value, at: usize) -> Vec<usize> {
    let e = &entities[at];
    ["NaN", "Inf", "NegInf"]
        .iter()
        .flat_map(|k| e[*k].as_array().cloned().unwrap_or_default())
        .filter_map(|i| i.as_u64().map(|i| i as usize))
        .collect()
}

/// The legend name: the data source's (legendFormat applied), the panel's override, the labels, else the field's.
fn display_name(field: &Value, frame_name: &Value) -> String {
    let config = &field["config"];
    for key in ["displayNameFromDS", "displayName"] {
        if let Some(name) = config[key].as_str().filter(|n| !n.is_empty()) {
            return name.to_owned();
        }
    }
    if let Some(labels) = field["labels"].as_object().filter(|l| !l.is_empty()) {
        let mut pairs: Vec<String> = labels
            .iter()
            .map(|(k, v)| format!("{k}=\"{}\"", v.as_str().unwrap_or_default()))
            .collect();
        pairs.sort();
        return format!("{{{}}}", pairs.join(", "));
    }
    match (field["name"].as_str(), frame_name.as_str()) {
        (Some("Value") | None, Some(frame)) if !frame.is_empty() => frame.to_owned(),
        (Some(name), _) => name.to_owned(),
        _ => String::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn frames_become_named_series() {
        let data = parse_response(include_str!("../../tests/fixtures/query-latency.json")).unwrap();
        assert_eq!(data.error, None);
        assert_eq!(data.series.len(), 2);
        assert_eq!(data.series[0].name, "p50");
        assert_eq!(data.series[1].name, "p95");
        assert_eq!(data.series[0].points.len(), 5);
        assert_eq!(data.series[0].points[0], (1_731_400_000_000, Some(0.012)));
        // The NaN in `entities` and the null both break the line.
        assert_eq!(data.series[1].points[2].1, None);
        assert_eq!(data.series[1].points[3].1, None);
        assert_eq!(data.series[1].last_value(), Some(0.09));
    }

    #[test]
    fn labels_name_a_series_without_a_legend_format() {
        let data = parse_response(include_str!("../../tests/fixtures/query-labels.json")).unwrap();
        assert_eq!(data.series[0].name, r#"{status="2xx"}"#);
    }

    #[test]
    fn errors_are_kept() {
        let data = parse_response(include_str!("../../tests/fixtures/query-error.json")).unwrap();
        assert!(data.series.is_empty());
        assert_eq!(data.error.as_deref(), Some("bad_data: parse error"));
    }

    #[test]
    fn stats_reduce() {
        let s = Series {
            name: String::new(),
            points: vec![(0, Some(1.0)), (1, Some(3.0)), (2, None)],
        };
        assert_eq!(s.reduce("lastNotNull"), Some(3.0));
        assert_eq!(s.reduce("mean"), Some(2.0));
        assert_eq!(s.reduce("max"), Some(3.0));
        assert_eq!(s.reduce("sum"), Some(4.0));
    }
}
