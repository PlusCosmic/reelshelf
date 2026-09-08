import {
  IconBrandTwitch,
  IconExternalLink,
  IconPlus,
  IconRefresh,
} from "@tabler/icons-react";
import { Link } from "@tanstack/react-router";
import type { LinkNotice } from "@/components/Reelshelf/LinkedAccounts";
import { ProviderIcon } from "@/components/Reelshelf/ProviderIcon";
import {
  formatDate,
  formatDuration,
} from "@/components/Reelshelf/reelshelf-model";
import {
  Badge,
  Button,
  Checkbox,
  Field,
  Input,
  Select,
  type SelectOption,
} from "@/components/ui";
import type { TwitchImportRow } from "@/hooks/twitchClipsImport";
import { startLink } from "@/shared/services/auth";
import type { TwitchClipsWindow } from "@/shared/services/twitch";
import { useTwitchClipsImport } from "./useTwitchClipsImport";

const windowOptions: SelectOption[] = [
  { label: "Last 7 days", value: "7" },
  { label: "Last 30 days", value: "30" },
  { label: "Last 90 days", value: "90" },
  { label: "All time", value: "all" },
];

export function TwitchClipsPanel({ notice }: { notice: LinkNotice | null }) {
  const controller = useTwitchClipsImport();
  const returnUrl = `${window.location.origin}/upload?source=twitch`;

  return (
    <section className="rs-twitch-panel" aria-label="Twitch clips">
      {notice ? (
        <p className="rs-linked-notice" data-tone={notice.tone} role="status">
          {notice.text}
        </p>
      ) : null}

      {controller.clipsQuery.isPending ? (
        <p className="rs-inline-status rs-twitch-loading">
          <span className="rs-spinner" aria-hidden="true" />
          Checking your Twitch account…
        </p>
      ) : null}

      {controller.clipsQuery.isError ? (
        <div className="rs-upload-error" role="alert">
          Your Twitch clips could not be loaded.{" "}
          <button
            className="rs-inline-link"
            type="button"
            onClick={controller.refresh}
          >
            Try again
          </button>
        </div>
      ) : null}

      {controller.state === "NotLinked" ? (
        <TwitchCallToAction
          title="Link Twitch to import your clips."
          copy="Once your Twitch account is linked, Reelshelf can list the clips from your channel and copy the ones you pick straight into your library. No downloading and re-uploading."
          action="Link Twitch"
          onAction={() => startLink("twitch", returnUrl)}
        />
      ) : null}

      {controller.state === "NeedsAuthorization" ? (
        <TwitchCallToAction
          title="Reconnect Twitch to allow clip downloads."
          copy={`Your Twitch account${controller.twitchLogin ? ` (@${controller.twitchLogin})` : ""} is linked, but Reelshelf needs permission to fetch your clips. Reconnecting takes you through Twitch once more and changes nothing else on your account.`}
          action="Reconnect Twitch"
          onAction={() => startLink("twitch", returnUrl)}
        />
      ) : null}

      {controller.state === "Ready" ? (
        <TwitchClipList controller={controller} />
      ) : null}
    </section>
  );
}

function TwitchCallToAction({
  action,
  copy,
  onAction,
  title,
}: {
  action: string;
  copy: string;
  onAction: () => void;
  title: string;
}) {
  return (
    <div className="rs-twitch-cta">
      <span className="rs-twitch-cta-icon" aria-hidden="true">
        <IconBrandTwitch size={28} />
      </span>
      <h2 className="rs-twitch-cta-title">{title}</h2>
      <p className="rs-twitch-cta-copy">{copy}</p>
      <button
        className="rs-provider-login rs-provider-login-compact"
        data-provider="twitch"
        type="button"
        onClick={onAction}
      >
        <ProviderIcon provider="twitch" />
        {action}
      </button>
      <p className="rs-twitch-cta-footnote">
        Manage linked accounts in <Link to="/settings">Settings</Link>.
      </p>
    </div>
  );
}

