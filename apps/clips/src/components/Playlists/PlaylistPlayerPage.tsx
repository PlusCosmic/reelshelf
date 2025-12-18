/**
 * Playlist Player Page
 * Route: /playlists/:playlistId
 *
 * Main viewing experience - plays clips sequentially with queue sidebar
 * Matches ApexClip.tsx layout exactly, just with playlist queue sidebar
 */

import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Card, Center, Loader, Stack, Text } from "@mantine/core";
import { useNavigate } from "@tanstack/react-router";
import { fetchPlaylistById } from "@repo/shared";
import { useClipActions } from "../../hooks/apex/useClipActions";
import { VideoPlayer } from "../Apex/VideoPlayer";
import { ClipInfoCard } from "../Apex/ClipInfoCard";
import { PlaylistQueueSidebar } from "./PlaylistQueueSidebar";
import { useClipFormState } from "@/hooks/apex/useClipFormState.ts";
import { useClip, useMarkAsViewed, useUserById } from "@/hooks/queries.ts";

type PlaylistPlayerPageProps = {
  playlistId: string;
};

export function PlaylistPlayerPage({ playlistId }: PlaylistPlayerPageProps) {
  const navigate = useNavigate();
  const titleInputRef = useRef<HTMLInputElement>(null);
  const [currentClipIndex, setCurrentClipIndex] = useState(0);

  // Fetch playlist to get clip IDs
  const { data: playlist, isLoading: loadingPlaylist } = useQuery({
    queryKey: ["playlist", playlistId],
    queryFn: () => fetchPlaylistById(playlistId),
  });

  // Get current clip ID from playlist
  const currentClipId = playlist?.clips[currentClipIndex]?.clipId;

  // Data queries for the current clip
  const { data: clip, isLoading: loadingClip } = useClip(currentClipId);
  const { data: clipOwner, isLoading: loadingClipOwner } = useUserById(clip?.ownerId);
  const markAsViewed = useMarkAsViewed();

  // Custom hooks for business logic
  const { tagsValue, setTagsValue, titleValue, setTitleValue, topTags } = useClipFormState(clip);
  const { handleSave, handleDownload, handleDelete } = useClipActions({
    clip,
    titleValue,
    titleInputRef,
    navigate,
  });

  // Mark clip as viewed when it loads
  useEffect(() => {
    if (currentClipId) {
      markAsViewed.mutate(currentClipId);
    }
  }, [currentClipId]);

  // Handle clip selection from sidebar
  const handleClipSelect = (_clipId: string, index: number) => {
    setCurrentClipIndex(index);
  };

  // Loading state
  if (loadingPlaylist) {
    return (
      <div style={{ height: "calc(100vh - 138px)", display: "flex", justifyContent: "center", alignItems: "center" }}>
        <Loader size="lg" />
      </div>
    );
  }

  // Not found state
  if (!playlist) {
    return (
      <div style={{ height: "calc(100vh - 138px)", display: "flex", justifyContent: "center", alignItems: "center" }}>
        <Card
          radius="xl"
          p="xl"
          style={{
            background: "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
            backdropFilter: "blur(10px)",
            border: "1px solid rgba(0, 212, 255, 0.15)",
            boxShadow: "0 4px 30px rgba(0, 0, 0, 0.3), 0 0 20px rgba(0, 212, 255, 0.05)",
          }}
        >
          <Center>
            <Text size="lg" fw={600}>
              Playlist not found
            </Text>
          </Center>
        </Card>
      </div>
    );
  }

  return (
    <div style={{ height: "calc(100vh - 138px)", display: "flex", gap: "1rem" }}>
      {/* Sidebar - Left Side */}
      <PlaylistQueueSidebar
        playlistId={playlistId}
        currentClipIndex={currentClipIndex}
        onClipSelect={handleClipSelect}
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
