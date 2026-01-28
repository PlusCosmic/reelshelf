import type { RefObject } from 'react';
import { downloadVideo } from '@repo/shared';
import { notifications } from '@mantine/notifications';
import { modals } from '@mantine/modals';
import type { NavigateOptions } from '@tanstack/react-router';
import { Text } from '@mantine/core';
import { useDeleteClip, useUpdateClipTitle } from '../queries';
import type { Clip } from '@repo/nucleus-api-client';

interface UseClipActionsParams {
  clip: Clip | null | undefined;
  titleValue: string;
  titleInputRef: RefObject<HTMLInputElement | null>;
  navigate: (opts: NavigateOptions) => Promise<void>;
  categorySlug?: string;
}

export function useClipActions({ clip, titleValue, titleInputRef, navigate, categorySlug }: UseClipActionsParams) {
  const updateTitle = useUpdateClipTitle();
  const deleteClip = useDeleteClip();

  function handleSave() {
    if (!clip) {
      return;
    }
    updateTitle.mutate(
      { clipId: clip.clipId, title: titleValue },
      {
        onSuccess: () => {
          titleInputRef.current?.blur();
          notifications.show({
            title: 'Title Changed ✅',
            message: `Title was updated to ${titleValue}`,
          });
        },
        onError: () => {
          notifications.show({
            title: 'Update Failed',
            message: 'Failed to update title',
            color: 'red',
          });
        }
      }
    );
  }

  async function handleDownload() {
    if (!clip) {
      return;
    }

    try {
      await downloadVideo(clip.video.guid);
      notifications.show({
        title: 'Download Started',
        message: 'Your video download has started',
        color: 'green',
      });
    } catch (error) {
      notifications.show({
        title: 'Download Failed',
        message: error instanceof Error ? error.message : 'Failed to download video',
        color: 'red',
      });
    }
  }

  function handleDelete() {
    if (!clip) {
      return;
    }

    modals.openConfirmModal({
      title: 'Delete Video',
      children: (
        <Text size="sm">
          Are you sure you want to delete "{titleValue}"? This action cannot be undone.
        </Text>
      ),
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => {
        deleteClip.mutate(clip.clipId, {
          onSuccess: () => {
            notifications.show({
              title: 'Video Deleted',
              message: 'The video has been successfully deleted',
              color: 'green',
            });
            navigate({ to: '/games/$slug', params: { slug: categorySlug ?? '' } });
          },
          onError: () => {
            notifications.show({
              title: 'Delete Failed',
              message: 'Failed to delete video',
              color: 'red',
            });
          }
        });
      },
    });
  }

  return {
    handleSave,
    handleDownload,
    handleDelete,
  };
}
