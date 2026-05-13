import type { GameCategoryResponse } from "@/api-client";

type BulkUploadStatus =
  | "ready"
  | "needs_game"
  | "hashing"
  | "creating"
  | "uploading"
  | "paused"
  | "uploaded"
  | "filing"
  | "saved"
  | "filing_error"
  | "duplicate"
  | "cancelled"
  | "error";

export type GameAssignmentSource = "source_path" | "fallback_context";

export type BulkUploadRow = {
  id: string;
  file: File;
  sourcePath: string;
  title: string;
  categoryId: string | null;
  gameAssignmentSource: GameAssignmentSource | null;
  createdAt: Date;
  sessionDate: string;
  tags: string[];
  playlistId: string | null;
  progress: number;
  bytesUploaded: number;
  md5Hash: string | null;
  sessionPlaylistId: string | null;
  uploadedClipId: string | null;
  uploadedVideoId: string | null;
  error: string | null;
  status: BulkUploadStatus;
  selected: boolean;
};

export type BulkUploadRejectedFile = {
  file: File;
  sourcePath: string;
  reason: "unsupported_type" | "file_too_large";
  message: string;
};

export type BulkUploadFileInput =
  | File
  | {
      file: File;
      sourcePath?: string;
    };

export type BuildBulkUploadQueueOptions = {
  fallbackCategoryId?: string | null;
  maxFileSize?: number;
};

export type BuildBulkUploadQueueResult = {
  rows: BulkUploadRow[];
  rejected: BulkUploadRejectedFile[];
};

export type BulkUploadSession = {
  key: string;
  categoryId: string | null;
  sessionDate: string | null;
  rows: BulkUploadRow[];
};

const BULK_UPLOAD_MAX_FILE_SIZE = 4 * 1024 ** 3;

const VIDEO_TYPES = new Set([
  "video/mp4",
  "video/quicktime",
  "video/webm",
  "video/x-matroska",
]);

export function buildBulkUploadQueue(
  inputs: BulkUploadFileInput[],
  categories: GameCategoryResponse[],
  options: BuildBulkUploadQueueOptions = {},
): BuildBulkUploadQueueResult {
  const maxFileSize = options.maxFileSize ?? BULK_UPLOAD_MAX_FILE_SIZE;
  const rows: BulkUploadRow[] = [];
  const rejected: BulkUploadRejectedFile[] = [];

  inputs.forEach((input, index) => {
    const { file, sourcePath } = normalizeInput(input);

    if (!isSupportedVideoFile(file)) {
      rejected.push({
        file,
        sourcePath,
        reason: "unsupported_type",
        message: "Choose an MP4, MOV, WebM, or MKV video file.",
      });
      return;
    }

    if (file.size > maxFileSize) {
      rejected.push({
        file,
        sourcePath,
        reason: "file_too_large",
        message: "This file is larger than the 4 GB upload limit.",
      });
      return;
    }

    const assignment = assignGame(sourcePath, categories, {
      fallbackCategoryId: options.fallbackCategoryId,
    });
    const createdAt = deriveCreatedAt(file, sourcePath);
    const sessionDate = gamingSessionDate(createdAt);

    rows.push({
      id: createRowId(file, sourcePath, index),
      file,
      sourcePath,
      title: stripExtension(file.name),
      categoryId: assignment.categoryId,
      gameAssignmentSource: assignment.source,
      createdAt,
      sessionDate,
      tags: [],
      playlistId: null,
      progress: 0,
      bytesUploaded: 0,
      md5Hash: null,
      sessionPlaylistId: null,
      uploadedClipId: null,
      uploadedVideoId: null,
      error: null,
      status: assignment.categoryId ? "ready" : "needs_game",
      selected: true,
    });
  });

  return { rows, rejected };
}

export function sourcePathForFile(file: File, explicitSourcePath?: string) {
  const browserRelativePath = (file as File & { webkitRelativePath?: string })
    .webkitRelativePath;
  return explicitSourcePath || browserRelativePath || file.name;
}

function isSupportedVideoFile(file: File) {
  return VIDEO_TYPES.has(file.type) || /\.(mp4|mov|webm|mkv)$/i.test(file.name);
}

export function assignGame(
  sourcePath: string,
  categories: GameCategoryResponse[],
  options: { fallbackCategoryId?: string | null } = {},
): { categoryId: string | null; source: GameAssignmentSource | null } {
  const normalizedPath = normalizeForMatch(sourcePath);
  const pathCompact = compactForMatch(sourcePath);

  let bestMatch: { category: GameCategoryResponse; score: number } | null =
    null;

  for (const category of categories) {
    const score = [category.name, category.slug].reduce((best, value) => {
      if (!value) return best;
      const normalizedValue = normalizeForMatch(value);
      const compactValue = compactForMatch(value);
      const matched =
        containsPhrase(normalizedPath, normalizedValue) ||
        containsCompactPath(pathCompact, compactValue);
      return matched ? Math.max(best, compactValue.length) : best;
    }, 0);

    if (score > 0 && (!bestMatch || score > bestMatch.score)) {
      bestMatch = { category, score };
    }
  }

  if (bestMatch) {
    return { categoryId: bestMatch.category.id, source: "source_path" };
  }

  return options.fallbackCategoryId
    ? { categoryId: options.fallbackCategoryId, source: "fallback_context" }
    : { categoryId: null, source: null };
}

export function deriveCreatedAt(file: File, sourcePath?: string) {
  if (file.lastModified > 0) {
    return new Date(file.lastModified);
  }

  return parseTimestampFromSourcePath(sourcePath ?? file.name) ?? new Date(0);
}

