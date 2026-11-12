//! Costs: this month's spend across providers, then a card per provider set up on the API with its month, last month,
//! its last 30 days drawn with the time series chart and its breakdown by service or model (Stripe's card is revenue,
//! gross and net), how its latest read went, and which providers aren't set up and where their keys go.

use rune::{El, Tx, el};

use reelshelf_api::{OpsCostProvider, OpsCosts};
use reelshelf_app::theme::Look;

use super::chart::{self, Chart, Paint};
use super::parts::{self, Cell, Tone, column};
use crate::copy;
use crate::grafana::Series;
use crate::state::{Hover, Monitor};
use crate::units::when;

/// Hover ids for the cost charts, apart from the dashboards' panel ids.
const HOVER_BASE: u32 = 90_000;
const CHART_HEIGHT: f32 = 190.0;
const DAY_MS: i64 = 86_400_000;

pub fn page(s: &Monitor, l: &Look, p: &Paint) -> El {
    let Some(v) = &s.costs.value else {
        return super::loading(s, l);
    };
    let mut cards = vec![summary(v, l)];
    for (i, provider) in v.providers.iter().enumerate() {
        if provider.status != "not_configured" {
            cards.push(card(s, provider, i, l, p));
        }
    }
    let unset: Vec<&OpsCostProvider> = v.providers.iter().filter(|p| p.status == "not_configured").collect();
    if !unset.is_empty() {
        let mut children = vec![parts::note(l, copy::NOT_SET_UP_INTRO)];
        children.extend(unset.iter().map(|p| parts::note(l, copy::cost_setting(&p.provider))));
        cards.push(parts::card(l, "costs-not-set-up", copy::NOT_SET_UP_TITLE, children));
    }
    parts::page("costs", cards)
}

/// A provider's amount in its currency, or "–".
fn amount(p: &OpsCostProvider, value: Option<f64>) -> String {
    match (value, p.currency.as_deref()) {
        (Some(v), Some(currency)) => copy::money(v, currency),
        _ => copy::NOT_KNOWN.to_owned(),
    }
}

fn status(p: &OpsCostProvider) -> Cell {
    match p.status.as_str() {
        "ok" => Cell::new(copy::COST_OK).tone(Tone::Soft),
        "pending" => Cell::new(copy::COST_PENDING).tone(Tone::Soft),
        "failed" => Cell::new(copy::COST_FAILED).tone(Tone::Danger),
        _ => Cell::new(copy::COST_NOT_SET_UP).tone(Tone::Soft),
    }
}

/// Stripe's net revenue this month, when it was read.
pub fn net_revenue(v: &OpsCosts) -> Option<(f64, &str)> {
    v.providers.iter().find_map(|p| {
        let revenue = p.revenue.as_ref()?;
        Some((revenue.net_month_to_date, p.currency.as_deref()?))
    })
}

fn summary(v: &OpsCosts, l: &Look) -> El {
    let mut stats = vec![parts::stat(
        l,
        copy::SPEND_THIS_MONTH,
        copy::money(v.month_to_date_usd, "USD"),
        false,
    )];
    if let Some((net, currency)) = net_revenue(v) {
        stats.push(parts::stat(
            l,
            copy::REVENUE_THIS_MONTH,
            copy::money(net, currency),
            false,
        ));
    }
    let rows = v
        .providers
        .iter()
        .map(|p| {
            vec![
                Cell::new(copy::provider(&p.provider)).tone(Tone::Strong),
                status(p),
                Cell::new(amount(p, p.month_to_date)),
                Cell::new(amount(p, p.last_month)).tone(Tone::Soft),
            ]
        })
        .collect();
    // Learns the cards' inner width, which the charts are drawn to.
    let measure = el()
        .key("costs-width")
        .w_full()
        .h(0.0)
        .on_layout_tx(|s: &mut Monitor, r: [f32; 4], tx: &mut Tx| {
            if (s.chart_width - r[2]).abs() > 0.5 {
                s.chart_width = r[2];
            } else {
                tx.live_only();
            }
        });
    parts::card(
        l,
        "costs-summary",
        copy::THIS_MONTH_TITLE,
        vec![
            measure,
            parts::stats(stats),
            parts::table(
                l,
                "costs-table",
                &[
                    column(copy::PROVIDER, 0.0),
                    column(copy::STATUS, 110.0),
                    column(copy::THIS_MONTH, 110.0),
                    column(copy::LAST_MONTH, 110.0),
                ],
                rows,
            ),
            parts::note(l, copy::SPEND_TOTAL_NOTE),
        ],
    )
}

