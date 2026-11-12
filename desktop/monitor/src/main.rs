//! `reelshelf-monitor`: signs in to Reelshelf with the device token kept in the Keychain, reads Grafana's address and
//! token and the GitHub token from the Keychain (debug builds take `GRAFANA_URL`, `GRAFANA_TOKEN` and `GITHUB_TOKEN`
//! first), and opens the window. With
//! `REELSHELF_MONITOR_FIXTURES=1` it shows a fixture scene instead (`REELSHELF_MONITOR_SCENE`), reaching nothing.

use std::sync::Arc;

use chrono::Offset;
use reelshelf_monitor::fetch::Fetcher;
use reelshelf_monitor::github;
use reelshelf_monitor::grafana::Client;
use reelshelf_monitor::state::{Monitor, Section};
use reelshelf_monitor::{fixtures, links, secrets, ui};

fn main() {
    let tz = chrono::Local::now().offset().fix();
    let app = rune::App::new(ui::spec()).chrome(ui::CHROME);
    let app = if std::env::var("REELSHELF_MONITOR_FIXTURES").is_ok_and(|v| v == "1") {
        let scene = std::env::var("REELSHELF_MONITOR_SCENE").unwrap_or_else(|_| "overview".to_owned());
        app.init(move || fixtures::scene(&scene, tz).unwrap_or_else(|| fixtures::scene("overview", tz).unwrap()))
    } else {
        let secrets = secrets::native();
        let from_env = if cfg!(debug_assertions) {
            Client::from_env()
        } else {
            None
        };
        let env = from_env.is_some();
        let grafana = from_env.or_else(|| Fetcher::kept_grafana(secrets.as_ref()));
        let url = grafana.as_ref().map(|c| c.base().to_owned());
        let github_env = if cfg!(debug_assertions) {
            github::Client::from_env()
        } else {
            None
        };
        let github_from_env = github_env.is_some();
        let github = github_env.or_else(|| Fetcher::kept_github(secrets.as_ref()));
        let fetch = match Fetcher::new(links::site(), secrets, grafana, github, app.handle()) {
            Ok(f) => Some(Arc::new(f)),
            Err(error) => {
                eprintln!("Couldn't start the network runtime: {error}");
                None
            }
        };
        // The section to open on, for screenshots (`REELSHELF_MONITOR_SECTION=transcoding`).
        let section = std::env::var("REELSHELF_MONITOR_SECTION")
            .ok()
            .and_then(|n| Section::named(&n));
        // Start signing in at launch, as Sign in does (`REELSHELF_MONITOR_SIGN_IN=1`), for scripted runs.
        let sign_in = std::env::var("REELSHELF_MONITOR_SIGN_IN").is_ok_and(|v| v == "1");
        app.init(move || {
            let mut s = Monitor::new(fetch, url, tz);
            s.grafana_form.from_env = env;
            s.github_form.from_env = github_from_env;
            if let Some(section) = section {
                s.section = section;
            }
            // Releases reads GitHub without signing in to Reelshelf.
            s.poll();
            if sign_in {
                s.sign_in();
            }
            s
        })
    };
    app.run();
}
