//! Storage: the totals, the object store in use, the bucket's size and objects over 30 days and its difference from
//! what the database accounts for, CloudFront's traffic and error rates over 7 days (CloudWatch's numbers, through
//! the API), clips by tier, the accounts holding the most, and exports.

use rune::{El, Tx, el};

use reelshelf_api::{OpsBucketMetrics, OpsCdnMetrics, OpsMetricPoint, OpsStorage, OpsStorageGap};
use reelshelf_app::theme::Look;

use super::chart::{self, Chart, Paint};
use super::parts::{self, Cell, Tone, column};
use crate::grafana::Series;
use crate::state::{Hover, Monitor};
use crate::units::{bytes, count, signed_bytes, when};
use crate::{alerts, copy};

/// Hover ids for the storage charts, apart from the dashboards' and the costs'.
const HOVER_BASE: u32 = 91_000;
const CHART_HEIGHT: f32 = 170.0;
const HOUR_MS: i64 = 3_600_000;
const DAY_MS: i64 = 86_400_000;

pub fn page(s: &Monitor, l: &Look, p: &Paint) -> El {
    let Some(v) = &s.storage.value else {
        return super::loading(s, l);
    };
    let mut cards = vec![totals(v, l), store(v, l)];
    if let Some(bucket) = &v.bucket {
        cards.push(bucket_card(s, bucket, l, p));
        if let Some(gap) = &bucket.gap {
            cards.push(gap_card(s, gap, l, p));
        }
    }
    if let Some(cdn) = &v.cdn {
        cards.push(cdn_card(s, cdn, l, p));
    }
    cards.extend([by_tier(v, l), top_accounts(v, l), exports(v, l)]);
    parts::page("storage", cards)
}

/// How a CloudWatch read went, as the API reports it for the bucket and the CDN.
struct Read<'a> {
    status: &'a str,
    error: Option<&'a str>,
    permission: Option<&'a str>,
    read_at: Option<chrono::DateTime<chrono::Utc>>,
    /// The words for `not_configured` and `unavailable`.
    not_configured: &'a str,
    unavailable: &'a str,
}

/// Why CloudWatch's numbers aren't shown, or when they were read.
fn read_state(s: &Monitor, l: &Look, r: Read<'_>) -> Option<El> {
    match r.status {
        "ok" => r
            .read_at
            .map(|at| parts::note(l, copy::last_read(&when(at, s.now_ms, &s.tz)))),
        "denied" => Some(parts::alert(
            l,
            copy::metrics_denied(r.permission.unwrap_or("cloudwatch:GetMetricData")),
        )),
        "failed" => Some(parts::alert(l, copy::metrics_failed(r.error.unwrap_or(copy::NO_ERROR)))),
        "unavailable" => Some(parts::note(l, r.unavailable)),
        _ => Some(parts::note(l, r.not_configured)),
    }
}

/// CloudWatch's points as a chart series, each moved by `shift` (to the middle of its day, say).
fn series(name: &str, points: &[OpsMetricPoint], shift: i64) -> Series {
    Series {
        name: name.to_owned(),
        points: points
            .iter()
            .map(|pt| (pt.at.timestamp_millis() + shift, Some(pt.value)))
            .collect(),
    }
}

/// The latest time across the series, else now.
fn latest(s: &Monitor, series: &[Series]) -> i64 {
    series
        .iter()
        .filter_map(|x| x.points.last().map(|pt| pt.0))
        .max()
        .unwrap_or(s.now_ms)
}

/// One storage chart: its key, its hover id's offset, the series and their unit, and the x range.
struct Plot<'a> {
    key: &'a str,
    index: u32,
    series: &'a [Series],
    unit: Option<&'a str>,
    span: (i64, i64),
    /// The y-axis' floor, for counts and rates that can't go below it.
    min: Option<f64>,
}

fn draw(s: &Monitor, p: &Paint, plot: Plot<'_>) -> El {
    let id = HOVER_BASE + plot.index;
    chart::time_series(
        &Chart {
            key: plot.key.to_owned(),
            series: plot.series,
            unit: plot.unit,
            decimals: None,
            min: plot.min,
            max: None,
            span: plot.span,
            size: (s.chart_width.max(240.0), CHART_HEIGHT),
            hover: s.hover.filter(|h| h.panel == id).map(|h| h.x),
            tz: &s.tz,
        },
        p,
        move |s: &mut Monitor, x: Option<f32>| {
            s.hover = x.map(|x| Hover { panel: id, x });
        },
    )
}

