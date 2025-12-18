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
        background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.95) 0%, rgba(20, 20, 35, 0.9) 100%)',
        backdropFilter: 'blur(10px)',
        border: '1px solid rgba(0, 212, 255, 0.2)',
        boxShadow: '0 8px 40px rgba(0, 0, 0, 0.4), 0 0 40px rgba(0, 212, 255, 0.1), inset 0 0 80px rgba(0, 212, 255, 0.02)',
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
                border: "2px solid rgba(0, 212, 255, 0.15)",
                height: "100%",
                aspectRatio: "16/9",
                borderRadius: "12px",
                boxShadow: "0 0 30px rgba(0, 212, 255, 0.1)",
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
