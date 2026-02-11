import { Badge, Button, Card, Collapse, Group, Select, Stack, Switch, Text } from '@mantine/core';
import { DateTimePicker } from "@mantine/dates";

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
          background: 'rgba(168, 85, 247, 0.05)',
          border: '1px solid rgba(168, 85, 247, 0.2)',
          boxShadow: 'inset 0 0 30px rgba(168, 85, 247, 0.05)',
        }}
      >
        <Stack gap="md">
          <div>
            <Text size="sm" fw={600} mb="xs" style={{ color: '#a855f7' }}>
              View Options
            </Text>
            <Switch
              label="Only show unviewed clips"
              checked={showUnviewed}
              onChange={(e) => onShowUnviewedChange(e.currentTarget.checked)}
              styles={{
                track: {
                  backgroundColor: showUnviewed ? '#a855f7' : 'rgba(255, 255, 255, 0.1)',
                  borderColor: showUnviewed ? '#a855f7' : 'rgba(255, 255, 255, 0.2)',
                },
              }}
            />
          </div>

          <div>
            <Text size="sm" fw={600} mb="xs" style={{ color: '#a855f7' }}>
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
                styles={{
                  input: {
                    backgroundColor: 'rgba(168, 85, 247, 0.05)',
                    border: '1px solid rgba(168, 85, 247, 0.2)',
                    '&:focus': {
                      borderColor: 'rgba(168, 85, 247, 0.5)',
                    },
                  },
                }}
              />
              <DateTimePicker
                placeholder="Start date"
                clearable
                value={startDate || null}
                onChange={onStartDateChange}
                styles={{
                  input: {
                    backgroundColor: 'rgba(168, 85, 247, 0.05)',
                    border: '1px solid rgba(168, 85, 247, 0.2)',
                    '&:focus': {
                      borderColor: 'rgba(168, 85, 247, 0.5)',
                    },
                  },
                }}
              />
              <DateTimePicker
                placeholder="End date"
                clearable
                value={endDate || null}
                onChange={onEndDateChange}
                minDate={startDate ? new Date(startDate) : undefined}
                styles={{
                  input: {
                    backgroundColor: 'rgba(168, 85, 247, 0.05)',
                    border: '1px solid rgba(168, 85, 247, 0.2)',
                    '&:focus': {
                      borderColor: 'rgba(168, 85, 247, 0.5)',
                    },
                  },
                }}
              />
            </Group>
          </div>

          {allTags.length > 0 && (
            <div>
              <Group justify="space-between" mb="xs">
                <Text size="sm" fw={600} style={{ color: '#a855f7' }}>
                  Filter by Tags
                </Text>
                {selectedTags.length > 0 && (
                  <Button
                    size="xs"
                    variant="subtle"
                    onClick={onClearTags}
                    style={{
                      color: '#ec4899',
                    }}
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
                      role="button"
                      tabIndex={0}
                      style={{
                        cursor: 'pointer',
                        textTransform: 'none',
                        transition: 'all 0.2s ease',
                        transform: isSelected ? 'scale(1.05)' : 'scale(1)',
                        background: isSelected
                          ? 'linear-gradient(135deg, #a855f7 0%, #ec4899 100%)'
                          : 'rgba(168, 85, 247, 0.1)',
                        border: isSelected
                          ? 'none'
                          : '1px solid rgba(168, 85, 247, 0.3)',
                        color: isSelected ? 'white' : '#a855f7',
                        boxShadow: isSelected ? '0 0 15px rgba(168, 85, 247, 0.4)' : 'none',
                      }}
                      onClick={() => onToggleTag(tag)}
                      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onToggleTag(tag); } }}
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
