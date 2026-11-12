//! Loading from Grafana off the UI thread: each call runs on the fetcher's own Tokio runtime and hands its answer to
//! the window through Rune's `AppHandle::update`. Answers for a dashboard load that has since been replaced (another
//! dashboard, a new range, a refresh) are dropped.

use rune::AppHandle;

use crate::grafana::{Client, TimeRange, query};
use crate::state::{Load, Monitor};

pub struct Fetcher {
    client: Client,
    runtime: tokio::runtime::Runtime,
    window: AppHandle<Monitor>,
}

impl Fetcher {
    pub fn new(client: Client, window: AppHandle<Monitor>) -> std::io::Result<Fetcher> {
        let runtime = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()?;
        Ok(Fetcher {
            client,
            runtime,
            window,
        })
    }

    pub fn list(&self) {
        let (client, window) = (self.client.clone(), self.window.clone());
        self.runtime.spawn(async move {
            let list = match client.search().await {
                Ok(hits) => Load::Ready(hits),
                Err(e) => Load::Failed(e.to_string()),
            };
            window.update(move |s: &mut Monitor, _| s.list = list);
        });
    }

    /// Loads dashboard `uid` and runs every panel the app draws, over `range` (else the dashboard's own).
    pub fn open(&self, uid: String, range: Option<TimeRange>, generation: u64) {
        let (client, window) = (self.client.clone(), self.window.clone());
        self.runtime.spawn(async move {
            let dashboard = match client.dashboard(&uid).await {
                Ok(d) => d,
                Err(e) => {
                    let error = e.to_string();
                    window.update(move |s: &mut Monitor, _| {
                        if let Some(o) = s.current(generation) {
                            o.dashboard = Load::Failed(error);
                        }
                    });
                    return;
                }
            };
            let range = range.unwrap_or_else(|| TimeRange::from_grafana(&dashboard.time_from));
            let span = range.span(chrono::Utc::now().timestamp_millis());
            let bodies: Vec<_> = dashboard
                .panels
                .iter()
                .filter(|p| query::is_drawn(p))
                .map(|p| (p.id, query::query_body(p, &dashboard, span)))
                .collect();
            let ids: Vec<u32> = bodies.iter().map(|(id, _)| *id).collect();
            window.update(move |s: &mut Monitor, _| {
                if let Some(o) = s.current(generation) {
                    o.span = span;
                    for id in ids {
                        o.data.entry(id).or_insert(Load::Loading);
                    }
                    o.dashboard = Load::Ready(dashboard);
                    s.range = range;
                }
            });
            for (id, body) in bodies {
                let (client, window) = (client.clone(), window.clone());
                tokio::spawn(async move {
                    let data = match client.query(&body).await {
                        Ok(data) => Load::Ready(data),
                        Err(e) => Load::Failed(e.to_string()),
                    };
                    window.update(move |s: &mut Monitor, _| {
                        if let Some(o) = s.current(generation) {
                            o.data.insert(id, data);
                        }
                    });
                });
            }
        });
    }
}