function TwitchClipList({
  controller,
}: {
  controller: ReturnType<typeof useTwitchClipsImport>;
}) {
  const hasClips = controller.rows.length > 0;

  return (
    <>
      <div className="rs-twitch-toolbar" aria-label="Twitch clip filters">
        <div className="rs-twitch-account">
          <ProviderIcon provider="twitch" size={16} />
          {controller.twitchLogin ? `@${controller.twitchLogin}` : "Twitch"}
        </div>
        <Field className="rs-bulk-inline-field" label="Clips from">
          <Select
            aria-label="Time window"
            options={windowOptions}
            value={controller.days === null ? "all" : String(controller.days)}
            onValueChange={(value) =>
              controller.setDays(
                value === "all" ? null : (Number(value) as TwitchClipsWindow),
              )
            }
          />
        </Field>
        <Button size="sm" onClick={controller.refresh}>
          <IconRefresh size={14} />
          Refresh
        </Button>
      </div>

      {controller.gamesToAdd.length ? (
        <div className="rs-twitch-games" role="status">
          <span>
            Twitch tagged some clips with games not in your library yet.
          </span>
          {controller.gamesToAdd.map((game) => (
            <Button
              key={game.igdbId}
              size="sm"
              disabled={controller.addingGame !== null}
              onClick={() => controller.addGameForRows(game.igdbId)}
            >
              <IconPlus size={14} />
              {controller.addingGame === game.igdbId
                ? `Adding ${game.name}…`
                : `Add ${game.name} (${game.count})`}
            </Button>
          ))}
          {controller.addGameError ? (
            <small className="rs-twitch-games-error">
              {controller.addGameError}
            </small>
          ) : null}
        </div>
      ) : null}

      {hasClips ? (
        <div className="rs-bulk-actions" aria-label="Twitch import actions">
          <label className="rs-bulk-select-all">
            <Checkbox
              checked={controller.allSelected}
              onChange={controller.toggleAll}
            />
            {controller.selectedRows.length} of {controller.rows.length}{" "}
            selected
          </label>
          <span className="rs-twitch-summary">
            {controller.needsGameCount > 0 ? (
              <span>{controller.needsGameCount} need game</span>
            ) : null}
            {controller.activeCount > 0 ? (
              <span>{controller.activeCount} importing</span>
            ) : null}
            {controller.savedCount > 0 ? (
              <span>{controller.savedCount} saved</span>
            ) : null}
          </span>
          <Button
            className="rs-bulk-primary-action"
            disabled={controller.importableRows.length === 0}
            onClick={controller.importSelected}
            variant="primary"
          >
            Add{" "}
            {controller.importableRows.length || controller.selectedRows.length}{" "}
            to library
          </Button>
        </div>
      ) : null}

      {hasClips ? (
        <div className="rs-twitch-grid">
          {controller.rows.map((row) => (
            <TwitchClipCard
              key={row.clip.id}
              categoriesLoading={controller.categoriesLoading}
              categoryName={
                row.categoryId
                  ? controller.categoryById.get(row.categoryId)?.name
                  : null
              }
              categoryOptions={controller.categoryOptions}
              onRetry={() => controller.retryRow(row.clip.id)}
              onSelect={() => controller.toggleRow(row.clip.id)}
              onSetCategory={(categoryId) =>
                controller.setRowCategory(row.clip.id, categoryId)
              }
              onSetTitle={(title) => controller.setRowTitle(row.clip.id, title)}
              row={row}
            />
          ))}
        </div>
      ) : !controller.clipsQuery.isPending ? (
        <div className="rs-empty">
          No clips found on your channel for this period. Try a longer window.
        </div>
      ) : null}

      {controller.clipsQuery.hasNextPage ? (
        <div className="rs-twitch-more">
          <Button
            disabled={controller.clipsQuery.isFetchingNextPage}
            onClick={() => void controller.clipsQuery.fetchNextPage()}
          >
            {controller.clipsQuery.isFetchingNextPage
              ? "Loading…"
              : "Load more clips"}
          </Button>
        </div>
      ) : null}
    </>
  );
}