fn card(s: &Monitor, p: &OpsCostProvider, index: usize, l: &Look, paint: &Paint) -> El {
    let key = format!("costs-{}", p.provider);
    let mut children = Vec::new();
    match p.status.as_str() {
        "failed" => {
            children.push(parts::alert(
                l,
                copy::couldnt_read(p.error.as_deref().unwrap_or(copy::NO_ERROR)),
            ));
            if let Some(at) = p.last_success_at {
                children.push(parts::note(l, copy::showing_from(&when(at, s.now_ms, &s.tz))));
            }
        }
        "pending" => children.push(parts::note(l, copy::PENDING_NOTE)),
        _ => {
            if let Some(at) = p.last_success_at {
                children.push(parts::note(l, copy::last_read(&when(at, s.now_ms, &s.tz))));
            }
        }
    }
    if p.last_success_at.is_none() {
        return parts::card(l, &key, copy::provider(&p.provider), children);
    }

    children.push(parts::stats(match &p.revenue {
        Some(r) => {
            let currency = p.currency.as_deref().unwrap_or("USD");
            vec![
                parts::stat(
                    l,
                    copy::GROSS_THIS_MONTH,
                    copy::money(r.gross_month_to_date, currency),
                    false,
                ),
                parts::stat(
                    l,
                    copy::NET_THIS_MONTH,
                    copy::money(r.net_month_to_date, currency),
                    false,
                ),
                parts::stat(
                    l,
                    copy::GROSS_LAST_MONTH,
                    copy::money(r.gross_last_month, currency),
                    false,
                ),
                parts::stat(l, copy::NET_LAST_MONTH, copy::money(r.net_last_month, currency), false),
            ]
        }
        None => {
            let mut stats = vec![
                parts::stat(l, copy::THIS_MONTH, amount(p, p.month_to_date), false),
                parts::stat(l, copy::LAST_MONTH, amount(p, p.last_month), false),
            ];
            if p.balance.is_some() {
                stats.push(parts::stat(l, copy::CREDIT_LEFT, amount(p, p.balance), false));
            }
            stats
        }
    }));

    if p.daily.is_empty() {
        children.push(parts::note(l, copy::NO_DAILY));
    } else {
        children.push(daily_chart(s, p, index, &key, paint));
    }

    if !p.breakdown.is_empty() {
        let rows = p
            .breakdown
            .iter()
            .map(|item| vec![Cell::new(item.name.clone()), Cell::new(amount(p, Some(item.amount)))])
            .collect();
        children.push(parts::table(
            l,
            &format!("{key}-breakdown"),
            &[
                column(copy::breakdown(&p.provider), 0.0),
                column(copy::LAST_30_DAYS, 120.0),
            ],
            rows,
        ));
    }
    if p.revenue.is_some() {
        children.push(parts::note(l, copy::REVENUE_NOTE));
    } else if p.provider == "openrouter" {
        children.push(parts::note(l, copy::OPENROUTER_HISTORY));
    }
    parts::card(l, &key, copy::provider(&p.provider), children)
}

/// The provider's days as one series, each day's point at its middle (UTC), over the 30 days to today.
fn daily_chart(s: &Monitor, p: &OpsCostProvider, index: usize, key: &str, paint: &Paint) -> El {
    let points: Vec<(i64, Option<f64>)> = p
        .daily
        .iter()
        .filter_map(|d| d.date.and_hms_opt(12, 0, 0))
        .map(|t| t.and_utc().timestamp_millis())
        .zip(p.daily.iter().map(|d| Some(d.amount)))
        .collect();
    let series = [Series {
        name: copy::provider(&p.provider).to_owned(),
        points,
    }];
    let last = series[0].points.last().map_or(s.now_ms, |pt| pt.0);
    let span = (last + DAY_MS / 2 - 30 * DAY_MS, last + DAY_MS / 2);
    let unit = match p.currency.as_deref() {
        Some("GBP") => Some("currencyGBP"),
        Some("EUR") => Some("currencyEUR"),
        Some("USD") => Some("currencyUSD"),
        _ => None,
    };
    let id = HOVER_BASE + index as u32;
    chart::time_series(
        &Chart {
            key: format!("{key}-chart"),
            series: &series,
            unit,
            decimals: None,
            min: None,
            max: None,
            span,
            size: (s.chart_width.max(240.0), CHART_HEIGHT),
            hover: s.hover.filter(|h| h.panel == id).map(|h| h.x),
            tz: &s.tz,
        },
        paint,
        move |s: &mut Monitor, x: Option<f32>| {
            s.hover = x.map(|x| Hover { panel: id, x });
        },
    )
}
