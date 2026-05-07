import { Card, Group, Pagination, Select, Text } from "@mantine/core";

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
  onPageSizeChange,
}: ClipsPaginationControlsProps) {
  return (
    <Card
      radius="lg"
      p="md"
      style={{
        border: "1px solid rgba(0, 212, 255, 0.15)",
        background:
          "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
        boxShadow: "0 4px 20px rgba(0, 0, 0, 0.2)",
      }}
    >
      <Group justify="space-between" align="center" wrap="nowrap">
        <Group gap="xs" align="center">
          <Text size="sm" c="dimmed" style={{ whiteSpace: "nowrap" }}>
            Items per page:
          </Text>
          <Select
            value={pageSize.toString()}
            onChange={onPageSizeChange}
            data={[
              { value: "10", label: "10" },
              { value: "20", label: "20" },
              { value: "30", label: "30" },
              { value: "50", label: "50" },
            ]}
            w={80}
            size="sm"
            styles={{
              input: {
                backgroundColor: "rgba(0, 212, 255, 0.05)",
                border: "1px solid rgba(0, 212, 255, 0.2)",
              },
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
                backgroundColor: "rgba(0, 212, 255, 0.05)",
                border: "1px solid rgba(0, 212, 255, 0.2)",
                color: "#f8fafc",
                transition: "all 0.2s ease",
                "&[data-active]": {
                  background:
                    "linear-gradient(135deg, #00d4ff 0%, #0ea5e9 100%)",
                  border: "1px solid #00d4ff",
                  color: "#0a0a14",
                  boxShadow: "0 0 15px rgba(0, 212, 255, 0.4)",
                },
                "&:hover:not([data-active])": {
                  backgroundColor: "rgba(0, 212, 255, 0.15)",
                  borderColor: "rgba(0, 212, 255, 0.4)",
                },
              },
            }}
          />
        )}

        <Text size="sm" style={{ whiteSpace: "nowrap", color: "#00d4ff" }}>
          Page {page} of {totalPages}
        </Text>
      </Group>
    </Card>
  );
}
