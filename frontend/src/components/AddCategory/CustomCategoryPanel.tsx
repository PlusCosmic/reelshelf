import type { FormEvent } from "react";
import { IconPhoto } from "@tabler/icons-react";
import { addGameCopy } from "@/copy/addGame";

export function CustomCategoryPanel({
  customCoverUrl,
  customName,
  isSaving,
  isSubmitting,
  onSubmit,
  setCustomCoverUrl,
  setCustomName,
}: {
  customCoverUrl: string;
  customName: string;
  isSaving: boolean;
  isSubmitting: boolean;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  setCustomCoverUrl: (coverUrl: string) => void;
  setCustomName: (name: string) => void;
}) {
  return (
    <form className="rs-modal-panel" role="tabpanel" onSubmit={onSubmit}>
      <label className="rs-field">
        <span>{addGameCopy.custom.nameLabel}</span>
        <span className="rs-input-shell">
          <input
            value={customName}
            onChange={(event) => setCustomName(event.currentTarget.value)}
            placeholder={addGameCopy.custom.namePlaceholder}
          />
        </span>
      </label>
      <label className="rs-field">
        <span>{addGameCopy.custom.coverLabel}</span>
        <span className="rs-input-shell">
          <IconPhoto size={16} />
          <input
            value={customCoverUrl}
            onChange={(event) => setCustomCoverUrl(event.currentTarget.value)}
            placeholder={addGameCopy.custom.coverPlaceholder}
          />
        </span>
      </label>
      <button
        className="rs-primary rs-modal-submit"
        type="submit"
        disabled={!customName.trim() || isSaving}
      >
        {isSubmitting ? addGameCopy.custom.adding : addGameCopy.custom.submit}
      </button>
    </form>
  );
}
