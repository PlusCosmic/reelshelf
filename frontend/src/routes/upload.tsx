import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useRef, useState } from "react";
import { UploadActions } from "@/components/Upload/UploadActions";
import { UploadDetailsForm } from "@/components/Upload/UploadDetailsForm";
import { UploadDropzone } from "@/components/Upload/UploadDropzone";
import { UploadHeader } from "@/components/Upload/UploadHeader";
import { UploadProgress } from "@/components/Upload/UploadProgress";
import { useCategories } from "@/hooks/queries";
import { useClipUpload } from "@/hooks/useClipUpload";

export const Route = createFileRoute("/upload")({
  component: UploadRoute,
});

function UploadRoute() {
  const navigate = useNavigate();
  const { data: categories = [], isLoading: categoriesLoading } =
    useCategories();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [categoryId, setCategoryId] = useState("");
  const selectedCategoryId = categoryId || categories[0]?.id || "";
  const upload = useClipUpload(selectedCategoryId);
  const selectedCategory = categories.find(
    (category) => category.id === selectedCategoryId,
  );

  return (
    <section className="rs-upload-page">
      <UploadHeader status={upload.status} step={upload.step} />
      <UploadDropzone fileInputRef={fileInputRef} upload={upload} />

      {!upload.file && upload.error ? (
        <div className="rs-upload-error">{upload.error}</div>
      ) : null}

      {upload.file ? (
        <div className="rs-upload-panel">
          <div className="rs-upload-file">
            <div className="rs-upload-thumb" />
            <div>
              <div className="rs-upload-file-name">{upload.file.name}</div>
              <div className="rs-meta">
                {upload.fileMeta?.size} -{" "}
                {selectedCategory?.name ?? "Choose a game"}
              </div>
            </div>
          </div>

          <UploadDetailsForm
            categories={categories}
            categoriesLoading={categoriesLoading}
            selectedCategoryId={selectedCategoryId}
            setCategoryId={setCategoryId}
            upload={upload}
          />
          <UploadProgress upload={upload} />

          {upload.error ? (
            <div className="rs-upload-error">{upload.error}</div>
          ) : null}

          <UploadActions
            onDone={() => void navigate({ to: "/" })}
            upload={upload}
          />
        </div>
      ) : null}
    </section>
  );
}
