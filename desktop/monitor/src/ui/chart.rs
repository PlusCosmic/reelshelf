//! A time-series chart and a stat, drawn on Rune's canvas (`Shape` strokes and fills) with the axes' labels, the
//! legend and the hover readout as text elements placed over it. Reusable: the caller gives the series, the unit, the
//! range, the size and the pointer's x, and is told where the pointer goes.

use chrono::FixedOffset;
use rune::{Color, El, LineCap, PointerEvent, Shape, TextStyle, Tx, canvas, col, el, row, text};

use crate::copy;
use crate::grafana::Series;
use crate::units;

/// The colours a chart paints with, all from the theme.
#[derive(Clone, Debug)]
pub struct Paint {
    pub ink: Color,
    pub ink_soft: Color,
    pub grid: Color,
    pub surface: Color,
    pub accent: Color,
    pub series: Vec<Color>,
    /// The axes' and legend's text.
    pub small: TextStyle,
    pub small_strong: TextStyle,
    /// A stat's number.
    pub big: TextStyle,
}

impl Paint {
    pub fn series_color(&self, i: usize) -> Color {
        self.series[i % self.series.len().max(1)]
    }
}

/// What one chart shows.
pub struct Chart<'a> {
    /// Keys the chart's elements apart from other charts'.
    pub key: String,
    pub series: &'a [Series],
    pub unit: Option<&'a str>,
    pub decimals: Option<u32>,
    pub min: Option<f64>,
    pub max: Option<f64>,
    /// The range on the x-axis, epoch milliseconds.
    pub span: (i64, i64),
    pub size: (f32, f32),
    /// The pointer's x in the chart's points, when it's over this chart.
    pub hover: Option<f32>,
    pub tz: &'a FixedOffset,
}

const LEGEND_LINE: f32 = 18.0;
const X_LABELS: f32 = 18.0;
const DOT: f32 = 8.0;

fn measure(s: &str, font: TextStyle) -> f32 {
    rune::text::measure(s, &[], font, None).0
}

fn label(s: impl Into<String>, font: TextStyle, color: Color, at: (f32, f32)) -> El {
    text(s.into()).font(font).color(color).absolute(at.0, at.1)
}

/// Names longer than this are cut with an ellipsis in the legend and readout.
fn short_name(name: &str, max: usize) -> String {
    if name.chars().count() <= max {
        name.to_owned()
    } else {
        format!("{}…", name.chars().take(max - 1).collect::<String>())
    }
}

/// A legend entry: the series, where it sits (x, line's y) and its name as shown.
type LegendItem = (usize, f32, f32, String);

/// The legend: as many series as fit in two lines, then "+N more" (taking the last entry's place when it must).
fn legend(series: &[Series], width: f32, p: &Paint) -> (Vec<LegendItem>, Option<(f32, f32, String)>, f32) {
    let item_w = |name: &str| DOT + 6.0 + measure(name, p.small) + 14.0;
    let mut placed: Vec<LegendItem> = Vec::new();
    let (mut x, mut line) = (0.0, 0usize);
    for (i, s) in series.iter().enumerate() {
        let name = short_name(&s.name, 48);
        let w = item_w(&name);
        if x > 0.0 && x + w > width {
            line += 1;
            x = 0.0;
        }
        if line >= 2 {
            let mut rest = series.len() - i;
            let mut at = placed.last().map_or(0.0, |(_, lx, _, n)| lx + item_w(n));
            if at + measure(&copy::more_series(rest), p.small) > width
                && let Some((_, lx, _, _)) = placed.pop()
            {
                at = lx;
                rest += 1;
            }
            return (
                placed,
                Some((at, LEGEND_LINE, copy::more_series(rest))),
                2.0 * LEGEND_LINE,
            );
        }
        placed.push((i, x, line as f32 * LEGEND_LINE, name));
        x += w;
    }
    let lines = if series.is_empty() { 0 } else { line + 1 };
    (placed, None, lines as f32 * LEGEND_LINE)
}

