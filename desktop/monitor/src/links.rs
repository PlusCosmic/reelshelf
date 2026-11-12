//! Where the monitor talks to, the version it calls itself and the name it signs in as.
//!
//! Release builds always use `https://reelshelf.app`; debug builds read `REELSHELF_API_BASE_URL` (a local or dev API),
//! as the uploader's do (`reelshelf_app::links::site`). The version is compiled in from `REELSHELF_APP_VERSION`, else
//! the crate's, and goes out as `X-Reelshelf-Client: monitor/<version>`.

/// The site the API is served from, without a trailing slash.
pub fn site() -> String {
    reelshelf_app::links::site()
}

pub fn version() -> &'static str {
    option_env!("REELSHELF_APP_VERSION").unwrap_or(env!("CARGO_PKG_VERSION"))
}

/// "reelshelf.app/device", as the sign-in names the page.
pub fn device_page(site: &str) -> String {
    reelshelf_app::links::device_page(site)
}

/// The Mac's name, which it signs in as and Settings → Devices shows: its Computer Name on macOS.
pub fn device_name() -> String {
    #[cfg(target_os = "macos")]
    {
        let name = std::process::Command::new("scutil")
            .args(["--get", "ComputerName"])
            .output()
            .ok()
            .filter(|out| out.status.success())
            .and_then(|out| String::from_utf8(out.stdout).ok())
            .map(|name| name.trim().to_owned())
            .filter(|name| !name.is_empty());
        if let Some(name) = name {
            return name;
        }
    }
    reelshelf_app::platform::device_name()
}
