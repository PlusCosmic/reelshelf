import { atomWithStorage } from 'jotai/utils';

// Pagination atoms
export const pageSizeAtom = atomWithStorage<number>('page-size', 20);
export const pageAtom = atomWithStorage<number>('page', 1);
export const totalPagesAtom = atomWithStorage<number>('total-pages', 1);

// Filter atoms
export const searchQueryAtom = atomWithStorage<string>('search-query', '');
export const selectedTagsAtom = atomWithStorage<Array<string>>('selected-tags', []);
export const showUnviewedAtom = atomWithStorage<boolean>('show-unviewed', false);
export const sortOrderAtom = atomWithStorage<number | undefined>('sort-order', undefined);
export const startDateAtom = atomWithStorage<string | undefined>('start-date', undefined);
export const endDateAtom = atomWithStorage<string | undefined>('end-date', undefined);

// Quick filter toggle
export const todayFilterActiveAtom = atomWithStorage<boolean>('today-filter-active', false);