/// The time series chart. `on_hover` hears the pointer's x in the chart's points, and None when it leaves.
pub fn time_series<S: 'static>(
    c: &Chart<'_>,
    p: &Paint,
    on_hover: impl Fn(&mut S, Option<f32>) + Clone + 'static,
) -> El {
    let (w, h) = c.size;
    let (from, to) = c.span;
    let span = (to - from).max(1) as f64;
    let (legend_items, more, legend_h) = legend(c.series, w, p);

    // The values' range, from zero unless the data goes below it or the panel says otherwise.
    let values = c.series.iter().flat_map(|s| s.points.iter().filter_map(|p| p.1));
    let (lo, hi) = values.fold((f64::INFINITY, f64::NEG_INFINITY), |(lo, hi), v| (lo.min(v), hi.max(v)));
    let (lo, hi) = if lo.is_finite() { (lo.min(0.0), hi) } else { (0.0, 1.0) };
    let (lo, hi) = (c.min.unwrap_or(lo), c.max.unwrap_or(hi));

    let y0 = 6.0;
    let y1 = (h - legend_h - X_LABELS - 4.0).max(y0 + 20.0);
    let count = (((y1 - y0) / 34.0) as usize).clamp(2, 6);
    let (lo, hi, ticks) = units::nice_ticks(lo, hi, count);
    let tick_labels: Vec<String> = ticks.iter().map(|t| units::format(*t, c.unit, c.decimals)).collect();
    let label_w = tick_labels.iter().map(|t| measure(t, p.small)).fold(0.0, f32::max);
    let x0 = label_w + 8.0;
    let x1 = (w - 6.0).max(x0 + 20.0);
    let px = |t: i64| x0 + ((t - from) as f64 / span) as f32 * (x1 - x0);
    let py = |v: f64| y1 - (((v - lo) / (hi - lo).max(f64::EPSILON)) as f32) * (y1 - y0);

    let mut shape = Shape::new();
    let mut labels = Vec::new();
    for (t, l) in ticks.iter().zip(&tick_labels) {
        let y = py(*t);
        shape = shape.line([x0, y], [x1, y], 1.0, LineCap::Butt, p.grid);
        labels.push(label(
            l.clone(),
            p.small,
            p.ink_soft,
            (x0 - 6.0 - measure(l, p.small), y - 8.0),
        ));
    }
    let time_count = (((x1 - x0) / 70.0) as usize).max(2);
    for (t, l) in units::time_ticks(from, to, time_count, c.tz) {
        let x = px(t);
        shape = shape.line([x, y1], [x, y1 + 4.0], 1.0, LineCap::Butt, p.grid);
        let lw = measure(&l, p.small);
        if x - lw / 2.0 >= x0 - label_w && x + lw / 2.0 <= w {
            labels.push(label(l, p.small, p.ink_soft, (x - lw / 2.0, y1 + 5.0)));
        }
    }

    // Each series as polylines broken where values are missing.
    for (i, s) in c.series.iter().enumerate() {
        let color = p.series_color(i);
        let mut run: Vec<[f32; 2]> = Vec::new();
        let flush = |run: &mut Vec<[f32; 2]>, shape: Shape| -> Shape {
            let out = match run.len() {
                0 => shape,
                1 => shape.fill(dot(run[0], 1.5), color),
                _ => shape.stroke(std::mem::take(run), 1.5, color),
            };
            run.clear();
            out
        };
        // Prometheus leaves out samples it has nothing for; a gap well past the series' step breaks the line, as
        // Grafana's does.
        let step = series_step(s);
        let mut last: Option<i64> = None;
        for (t, v) in &s.points {
            if last.is_some_and(|l| step.is_some_and(|step| t - l > step * 5 / 2)) {
                shape = flush(&mut run, shape);
            }
            match v {
                Some(v) if *t >= from && *t <= to => {
                    run.push([px(*t), py(*v)]);
                    last = Some(*t);
                }
                _ => {
                    shape = flush(&mut run, shape);
                    last = None;
                }
            }
        }
        shape = flush(&mut run, shape);
    }

    // Hover: a crosshair at the nearest sample, a dot on each line, and a readout of every value there.
    let mut readout = None;
    if let Some(hx) = c.hover.filter(|x| *x >= x0 && *x <= x1)
        && let Some(t) = nearest_time(c.series, from + (f64::from((hx - x0) / (x1 - x0)) * span) as i64)
    {
        let x = px(t);
        shape = shape.line([x, y0], [x, y1], 1.0, LineCap::Butt, p.ink_soft.alpha(0.6));
        let mut rows: Vec<(usize, f64)> = Vec::new();
        for (i, s) in c.series.iter().enumerate() {
            if let Some(v) = s.points.iter().find(|pt| pt.0 == t).and_then(|pt| pt.1) {
                shape = shape
                    .fill(dot([x, py(v)], 3.5), p.surface)
                    .fill(dot([x, py(v)], 2.5), p.series_color(i));
                rows.push((i, v));
            }
        }
        rows.sort_by(|a, b| b.1.total_cmp(&a.1));
        readout = Some(readout_card(c, p, t, &rows, x, (x0, x1, y0)));
    }

    let mut chart = el()
        .key(c.key.clone())
        .size(w, h)
        .child(canvas(shape).absolute(0.0, 0.0).size(w, h).a11y_hidden());
    for l in labels {
        chart = chart.child(l);
    }
    let legend_y = h - legend_h;
    for (i, x, y, name) in legend_items {
        chart = chart
            .child(
                canvas(Shape::new().fill(dot([DOT / 2.0, DOT / 2.0], DOT / 2.0), p.series_color(i)))
                    .absolute(x, legend_y + y + 5.0)
                    .size(DOT, DOT)
                    .a11y_hidden(),
            )
            .child(label(name, p.small, p.ink_soft, (x + DOT + 6.0, legend_y + y)));
    }
    if let Some((x, y, more)) = more {
        chart = chart.child(label(more, p.small, p.ink_soft, (x, legend_y + y)));
    }
    if let Some(card) = readout {
        chart = chart.child(card);
    }
    let leave = on_hover.clone();
    chart
        .on_pointer_move_local(move |s: &mut S, e: PointerEvent, _: &mut Tx| on_hover(s, Some(e.x)))
        .on_hover_change(move |s: &mut S, inside: bool, _: &mut Tx| {
            if !inside {
                leave(s, None);
            }
        })
}

