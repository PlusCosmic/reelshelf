import { Card, Group } from '@mantine/core';
import type { Clip } from '@repo/nucleus-api-client';

interface VideoPlayerProps {
  clip: Clip | null | undefined;
  isLoading: boolean;
}

export function VideoPlayer({ clip, isLoading }: VideoPlayerProps) {
  return (
    <Card
      radius="xl"
      p={20}
      style={{
        background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
        backdropFilter: 'blur(10px)',
        border: '1px solid rgba(255, 255, 255, 0.1)',
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
      }}
    >
      {!isLoading && clip && (
        <div style={{
          width: "100%",
          height: "100%",
          alignContent: "center"
        }}>
          <Group h={"100%"} justify={"center"}>
            <iframe
              src={`https://player.mediadelivery.net/embed/${clip.video.videoLibraryId}/${clip.video.guid}?autoplay=false`}
              loading="lazy"
              style={{
                border: "none",
                height: "100%",
                aspectRatio: "16/9",
                borderRadius: "12px",
              }}
              allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture;"
              allowFullScreen={true}
            />
          </Group>
        </div>
      )}
    </Card>
  );
}
