import { describe, expect, it } from "vitest";
import { calculateFileMD5, hashFileMd5 } from "./fileHash";

describe("calculateFileMD5", () => {
  it("hashes an empty file", async () => {
    expect(await calculateFileMD5(new Blob([]))).toBe(
      "d41d8cd98f00b204e9800998ecf8427e",
    );
  });

  it("gives the same hash across chunk boundaries", async () => {
    const blob = new Blob(["The quick brown fox jumps over the lazy dog"]);
    expect(await calculateFileMD5(blob, 5)).toBe(
      "9e107d9d372bb6826bd81d3542a419d6",
    );
  });

  it("falls back to the page's thread without workers", async () => {
    const file = new File(["The quick brown fox jumps over the lazy dog"], "a");
    expect(await hashFileMd5(file)).toBe("9e107d9d372bb6826bd81d3542a419d6");
  });
});
