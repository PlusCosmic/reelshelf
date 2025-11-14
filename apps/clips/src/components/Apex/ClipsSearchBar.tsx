import { TextInput } from '@mantine/core';
import { IconSearch } from '@tabler/icons-react';

interface ClipsSearchBarProps {
  searchQuery: string;
  onSearchChange: (query: string) => void;
}

export function ClipsSearchBar({ searchQuery, onSearchChange }: ClipsSearchBarProps) {
  return (
    <TextInput
      size="lg"
      radius="xl"
      placeholder="Search clips by title, tags, or description..."
      leftSection={<IconSearch size={20} />}
      value={searchQuery}
      onChange={(e) => onSearchChange(e.target.value)}
      styles={{
        input: {
          paddingLeft: '2.5rem',
          fontSize: '0.95rem',
          fontWeight: 500,
          backgroundColor: 'rgba(255, 255, 255, 0.03)',
          border: '1px solid rgba(255, 255, 255, 0.08)',
          transition: 'all 0.2s ease',
          '&:focus': {
            backgroundColor: 'rgba(255, 255, 255, 0.05)',
            border: '1px solid rgba(99, 102, 241, 0.4)',
            transform: 'translateY(-1px)',
          },
          '&::placeholder': {
            color: 'rgba(255, 255, 255, 0.4)',
          }
        }
      }}
    />
  );
}
