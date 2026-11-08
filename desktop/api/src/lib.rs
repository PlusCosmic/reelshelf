//! `reelshelf-api`: the Reelshelf API calls the desktop uploader makes, and their types.
//!
//! The types follow the API's OpenAPI document (`frontend/src/api-client/Reelshelf.json`), which
//! `tests/contract.rs` checks them against, so an API change that would break the app fails `cargo test`.

mod client;
pub mod endpoints;
mod error;
pub mod types;

pub use client::{ApiClient, CLIENT_HEADER};
pub use error::{ApiError, Problem};
pub use types::*;
