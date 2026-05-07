import { Card, Group, Skeleton, Stack } from "@mantine/core";

export function ClipCardSkeleton() {
  return (
    <Card
      w="100%"
      radius="md"
      p="xs"
      style={{
        border: "1px solid rgba(0, 212, 255, 0.1)",
        background:
          "linear-gradient(135deg, rgba(15, 15, 25, 0.8) 0%, rgba(20, 20, 35, 0.7) 100%)",
      }}
    >
      <Group wrap="nowrap" gap="sm" align="center">
        <Skeleton
          height={90}
          width={160}
          radius="sm"
          style={
            {
              "--skeleton-color-from": "rgba(0, 212, 255, 0.05)",
              "--skeleton-color-to": "rgba(168, 85, 247, 0.1)",
            } as React.CSSProperties
          }
        />
        <Stack gap={4} style={{ flex: 1 }}>
          <Skeleton height={16} width="70%" radius="sm" />
          <Group gap={4}>
            <Skeleton height={18} width={50} radius="sm" />
            <Skeleton height={18} width={50} radius="sm" />
            <Skeleton height={18} width={50} radius="sm" />
          </Group>
          <Group gap="sm">
            <Skeleton height={12} width={80} radius="sm" />
            <Skeleton height={12} width={60} radius="sm" />
          </Group>
        </Stack>
      </Group>
    </Card>
  );
}
