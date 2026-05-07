import { useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { IconPhoto, IconPlus, IconSearch, IconX } from "@tabler/icons-react";
import {
  useAddCustomCategory,
  useAddGameFromIgdb,
  useGameSearch,
} from "@/hooks/queries";

interface AddCategoryModalProps {
  opened: boolean;
  onClose: () => void;
}

type AddCategoryTab = "search" | "custom";

function useDebouncedValue(value: string, delayMs = 300) {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedValue(value), delayMs);
    return () => window.clearTimeout(timeout);
  }, [delayMs, value]);

  return debouncedValue;
}

export function AddCategoryModal({ opened, onClose }: AddCategoryModalProps) {
  const titleId = useId();
  const [activeTab, setActiveTab] = useState<AddCategoryTab>("search");
  const [search, setSearch] = useState("");
  const [customName, setCustomName] = useState("");
  const [customCoverUrl, setCustomCoverUrl] = useState("");
  const debouncedSearch = useDebouncedValue(search.trim());

  const { data: searchResults = [], isLoading: isSearching } =
    useGameSearch(debouncedSearch);
  const addFromIgdbMutation = useAddGameFromIgdb();
  const addCustomMutation = useAddCustomCategory();
  const isSaving = addFromIgdbMutation.isPending || addCustomMutation.isPending;
  const error = addFromIgdbMutation.error ?? addCustomMutation.error;

  useEffect(() => {
    if (!opened) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !isSaving) onClose();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isSaving, onClose, opened]);

  if (!opened) return null;

  const closeModal = () => {
    if (!isSaving) onClose();
  };

  const handleAddFromIgdb = (igdbId: number) => {
    addFromIgdbMutation.mutate(igdbId, {
      onSuccess: () => {
        setSearch("");
        closeModal();
      },
    });
  };

  const handleAddCustom = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const name = customName.trim();
    if (!name) return;

    addCustomMutation.mutate(
      { name, coverUrl: customCoverUrl.trim() || undefined },
      {
        onSuccess: () => {
          setCustomName("");
          setCustomCoverUrl("");
          closeModal();
        },
      },
    );
  };

  return (
    <div
      className="rs-modal-layer"
      role="presentation"
      onMouseDown={closeModal}
    >
      <section
        className="rs-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header className="rs-modal-header">
          <div>
            <p className="rs-eyebrow">Bookshelf category</p>
            <h2 className="rs-modal-title" id={titleId}>
              Add a new book
            </h2>
          </div>
          <button
            className="rs-icon-button"
            type="button"
            aria-label="Close modal"
            onClick={closeModal}
            disabled={isSaving}
          >
            <IconX size={16} />
          </button>
        </header>

        <div
          className="rs-modal-tabs"
          role="tablist"
          aria-label="Category type"
        >
          <button
            className={`rs-modal-tab${activeTab === "search" ? " active" : ""}`}
            type="button"
            role="tab"
            aria-selected={activeTab === "search"}
            onClick={() => setActiveTab("search")}
          >
            Search games
          </button>
          <button
            className={`rs-modal-tab${activeTab === "custom" ? " active" : ""}`}
            type="button"
            role="tab"
            aria-selected={activeTab === "custom"}
            onClick={() => setActiveTab("custom")}
          >
            Custom category
          </button>
        </div>

        {activeTab === "search" ? (
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
                    onClick={() => handleAddFromIgdb(game.igdbId)}
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
                    No games found. Try another search or create a custom
                    category.
                  </p>
                ) : null}
              </div>
            ) : (
              <p className="rs-modal-note">
                Search IGDB to add a game-backed book to your shelf.
              </p>
            )}
          </div>
        ) : (
          <form
            className="rs-modal-panel"
            role="tabpanel"
            onSubmit={handleAddCustom}
          >
            <label className="rs-field">
              <span>Category name</span>
              <span className="rs-input-shell">
                <input
                  autoFocus
                  value={customName}
                  onChange={(event) => setCustomName(event.currentTarget.value)}
                  placeholder="Snowboarding, hiking, speedruns..."
                />
              </span>
            </label>
            <label className="rs-field">
              <span>Cover image URL</span>
              <span className="rs-input-shell">
                <IconPhoto size={16} />
                <input
                  value={customCoverUrl}
                  onChange={(event) =>
                    setCustomCoverUrl(event.currentTarget.value)
                  }
                  placeholder="https://..."
                />
              </span>
            </label>
            <button
              className="rs-primary rs-modal-submit"
              type="submit"
              disabled={!customName.trim() || isSaving}
            >
              {addCustomMutation.isPending ? "Adding..." : "Add category"}
            </button>
          </form>
        )}

        {error ? (
          <p className="rs-modal-error">
            {error instanceof Error ? error.message : "Could not add category."}
          </p>
        ) : null}
      </section>
    </div>
  );
}
