import { Card, Group, Pagination, Select, Text } from '@mantine/core';

interface ClipsPaginationControlsProps {
  page: number;
  totalPages: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: string | null) => void;
}

export function ClipsPaginationControls({
  page,
  totalPages,
  pageSize,
  onPageChange,
  onPageSizeChange
}: ClipsPaginationControlsProps) {
  return (
    <Card
      radius="lg"
      p="md"
      style={{
        border: '1px solid rgba(255, 255, 255, 0.05)',
        background: 'rgba(255, 255, 255, 0.02)',
      }}
    >
      <Group justify="space-between" align="center" wrap="nowrap">
        <Group gap="xs" align="center">
          <Text size="sm" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            Items per page:
          </Text>
          <Select
            value={pageSize.toString()}
            onChange={onPageSizeChange}
            data={[
              { value: '10', label: '10' },
              { value: '20', label: '20' },
              { value: '30', label: '30' },
              { value: '50', label: '50' },
            ]}
            w={80}
            size="sm"
            styles={{
              input: {
                backgroundColor: 'rgba(255, 255, 255, 0.03)',
                border: '1px solid rgba(255, 255, 255, 0.08)',
              }
            }}
          />
        </Group>

        {totalPages > 1 && (
          <Pagination
            value={page}
            onChange={onPageChange}
            total={totalPages}
            size="sm"
            radius="md"
            styles={{
              control: {
                backgroundColor: 'rgba(255, 255, 255, 0.03)',
                border: '1px solid rgba(255, 255, 255, 0.08)',
                '&[data-active]': {
                  backgroundColor: 'var(--mantine-color-blue-6)',
                  border: '1px solid var(--mantine-color-blue-6)',
                },
                '&:hover:not([data-active])': {
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                }
              }
            }}
          />
        )}

        <Text size="sm" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
          Page {page} of {totalPages}
        </Text>
      </Group>
    </Card>
  );
}
