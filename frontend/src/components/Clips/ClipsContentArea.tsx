import { ScrollArea, Stack } from "@mantine/core";
import { ClipCard } from "./ClipCard";
import { ClipCardSkeleton } from "./ClipCardSkeleton";
import { ClipsEmptyState } from "./ClipsEmptyState";
import type { Clip } from "@/api-client";

interface ClipsContentAreaProps {
  isLoading: boolean;
  clips: Array<Clip>;
  hasActiveFilters: boolean;
  categoryId: string;
  categorySlug: string;
}

export function ClipsContentArea({
  isLoading,
  clips,
  hasActiveFilters,
  categoryId,
  categorySlug,
}: ClipsContentAreaProps) {
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
    return (
      <ClipsEmptyState categoryId={categoryId} hasFilters={hasActiveFilters} />
    );
  }

  return (
    <ScrollArea
      h="100%"
      type="scroll"
      scrollbarSize={8}
      styles={{
        scrollbar: {
          "&:hover": {
            backgroundColor: "rgba(0, 212, 255, 0.1)",
          },
        },
        thumb: {
          backgroundColor: "rgba(0, 212, 255, 0.3)",
          "&:hover": {
            backgroundColor: "rgba(0, 212, 255, 0.5)",
          },
        },
      }}
    >
      <Stack gap="xs">
        {clips.map((clip, index) => (
          <div
            key={clip.clipId}
            style={{
              animation: "fadeIn 0.3s ease-in-out",
              animationDelay: `${index * 0.05}s`,
              animationFillMode: "both",
            }}
          >
            <ClipCard clip={clip} categorySlug={categorySlug} />
          </div>
        ))}
      </Stack>
    </ScrollArea>
  );
}
