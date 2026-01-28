import { useAtom } from 'jotai';
import {
  searchQueryAtom,
  selectedTagsAtom,
  showUnviewedAtom,
  sortOrderAtom,
  startDateAtom,
  endDateAtom,
} from '../../atoms/clipsFilters';
import { useClips } from '../queries';

export function useRelatedClips(clipId: string, categoryId: string | undefined) {
  // Filter state (shared via Jotai atoms)
  const [searchQuery] = useAtom(searchQueryAtom);
  const [selectedTags] = useAtom(selectedTagsAtom);
  const [showUnviewed] = useAtom(showUnviewedAtom);
  const [sortOrder] = useAtom(sortOrderAtom);
  const [startDate] = useAtom(startDateAtom);
  const [endDate] = useAtom(endDateAtom);

  // Format filter params
  const tagsParam = selectedTags.length > 0 ? selectedTags.join(',') : undefined;
  const titleSearchParam = searchQuery.trim() || undefined;

  // Convert string dates to Date objects for API
  const startDateObj = startDate ? new Date(startDate) : undefined;
  const endDateObj = endDate ? new Date(endDate) : undefined;

  // Related clips - fetch with same filters, get enough to find next 5
  const { data: relatedClipsData, isLoading: loadingRelatedClips } = useClips({
    categoryId: categoryId ?? '',
    page: 1,
    pageSize: 100, // Fetch more to ensure we get clips after current one
    tags: tagsParam,
    titleSearch: titleSearchParam,
    unviewedOnly: showUnviewed,
    sortOrder,
    startDate: startDateObj,
    endDate: endDateObj,
  });

  // Find current clip's index and get next 5 clips
  const currentIndex = relatedClipsData?.clips.findIndex(c => c.clipId === clipId) ?? -1;
  const relatedClips = currentIndex >= 0 && relatedClipsData
    ? relatedClipsData.clips.slice(currentIndex + 1, currentIndex + 6)
    : [];

  return {
    relatedClips,
    isLoading: loadingRelatedClips || !categoryId,
  };
}
