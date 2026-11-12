//! Reelshelf's monitoring app for the Mac (`reelshelf-monitor`): a Rune window with a sidebar of sections. It signs in
//! to Reelshelf as the monitor (a device token in the Keychain) for Overview, Storage, Transcoding, AI pipelines and Admin,
//! and draws Grafana's dashboards natively from data Grafana runs for it.

pub mod admin;
pub mod alerts;
pub mod copy;
pub mod fetch;
pub mod fixtures;
pub mod github;
pub mod grafana;
pub mod links;
pub mod secrets;
pub mod session;
pub mod state;
pub mod ui;
pub mod units;
