import { useEffect, useEffectEvent, useId, useState } from "react";
import type { FormEvent } from "react";
import { IconX } from "@tabler/icons-react";
import { CustomCategoryPanel } from "@/components/AddCategory/CustomCategoryPanel";
import { GameSearchPanel } from "@/components/AddCategory/GameSearchPanel";
import {
  type AddCategoryTab,
  ModalTabs,
} from "@/components/AddCategory/ModalTabs";
import {
  useAddCustomCategory,
  useAddGameFromIgdb,
  useGameSearch,
} from "@/hooks/queries";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";

interface AddCategoryModalProps {
  opened: boolean;
  onClose: () => void;
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
  const closeOnEscape = useEffectEvent(() => {
    if (!isSaving) onClose();
  });

  useEffect(() => {
    if (!opened) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") closeOnEscape();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [opened]);

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

        <ModalTabs activeTab={activeTab} setActiveTab={setActiveTab} />

        {activeTab === "search" ? (
          <GameSearchPanel
            debouncedSearch={debouncedSearch}
            isSaving={isSaving}
            isSearching={isSearching}
            onAddGame={handleAddFromIgdb}
            search={search}
            searchResults={searchResults}
            setSearch={setSearch}
          />
        ) : (
          <CustomCategoryPanel
            customCoverUrl={customCoverUrl}
            customName={customName}
            isSaving={isSaving}
            isSubmitting={addCustomMutation.isPending}
            onSubmit={handleAddCustom}
            setCustomCoverUrl={setCustomCoverUrl}
            setCustomName={setCustomName}
          />
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
