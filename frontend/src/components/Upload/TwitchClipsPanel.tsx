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
  LoadMore,
  Select,
  type SelectOption,
} from "@/components/ui";
import type { TwitchImportRow } from "@/hooks/twitchClipsImport";
import { startLink } from "@/shared/services/auth";
import type { TwitchClipsWindow } from "@/shared/services/twitch";
import { useTwitchClipsImport } from "./useTwitchClipsImport";
import { twitchImportCopy } from "@/copy/twitchImport";

const windowOptions: SelectOption[] = [
  { label: twitchImportCopy.list.windows.week, value: "7" },
  { label: twitchImportCopy.list.windows.month, value: "30" },
  { label: twitchImportCopy.list.windows.quarter, value: "90" },
  { label: twitchImportCopy.list.windows.all, value: "all" },
];

export function TwitchClipsPanel({ notice }: { notice: LinkNotice | null }) {
  const controller = useTwitchClipsImport();
  const returnUrl = `${window.location.origin}/upload?source=twitch`;

  return (
    <section
      className="rs-twitch-panel"
      aria-label={twitchImportCopy.panel.label}
    >
      {notice ? (
        <p className="rs-linked-notice" data-tone={notice.tone} role="status">
          {notice.text}
        </p>
      ) : null}

      {controller.clipsQuery.isPending ? (
        <p className="rs-inline-status rs-twitch-loading">
          <span className="rs-spinner" aria-hidden="true" />
          {twitchImportCopy.panel.checking}
        </p>
      ) : null}

      {controller.clipsQuery.isError && !controller.clipsQuery.data ? (
        <div className="rs-upload-error" role="alert">
          {twitchImportCopy.panel.loadFailed}{" "}
          <button
            className="rs-inline-link"
            type="button"
            onClick={controller.refresh}
          >
            {twitchImportCopy.panel.tryAgain}
          </button>
        </div>
      ) : null}

      {controller.state === "NotLinked" ? (
        <TwitchCallToAction
          title={twitchImportCopy.panel.notLinked.title}
          copy={twitchImportCopy.panel.notLinked.copy}
          action={twitchImportCopy.panel.notLinked.action}
          onAction={() => startLink("twitch", returnUrl)}
        />
      ) : null}

      {controller.state === "NeedsAuthorization" ? (
        <TwitchCallToAction
          title={twitchImportCopy.panel.needsAuthorization.title}
          copy={twitchImportCopy.panel.needsAuthorization.copy(
            controller.twitchLogin,
          )}
          action={twitchImportCopy.panel.needsAuthorization.action}
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
        {twitchImportCopy.panel.manageAccounts(
          <Link to="/settings">{twitchImportCopy.panel.settingsLink}</Link>,
        )}
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
      <div
        className="rs-twitch-toolbar"
        aria-label={twitchImportCopy.list.filtersLabel}
      >
        <div className="rs-twitch-account">
          <ProviderIcon provider="twitch" size={16} />
          {twitchImportCopy.list.account(controller.twitchLogin)}
        </div>
        <Field
          className="rs-bulk-inline-field"
          label={twitchImportCopy.list.clipsFrom}
        >
          <Select
            aria-label={twitchImportCopy.list.timeWindow}
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
          {twitchImportCopy.list.refresh}
        </Button>
      </div>

      {controller.gamesToAdd.length ? (
        <div className="rs-twitch-games" role="status">
          <span>{twitchImportCopy.list.gamesToAdd}</span>
          {controller.gamesToAdd.map((game) => (
            <Button
              key={game.igdbId}
              size="sm"
              disabled={controller.addingGame !== null}
              onClick={() => controller.addGameForRows(game.igdbId)}
            >
              <IconPlus size={14} />
              {controller.addingGame === game.igdbId
                ? twitchImportCopy.list.addingGame(game.name)
                : twitchImportCopy.list.addGame(game.name, game.count)}
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
        <div
          className="rs-bulk-actions"
          aria-label={twitchImportCopy.list.actionsLabel}
        >
          <label className="rs-bulk-select-all">
            <Checkbox
              checked={controller.allSelected}
              onChange={controller.toggleAll}
            />
            {twitchImportCopy.list.selected(
              controller.selectedRows.length,
              controller.rows.length,
            )}
          </label>
          <span className="rs-twitch-summary">
            {controller.needsGameCount > 0 ? (
              <span>
                {twitchImportCopy.list.needGame(controller.needsGameCount)}
              </span>
            ) : null}
            {controller.activeCount > 0 ? (
              <span>
                {twitchImportCopy.list.importing(controller.activeCount)}
              </span>
            ) : null}
            {controller.savedCount > 0 ? (
              <span>{twitchImportCopy.list.saved(controller.savedCount)}</span>
            ) : null}
          </span>
          <Button
            className="rs-bulk-primary-action"
            disabled={controller.importableRows.length === 0}
            onClick={controller.importSelected}
            variant="primary"
          >
            {twitchImportCopy.list.addToLibrary(
              controller.importableRows.length ||
                controller.selectedRows.length,
            )}
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
        <div className="rs-empty">{twitchImportCopy.list.empty}</div>
      ) : null}

      <LoadMore
        label={twitchImportCopy.list.loadMoreLabel}
        query={controller.clipsQuery}
      />
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
          aria-label={twitchImportCopy.card.select(row.title)}
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
            aria-label={twitchImportCopy.card.titleFor(row.clip.title)}
            compact
            value={row.title}
            onChange={(event) => onSetTitle(event.currentTarget.value)}
          />
        ) : (
          <div className="rs-twitch-card-title">{row.title}</div>
        )}
        <div className="rs-meta">
          {formatDate(row.clip.createdAt)} ·{" "}
          {twitchImportCopy.card.views(row.clip.viewCount.toLocaleString())}
          {row.clip.gameName ? ` · ${row.clip.gameName}` : ""}
          {" · "}
          <a
            href={row.clip.url}
            target="_blank"
            rel="noreferrer"
            className="rs-inline-link"
          >
            {twitchImportCopy.card.twitchLink} <IconExternalLink size={11} />
          </a>
        </div>

        {editable ? (
          <Field
            className="rs-twitch-card-field"
            label={twitchImportCopy.card.game}
          >
            <Select
              aria-label={twitchImportCopy.card.gameFor(row.title)}
              options={categoryOptions}
              value={row.categoryId ?? ""}
              disabled={categoriesLoading}
              onValueChange={(value) => onSetCategory(value || null)}
            />
          </Field>
        ) : (
          <div className="rs-meta">
            {categoryName ?? twitchImportCopy.card.noGame}
          </div>
        )}

        <div className="rs-twitch-card-status">
          <TwitchStatusBadge status={row.status} />
          {row.error ? <small>{row.error}</small> : null}
          {row.status === "saved" && row.sessionPlaylistId ? (
            <Link
              className="rs-bulk-session-link"
              to="/collections/$playlistId"
              params={{ playlistId: row.sessionPlaylistId }}
            >
              {twitchImportCopy.card.openCollection}
            </Link>
          ) : null}
          {row.status === "error" ? (
            <Button size="sm" onClick={onRetry}>
              {twitchImportCopy.card.retry}
            </Button>
          ) : null}
        </div>
      </div>
    </article>
  );
}

function TwitchStatusBadge({ status }: { status: TwitchImportRow["status"] }) {
  const labels: Record<TwitchImportRow["status"], string> =
    twitchImportCopy.status;
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
