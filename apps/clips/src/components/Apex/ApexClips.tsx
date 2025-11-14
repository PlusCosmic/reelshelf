import { useState } from "react";
import { Box, Card, Stack } from "@mantine/core";
import { useApexClips, useCurrentUser, useTopTags } from '../../hooks/queries';
import { useApexClipsFilters } from '../../hooks/apex/useApexClipsFilters';
import { ClipsHeader } from './ClipsHeader';
import { ClipsSearchBar } from './ClipsSearchBar';
import { ClipsFilters } from './ClipsFilters';
import { ClipsContentArea } from './ClipsContentArea';
import { ClipsPaginationControls } from './ClipsPaginationControls';

export function ApexClips() {
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
  } = useApexClipsFilters();

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
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
      };

      // Reset ALL filters to defaults
      setSearchQuery('');
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
  const allTags = topTagsData?.map(t => t.name) || [];

  const { data: clipsData, isLoading: isLoadingClips } = useApexClips({
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
  if (clipsData && clipsData.totalPages !== totalPages) {
    setTotalPages(clipsData.totalPages);
  }

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
              <ClipsHeader
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
