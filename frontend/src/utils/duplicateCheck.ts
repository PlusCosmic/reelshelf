type FindDuplicates = (
  categoryId: string,
  md5Hashes: string[],
) => Promise<string[]>;

type Waiter = { resolve: (duplicate: boolean) => void };

/**
 * Asks the API whether files are already in a book, gathering the hashes that finish within `delayMs` of each other
 * (up to `maxBatch`) into one request per book, so a dropped folder's duplicates cost a handful of requests rather
 * than one clip preparation each. A failed check answers "not a duplicate": preparing the clip checks again.
 */
export function createDuplicateChecker(
  findDuplicates: FindDuplicates,
  {
    delayMs = 200,
    maxBatch = 100,
  }: { delayMs?: number; maxBatch?: number } = {},
) {
  const pending = new Map<string, Map<string, Waiter[]>>();
  const timers = new Map<string, ReturnType<typeof setTimeout>>();

  const flush = (categoryId: string) => {
    clearTimeout(timers.get(categoryId));
    timers.delete(categoryId);
    const batch = pending.get(categoryId);
    pending.delete(categoryId);
    if (!batch?.size) return;

    const settle = (duplicates: Set<string>) => {
      for (const [hash, waiters] of batch) {
        waiters.forEach((waiter) => waiter.resolve(duplicates.has(hash)));
      }
    };
    findDuplicates(categoryId, [...batch.keys()]).then(
      (duplicates) => settle(new Set(duplicates)),
      () => settle(new Set()),
    );
  };

  return (categoryId: string, md5Hash: string) =>
    new Promise<boolean>((resolve) => {
      let batch = pending.get(categoryId);
      if (!batch) {
        batch = new Map();
        pending.set(categoryId, batch);
      }
      batch.set(md5Hash, [...(batch.get(md5Hash) ?? []), { resolve }]);

      if (batch.size >= maxBatch) flush(categoryId);
      else if (!timers.has(categoryId))
        timers.set(
          categoryId,
          setTimeout(() => flush(categoryId), delayMs),
        );
    });
}
