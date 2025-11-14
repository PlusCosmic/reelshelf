import { Badge, Button, Group, Text } from '@mantine/core';
import { IconAdjustments, IconChevronDown, IconChevronUp, IconHours24 } from '@tabler/icons-react';
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
        <ApexIcon />
        <Text size="xl" fw={600} style={{ letterSpacing: '-0.5px' }}>
          Apex Legends Clips
        </Text>
        {totalClips > 0 && (
          <Badge size="lg" radius="md" variant="light" color="blue">
            {totalClips} {totalClips === 1 ? 'clip' : 'clips'}
          </Badge>
        )}
      </Group>

      <Group gap="xs">
        <Button
          variant={todayFilterActive ? "filled" : "light"}
          color="blue"
          leftSection={<IconHours24 size={18} />}
          onClick={onToggleTodayFilter}
          radius="md"
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