/// A chart's heading, as the dashboards' panels have.
fn chart_title(l: &Look, words: &str) -> El {
    parts::note(l, words)
}

fn bucket_card(s: &Monitor, b: &OpsBucketMetrics, l: &Look, p: &Paint) -> El {
    // Learns the cards' inner width, which the charts are drawn to.
    let measure =
        el().key("storage-width")
            .w_full()
            .h(0.0)
            .on_layout_tx(|s: &mut Monitor, r: [f32; 4], tx: &mut Tx| {
                if (s.chart_width - r[2]).abs() > 0.5 {
                    s.chart_width = r[2];
                } else {
                    tx.live_only();
                }
            });
    let mut children = vec![measure];
    children.extend(read_state(
        s,
        l,
        Read {
            status: &b.status,
            error: b.error.as_deref(),
            permission: b.permission.as_deref(),
            read_at: b.read_at,
            not_configured: copy::NO_STORE,
            unavailable: copy::BUCKET_UNAVAILABLE,
        },
    ));
    if b.status != "ok" {
        return parts::card(l, "bucket", copy::BUCKET_TITLE, children);
    }
    if b.sizes.is_empty() && b.objects.is_empty() {
        children.push(parts::note(l, copy::NO_BUCKET_DATA));
        return parts::card(l, "bucket", copy::BUCKET_TITLE, children);
    }

    let size: f64 = b.sizes.iter().filter_map(|c| c.points.last()).map(|pt| pt.value).sum();
    let mut stats = vec![parts::stat(l, copy::BUCKET_SIZE, bytes(size as i64), false)];
    if let Some(objects) = b.objects.last() {
        stats.push(parts::stat(l, copy::OBJECTS, count(objects.value as i64), false));
    }
    children.push(parts::stats(stats));

    // Each day's point at its middle (UTC), over the 30 days to the latest.
    let sizes: Vec<Series> = b
        .sizes
        .iter()
        .map(|c| series(copy::storage_class(&c.storage_type), &c.points, DAY_MS / 2))
        .collect();
    let objects = [series(copy::OBJECTS, &b.objects, DAY_MS / 2)];
    let last = latest(s, &sizes).max(latest(s, &objects));
    let span = (last + DAY_MS / 2 - 30 * DAY_MS, last + DAY_MS / 2);
    if !sizes.is_empty() {
        children.push(chart_title(l, copy::SIZE_BY_CLASS));
        children.push(draw(
            s,
            p,
            Plot {
                key: "bucket-sizes-chart",
                index: 0,
                series: &sizes,
                unit: Some("bytes"),
                span,
                min: Some(0.0),
            },
        ));
    }
    if !b.objects.is_empty() {
        children.push(chart_title(l, copy::OBJECTS_30_DAYS));
        children.push(draw(
            s,
            p,
            Plot {
                key: "bucket-objects-chart",
                index: 1,
                series: &objects,
                unit: None,
                span,
                min: None,
            },
        ));
    }
    children.push(parts::note(l, copy::BUCKET_NOTE));
    parts::card(l, "bucket", copy::BUCKET_TITLE, children)
}

fn gap_card(s: &Monitor, g: &OpsStorageGap, l: &Look, p: &Paint) -> El {
    let growing = alerts::gap_growing(g);
    let mut stats = vec![
        parts::stat(l, copy::BUCKET_SIZE, bytes(g.bucket_bytes), false),
        parts::stat(l, copy::DATABASE_ACCOUNTS_FOR, bytes(g.database_bytes), false),
        parts::stat(l, copy::DIFFERENCE, signed_bytes(g.difference_bytes), false),
    ];
    if let Some(growth) = g.growth_last_week_bytes {
        stats.push(parts::stat(l, copy::CHANGE_THIS_WEEK, signed_bytes(growth), growing));
    }
    let mut children = vec![parts::stats(stats)];
    if growing {
        children.push(parts::alert(
            l,
            copy::gap_growing(&bytes(g.growth_last_week_bytes.unwrap_or(0))),
        ));
    }
    if g.days.len() > 1 {
        let difference = [Series {
            name: copy::DIFFERENCE.to_owned(),
            points: g
                .days
                .iter()
                .filter_map(|d| d.day.and_hms_opt(12, 0, 0))
                .map(|t| t.and_utc().timestamp_millis())
                .zip(g.days.iter().map(|d| Some(d.difference_bytes as f64)))
                .collect(),
        }];
        // The days recorded so far, at least a week of them.
        let first = difference[0].points.first().map_or(s.now_ms, |pt| pt.0);
        let last = latest(s, &difference);
        let span = (
            (first - DAY_MS / 2).min(last + DAY_MS / 2 - 7 * DAY_MS),
            last + DAY_MS / 2,
        );
        children.push(chart_title(l, copy::DIFFERENCE_BY_DAY));
        children.push(draw(
            s,
            p,
            Plot {
                key: "gap-chart",
                index: 2,
                series: &difference,
                unit: Some("bytes"),
                span,
                min: None,
            },
        ));
    } else {
        children.push(parts::note(l, copy::GAP_NO_HISTORY));
    }
    children.push(parts::note(l, copy::GAP_NOTE));
    parts::card(l, "gap", copy::GAP_TITLE, children)
}

