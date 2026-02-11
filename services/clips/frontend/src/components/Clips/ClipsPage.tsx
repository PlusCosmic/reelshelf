import { useEffect, useState } from "react";
import { Box, Card, Stack } from "@mantine/core";
import { ClipsHeader } from "./ClipsHeader";
import { ClipsSearchBar } from "./ClipsSearchBar";
import { ClipsFilters } from "./ClipsFilters";
import { ClipsContentArea } from "./ClipsContentArea";
import { ClipsPaginationControls } from "./ClipsPaginationControls";
import { useClipsFilters } from "@/hooks/clips/useClipsFilters";
import { useClips, useCurrentUser, useTopTags } from "@/hooks/queries";

export interface ClipsPageProps {
  categoryId: string;
  categorySlug: string;
  categoryName: string;
}

export function ClipsPage({
  categoryId,
  categorySlug,
  categoryName,
}: ClipsPageProps) {
  // UI state
  const [filtersOpen, setFiltersOpen] = useState(false);

  // Filter state from custom hook
  const {
    pageSize,
    page,
    totalPages,
    searchQuery,
    selectedTags,
    showUnviewed,
    sortOrder,
    startDate,
    endDate,
    startDateObj,
    endDateObj,
    todayFilterActive,
    setTotalPages,
    setSearchQuery,
    setSelectedTags,
    setShowUnviewed,
    setSortOrder,
    setStartDate,
    setEndDate,
    setTodayFilterActive,
    tagsParam,
    titleSearchParam,
    activeFilterCount,
    hasActiveFilters,
    toggleTag,
    handlePageChange,
    handlePageSizeChange,
  } = useClipsFilters();

  // Handlers for new filters
  const handleSortOrderChange = (value: string | null) => {
    setSortOrder(value ? parseInt(value, 10) : undefined);
  };

  const handleStartDateChange = (value: string | null) => {
    setStartDate(value || undefined);
  };

  const handleEndDateChange = (value: string | null) => {
    setEndDate(value || undefined);
  };

  // Quick filter: Toggle "Today's New" preset
  const handleToggleTodayFilter = () => {
    if (todayFilterActive) {
      // Turning off - clear the preset filters
      setStartDate(undefined);
      setEndDate(undefined);
      setShowUnviewed(false);
      setSortOrder(undefined);
      setTodayFilterActive(false);
    } else {
      // Turning on - reset ALL filters to default, then apply the preset
      const now = new Date();
      const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);

      // Format dates as YYYY-MM-DD for Mantine DatePickerInput
      const formatDate = (date: Date) => {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        const hour = String(date.getHours()).padStart(2, "0");
        const minute = String(date.getMinutes()).padStart(2, "0");
        return `${year}-${month}-${day}T${hour}:${minute}:00.000Z`;
      };

      // Reset ALL filters to defaults
      setSearchQuery("");
      setSelectedTags([]);

      // Apply today's preset filters
      setStartDate(formatDate(yesterday));
      setEndDate(formatDate(now));
      setShowUnviewed(true);
      setSortOrder(1); // Oldest first
      setTodayFilterActive(true);
      setFiltersOpen(false); // Close filters dropdown
    }
  };

  // React Query hooks
  const { isLoading: isLoadingUser } = useCurrentUser();
  const { data: topTagsData } = useTopTags();
  const allTags = topTagsData?.map((t) => t.name) || [];

  const { data: clipsData, isLoading: isLoadingClips } = useClips({
    categoryId,
    page,
    pageSize,
    tags: tagsParam,
    titleSearch: titleSearchParam,
    unviewedOnly: showUnviewed,
    sortOrder,
    startDate: startDateObj,
    endDate: endDateObj,
  });

  const clips = clipsData?.clips || [];
  const totalClips = clipsData?.totalClips || 0;

  // Update totalPages atom when data changes
  useEffect(() => {
    if (clipsData && clipsData.totalPages !== totalPages) {
      setTotalPages(clipsData.totalPages);
    }
  }, [clipsData?.totalPages]);

  const isLoading = isLoadingUser || isLoadingClips;

  return (
    <div style={{ height: "calc(100vh - 138px)" }}>
      <Stack align="stretch" justify="space-between" h="100%" gap="md">
        <Box>
          <Card
            p="lg"
            radius="xl"
            style={{
              background:
                "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
              backdropFilter: "blur(10px)",
              border: "1px solid rgba(0, 212, 255, 0.15)",
              boxShadow:
                "0 4px 30px rgba(0, 0, 0, 0.3), inset 0 0 60px rgba(0, 212, 255, 0.03)",
            }}
          >
            <Stack gap="md">
              <ClipsHeader
                categoryId={categoryId}
                categoryName={categoryName}
                totalClips={totalClips}
                filtersOpen={filtersOpen}
                activeFilterCount={activeFilterCount}
                todayFilterActive={todayFilterActive}
                onToggleFilters={() => setFiltersOpen(!filtersOpen)}
                onToggleTodayFilter={handleToggleTodayFilter}
              />

              <ClipsSearchBar
                searchQuery={searchQuery}
                onSearchChange={setSearchQuery}
              />

              <ClipsFilters
                filtersOpen={filtersOpen}
                showUnviewed={showUnviewed}
                selectedTags={selectedTags}
                allTags={allTags}
                sortOrder={sortOrder}
                startDate={startDate}
                endDate={endDate}
                onShowUnviewedChange={setShowUnviewed}
                onToggleTag={toggleTag}
                onClearTags={() => setSelectedTags([])}
                onSortOrderChange={handleSortOrderChange}
                onStartDateChange={handleStartDateChange}
                onEndDateChange={handleEndDateChange}
              />
            </Stack>
          </Card>
        </Box>

        <ClipsContentArea
          isLoading={isLoading}
          clips={clips}
          hasActiveFilters={hasActiveFilters}
          categoryId={categoryId}
          categorySlug={categorySlug}
        />

        {!isLoading && clips.length > 0 && (
          <ClipsPaginationControls
            page={page}
            totalPages={totalPages}
            pageSize={pageSize}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
          />
        )}
      </Stack>
    </div>
  );
}
