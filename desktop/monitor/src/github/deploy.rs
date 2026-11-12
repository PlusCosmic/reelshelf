//! What the compose files in PlusCosmic/infrastructure deploy: every `ghcr.io/pluscosmic/reelshelf*` image on an
//! `image:` line, and the worker image handed to the overflow pools as `Overflow__<Pool>__WorkerImage=` (in
//! `services/clips.yml`), as `publish-app-images.yml` reads and bumps them.

use super::model::Entry;

/// Where images come from.
pub const REGISTRY: &str = "ghcr.io/pluscosmic/";

/// One image a compose file deploys.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Image {
    /// `services/clips.yml`.
    pub file: String,
    /// `reelshelf`, `reelshelf-migrations`, `reelshelf-worker`, ...
    pub image: String,
    /// `sha-<commit>`, or another tag such as `main`.
    pub tag: String,
    /// The overflow pools it is handed to (`ec2`, `ecs`, `batch`), in the file's order; empty for an `image:` line.
    pub pools: Vec<String>,
}

impl Image {
    /// The reelshelf commit the tag names, when it is `sha-<40 hex>`.
    pub fn sha(&self) -> Option<&str> {
        sha(&self.tag)
    }
}

/// The commit in a `sha-<40 hex>` tag.
pub fn sha(tag: &str) -> Option<&str> {
    let sha = tag.strip_prefix("sha-")?;
    (sha.len() == 40 && sha.bytes().all(|b| b.is_ascii_hexdigit())).then_some(sha)
}

/// The compose files that deploy Reelshelf, from `services/`'s entries: `clips.yml` (prod), `clips-dev.yml` (dev),
/// then every `reelshelf-worker*.yml`, by name.
pub fn files(entries: &[Entry]) -> Vec<String> {
    let mut workers: Vec<&Entry> = entries
        .iter()
        .filter(|e| e.kind == "file" && e.name.starts_with("reelshelf-worker") && e.name.ends_with(".yml"))
        .collect();
    workers.sort_by(|a, b| a.name.cmp(&b.name));
    ["clips.yml", "clips-dev.yml"]
        .iter()
        .filter_map(|name| entries.iter().find(|e| e.kind == "file" && e.name == *name))
        .chain(workers)
        .map(|e| e.path.clone())
        .collect()
}

/// The Reelshelf images `text` (the compose file at `file`) deploys, each once: an `image:` line, or the overflow
/// pools' worker image with the pools that share its tag.
pub fn images(file: &str, text: &str) -> Vec<Image> {
    let mut out: Vec<Image> = Vec::new();
    for line in text.lines() {
        let line = line.trim().trim_start_matches('-').trim();
        if line.starts_with('#') {
            continue;
        }
        let (value, pool) = if let Some(value) = line.strip_prefix("image:") {
            (value, None)
        } else if let Some((key, value)) = line.split_once('=')
            && let Some(pool) = key
                .trim()
                .strip_prefix("Overflow__")
                .and_then(|k| k.strip_suffix("__WorkerImage"))
        {
            (value, Some(pool.to_lowercase()))
        } else {
            continue;
        };
        let value = value.trim().trim_matches(|c| c == '\'' || c == '"');
        let Some((image, tag)) = value
            .strip_prefix(REGISTRY)
            .and_then(|rest| rest.rsplit_once(':'))
            .filter(|(image, _)| image.starts_with("reelshelf"))
        else {
            continue;
        };
        let pools: Vec<String> = pool.into_iter().collect();
        match out
            .iter_mut()
            .find(|i| i.image == image && i.tag == tag && i.pools.is_empty() == pools.is_empty())
        {
            Some(found) => {
                for p in pools {
                    if !found.pools.contains(&p) {
                        found.pools.push(p);
                    }
                }
            }
            None => out.push(Image {
                file: file.to_owned(),
                image: image.to_owned(),
                tag: tag.to_owned(),
                pools,
            }),
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::super::model::parse_entries;
    use super::*;

    const SERVICES: &str = include_str!("../../tests/fixtures/github/services.json");
    const CLIPS: &str = include_str!("../../tests/fixtures/github/clips.yml");
    const CLIPS_DEV: &str = include_str!("../../tests/fixtures/github/clips-dev.yml");
    const WORKER: &str = include_str!("../../tests/fixtures/github/reelshelf-worker.yml");

    #[test]
    fn the_files_are_prod_dev_then_the_workers() {
        assert_eq!(
            files(&parse_entries(SERVICES).unwrap()),
            vec![
                "services/clips.yml",
                "services/clips-dev.yml",
                "services/reelshelf-worker-dev.yml",
                "services/reelshelf-worker.yml",
            ]
        );
    }

    #[test]
    fn prod_deploys_the_app_migrations_and_the_pools_worker() {
        let images = images("services/clips.yml", CLIPS);
        let summary: Vec<(&str, &str, Vec<&str>)> = images
            .iter()
            .map(|i| {
                (
                    i.image.as_str(),
                    &i.tag[..11],
                    i.pools.iter().map(String::as_str).collect(),
                )
            })
            .collect();
        assert_eq!(
            summary,
            vec![
                ("reelshelf-migrations", "sha-3f8936c", vec![]),
                ("reelshelf", "sha-3f8936c", vec![]),
                ("reelshelf-worker", "sha-118e6fc", vec!["batch", "ecs", "ec2"]),
            ]
        );
        assert_eq!(images[1].sha(), Some("3f8936c3213939ff0ecf85f0d8856fb0628b51c6"));
    }

    #[test]
    fn dev_runs_its_base_image_once_with_no_commit() {
        let images = images("services/clips-dev.yml", CLIPS_DEV);
        assert_eq!(images.len(), 1);
        assert_eq!(images[0].image, "reelshelf-dev-base");
        assert_eq!(images[0].tag, "main");
        assert_eq!(images[0].sha(), None);
    }

    #[test]
    fn a_worker_file_deploys_the_worker() {
        let images = images("services/reelshelf-worker.yml", WORKER);
        assert_eq!(images.len(), 1);
        assert!(images[0].pools.is_empty());
        assert_eq!(images[0].sha(), Some("118e6fc57a33f9bba08eac689f75c578003da0dc"));
    }

    #[test]
    fn pools_on_different_tags_stay_apart() {
        let text = "  - Overflow__Batch__WorkerImage=ghcr.io/pluscosmic/reelshelf-worker:sha-a\n  - \
                    Overflow__Ecs__WorkerImage=ghcr.io/pluscosmic/reelshelf-worker:sha-b\n    image: \
                    \"ghcr.io/pluscosmic/reelshelf-worker:sha-a\"\n    # image: ghcr.io/pluscosmic/reelshelf:old\n";
        let images = images("f", text);
        assert_eq!(images.len(), 3);
        assert_eq!(images[0].pools, vec!["batch"]);
        assert_eq!(images[1].pools, vec!["ecs"]);
        assert!(images[2].pools.is_empty());
    }

    #[test]
    fn only_full_shas_are_commits() {
        assert_eq!(
            sha("sha-0262ee127b6c63559afa791496ba7d26245dcc77").map(str::len),
            Some(40)
        );
        assert_eq!(sha("sha-0262ee1"), None);
        assert_eq!(sha("main"), None);
    }
}
