//! Grafana, read through its HTTP API only: the dashboard list, one dashboard, and each panel's queries run by
//! Grafana (`/api/ds/query`), so every data source it has works without the app reaching any of them.

pub mod client;
pub mod frames;
pub mod model;
pub mod query;

pub use client::{Client, Error};
pub use frames::{PanelData, Series};
pub use model::{Dashboard, Panel, PanelKind, SearchHit};
pub use query::TimeRange;
