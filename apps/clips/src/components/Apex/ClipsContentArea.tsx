import { ScrollArea, Stack } from '@mantine/core';
import type { Clip } from '@repo/nucleus-api-client';
import { ClipCard } from './ClipCard';
import { ClipCardSkeleton } from './ClipCardSkeleton';
import { ClipsEmptyState } from './ClipsEmptyState';

interface ClipsContentAreaProps {
  isLoading: boolean;
  clips: Array<Clip>;
  hasActiveFilters: boolean;
}

export function ClipsContentArea({ isLoading, clips, hasActiveFilters }: ClipsContentAreaProps) {
  if (isLoading) {
    return (
      <ScrollArea h="100%" type="scroll" scrollbarSize={8}>
        <Stack gap="xs">
          {Array.from({ length: 5 }).map((_, index) => (
            <ClipCardSkeleton key={index} />
          ))}
        </Stack>
      </ScrollArea>
    );
  }

  if (clips.length === 0) {
    return <ClipsEmptyState hasFilters={hasActiveFilters} />;
  }

  return (
    <ScrollArea
      h="100%"
      type="scroll"
      scrollbarSize={8}
      styles={{
        scrollbar: {
          '&:hover': {
            backgroundColor: 'rgba(255, 255, 255, 0.05)',
          }
        },
        thumb: {
          backgroundColor: 'rgba(255, 255, 255, 0.2)',
          '&:hover': {
            backgroundColor: 'rgba(255, 255, 255, 0.3)',
          }
        }
      }}
    >
      <Stack gap="xs">
        {clips.map((clip, index) => (
          <div
            key={clip.clipId}
            style={{
              animation: 'fadeIn 0.3s ease-in-out',
              animationDelay: `${index * 0.05}s`,
              animationFillMode: 'both',
            }}
          >
            <ClipCard clip={clip} />
          </div>
        ))}
      </Stack>
    </ScrollArea>
  );
}
