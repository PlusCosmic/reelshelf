//! Where the app talks to and links to, and the version it calls itself.
//!
//! Release builds always use `https://reelshelf.app`. Debug builds read `REELSHELF_API_BASE_URL` (a local or dev
//! API), falling back to the same. The version is compiled in from `REELSHELF_APP_VERSION`, which packaging sets, else
//! the crate's.

use reelshelf_core::notifications::NotificationAction;
use uuid::Uuid;

pub const PRODUCTION_SITE: &str = "https://reelshelf.app";

/// The app's Store ID from Partner Center, which T8 and N4 open.
pub const STORE_PRODUCT_ID: &str = "9PC7WVJ4H5MD";

/// The app's reverse-DNS id: the single-instance pipe's name and the tray's id.
pub const APP_ID: &str = "app.reelshelf.uploader";

/// The version `X-Reelshelf-Client` sends and S5 shows.
pub fn version() -> &'static str {
    option_env!("REELSHELF_APP_VERSION").unwrap_or(env!("CARGO_PKG_VERSION"))
}

/// The site the API is served from, without a trailing slash.
pub fn site() -> String {
    site_from(if cfg!(debug_assertions) {
        std::env::var("REELSHELF_API_BASE_URL").ok()
    } else {
        None
    })
}

/// The site for a configured base URL: its scheme, host and port, when it is an absolute http(s) URL.
pub fn site_from(configured: Option<String>) -> String {
    configured
        .as_deref()
        .map(str::trim)
        .and_then(origin)
        .unwrap_or_else(|| PRODUCTION_SITE.to_owned())
}

fn origin(url: &str) -> Option<String> {
    let (scheme, rest) = url.split_once("://")?;
    if !matches!(scheme, "http" | "https") {
        return None;
    }
    let authority = rest.split(['/', '?', '#']).next()?;
    (!authority.is_empty() && !authority.contains(char::is_whitespace)).then(|| format!("{scheme}://{authority}"))
}

/// "reelshelf.app/device", as S1 names the page: the site's host (and port) and `/device`.
pub fn device_page(site: &str) -> String {
    let host = site.split_once("://").map_or(site, |(_, host)| host);
    format!("{host}/device")
}

/// A page the app opens in the browser (or the Store).
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Link {
    Library,
    Plans,
    AddGame,
    Store,
    /// A clip on its book.
    Clip {
        book_slug: String,
        clip_id: Uuid,
    },
    /// Any other address the API gave, such as the device page with its code filled in.
    Url(String),
}

impl Link {
    pub fn url(&self, site: &str) -> String {
        match self {
            Link::Library | Link::AddGame => site.to_owned(),
            Link::Plans => format!("{site}/plan"),
            // The Store's web listing, which hands over to the Store app; the browser opens no `ms-windows-store:`.
            Link::Store => format!("https://apps.microsoft.com/detail/{STORE_PRODUCT_ID}"),
            Link::Clip { book_slug, clip_id } => format!("{site}/games/{}/{clip_id}", encode(book_slug)),
            Link::Url(url) => url.clone(),
        }
    }
}

/// The scheme the package declares, so a notification's button reaches the running app (or starts it).
pub const SCHEME: &str = "reelshelf-app";

/// A notification button's link: `reelshelf-app://choose-book?folder=Marvel%20Rivals`.
pub fn app_link(action: &NotificationAction) -> String {
    let path = match action {
        NotificationAction::OpenLibrary => "open-library".to_owned(),
        NotificationAction::ChooseBook(folder) => format!("choose-book?folder={}", encode(folder)),
        NotificationAction::NotNow => "not-now".to_owned(),
        NotificationAction::SeePlans => "see-plans".to_owned(),
        NotificationAction::OpenStore => "open-store".to_owned(),
        NotificationAction::SignIn => "sign-in".to_owned(),
    };
    format!("{SCHEME}://{path}")
}

