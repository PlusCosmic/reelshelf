import { Badge, Box, Button, Group, Text } from '@mantine/core';
import { IconAdjustments, IconChevronDown, IconChevronUp, IconHours24, IconPlaylist } from '@tabler/icons-react';
import { Link } from '@tanstack/react-router';
import { VideoUpload } from '../VideoUpload';
import { ApexIcon } from './ApexIcon';

interface ClipsHeaderProps {
  totalClips: number;
  filtersOpen: boolean;
  activeFilterCount: number;
  todayFilterActive: boolean;
  onToggleFilters: () => void;
  onToggleTodayFilter: () => void;
}

export function ClipsHeader({
  totalClips,
  filtersOpen,
  activeFilterCount,
  todayFilterActive,
  onToggleFilters,
  onToggleTodayFilter,
}: ClipsHeaderProps) {
  return (
    <Group justify="space-between" align="center">
      <Group gap="md" flex={1}>
        <Box
          style={{
            filter: 'drop-shadow(0 0 8px rgba(0, 212, 255, 0.5))',
          }}
        >
          <ApexIcon />
        </Box>
        <Text
          size="xl"
          fw={700}
          style={{
            letterSpacing: '-0.5px',
            background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
            WebkitBackgroundClip: 'text',
            WebkitTextFillColor: 'transparent',
            backgroundClip: 'text',
          }}
        >
          Apex Legends Clips
        </Text>
        {totalClips > 0 && (
          <Badge
            size="lg"
            radius="md"
            variant="light"
            style={{
              background: 'rgba(0, 212, 255, 0.1)',
              border: '1px solid rgba(0, 212, 255, 0.3)',
              color: '#00d4ff',
            }}
          >
            {totalClips} {totalClips === 1 ? 'clip' : 'clips'}
          </Badge>
        )}
      </Group>

      <Group gap="xs">
        <Button
          component={Link}
          to="/playlists"
          variant="subtle"
          leftSection={<IconPlaylist size={18} />}
          radius="md"
          style={{
            color: 'rgba(255, 255, 255, 0.7)',
            transition: 'all 0.2s ease',
          }}
          styles={{
            root: {
              '&:hover': {
                color: '#a855f7',
                background: 'rgba(168, 85, 247, 0.1)',
              },
            },
          }}
        >
          Playlists
        </Button>
        <Button
          variant={todayFilterActive ? "filled" : "light"}
          leftSection={<IconHours24 size={18} />}
          onClick={onToggleTodayFilter}
          radius="md"
          style={{
            background: todayFilterActive
              ? 'linear-gradient(135deg, #00d4ff 0%, #0ea5e9 100%)'
              : 'rgba(0, 212, 255, 0.1)',
            border: '1px solid rgba(0, 212, 255, 0.3)',
            color: todayFilterActive ? '#0a0a14' : '#00d4ff',
            boxShadow: todayFilterActive ? '0 0 20px rgba(0, 212, 255, 0.4)' : 'none',
            transition: 'all 0.2s ease',
          }}
        >
          Today
        </Button>
        <Button
          variant={filtersOpen ? "light" : "subtle"}
          leftSection={<IconAdjustments size={18} />}
          rightSection={filtersOpen ? <IconChevronUp size={16} /> : <IconChevronDown size={16} />}
          onClick={onToggleFilters}
          radius="md"
          disabled={todayFilterActive}
          style={{
            background: filtersOpen ? 'rgba(168, 85, 247, 0.15)' : 'transparent',
            border: filtersOpen ? '1px solid rgba(168, 85, 247, 0.3)' : '1px solid transparent',
            color: filtersOpen ? '#a855f7' : 'rgba(255, 255, 255, 0.7)',
            transition: 'all 0.2s ease',
          }}
        >
          Filters
          {activeFilterCount > 0 && (
            <Badge
              size="sm"
              circle
              style={{
                marginLeft: '0.5rem',
                minWidth: '20px',
                height: '20px',
                padding: '0 6px',
                background: 'linear-gradient(135deg, #a855f7 0%, #ec4899 100%)',
                border: 'none',
              }}
            >
              {activeFilterCount}
            </Badge>
          )}
        </Button>
        <VideoUpload />
      </Group>
    </Group>
  );
}
