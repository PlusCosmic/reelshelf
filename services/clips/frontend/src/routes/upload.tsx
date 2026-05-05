import { createFileRoute } from "@tanstack/react-router";
import type { FormEvent } from "react";
import { useState } from "react";
import { IconUpload } from "@tabler/icons-react";
import { useCategories, useCreateVideo } from "@/hooks/queries";

export const Route = createFileRoute("/upload")({
  component: UploadRoute,
});

function UploadRoute() {
  const { data: categories = [], isLoading } = useCategories();
  const createVideo = useCreateVideo();
  const [categoryId, setCategoryId] = useState("");
  const [title, setTitle] = useState("");
  const [md5Hash, setMd5Hash] = useState("");
  const [createdAt, setCreatedAt] = useState("");

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const selectedCategory = categoryId || categories[0]?.id;
    if (!selectedCategory || !title.trim()) return;
    await createVideo.mutateAsync({
      categoryId: selectedCategory,
      title: title.trim(),
      md5Hash: md5Hash.trim() || undefined,
      createdAt: createdAt ? new Date(createdAt) : undefined,
    });
    setTitle("");
    setMd5Hash("");
    setCreatedAt("");
  };

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">Upload</div>
        <h1 className="rs-display rs-h1">
          Add a new clip request to the <em>shelf</em>.
        </h1>
      </section>
      <section className="rs-section rs-split">
        <form className="rs-form" onSubmit={submit}>
          <div className="rs-field">
            <label htmlFor="clip-title">Title</label>
            <input id="clip-title" value={title} onChange={(event) => setTitle(event.currentTarget.value)} required />
          </div>
          <div className="rs-field">
            <label htmlFor="clip-category">Game</label>
            <select
              id="clip-category"
              value={categoryId}
              onChange={(event) => setCategoryId(event.currentTarget.value)}
              disabled={isLoading}
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
            <label htmlFor="clip-hash">MD5 hash</label>
            <input id="clip-hash" value={md5Hash} onChange={(event) => setMd5Hash(event.currentTarget.value)} />
          </div>
          <div className="rs-field">
            <label htmlFor="clip-created">Recorded at</label>
            <input
              id="clip-created"
              type="datetime-local"
              value={createdAt}
              onChange={(event) => setCreatedAt(event.currentTarget.value)}
            />
          </div>
          <button className="rs-primary" type="submit" disabled={createVideo.isPending}>
            <IconUpload size={15} />
            {createVideo.isPending ? "Creating..." : "Create request"}
          </button>
          {createVideo.isSuccess ? <div className="rs-meta">Clip request created.</div> : null}
          {createVideo.isError ? <div className="rs-meta">The clip request could not be created.</div> : null}
        </form>

        <aside className="rs-sidebar-panel">
          <h2 className="rs-eyebrow">Upload state</h2>
          <p style={{ color: "var(--fg-soft)", fontSize: 14, marginTop: 0 }}>
            The current backend exposes clip request creation and Bunny upload status, so this screen keeps to those supported fields.
            Drag-and-drop files, upload progress lanes, and review tasks are documented as mocked design gaps.
          </p>
        </aside>
      </section>
    </>
  );
}
