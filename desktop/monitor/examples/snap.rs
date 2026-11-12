//! Renders one dashboard from the real Grafana headless, for screenshots without opening the window:
//! `GRAFANA_URL=… GRAFANA_TOKEN=… cargo run -p reelshelf-monitor --example snap -- <uid> <1h|6h|24h|7d> [hover-panel-title hover-x] -- --snap out.png --theme dark`
//! Everything after the second `--` goes to Rune.

use std::collections::HashMap;

use chrono::Offset;
use reelshelf_monitor::grafana::{Client, TimeRange, query};
use reelshelf_monitor::state::{Hover, Load, Monitor, Opened};
use reelshelf_monitor::ui;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let split = args.iter().position(|a| a == "--").unwrap_or(args.len());
    let (ours, rune_args) = (&args[..split], args.get(split + 1..).unwrap_or_default().to_vec());
    let uid = ours.first().expect("a dashboard uid").clone();
    let range = TimeRange::ALL
        .into_iter()
        .find(|r| Some(reelshelf_monitor::copy::range(*r)) == ours.get(1).map(String::as_str))
        .unwrap_or(TimeRange::Last6Hours);
    let hover = ours
        .get(2)
        .cloned()
        .zip(ours.get(3).and_then(|x| x.parse::<f32>().ok()));
    let client = Client::from_env().expect("GRAFANA_URL and GRAFANA_TOKEN");
    let runtime = tokio::runtime::Runtime::new().unwrap();
    let (dashboard, data, span) = runtime.block_on(async {
        let d = client.dashboard(&uid).await.expect("the dashboard");
        let span = range.span(chrono::Utc::now().timestamp_millis());
        let mut data = HashMap::new();
        for p in d.panels.iter().filter(|p| query::is_drawn(p)) {
            let loaded = match client.query(&query::query_body(p, &d, span)).await {
                Ok(data) => Load::Ready(data),
                Err(e) => Load::Failed(e.to_string()),
            };
            data.insert(p.id, loaded);
        }
        (d, data, span)
    });
    let tz = chrono::Local::now().offset().fix();
    let base = client.base().to_owned();
    rune::App::new(ui::spec())
        .chrome(ui::CHROME)
        .init(move || {
            let mut s = Monitor::new(None, Some(base), tz);
            s.range = range;
            s.hover = hover.and_then(|(title, x)| {
                dashboard
                    .panels
                    .iter()
                    .find(|p| p.title == title)
                    .map(|p| Hover { panel: p.id, x })
            });
            s.opened = Some(Opened {
                uid,
                dashboard: Load::Ready(dashboard),
                data,
                span,
                generation: 0,
            });
            s
        })
        .args(rune_args)
        .run();
}
