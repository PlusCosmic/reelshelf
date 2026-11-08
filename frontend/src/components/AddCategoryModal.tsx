import { useState } from "react";
import type { FormEvent } from "react";
import type { GameCategoryResponse } from "@/api-client";
import { Dialog } from "@pluscosmic/ui";
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
import { addGameCopy } from "@/copy/addGame";

interface AddCategoryModalProps {
  opened: boolean;
  onClose: () => void;
  /** Clips dropped on New book, waiting for the game they belong to. */
  pendingClipCount?: number;
  onAdded?: (category: GameCategoryResponse) => void;
}

export function AddCategoryModal({
  opened,
  onClose,
  pendingClipCount = 0,
  onAdded,
}: AddCategoryModalProps) {
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

  if (!opened) return null;

  const handleAddFromIgdb = (igdbId: number) => {
    addFromIgdbMutation.mutate(igdbId, {
      onSuccess: (category) => {
        setSearch("");
        onAdded?.(category);
        onClose();
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
        onSuccess: (category) => {
          setCustomName("");
          setCustomCoverUrl("");
          onAdded?.(category);
          onClose();
        },
      },
    );
  };

  return (
    <Dialog
      eyebrow={addGameCopy.eyebrow}
      title={
        pendingClipCount > 0
          ? addGameCopy.pendingTitle(pendingClipCount)
          : addGameCopy.title
      }
      busy={isSaving}
      onClose={onClose}
    >
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
          {error instanceof Error ? error.message : addGameCopy.addFailed}
        </p>
      ) : null}
    </Dialog>
  );
}