function parseTimestampFromSourcePath(sourcePath: string) {
  const patterns: RegExp[] = [
    /(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})[_\-\s](?<hour>\d{2})(?<minute>\d{2})/,
    /(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})[_\-\s](?<hour>\d{2})-(?<minute>\d{2})/,
    /(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})[_\-\s]?(?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})/,
  ];

  for (const pattern of patterns) {
    const match = pattern.exec(sourcePath);
    if (!match?.groups) continue;

    const year = Number(match.groups.year);
    const month = Number(match.groups.month);
    const day = Number(match.groups.day);
    const hour = Number(match.groups.hour);
    const minute = Number(match.groups.minute);
    const second = Number(match.groups.second ?? "0");
    const date = new Date(year, month - 1, day, hour, minute, second);

    if (
      date.getFullYear() === year &&
      date.getMonth() === month - 1 &&
      date.getDate() === day &&
      date.getHours() === hour &&
      date.getMinutes() === minute &&
      date.getSeconds() === second
    ) {
      return date;
    }
  }

  return null;
}

export function gamingSessionDate(date: Date) {
  const sessionDate = new Date(date);
  if (sessionDate.getHours() < 5) {
    sessionDate.setDate(sessionDate.getDate() - 1);
  }

  return formatLocalDate(sessionDate);
}

export function groupRowsIntoSessions(rows: BulkUploadRow[]) {
  const sorted = rows.toSorted(
    (a, b) =>
      a.createdAt.getTime() - b.createdAt.getTime() ||
      a.sourcePath.localeCompare(b.sourcePath),
  );
  const sessions = new Map<string, BulkUploadSession>();

  for (const row of sorted) {
    const sessionDate = row.categoryId ? row.sessionDate : null;
    const key = row.categoryId
      ? `${row.categoryId}:${sessionDate}`
      : "unmatched";
    const existing = sessions.get(key);

    if (existing) {
      existing.rows.push(row);
    } else {
      sessions.set(key, {
        key,
        categoryId: row.categoryId,
        sessionDate,
        rows: [row],
      });
    }
  }

  return [...sessions.values()];
}

export function toggleRowSelection(
  rows: BulkUploadRow[],
  rowId: string,
  selected?: boolean,
) {
  return rows.map((row) =>
    row.id === rowId ? { ...row, selected: selected ?? !row.selected } : row,
  );
}

export function setAllRowsSelected(rows: BulkUploadRow[], selected: boolean) {
  return rows.map((row) => ({ ...row, selected }));
}

export function setSessionRowsSelected(
  rows: BulkUploadRow[],
  session: BulkUploadSession,
  selected: boolean,
) {
  const ids = new Set(session.rows.map((row) => row.id));
  return rows.map((row) => (ids.has(row.id) ? { ...row, selected } : row));
}

export function setRowTitle(
  rows: BulkUploadRow[],
  rowId: string,
  title: string,
) {
  return rows.map((row) => (row.id === rowId ? { ...row, title } : row));
}

export function setRowCategory(
  rows: BulkUploadRow[],
  rowId: string,
  categoryId: string | null,
): BulkUploadRow[] {
  return rows.map((row) =>
    row.id === rowId
      ? {
          ...row,
          categoryId,
          gameAssignmentSource: categoryId ? "fallback_context" : null,
          status: categoryId ? "ready" : "needs_game",
        }
      : row,
  );
}

export function applyTagsToSelectedRows(rows: BulkUploadRow[], tags: string[]) {
  const normalizedTags = normalizeTags(tags);
  return rows.map((row) => {
    if (!row.selected) return row;

    return {
      ...row,
      tags: normalizeTags([...row.tags, ...normalizedTags]).slice(0, 5),
    };
  });
}

export function setSelectedRowsPlaylist(
  rows: BulkUploadRow[],
  playlistId: string | null,
) {
  return rows.map((row) => (row.selected ? { ...row, playlistId } : row));
}

function normalizeTags(tags: string[]) {
  const normalizedTags = new Set<string>();
  for (const tag of tags) {
    const normalized = tag.trim().replace(/^#/, "").toLowerCase();
    if (normalized) normalizedTags.add(normalized);
  }
  return [...normalizedTags];
}

function normalizeInput(input: BulkUploadFileInput) {
  if (input instanceof File) {
    return { file: input, sourcePath: sourcePathForFile(input) };
  }

  return {
    file: input.file,
    sourcePath: sourcePathForFile(input.file, input.sourcePath),
  };
}

function createRowId(file: File, sourcePath: string, index: number) {
  return `${index}:${sourcePath}:${file.size}:${file.lastModified}`;
}

function stripExtension(name: string) {
  return name.replace(/\.[^.]+$/, "");
}

function normalizeForMatch(value: string) {
  return ` ${value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim()} `;
}

function compactForMatch(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "");
}

function containsPhrase(normalizedPath: string, normalizedValue: string) {
  return (
    normalizedValue.trim().length > 0 &&
    normalizedPath.includes(normalizedValue)
  );
}

function containsCompactPath(path: string, value: string) {
  if (!value) return false;

  for (let index = 0; index <= path.length - value.length; index += 1) {
    if (path.startsWith(value, index)) return true;
  }

  return false;
}

function formatLocalDate(date: Date) {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, "0");
  const day = `${date.getDate()}`.padStart(2, "0");
  return `${year}-${month}-${day}`;
}
