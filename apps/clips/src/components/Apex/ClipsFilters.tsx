import { Badge, Button, Card, Collapse, Group, Select, Stack, Switch, Text } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';

interface ClipsFiltersProps {
  filtersOpen: boolean;
  showUnviewed: boolean;
  selectedTags: Array<string>;
  allTags: Array<string>;
  sortOrder?: number;
  startDate?: string;
  endDate?: string;
  onShowUnviewedChange: (checked: boolean) => void;
  onToggleTag: (tag: string) => void;
  onClearTags: () => void;
  onSortOrderChange: (value: string | null) => void;
  onStartDateChange: (value: string | null) => void;
  onEndDateChange: (value: string | null) => void;
}

export function ClipsFilters({
  filtersOpen,
  showUnviewed,
  selectedTags,
  allTags,
  sortOrder,
  startDate,
  endDate,
  onShowUnviewedChange,
  onToggleTag,
  onClearTags,
  onSortOrderChange,
  onStartDateChange,
  onEndDateChange,
}: ClipsFiltersProps) {
  return (
    <Collapse in={filtersOpen}>
      <Card
        radius="lg"
        p="md"
        style={{
          background: 'rgba(255, 255, 255, 0.03)',
          border: '1px solid rgba(255, 255, 255, 0.05)',
        }}
      >
        <Stack gap="md">
          <div>
            <Text size="sm" fw={500} c="dimmed" mb="xs">
              View Options
            </Text>
            <Switch
              label="Only show unviewed clips"
              checked={showUnviewed}
              onChange={(e) => onShowUnviewedChange(e.currentTarget.checked)}
            />
          </div>

          <div>
            <Text size="sm" fw={500} c="dimmed" mb="xs">
              Sort & Filter
            </Text>
            <Group gap="md" grow>
              <Select
                placeholder="Sort order"
                clearable
                value={sortOrder !== undefined ? sortOrder.toString() : null}
                onChange={onSortOrderChange}
                data={[
                  { value: '0', label: 'Newest First' },
                  { value: '1', label: 'Oldest First' },
                ]}
              />
              <DatePickerInput
                placeholder="Start date"
                clearable
                value={startDate || null}
                onChange={onStartDateChange}
              />
              <DatePickerInput
                placeholder="End date"
                clearable
                value={endDate || null}
                onChange={onEndDateChange}
                minDate={startDate ? new Date(startDate) : undefined}
              />
            </Group>
          </div>

          {allTags.length > 0 && (
            <div>
              <Group justify="space-between" mb="xs">
                <Text size="sm" fw={500} c="dimmed">
                  Filter by Tags
                </Text>
                {selectedTags.length > 0 && (
                  <Button
                    size="xs"
                    variant="subtle"
                    onClick={onClearTags}
                  >
                    Clear all
                  </Button>
                )}
              </Group>
              <Group gap="xs">
                {allTags.map((tag) => {
                  const isSelected = selectedTags.includes(tag);
                  return (
                    <Badge
                      key={tag}
                      size="lg"
                      radius="md"
                      variant={isSelected ? "filled" : "light"}
                      color={isSelected ? "blue" : "gray"}
                      style={{
                        cursor: 'pointer',
                        textTransform: 'none',
                        transition: 'all 0.2s ease',
                        transform: isSelected ? 'scale(1.05)' : 'scale(1)',
                      }}
                      onClick={() => onToggleTag(tag)}
                    >
                      {tag}
                    </Badge>
                  );
                })}
              </Group>
            </div>
          )}
        </Stack>
      </Card>
    </Collapse>
  );
}
