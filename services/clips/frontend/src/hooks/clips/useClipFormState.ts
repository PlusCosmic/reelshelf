import { useEffect, useRef, useState } from 'react';
import { useAddTag, useRemoveTag, useTopTags } from '../queries';
import type { Clip } from '@repo/clips-api-client';

export function useClipFormState(clip: Clip | null | undefined) {
  const [tagsValue, setTagsValue] = useState<Array<string>>([]);
  const [titleValue, setTitleValue] = useState<string>("");
  const isInitializing = useRef(true);

  const { data: topTagsData } = useTopTags();
  const topTags = topTagsData?.map((t) => t.name.toLowerCase()) || [];

  const addTag = useAddTag();
  const removeTag = useRemoveTag();

  // Initialize form values when clip loads
  useEffect(() => {
    if (clip) {
      isInitializing.current = true;
      setTagsValue(clip.tags);
      setTitleValue(clip.video.title);
      // Allow the state update to flush before enabling sync
      requestAnimationFrame(() => {
        isInitializing.current = false;
      });
    }
  }, [clip]);

  // Sync tags when they change
  useEffect(() => {
    if (!clip) return;
    if (isInitializing.current) return;

    const current = new Set(clip.tags);
    const nextLower = tagsValue.map((t) => t.toLowerCase());
    const next = new Set(nextLower);

    const toAdd = [...next].filter((t) => !current.has(t));
    const toRemove = [...current].filter((t) => !next.has(t));

    // Call mutations for each tag change
    toAdd.forEach((tag) => addTag.mutate({ clipId: clip.clipId, tag }));
    toRemove.forEach((tag) => removeTag.mutate({ clipId: clip.clipId, tag }));
  }, [tagsValue, clip?.clipId]);

  return {
    tagsValue,
    setTagsValue,
    titleValue,
    setTitleValue,
    topTags,
  };
}