/// The action an app link names. Anyone can hand the app a link, so anything else is None.
pub fn parse_app_link(url: &str) -> Option<NotificationAction> {
    let rest = url.trim().strip_prefix(SCHEME)?.strip_prefix("://")?;
    let (path, query) = rest.split_once('?').unwrap_or((rest, ""));
    match path.trim_end_matches('/') {
        "open-library" => Some(NotificationAction::OpenLibrary),
        "choose-book" => {
            let folder = query
                .split('&')
                .find_map(|pair| pair.strip_prefix("folder="))
                .map(decode)
                .unwrap_or_default()?;
            Some(NotificationAction::ChooseBook(folder))
        }
        "not-now" => Some(NotificationAction::NotNow),
        "see-plans" => Some(NotificationAction::SeePlans),
        "open-store" => Some(NotificationAction::OpenStore),
        "sign-in" => Some(NotificationAction::SignIn),
        _ => None,
    }
}

/// Percent-decodes a query value; None when it isn't valid UTF-8 or has a bad escape.
fn decode(value: &str) -> Option<String> {
    let bytes = value.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut index = 0;
    while index < bytes.len() {
        match bytes[index] {
            b'%' => {
                let hex = value.get(index + 1..index + 3)?;
                out.push(u8::from_str_radix(hex, 16).ok()?);
                index += 3;
            }
            b'+' => {
                out.push(b' ');
                index += 1;
            }
            byte => {
                out.push(byte);
                index += 1;
            }
        }
    }
    String::from_utf8(out).ok()
}

/// Percent-encodes a path segment.
fn encode(segment: &str) -> String {
    segment
        .bytes()
        .map(|byte| match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (byte as char).to_string(),
            other => format!("%{other:02X}"),
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_site_is_production_unless_a_debug_build_names_another() {
        assert_eq!(site_from(None), "https://reelshelf.app");
        assert_eq!(
            site_from(Some(" http://localhost:5173/api/ ".into())),
            "http://localhost:5173"
        );
        assert_eq!(site_from(Some("ftp://example.com".into())), "https://reelshelf.app");
        assert_eq!(site_from(Some("not a url".into())), "https://reelshelf.app");
        assert_eq!(device_page("https://reelshelf.app"), "reelshelf.app/device");
        assert_eq!(device_page("http://localhost:5173"), "localhost:5173/device");
    }

    #[test]
    fn links_point_where_the_site_has_them() {
        let site = "https://reelshelf.app";
        let id = Uuid::nil();
        assert_eq!(Link::Plans.url(site), "https://reelshelf.app/plan");
        assert_eq!(
            Link::Clip {
                book_slug: "apex legends".into(),
                clip_id: id
            }
            .url(site),
            format!("https://reelshelf.app/games/apex%20legends/{id}")
        );
        assert_eq!(Link::Store.url(site), "https://apps.microsoft.com/detail/9PC7WVJ4H5MD");
        assert!(!version().is_empty());
    }

    #[test]
    fn notification_links_round_trip_and_nothing_else_parses() {
        for action in [
            NotificationAction::OpenLibrary,
            NotificationAction::ChooseBook("Marvel Rivals".into()),
            NotificationAction::ChooseBook(String::new()),
            NotificationAction::NotNow,
            NotificationAction::SeePlans,
            NotificationAction::OpenStore,
            NotificationAction::SignIn,
        ] {
            assert_eq!(parse_app_link(&app_link(&action)), Some(action));
        }
        assert_eq!(
            app_link(&NotificationAction::ChooseBook("Marvel Rivals".into())),
            "reelshelf-app://choose-book?folder=Marvel%20Rivals"
        );
        assert_eq!(parse_app_link("https://reelshelf.app"), None);
        assert_eq!(parse_app_link("reelshelf-app://delete-everything"), None);
        assert_eq!(parse_app_link("reelshelf-app://choose-book?folder=%ZZ"), None);
    }
}
