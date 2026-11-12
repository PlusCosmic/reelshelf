//! `reelshelf-monitor`: reads `GRAFANA_URL` and `GRAFANA_TOKEN` and opens the window. With
//! `REELSHELF_MONITOR_FIXTURES=1` it shows the built-in fixture dashboards instead, without Grafana.

use std::sync::Arc;

use chrono::Offset;
use reelshelf_monitor::fetch::Fetcher;
use reelshelf_monitor::grafana::Client;
use reelshelf_monitor::state::Monitor;
use reelshelf_monitor::{fixtures, ui};

fn main() {
    let tz = chrono::Local::now().offset().fix();
    let app = rune::App::new(ui::spec()).chrome(ui::CHROME);
    let app = if std::env::var("REELSHELF_MONITOR_FIXTURES").is_ok_and(|v| v == "1") {
        let scene = std::env::var("REELSHELF_MONITOR_SCENE").unwrap_or_else(|_| "dashboard-pipeline".to_owned());
        app.init(move || fixtures::scene(&scene, tz).unwrap_or_else(|| fixtures::scene("dashboards", tz).unwrap()))
    } else {
        let client = Client::from_env();
        let grafana = client.as_ref().map(|c| c.base().to_owned());
        let fetch = client.and_then(|c| match Fetcher::new(c, app.handle()) {
            Ok(f) => Some(Arc::new(f)),
            Err(error) => {
                eprintln!("Couldn't start the network runtime: {error}");
                None
            }
        });
        app.init(move || Monitor::new(fetch, grafana, tz))
    };
    app.run();
}
