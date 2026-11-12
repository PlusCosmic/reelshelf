//! Renders the Admin section from the real Reelshelf headless, signed in with the token kept in the Keychain, for
//! screenshots without opening the window. It only reads (`GET /api/me`, `/api/admin/reports`, `/api/admin/accounts`,
//! one account's detail and `/api/admin/ops/*`); it changes nothing.
//!
//! `cargo run -p reelshelf-monitor --example admin_snap -- reports [open|taken_down|dismissed|removed] -- --snap out.png`
//! `cargo run -p reelshelf-monitor --example admin_snap -- accounts [query] -- --snap out.png --theme dark` (the query
//! defaults to the signed-in account's own username)
//! `cargo run -p reelshelf-monitor --example admin_snap -- overview -- --snap out.png`
//!
//! Everything after the second `--` goes to Rune. Debug builds read `REELSHELF_API_BASE_URL` first, as the app does.

use std::sync::Arc;

use chrono::Offset;
use reelshelf_api::{ApiClient, ClientApp};
use reelshelf_monitor::admin::{AdminTab, StatusFilter};
use reelshelf_monitor::session::SignIn;
use reelshelf_monitor::state::{Fetched, Load, Monitor, Section};
use reelshelf_monitor::{links, secrets, ui};

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let split = args.iter().position(|a| a == "--").unwrap_or(args.len());
    let (ours, rune_args) = (&args[..split], args.get(split + 1..).unwrap_or_default().to_vec());
    let view = ours.first().cloned().unwrap_or_else(|| "reports".to_owned());
    let mut extra = ours.get(1).cloned();
    let site = links::site();
    let api = Arc::new(ApiClient::for_app(
        reqwest::Client::new(),
        ClientApp::Monitor,
        &site,
        links::version(),
    ));
    let sign_in = SignIn::new(api.clone(), secrets::native(), &site);
    let runtime = tokio::runtime::Runtime::new().unwrap();
    let (session, open, resolved, results, detail, query, ops) = runtime.block_on(async {
        let session = sign_in.restore().await;
        assert!(session.signed_in(), "signed in with the kept token: {session:?}");
        let open = api.content_reports(None).await.expect("open reports");
        let resolved = api.content_reports(Some(true)).await.expect("reports dealt with");
        // Overview's other cards, so the snap is the window's (read-only, like the rest).
        let ops = (
            api.ops_storage().await.ok(),
            api.ops_transcoding().await.ok(),
            api.ops_ai().await.ok(),
        );
        let (results, detail, query) = match view.as_str() {
            "accounts" => {
                let query = extra.clone().unwrap_or_else(|| match &session {
                    reelshelf_monitor::session::Session::SignedIn { user: Some(u) } => u.username.clone(),
                    _ => String::new(),
                });
                let found = api.find_accounts(Some(&query)).await.expect("accounts");
                let detail = match found.first() {
                    Some(a) => Some(api.admin_account(a.id).await.expect("the account")),
                    None => None,
                };
                (Some(found), detail, Some(query))
            }
            _ => (None, None, None),
        };
        (session, open, resolved, results, detail, query, ops)
    });
    if query.is_some() {
        extra = query;
    }
    println!(
        "{} open reports, {} dealt with, {} accounts found",
        open.len(),
        resolved.len(),
        results.as_ref().map_or(0, Vec::len)
    );
    let tz = chrono::Local::now().offset().fix();
    rune::App::new(ui::spec())
        .chrome(ui::CHROME)
        .init(move || {
            let mut s = Monitor::new(None, None, tz);
            s.site = site;
            s.session = session;
            s.admin.open = Fetched::ready(open);
            s.admin.resolved = Fetched::ready(resolved);
            let (storage, transcoding, ai) = ops;
            s.storage = Fetched {
                value: storage,
                error: None,
            };
            s.transcoding = Fetched {
                value: transcoding,
                error: None,
            };
            s.ai = Fetched { value: ai, error: None };
            match view.as_str() {
                "overview" => s.section = Section::Overview,
                "accounts" => {
                    s.section = Section::Admin;
                    s.admin.tab = AdminTab::Accounts;
                    if let Some(query) = &extra {
                        s.admin.query.replace_text(query);
                        s.admin.searched = query.clone();
                    }
                    s.admin.results = results.map(Load::Ready);
                    s.admin.account = detail.as_ref().map(|d| d.account.id);
                    s.admin.detail = detail.map(Load::Ready);
                }
                _ => {
                    s.section = Section::Admin;
                    s.admin.tab = AdminTab::Reports;
                    s.admin.status = StatusFilter::ALL
                        .into_iter()
                        .find(|f| Some(f.status()) == extra.as_deref())
                        .unwrap_or(StatusFilter::Open);
                    s.admin.report = s.admin.reports().first().map(|r| r.id);
                }
            }
            s
        })
        .args(rune_args)
        .run();
}
