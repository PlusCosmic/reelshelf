import { useAtom } from "jotai";
import { useDebouncedValue } from "@mantine/hooks";
import {
  endDateAtom,
  pageAtom,
  pageSizeAtom,
  searchQueryAtom,
  selectedTagsAtom,
  showUnviewedAtom,
  sortOrderAtom,
  startDateAtom,
  todayFilterActiveAtom,
  totalPagesAtom,
} from "../../atoms/clipsFilters";

export function useClipsFilters() {
  // Jotai atoms for filter/pagination state
  const [pageSize, setPageSize] = useAtom(pageSizeAtom);
  const [page, setPage] = useAtom(pageAtom);
  const [totalPages, setTotalPages] = useAtom(totalPagesAtom);
  const [searchQuery, setSearchQuery] = useAtom(searchQueryAtom);
  const [debouncedSearch] = useDebouncedValue(searchQuery, 300);
  const [selectedTags, setSelectedTags] = useAtom(selectedTagsAtom);
  const [showUnviewed, setShowUnviewed] = useAtom(showUnviewedAtom);
  const [sortOrder, setSortOrder] = useAtom(sortOrderAtom);
  const [startDate, setStartDate] = useAtom(startDateAtom);
  const [endDate, setEndDate] = useAtom(endDateAtom);
  const [todayFilterActive, setTodayFilterActive] = useAtom(
    todayFilterActiveAtom,
  );

  // Convert string dates to Date objects for API
  const startDateObj = startDate ? new Date(startDate) : undefined;
  const endDateObj = endDate ? new Date(endDate) : undefined;

  // Format query params
  const tagsParam =
    selectedTags.length > 0 ? selectedTags.join(",") : undefined;
  const titleSearchParam = debouncedSearch.trim() || undefined;

  // Computed values
  const activeFilterCount =
    selectedTags.length +
    (showUnviewed ? 1 : 0) +
    (sortOrder !== undefined ? 1 : 0) +
    (startDate !== undefined ? 1 : 0) +
    (endDate !== undefined ? 1 : 0);
  const hasActiveFilters =
    searchQuery.length > 0 ||
    selectedTags.length > 0 ||
    showUnviewed ||
    sortOrder !== undefined ||
    startDate !== undefined ||
    endDate !== undefined;

  // Handlers
  const toggleTag = (tag: string) => {
    setSelectedTags((prev) =>
      prev.includes(tag) ? prev.filter((t) => t !== tag) : [...prev, tag],
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

  return {
    // State
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
    // Setters
    setPageSize,
    setPage,
    setTotalPages,
    setSearchQuery,
    setSelectedTags,
    setShowUnviewed,
    setSortOrder,
    setStartDate,
    setEndDate,
    setTodayFilterActive,
    // Formatted params
    tagsParam,
    titleSearchParam,
    // Computed values
    activeFilterCount,
    hasActiveFilters,
    // Handlers
    toggleTag,
    handlePageChange,
    handlePageSizeChange,
  };
}
