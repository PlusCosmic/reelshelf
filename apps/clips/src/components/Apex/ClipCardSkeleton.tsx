import { Card, Group, Skeleton, Stack } from '@mantine/core';

export function ClipCardSkeleton() {
  return (
    <Card
      w="100%"
      mt="xs"
      mb="xs"
      radius="lg"
      p="md"
      style={{
        border: '1px solid rgba(255, 255, 255, 0.05)',
        background: 'rgba(255, 255, 255, 0.02)',
      }}
    >
      <Group wrap="nowrap" gap="lg" align="center">
        <Skeleton height={135} width={240} radius="md" />
        <Stack gap="xs" style={{ flex: 1 }}>
          <Skeleton height={20} width="70%" radius="md" />
          <Skeleton height={20} width="50%" radius="md" />
          <Group gap="xs">
            <Skeleton height={24} width={60} radius="md" />
            <Skeleton height={24} width={60} radius="md" />
            <Skeleton height={24} width={60} radius="md" />
          </Group>
          <Group gap="md">
            <Skeleton height={14} width={100} radius="md" />
            <Skeleton height={14} width={80} radius="md" />
          </Group>
        </Stack>
      </Group>
    </Card>
  );
}
