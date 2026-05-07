import { IconPhoto, IconPlus, IconSearch } from "@tabler/icons-react";
import type { GameSearchResult } from "@/api-client";

export function GameSearchPanel({
  debouncedSearch,
  isSaving,
  isSearching,
  onAddGame,
  search,
  searchResults,
  setSearch,
}: {
  debouncedSearch: string;
  isSaving: boolean;
  isSearching: boolean;
  onAddGame: (igdbId: number) => void;
  search: string;
  searchResults: GameSearchResult[];
  setSearch: (search: string) => void;
}) {
  return (
    <div className="rs-modal-panel" role="tabpanel">
      <label className="rs-field">
        <span>Game title</span>
        <span className="rs-input-shell">
          <IconSearch size={16} />
          <input
            autoFocus
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            placeholder="Search for a game..."
          />
          {isSearching ? (
            <span className="rs-spinner" aria-label="Searching" />
          ) : null}
        </span>
      </label>

      {debouncedSearch.length >= 2 ? (
        <div className="rs-game-results">
          {searchResults.map((game) => (
            <button
              className="rs-game-result"
              key={game.igdbId}
              type="button"
              onClick={() => onAddGame(game.igdbId)}
              disabled={isSaving}
            >
              {game.coverUrl ? (
                <img src={game.coverUrl} alt="" loading="lazy" />
              ) : (
                <span className="rs-game-result-fallback">
                  <IconPhoto size={20} />
                </span>
              )}
              <span>{game.name}</span>
              <IconPlus size={16} />
            </button>
          ))}
          {!isSearching && searchResults.length === 0 ? (
            <p className="rs-modal-note">
              No games found. Try another search or create a custom category.
            </p>
          ) : null}
        </div>
      ) : (
        <p className="rs-modal-note">
          Search IGDB to add a game-backed book to your shelf.
        </p>
      )}
    </div>
  );
}
