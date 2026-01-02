import { useEffect, useRef } from "react";
import { Stack } from "@mantine/core";
import { useNavigate } from '@tanstack/react-router';
import { useCategories, useClip, useMarkAsViewed, useUserById } from '../../hooks/queries';
import { useClipFormState } from '../../hooks/clips/useClipFormState';
import { useRelatedClips } from '../../hooks/clips/useRelatedClips';
import { useClipActions } from '../../hooks/clips/useClipActions';
import { VideoPlayer } from './VideoPlayer';
import { ClipInfoCard } from './ClipInfoCard';
import { RelatedClipsSidebar } from './RelatedClipsSidebar';

interface ClipDetailProps {
  clipId: string;
  categorySlug: string;
}

export function ClipDetail({ clipId, categorySlug }: ClipDetailProps) {
  const navigate = useNavigate();
  const titleInputRef = useRef<HTMLInputElement>(null);

  // Get category from slug
  const { data: categories } = useCategories();
  const category = categories?.find((c) => c.slug === categorySlug);

  // Data queries
  const { data: clip, isLoading: loadingClip } = useClip(clipId);
  const { data: clipOwner, isLoading: loadingClipOwner } = useUserById(clip?.ownerId);
  const markAsViewed = useMarkAsViewed();

  // Custom hooks for business logic
  const { tagsValue, setTagsValue, titleValue, setTitleValue, topTags } = useClipFormState(clip);
  const { relatedClips, isLoading: loadingRelatedClips } = useRelatedClips(clipId, category?.id);
  const { handleSave, handleDownload, handleDelete } = useClipActions({
    clip,
    titleValue,
    titleInputRef,
    navigate,
    categorySlug,
  });

  // Mark clip as viewed when it loads
  useEffect(() => {
    if (clipId) {
      markAsViewed.mutate(clipId);
    }
  }, [clipId]);

  return (
    <div style={{ height: "calc(100vh - 138px)", display: "flex", gap: "1rem" }}>
      {/* Sidebar - Left Side */}
      <RelatedClipsSidebar
        relatedClips={relatedClips}
        isLoading={loadingRelatedClips}
        categorySlug={categorySlug}
      />

      {/* Main Content - Right Side */}
      <Stack style={{ flex: 1, minWidth: 0 }} gap="md">
        <VideoPlayer clip={clip} isLoading={loadingClip} />

        <ClipInfoCard
          clip={clip}
          clipOwner={clipOwner}
          isLoadingClip={loadingClip}
          isLoadingClipOwner={loadingClipOwner}
          titleValue={titleValue}
          setTitleValue={setTitleValue}
          tagsValue={tagsValue}
          setTagsValue={setTagsValue}
          topTags={topTags}
          titleInputRef={titleInputRef}
          onSave={handleSave}
          onDownload={handleDownload}
          onDelete={handleDelete}
        />
      </Stack>
    </div>
  );
}
