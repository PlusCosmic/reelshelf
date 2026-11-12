//! Storage: the totals, the object store in use, clips by tier, the accounts holding the most, and exports.

use rune::El;

use reelshelf_api::OpsStorage;
use reelshelf_app::theme::Look;

use super::parts::{self, Cell, Tone, column};
use crate::copy;
use crate::state::Monitor;
use crate::units::{bytes, count};

pub fn page(s: &Monitor, l: &Look) -> El {
    let Some(v) = &s.storage.value else {
        return super::loading(s, l);
    };
    parts::page(
        "storage",
        vec![
            totals(v, l),
            store(v, l),
            by_tier(v, l),
            top_accounts(v, l),
            exports(v, l),
        ],
    )
}

fn totals(v: &OpsStorage, l: &Look) -> El {
    parts::card(
        l,
        "totals",
        copy::TOTALS,
        vec![parts::stats(vec![
            parts::stat(l, copy::CLIPS, count(v.total.clips), false),
            parts::stat(l, copy::STORED, bytes(v.total.bytes), false),
            parts::stat(l, copy::IN_THE_BIN, count(v.total.binned_clips), false),
            parts::stat(l, copy::IN_THE_BIN, bytes(v.total.binned_bytes), false),
        ])],
    )
}

fn store(v: &OpsStorage, l: &Look) -> El {
    let st = &v.store;
    if !st.configured {
        return parts::card(
            l,
            "store",
            copy::OBJECT_STORE,
            vec![parts::alert(l, copy::NO_STORE.to_owned())],
        );
    }
    let mut rows = Vec::new();
    if let Some(kind) = &st.kind {
        rows.push(parts::pair(l, copy::STORE_KIND, copy::store_kind(kind).to_owned()));
    }
    for (label, value) in [
        (copy::STORE_REGION, &st.region),
        (copy::STORE_ENDPOINT, &st.endpoint_host),
        (copy::STORE_BUCKET, &st.bucket),
        (copy::STORE_MEDIA, &st.media_base_url),
    ] {
        if let Some(value) = value {
            rows.push(parts::pair(l, label, value.clone()));
        }
    }
    parts::card(l, "store", copy::OBJECT_STORE, rows)
}

fn by_tier(v: &OpsStorage, l: &Look) -> El {
    let rows = v
        .by_tier
        .iter()
        .map(|t| {
            vec![
                Cell::new(copy::tier(&t.tier)).tone(Tone::Strong),
                Cell::count(t.accounts, false),
                Cell::count(t.clips, false),
                Cell::new(bytes(t.bytes)),
            ]
        })
        .collect();
    parts::card(
        l,
        "by-tier",
        copy::BY_TIER,
        vec![parts::table(
            l,
            "by-tier-table",
            &[
                column(copy::TIER, 0.0),
                column(copy::ACCOUNTS, 90.0),
                column(copy::CLIPS, 90.0),
                column(copy::STORED, 100.0),
            ],
            rows,
        )],
    )
}

fn top_accounts(v: &OpsStorage, l: &Look) -> El {
    let rows = v
        .top_accounts
        .iter()
        .map(|a| {
            vec![
                Cell::new(a.username.clone()).tone(Tone::Strong),
                Cell::new(copy::tier(&a.tier)).tone(Tone::Soft),
                Cell::count(a.clips, false),
                Cell::new(bytes(a.bytes)),
            ]
        })
        .collect();
    parts::card(
        l,
        "top-accounts",
        copy::TOP_ACCOUNTS,
        vec![parts::table(
            l,
            "top-accounts-table",
            &[
                column(copy::ACCOUNT, 0.0),
                column(copy::TIER, 90.0),
                column(copy::CLIPS, 90.0),
                column(copy::STORED, 100.0),
            ],
            rows,
        )],
    )
}

fn exports(v: &OpsStorage, l: &Look) -> El {
    let e = &v.exports;
    parts::card(
        l,
        "exports",
        copy::EXPORTS,
        vec![parts::stats(vec![
            parts::stat(l, copy::EXPORTS_HELD, count(e.held), false),
            parts::stat(l, copy::EXPORTS_HELD_BYTES, bytes(e.held_bytes), false),
            parts::stat(l, copy::PENDING, count(e.pending), false),
            parts::stat(l, copy::RUNNING, count(e.running), false),
        ])],
    )
}
