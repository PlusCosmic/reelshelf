# Reelshelf Monitor (spike)

A Mac app, drawn with Rune, that will show everything about Reelshelf in one place. For now only **Dashboards**
works: it lists Grafana's dashboards and draws one natively, its panels placed by their grid positions, with a time
range picker (1h, 6h, 24h, 7d) and Refresh. The other sections in the sidebar are placeholders.

It talks only to Grafana: `/api/search`, `/api/dashboards/uid/{uid}`, and each panel's targets through
`POST /api/ds/query` with the dashboard's variables written in, so Grafana runs every query. Time series and stat
panels on Prometheus are drawn; any other panel (tables, Cloud Monitoring, BigQuery, Infinity) shows a card that opens
it in Grafana.

## Run it

Grafana is reachable on the tailnet only. Give the app its address and a Viewer service account token:

```sh
cd desktop
GRAFANA_URL=http://<tailscale host>:3020 GRAFANA_TOKEN=<token> cargo run -p reelshelf-monitor
```

Without Grafana, `REELSHELF_MONITOR_FIXTURES=1` shows the fixture dashboards with made-up data
(`REELSHELF_MONITOR_SCENE=dashboard-api`, `dashboard-pipeline`, `dashboard-hover` or `dashboards`).

The token is read once and only sent to Grafana; the app never logs or shows it.

## Layout

- `src/grafana/`: the client (`client.rs`), dashboards (`model.rs`), query bodies and variables (`query.rs`) and data
  frames (`frames.rs`), plain Rust with unit tests on `tests/fixtures/` (trimmed copies of the real dashboards).
- `src/ui/chart.rs`: the time series chart (several series, time axis, units, legend, hover crosshair and readout) and
  the stat, on Rune's canvas.
- `src/copy.rs`: every word the app shows.
- The theme is the uploader's (`reelshelf_app::theme`, the site's tokens); its `chart-1`..`chart-8` colour the series.
- `tests/scenes.rs` renders every scene light and dark and checks the words and controls, as the uploader's does.
