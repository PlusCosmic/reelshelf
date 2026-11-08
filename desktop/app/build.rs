//! On Windows, embeds the executable's icon, version and application manifest (`assets/`). The version is
//! `REELSHELF_APP_VERSION`, which packaging sets, else the crate's, as `links::version` reads it.

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=assets");
    println!("cargo:rerun-if-env-changed=REELSHELF_APP_VERSION");
    #[cfg(windows)]
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        resources();
    }
}

#[cfg(windows)]
fn resources() {
    let version = std::env::var("REELSHELF_APP_VERSION")
        .ok()
        .filter(|version| !version.is_empty())
        .unwrap_or_else(|| std::env::var("CARGO_PKG_VERSION").expect("cargo sets the crate's version"));
    let parts: Vec<u64> = version
        .split(['.', '-', '+'])
        .take(3)
        .map(|part| part.parse().unwrap_or(0))
        .collect();
    let packed = parts
        .iter()
        .chain(std::iter::repeat(&0))
        .take(4)
        .fold(0u64, |packed, part| (packed << 16) | (part & 0xffff));

    let mut resource = winresource::WindowsResource::new();
    resource
        .set_icon("assets/reelshelf.ico")
        .set_manifest_file("assets/reelshelf.exe.manifest")
        .set("ProductName", "Reelshelf")
        .set("FileDescription", "Reelshelf")
        .set("CompanyName", "Reelshelf")
        .set("OriginalFilename", "Reelshelf.exe")
        .set("InternalName", "Reelshelf")
        .set("FileVersion", &version)
        .set("ProductVersion", &version)
        .set_version_info(winresource::VersionInfo::FILEVERSION, packed)
        .set_version_info(winresource::VersionInfo::PRODUCTVERSION, packed);
    resource.compile().expect("the Windows resources compile");
}