/// The series' sampling step: the shortest time between two of its points.
fn series_step(s: &Series) -> Option<i64> {
    s.points.windows(2).map(|w| w[1].0 - w[0].0).filter(|d| *d > 0).min()
}

/// The sample time nearest `t` across every series.
fn nearest_time(series: &[Series], t: i64) -> Option<i64> {
    series
        .iter()
        .flat_map(|s| s.points.iter().filter(|p| p.1.is_some()).map(|p| p.0))
        .min_by_key(|pt| (pt - t).abs())
}

fn readout_card(c: &Chart<'_>, p: &Paint, t: i64, rows: &[(usize, f64)], x: f32, (x0, x1, y0): (f32, f32, f32)) -> El {
    const MAX_ROWS: usize = 8;
    let mut card = col()
        .key(format!("{}-readout", c.key))
        .padding(8.0)
        .gap(3.0)
        .fill(p.surface)
        .border(1.0, p.grid)
        .radius(4.0)
        .child(
            text(units::readout_time(t, c.span.1 - c.span.0, c.tz))
                .font(p.small_strong)
                .color(p.ink),
        );
    let mut widest: f32 = measure(&units::readout_time(t, c.span.1 - c.span.0, c.tz), p.small_strong);
    for (i, v) in rows.iter().take(MAX_ROWS) {
        let name = short_name(&c.series[*i].name, 40);
        let value = units::format(*v, c.unit, c.decimals);
        widest = widest.max(DOT + 6.0 + measure(&name, p.small) + 16.0 + measure(&value, p.small_strong));
        card = card.child(
            row()
                .gap(6.0)
                .center_y()
                .child(
                    canvas(Shape::new().fill(dot([DOT / 2.0, DOT / 2.0], DOT / 2.0), p.series_color(*i)))
                        .size(DOT, DOT)
                        .a11y_hidden(),
                )
                .child(text(name).font(p.small).color(p.ink_soft))
                .child(el().grow(1.0).w(10.0))
                .child(text(value).font(p.small_strong).color(p.ink)),
        );
    }
    if rows.len() > MAX_ROWS {
        card = card.child(
            text(copy::more_series(rows.len() - MAX_ROWS))
                .font(p.small)
                .color(p.ink_soft),
        );
    }
    let width = widest + 18.0;
    // Beside the crosshair, on whichever side has room.
    let left = if x + 12.0 + width <= x1 {
        x + 12.0
    } else {
        (x - 12.0 - width).max(x0)
    };
    card.w(width).absolute(left, y0 + 4.0)
}

