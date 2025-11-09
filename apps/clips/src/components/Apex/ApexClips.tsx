import { useState } from "react";
import {
  Badge,
  Box,
  Button,
  Card,
  Collapse,
  Group,
  Pagination,
  ScrollArea,
  Select,
  Skeleton,
  Stack,
  Switch,
  Text,
  TextInput,
} from "@mantine/core";
import { IconAdjustments, IconChevronDown, IconChevronUp, IconMovie, IconSearch } from "@tabler/icons-react";
import { atomWithStorage } from 'jotai/utils'
import { useAtom } from "jotai";
import { VideoUpload } from "../VideoUpload.tsx";
import { useApexClips, useCurrentUser, useTopTags } from '../../hooks/queries';
import { ClipCard } from "./ClipCard.tsx";
import { ApexIcon } from "./ApexIcon.tsx";

function ClipCardSkeleton() {
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

function EmptyState({ hasFilters }: { hasFilters: boolean }) {
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

interface PaginationControlsProps {
  page: number;
  totalPages: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: string | null) => void;
}

function PaginationControls({ page, totalPages, pageSize, onPageChange, onPageSizeChange }: PaginationControlsProps) {
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

const pageSizeAtom= atomWithStorage<number>('page-size', 20);
const pageAtom = atomWithStorage<number>('page', 1);
const totalPagesAtom = atomWithStorage<number>('total-pages', 1);
const searchQueryAtom = atomWithStorage<string>('search-query', '');
const selectedTagsAtom = atomWithStorage<Array<string>>('selected-tags', []);
const showUnviewedAtom = atomWithStorage<boolean>('show-unviewed', false);

export function ApexClips() {
  // UI state
  const [filtersOpen, setFiltersOpen] = useState(false);

  // Jotai atoms for filter/pagination state
  const [pageSize, setPageSize] = useAtom(pageSizeAtom);
  const [page, setPage] = useAtom(pageAtom);
  const [totalPages, setTotalPages] = useAtom(totalPagesAtom);
  const [searchQuery, setSearchQuery] = useAtom(searchQueryAtom);
  const [selectedTags, setSelectedTags] = useAtom(selectedTagsAtom);
  const [showUnviewed, setShowUnviewed] = useAtom(showUnviewedAtom);

  // React Query hooks
  const { isLoading: isLoadingUser } = useCurrentUser();
  const { data: topTagsData } = useTopTags();
  const allTags = topTagsData?.map(t => t.name) || [];

  // Format query params
  const tagsParam = selectedTags.length > 0 ? selectedTags.join(',') : undefined;
  const titleSearchParam = searchQuery.trim() || undefined;

  const { data: clipsData, isLoading: isLoadingClips } = useApexClips({
    page,
    pageSize,
    tags: tagsParam,
    titleSearch: titleSearchParam,
    unviewedOnly: showUnviewed
  });

  const clips = clipsData?.clips || [];
  const totalClips = clipsData?.totalClips || 0;

  // Update totalPages atom when data changes
  if (clipsData && clipsData.totalPages !== totalPages) {
    setTotalPages(clipsData.totalPages);
  }

  const toggleTag = (tag: string) => {
    setSelectedTags(prev =>
      prev.includes(tag)
        ? prev.filter(t => t !== tag)
        : [...prev, tag]
    );
  };

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
  };

  const handlePageSizeChange = (size: string | null) => {
    if (size) {
      setPageSize(parseInt(size, 10));
      setPage(1); // Reset to first page when page size changes
    }
  };

  const activeFilterCount = selectedTags.length + (showUnviewed ? 1 : 0);
  const hasActiveFilters = searchQuery.length > 0 || selectedTags.length > 0 || showUnviewed;

  const isLoading = isLoadingUser || isLoadingClips;

  return (
    <div style={{ height: "calc(100vh - 138px)" }}>
      <Stack align="stretch" justify="space-between" h="100%" gap="md">
        <Box>
          <Card
            p="lg"
            radius="xl"
            style={{
              background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
              backdropFilter: 'blur(10px)',
              border: '1px solid rgba(255, 255, 255, 0.1)',
            }}
          >
            <Stack gap="md">
              {/* Main Header Row */}
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
                    variant={filtersOpen ? "light" : "subtle"}
                    leftSection={<IconAdjustments size={18} />}
                    rightSection={filtersOpen ? <IconChevronUp size={16} /> : <IconChevronDown size={16} />}
                    onClick={() => setFiltersOpen(!filtersOpen)}
                    radius="md"
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

              {/* Search Bar */}
              <TextInput
                size="lg"
                radius="xl"
                placeholder="Search clips by title, tags, or description..."
                leftSection={<IconSearch size={20} />}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
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

              {/* Collapsible Filters */}
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
                        onChange={(e) => setShowUnviewed(e.currentTarget.checked)}
                      />
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
                              onClick={() => setSelectedTags([])}
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
                                onClick={() => toggleTag(tag)}
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
            </Stack>
          </Card>
        </Box>

        {isLoading ? (
          <ScrollArea h="100%" type="scroll" scrollbarSize={8}>
            <Stack gap="xs">
              {Array.from({ length: 5 }).map((_, index) => (
                <ClipCardSkeleton key={index} />
              ))}
            </Stack>
          </ScrollArea>
        ) : clips.length > 0 ? (
          <ScrollArea
            h="100%"
            type="scroll"
            scrollbarSize={8}
            styles={{
              scrollbar: {
                '&:hover': {
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                }
              },
              thumb: {
                backgroundColor: 'rgba(255, 255, 255, 0.2)',
                '&:hover': {
                  backgroundColor: 'rgba(255, 255, 255, 0.3)',
                }
              }
            }}
          >
            <Stack gap="xs">
              {clips.map((clip, index) => (
                <div
                  key={clip.clipId}
                  style={{
                    animation: 'fadeIn 0.3s ease-in-out',
                    animationDelay: `${index * 0.05}s`,
                    animationFillMode: 'both',
                  }}
                >
                  <ClipCard clip={clip} />
                </div>
              ))}
            </Stack>
          </ScrollArea>
        ) : (
          <EmptyState hasFilters={hasActiveFilters} />
        )}

        {!isLoading && clips.length > 0 && (
          <PaginationControls
            page={page}
            totalPages={totalPages}
            pageSize={pageSize}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
          />
        )}
      </Stack>
      <style>{`
        @keyframes fadeIn {
          from {
            opacity: 0;
            transform: translateY(10px);
          }
          to {
            opacity: 1;
            transform: translateY(0);
          }
        }
      `}</style>
    </div>
  );
}
