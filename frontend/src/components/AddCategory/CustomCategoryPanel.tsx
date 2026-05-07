import type { FormEvent } from "react";
import { IconPhoto } from "@tabler/icons-react";

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
            onChange={(event) => setCustomCoverUrl(event.currentTarget.value)}
            placeholder="https://..."
          />
        </span>
      </label>
      <button
        className="rs-primary rs-modal-submit"
        type="submit"
        disabled={!customName.trim() || isSaving}
      >
        {isSubmitting ? "Adding..." : "Add category"}
      </button>
    </form>
  );
}
