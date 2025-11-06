import { useEffect, useState } from "react";
import { fetchApexClips, fetchMe, fetchUnviewedApexClips, getTopTags } from "@repo/shared";
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
import { VideoUpload } from "../VideoUpload.tsx";
import { ClipCard } from "./ClipCard.tsx";
import { ApexIcon } from "./ApexIcon.tsx";
import type { Clip, DiscordUser } from "@repo/nucleus-api-client";

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

export function ApexClips() {
  const [user, setUser] = useState<DiscordUser | null>(null);
  const [loadingUser, setLoadingUser] = useState(true);
  const [clips, setClips] = useState<Array<Clip>>([]);
  const [loadingClips, setLoadingClips] = useState(true);
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(20);
  const [totalPages, setTotalPages] = useState<number>(1);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [showUnviewed, setShowUnviewed] = useState(false);
  const [selectedTags, setSelectedTags] = useState<Array<string>>([]);
  const [allTags, setAllTags] = useState<Array<string>>([]);
  const [totalClips, setTotalClips] = useState<number>(0);

  useEffect(() => {
    setLoadingUser(true);
    (async () => {
      try {
        const me = await fetchMe();
        setUser(me);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingUser(false);
      }
    })();
  }, []);

  useEffect(() => {
    (async () => {
      try {
        const topTags = await getTopTags();
        setAllTags(topTags.map(t => t.name));
      } catch (e) {
        console.error(e);
      }
    })();
  }, []);

  useEffect(() => {
    (async () => {
      if (!user) {
        setClips([]);
        setTotalPages(1);
        return;
      }
      setLoadingClips(true);
      try {
        // Format tags as comma-separated string
        const tagsParam = selectedTags.length > 0 ? selectedTags.join(',') : undefined;
        const titleSearchParam = searchQuery.trim() || undefined;

        // Use the appropriate endpoint based on unviewed toggle
        const xs = showUnviewed
          ? await fetchUnviewedApexClips(page, pageSize, tagsParam, titleSearchParam)
          : await fetchApexClips(page, pageSize, tagsParam, titleSearchParam);

        if (!xs) return;
        setClips(xs.clips);
        setTotalPages(xs.totalPages);
        setTotalClips(xs.totalClips);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingClips(false);
      }
    })();
  }, [user, page, pageSize, searchQuery, selectedTags, showUnviewed]);

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

  // Reset to page 1 when filters change
  useEffect(() => {
    setPage(1);
  }, [searchQuery, selectedTags, showUnviewed]);

  const activeFilterCount = selectedTags.length + (showUnviewed ? 1 : 0);
  const hasActiveFilters = searchQuery.length > 0 || selectedTags.length > 0 || showUnviewed;

  const isLoading = loadingUser || loadingClips;
  return (
    <div style={{ height: "calc(100vh - 122px)" }}>
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