function TwitchClipCard({
  categoriesLoading,
  categoryName,
  categoryOptions,
  onRetry,
  onSelect,
  onSetCategory,
  onSetTitle,
  row,
}: {
  categoriesLoading: boolean;
  categoryName: string | null | undefined;
  categoryOptions: SelectOption[];
  onRetry: () => void;
  onSelect: () => void;
  onSetCategory: (categoryId: string | null) => void;
  onSetTitle: (title: string) => void;
  row: TwitchImportRow;
}) {
  const editable = row.status === "ready" || row.status === "needs_game";
  const checkboxId = `twitch-clip-${row.clip.id}`;

  return (
    <article
      className={`rs-twitch-card${row.selected && editable ? " selected" : ""}${
        editable ? "" : " settled"
      }`}
    >
      <div className="rs-twitch-card-media">
        {row.clip.thumbnailUrl ? (
          <img alt="" loading="lazy" src={row.clip.thumbnailUrl} />
        ) : (
          <div className="rs-upload-thumb" />
        )}
        <span className="rs-twitch-card-duration">
          {formatDuration(row.clip.durationSeconds)}
        </span>
        <label
          className="rs-twitch-card-check"
          htmlFor={checkboxId}
          aria-label={`Select ${row.title}`}
        >
          <Checkbox
            id={checkboxId}
            checked={row.selected && editable}
            disabled={!editable}
            onChange={onSelect}
          />
        </label>
      </div>

      <div className="rs-twitch-card-body">
        {editable ? (
          <Input
            aria-label={`Title for ${row.clip.title}`}
            compact
            value={row.title}
            onChange={(event) => onSetTitle(event.currentTarget.value)}
          />
        ) : (
          <div className="rs-twitch-card-title">{row.title}</div>
        )}
        <div className="rs-meta">
          {formatDate(row.clip.createdAt)} ·{" "}
          {row.clip.viewCount.toLocaleString()} views
          {row.clip.gameName ? ` · ${row.clip.gameName}` : ""}
          {" · "}
          <a
            href={row.clip.url}
            target="_blank"
            rel="noreferrer"
            className="rs-inline-link"
          >
            Twitch <IconExternalLink size={11} />
          </a>
        </div>

        {editable ? (
          <Field className="rs-twitch-card-field" label="Game">
            <Select
              aria-label={`Game for ${row.title}`}
              options={categoryOptions}
              value={row.categoryId ?? ""}
              disabled={categoriesLoading}
              onValueChange={(value) => onSetCategory(value || null)}
            />
          </Field>
        ) : (
          <div className="rs-meta">{categoryName ?? "No game"}</div>
        )}

        <div className="rs-twitch-card-status">
          <TwitchStatusBadge status={row.status} />
          {row.error ? <small>{row.error}</small> : null}
          {row.status === "saved" && row.sessionPlaylistId ? (
            <Link
              className="rs-bulk-session-link"
              to="/playlists/$playlistId"
              params={{ playlistId: row.sessionPlaylistId }}
            >
              Open collection
            </Link>
          ) : null}
          {row.status === "error" ? (
            <Button size="sm" onClick={onRetry}>
              Retry
            </Button>
          ) : null}
        </div>
      </div>
    </article>
  );
}

function TwitchStatusBadge({ status }: { status: TwitchImportRow["status"] }) {
  const labels: Record<TwitchImportRow["status"], string> = {
    already_imported: "in library",
    needs_game: "needs game",
    ready: "ready",
    importing: "importing",
    imported: "imported",
    filing: "filing",
    saved: "saved",
    duplicate: "in library",
    error: "error",
  };
  const danger = status === "needs_game" || status === "error";
  const neutral = status === "already_imported" || status === "duplicate";

  return (
    <Badge
      className={`rs-bulk-status ${status}`}
      tone={danger ? "danger" : neutral ? "neutral" : "accent"}
    >
      {labels[status]}
    </Badge>
  );
}
