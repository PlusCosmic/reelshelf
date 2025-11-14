import { ActionIcon, Card, Group, ScrollArea, Stack, Text } from '@mantine/core';
import { IconArrowLeft } from '@tabler/icons-react';
import { useNavigate } from '@tanstack/react-router';
import type { Clip } from '@repo/nucleus-api-client';
import { SidebarClipCard } from './SidebarClipCard';

interface RelatedClipsSidebarProps {
  relatedClips: Array<Clip>;
  isLoading: boolean;
}

export function RelatedClipsSidebar({ relatedClips, isLoading }: RelatedClipsSidebarProps) {
  const navigate = useNavigate();

  return (
    <Card
      radius="xl"
      p="md"
      style={{
        width: '380px',
        background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
        backdropFilter: 'blur(10px)',
        border: '1px solid rgba(255, 255, 255, 0.1)',
        flexShrink: 0,
      }}
    >
      <Stack gap="md" h="100%">
        <Group justify="center" align="center" style={{ position: 'relative' }}>
          <ActionIcon
            variant="subtle"
            size="lg"
            onClick={() => navigate({ to: '/apex-legends' })}
            aria-label="Back to clips list"
            style={{ position: 'absolute', left: 0 }}
          >
            <IconArrowLeft size={20} />
          </ActionIcon>
          <Text size="lg" fw={600} style={{ letterSpacing: '-0.3px' }}>
            Up Next
          </Text>
        </Group>

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
            {!isLoading && relatedClips.map((relatedClip) => (
              <SidebarClipCard key={relatedClip.clipId} clip={relatedClip} />
            ))}
          </Stack>
        </ScrollArea>
      </Stack>
    </Card>
  );
}
