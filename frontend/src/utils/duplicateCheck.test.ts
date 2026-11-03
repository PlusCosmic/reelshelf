import { describe, expect, it, vi } from "vitest";
import { createDuplicateChecker } from "./duplicateCheck";

describe("createDuplicateChecker", () => {
  it("asks once per book for hashes that arrive together", async () => {
    const find = vi.fn(async (_categoryId: string, hashes: string[]) =>
      hashes.filter((hash) => hash === "b"),
    );
    const check = createDuplicateChecker(find, { delayMs: 5 });

    const answers = await Promise.all([
      check("book", "a"),
      check("book", "b"),
      check("book", "b"),
      check("other", "b"),
    ]);

    expect(answers).toEqual([false, true, true, true]);
    expect(find).toHaveBeenCalledTimes(2);
    expect(find).toHaveBeenCalledWith("book", ["a", "b"]);
    expect(find).toHaveBeenCalledWith("other", ["b"]);
  });

  it("sends a full batch at once", async () => {
    const find = vi.fn(async () => [] as string[]);
    const check = createDuplicateChecker(find, {
      delayMs: 60_000,
      maxBatch: 2,
    });

    await Promise.all([check("book", "a"), check("book", "b")]);

    expect(find).toHaveBeenCalledWith("book", ["a", "b"]);
  });

  it("answers not a duplicate when the check fails", async () => {
    const check = createDuplicateChecker(
      () => Promise.reject(new Error("offline")),
      { delayMs: 1 },
    );

    expect(await check("book", "a")).toBe(false);
  });
});
