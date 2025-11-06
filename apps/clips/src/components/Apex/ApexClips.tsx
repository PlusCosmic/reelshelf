import { useEffect, useState } from "react";
import { fetchApexClips, fetchMe } from "@repo/shared";
import {
  Badge,
  Box,
  Button,
  Card,
  Collapse,
  Group,
  ScrollArea,
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

export function ApexClips() {
  const [user, setUser] = useState<DiscordUser | null>(null);
  const [loadingUser, setLoadingUser] = useState(true);
  const [clips, setClips] = useState<Array<Clip>>([]);
  const [loadingClips, setLoadingClips] = useState(true);
  const [page] = useState<number>(1);
  const [pageSize] = useState<number>(50);
  const [_, setTotalPages] = useState<number>(1);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [showUnviewed, setShowUnviewed] = useState(false);
  const [selectedTags, setSelectedTags] = useState<Array<string>>([]);
  // Calculate page size based on available screen space

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
      if (!user) {
        setClips([]);
        return;
      }
      setLoadingClips(true);
      try {
        const xs = await fetchApexClips(page, pageSize);
        if (!xs) return;
        setTotalPages(xs.totalPages);
        setClips(xs.clips);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingClips(false);
      }
    })();
  }, [user, page, pageSize]);

  // Extract unique tags from all clips
  const allTags = Array.from(new Set(clips.flatMap(clip => clip.tags))).sort();

  // Filter clips based on search query, selected tags, and unviewed status
  const filteredClips = clips.filter(clip => {
    // Search filter
    if (searchQuery) {
      const query = searchQuery.toLowerCase();
      const matchesTitle = clip.video.title.toLowerCase().includes(query);
      const matchesTags = clip.tags.some(tag => tag.toLowerCase().includes(query));
      if (!matchesTitle && !matchesTags) return false;
    }

    // Tag filter
    if (selectedTags.length > 0) {
      const hasSelectedTag = selectedTags.some(tag => clip.tags.includes(tag));
      if (!hasSelectedTag) return false;
    }

    return true;
  });

  const items = filteredClips.map((clip) => <ClipCard clip={clip} />);

  const toggleTag = (tag: string) => {
    setSelectedTags(prev =>
      prev.includes(tag)
        ? prev.filter(t => t !== tag)
        : [...prev, tag]
    );
  };

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
                  {clips.length > 0 && (
                    <Badge size="lg" radius="md" variant="light" color="blue">
                      {filteredClips.length === clips.length
                        ? `${clips.length} clips`
                        : `${filteredClips.length} of ${clips.length} clips`}
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
        ) : filteredClips.length > 0 ? (
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
              {items.map((item, index) => (
                <div
                  key={item.key}
                  style={{
                    animation: 'fadeIn 0.3s ease-in-out',
                    animationDelay: `${index * 0.05}s`,
                    animationFillMode: 'both',
                  }}
                >
                  {item}
                </div>
              ))}
            </Stack>
          </ScrollArea>
        ) : (
          <EmptyState hasFilters={hasActiveFilters} />
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