/// A circle as a polygon.
fn dot(c: [f32; 2], r: f32) -> Vec<[f32; 2]> {
    (0..16)
        .map(|i| {
            let a = i as f32 / 16.0 * std::f32::consts::TAU;
            [c[0] + r * a.cos(), c[1] + r * a.sin()]
        })
        .collect()
}

/// A stat: one number, reduced from the first series, with a sparkline under it when there's a line to draw.
pub fn stat(
    key: &str,
    series: &[Series],
    reduce: &str,
    unit: Option<&str>,
    decimals: Option<u32>,
    size: (f32, f32),
    p: &Paint,
) -> El {
    let (w, h) = size;
    let first = series.first();
    let value = first.and_then(|s| s.reduce(reduce));
    let words = value.map_or_else(|| copy::NO_DATA.to_owned(), |v| units::format(v, unit, decimals));
    let mut out = el().key(key.to_owned()).size(w, h);
    if let Some(s) = first.filter(|s| s.points.iter().filter(|p| p.1.is_some()).count() > 2) {
        let (lo, hi) = s
            .points
            .iter()
            .filter_map(|p| p.1)
            .fold((f64::INFINITY, f64::NEG_INFINITY), |(a, b), v| (a.min(v), b.max(v)));
        let (t0, t1) = (s.points[0].0, s.points[s.points.len() - 1].0);
        let top = h * 0.55;
        let pts: Vec<[f32; 2]> = s
            .points
            .iter()
            .filter_map(|pt| pt.1.map(|v| (pt.0, v)))
            .map(|(t, v)| {
                let x = ((t - t0) as f64 / (t1 - t0).max(1) as f64) as f32 * w;
                let y = h - ((v - lo) / (hi - lo).max(f64::EPSILON)) as f32 * (h - top);
                [x, y]
            })
            .collect();
        let mut area = pts.clone();
        area.push([w, h]);
        area.push([0.0, h]);
        out = out.child(
            canvas(
                Shape::new()
                    .fill(area, p.accent.alpha(0.12))
                    .stroke(pts, 1.5, p.accent.alpha(0.7)),
            )
            .absolute(0.0, 0.0)
            .size(w, h)
            .a11y_hidden(),
        );
    }
    let ink = if value.is_some() { p.accent } else { p.ink_soft };
    out.child(
        row().absolute(0.0, 0.0).size(w, h * 0.6).center().child(
            text(words)
                .font(if value.is_some() { p.big } else { p.small })
                .color(ink),
        ),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn long_names_are_cut() {
        assert_eq!(short_name("abc", 5), "abc");
        assert_eq!(short_name("abcdefgh", 5), "abcd…");
    }

    #[test]
    fn the_step_is_the_shortest_spacing() {
        let s = Series {
            name: "a".into(),
            points: vec![(0, Some(1.0)), (60, Some(1.0)), (6000, Some(2.0))],
        };
        assert_eq!(series_step(&s), Some(60));
    }

    #[test]
    fn the_nearest_sample_wins() {
        let s = vec![Series {
            name: "a".into(),
            points: vec![(0, Some(1.0)), (100, None), (200, Some(2.0))],
        }];
        assert_eq!(nearest_time(&s, 90), Some(0));
        assert_eq!(nearest_time(&s, 160), Some(200));
    }
}
