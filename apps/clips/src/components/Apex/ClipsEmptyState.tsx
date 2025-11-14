import { Box, Text } from '@mantine/core';
import { IconMovie, IconSearch } from '@tabler/icons-react';
import { VideoUpload } from '../VideoUpload';

interface ClipsEmptyStateProps {
  hasFilters: boolean;
}

export function ClipsEmptyState({ hasFilters }: ClipsEmptyStateProps) {
  return (
    <Box
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        padding: '3rem',
      }}
    >
      <Box
        style={{
          width: '120px',
          height: '120px',
          borderRadius: '50%',
          background: 'linear-gradient(135deg, rgba(99, 102, 241, 0.1) 0%, rgba(139, 92, 246, 0.1) 100%)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          marginBottom: '1.5rem',
        }}
      >
        {hasFilters ? (
          <IconSearch size={60} style={{ opacity: 0.4 }} />
        ) : (
          <IconMovie size={60} style={{ opacity: 0.4 }} />
        )}
      </Box>
      <Text size="xl" fw={600} mb="xs" style={{ letterSpacing: '-0.3px' }}>
        {hasFilters ? 'No clips match your filters' : 'No clips found'}
      </Text>
      <Text size="sm" c="dimmed" ta="center" maw={400} mb="xl">
        {hasFilters
          ? 'Try adjusting your search or filter criteria to find more clips.'
          : 'Start uploading your Apex Legends clips to build your collection. Your epic moments deserve to be remembered!'}
      </Text>
      {!hasFilters && <VideoUpload />}
    </Box>
  );
}
