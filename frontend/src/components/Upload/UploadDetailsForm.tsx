import type { GameCategoryResponse } from "@/api-client";
import type { useClipUpload } from "@/hooks/useClipUpload";

type ClipUploadState = ReturnType<typeof useClipUpload>;

export function UploadDetailsForm({
  categories,
  categoriesLoading,
  selectedCategoryId,
  setCategoryId,
  upload,
}: {
  categories: GameCategoryResponse[];
  categoriesLoading: boolean;
  selectedCategoryId: string;
  setCategoryId: (categoryId: string) => void;
  upload: ClipUploadState;
}) {
  return (
    <div className="rs-upload-form-grid">
      <div className="rs-field">
        <label htmlFor="clip-title">Title</label>
        <input
          id="clip-title"
          value={upload.title}
          onChange={(event) => upload.setTitle(event.currentTarget.value)}
          disabled={upload.isDetailsLocked}
        />
      </div>
      <div className="rs-field">
        <label htmlFor="clip-category">Game</label>
        <select
          id="clip-category"
          value={selectedCategoryId}
          onChange={(event) => setCategoryId(event.currentTarget.value)}
          disabled={categoriesLoading || upload.isDetailsLocked}
        >
          <option value="">Choose a game</option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name}
            </option>
          ))}
        </select>
      </div>
      <div className="rs-field">
        <label htmlFor="clip-created">Recorded at</label>
        <input
          id="clip-created"
          type="datetime-local"
          value={upload.createdAt}
          onChange={(event) => upload.setCreatedAt(event.currentTarget.value)}
          disabled={upload.isDetailsLocked}
        />
      </div>
    </div>
  );
}