fn cdn_card(s: &Monitor, c: &OpsCdnMetrics, l: &Look, p: &Paint) -> El {
    let mut children = Vec::new();
    if let Some(id) = &c.distribution_id {
        children.push(parts::pair(l, copy::DISTRIBUTION, id.clone()));
    }
    children.extend(read_state(
        s,
        l,
        Read {
            status: &c.status,
            error: c.error.as_deref(),
            permission: c.permission.as_deref(),
            read_at: c.read_at,
            not_configured: copy::CDN_NOT_CONFIGURED,
            unavailable: copy::CDN_UNAVAILABLE,
        },
    ));
    if c.status != "ok" {
        return parts::card(l, "cdn", copy::CDN_TITLE, children);
    }
    if c.requests.is_empty() {
        children.push(parts::note(l, copy::NO_CDN_DATA));
        return parts::card(l, "cdn", copy::CDN_TITLE, children);
    }

    let requests: f64 = c.requests.iter().map(|pt| pt.value).sum();
    let downloaded: f64 = c.bytes_downloaded.iter().map(|pt| pt.value).sum();
    let mut stats = vec![
        parts::stat(l, copy::REQUESTS_7D, count(requests.round() as i64), false),
        parts::stat(l, copy::DOWNLOADED_7D, bytes(downloaded as i64), false),
    ];
    if let Some(rate) = c.server_error_rate_last_day {
        stats.push(parts::stat(
            l,
            copy::SERVER_ERRORS_24H,
            crate::units::format(rate, Some("percent"), Some(2)),
            rate > alerts::SERVER_ERROR_LIMIT,
        ));
    }
    children.push(parts::stats(stats));

    // Each hour's point at its middle, over the 7 days to the latest.
    let shift = HOUR_MS / 2;
    let req = [series(copy::REQUESTS_PER_HOUR, &c.requests, shift)];
    let down = [series(copy::DOWNLOADED_PER_HOUR, &c.bytes_downloaded, shift)];
    let errors = [
        series(copy::CLIENT_ERRORS, &c.client_error_rate, shift),
        series(copy::SERVER_ERRORS, &c.server_error_rate, shift),
    ];
    let last = latest(s, &req);
    let span = (last + shift - 7 * DAY_MS, last + shift);
    children.push(chart_title(l, copy::REQUESTS_PER_HOUR));
    children.push(draw(
        s,
        p,
        Plot {
            key: "cdn-requests-chart",
            index: 3,
            series: &req,
            unit: None,
            span,
            min: Some(0.0),
        },
    ));
    if !c.bytes_downloaded.is_empty() {
        children.push(chart_title(l, copy::DOWNLOADED_PER_HOUR));
        children.push(draw(
            s,
            p,
            Plot {
                key: "cdn-bytes-chart",
                index: 4,
                series: &down,
                unit: Some("bytes"),
                span,
                min: Some(0.0),
            },
        ));
    }
    if !c.client_error_rate.is_empty() || !c.server_error_rate.is_empty() {
        children.push(chart_title(l, copy::ERROR_RATES));
        children.push(draw(
            s,
            p,
            Plot {
                key: "cdn-errors-chart",
                index: 5,
                series: &errors,
                unit: Some("percent"),
                span,
                min: Some(0.0),
            },
        ));
    }
    children.push(parts::note(l, copy::CDN_NOTE));
    parts::card(l, "cdn", copy::CDN_TITLE, children)
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
