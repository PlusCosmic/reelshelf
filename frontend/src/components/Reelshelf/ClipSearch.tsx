import { Button, Input, Notice } from "@pluscosmic/ui";
import { IconSparkles, IconX } from "@tabler/icons-react";
import { useId, useState, type FormEvent } from "react";
import type { GameCategoryResponse } from "@/api-client";
import { clipSearchCopy } from "@/copy/clipSearch";
import { useClipSearch } from "@/hooks/clipSearch.queries";
import { ApiError } from "@/shared/services/apiError";
import {
  chipsFor,
  hasAnyFilter,
  type ClipSearchState,
} from "./clipSearchModel";
import { ClipGrid } from "./primitives/ClipViews";

/** The longest search the API reads (`ClipSearchService.MaxQueryLength`). */
const MAX_QUERY_LENGTH = 300;

/**
 * The library's search by description: a sentence about a clip, read into filters and a phrase by the API. Its own
 * labelled form, apart from the list's search box, which matches titles, tags and games as typed.
 */
export function ClipSearchForm({
  initialQuery,
  onSearch,
}: {
  initialQuery: string;
  onSearch: (query: string) => void;
}) {
  const [query, setQuery] = useState(initialQuery);
  const [shownInitial, setShownInitial] = useState(initialQuery);
  if (shownInitial !== initialQuery) {
    setShownInitial(initialQuery);
    setQuery(initialQuery);
  }
  const inputId = useId();
  const hintId = useId();
  const tooLong = query.trim().length > MAX_QUERY_LENGTH;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = query.trim();
    if (trimmed && !tooLong) onSearch(trimmed);
  };

  return (
    <form className="rs-describe" role="search" onSubmit={submit}>
      <label className="rs-describe-label" htmlFor={inputId}>
        <IconSparkles size={15} aria-hidden="true" />
        {clipSearchCopy.label}
      </label>
      <p className="rs-describe-hint" id={hintId}>
        {clipSearchCopy.hint}
      </p>
      <div className="rs-describe-row">
        <Input
          id={inputId}
          type="search"
          value={query}
          maxLength={MAX_QUERY_LENGTH + 50}
          aria-describedby={hintId}
          aria-invalid={tooLong || undefined}
          placeholder={clipSearchCopy.placeholder}
          onChange={(event) => setQuery(event.target.value)}
        />
        <Button
          type="submit"
          variant="primary"
          disabled={!query.trim() || tooLong}
        >
          {clipSearchCopy.submit}
        </Button>
      </div>
      {tooLong ? (
        <Notice tone="warning" compact>
          {clipSearchCopy.tooLong}
        </Notice>
      ) : null}
    </form>
  );
}

/**
 * What a search found: the filters it was read into, each a chip that takes it away and searches again without it,
 * then the clips, closest first.
 */
export function ClipSearchResults({
  state,
  categories,
  onChange,
  onClear,
}: {
  state: ClipSearchState;
  categories: GameCategoryResponse[];
  onChange: (next: ClipSearchState) => void;
  onClear: () => void;
}) {
  // With every filter taken away a search would only list every clip, so it isn't sent.
  const nothingLeft = !!state.filters && !hasAnyFilter(state.filters);
  const search = useClipSearch(nothingLeft ? null : state, onChange);
  const response = nothingLeft ? undefined : search.data;
  const chips = response ? chipsFor(response.interpretation) : [];

  return (
    <section
      className="rs-section rs-clips rs-describe-results"
      aria-busy={search.isFetching}
    >
      <header className="rs-describe-results-head">
        <div>
          <h2 className="rs-eyebrow">{clipSearchCopy.heading}</h2>
          <p className="rs-describe-query">{state.query}</p>
        </div>
        <button type="button" className="rs-small-button" onClick={onClear}>
          <IconX size={13} aria-hidden="true" />
          {clipSearchCopy.clear}
        </button>
      </header>

      {chips.length > 0 ? (
        <div className="rs-filter-chips rs-describe-chips">
          <span className="rs-eyebrow rs-filter-label">
            {clipSearchCopy.readAs}
          </span>
          {chips.map((chip) => (
            <button
              key={chip.key}
              type="button"
              className="ui-chip active rs-describe-chip"
              aria-label={clipSearchCopy.remove(chip.label)}
              disabled={search.isFetching}
              onClick={() =>
                onChange({ query: state.query, filters: chip.without })
              }
            >
              {chip.label}
              <IconX size={12} aria-hidden="true" />
            </button>
          ))}
        </div>
      ) : null}

      {nothingLeft ? (
        <div className="rs-empty">{clipSearchCopy.nothingLeft}</div>
      ) : search.isLoading ? (
        <div className="rs-empty" role="status">
          {clipSearchCopy.searching}
        </div>
      ) : search.isError ? (
        <div className="rs-empty" role="alert">
          {search.error instanceof ApiError && search.error.message
            ? search.error.message
            : clipSearchCopy.failed}
        </div>
      ) : response ? (
        <>
          <p className="rs-describe-count" role="status">
            {clipSearchCopy.count(response.clips.length)}
          </p>
          {response.clips.length === 0 ? (
            <div className="rs-empty">{clipSearchCopy.noResults}</div>
          ) : (
            <ClipGrid
              clips={response.clips}
              categories={categories}
              variant="filmstrip"
              showGame
              linkSearch={{ from: "library" }}
              fromList
            />
          )}
        </>
      ) : null}
    </section>
  );
}
