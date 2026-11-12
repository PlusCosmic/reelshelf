//! GitHub, for Releases: what is deployed (the image tags in PlusCosmic/infrastructure's compose files, each resolved
//! to its reelshelf commit and how far main is ahead of it), what is coming (the open image-bump PR, CI on main, the
//! open pull requests) and the uploader's latest tag and publish run. The app calls GitHub's REST API itself with a
//! fine-grained, read-only token (Contents, Pull requests and Actions on both repositories), which is never logged or
//! shown. Reading and parsing are plain Rust with unit tests on `tests/fixtures/github/`.

pub mod client;
pub mod deploy;
pub mod model;
pub mod releases;

pub use client::{Client, Error};
pub use model::{Checks, Commit, Pull, Run};
pub use releases::{Deployed, Environment, Releases};

/// The app's own repository, whose commits the images are built from.
pub const REELSHELF: &str = "PlusCosmic/reelshelf";
/// The repository whose compose files say what is deployed, and where image-bump PRs are opened.
pub const INFRASTRUCTURE: &str = "PlusCosmic/infrastructure";
/// The folder of compose files in [`INFRASTRUCTURE`].
pub const SERVICES: &str = "services";
/// The branch everything is measured against.
pub const MAIN: &str = "main";
/// The branch prefix `publish-app-images.yml` opens its bump PRs from.
pub const BUMP_BRANCH: &str = "automation/app-images-";
/// The workflows whose latest run on main Releases shows, by file, in this order.
pub const MAIN_WORKFLOWS: [&str; 3] = ["ci.yml", "publish-app-images.yml", "windlass.yml"];
/// The workflow that publishes the uploader on an `uploader-v*` tag.
pub const UPLOADER_WORKFLOW: &str = "publish-uploader.yml";
/// The uploader's release tags.
pub const UPLOADER_TAG: &str = "uploader-v";
