//! The Reelshelf uploader's core (`reelshelf-core`): everything the Windows app does that isn't Windows. It watches the
//! recordings folder, keeps the ledger, matches books, uploads clips one at a time, and works out the tray's state and
//! the notifications, with every word the app shows in [`copy`].
//!
//! Windows-only parts sit behind the traits in [`platform`] (`CredentialStore`, `GameActivity`, `Notifier`, `Startup`
//! and `Clock`), and the API behind [`api::UploaderApi`], which the `reelshelf-api` crate implements. So the core builds
//! and is tested on Linux. The build spec is `reelshelf-docs/desktop-uploader/README.md`, Part 3.

pub mod api;
pub mod copy;
pub mod dates;
pub mod engine;
pub mod files;
pub mod ledger;
pub mod logs;
pub mod matching;
pub mod media;
pub mod monitor;
pub mod notifications;
pub mod platform;
pub mod recent;
pub mod settings;
pub mod sign_in;
pub mod state;
pub mod uploads;
pub mod watcher;

use std::future::Future;
use std::pin::Pin;

/// A boxed future that can move between threads, for traits that must stay object safe.
pub type BoxFuture<'a, T> = Pin<Box<dyn Future<Output = T> + Send + 'a>>;
