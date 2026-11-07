using System.Text.Json;
using Microsoft.Data.Sqlite;
using Reelshelf.Uploader.Core.Files;

namespace Reelshelf.Uploader.Core.Ledger;

/// <summary>Where a file is in the uploader's hands.</summary>
public enum LedgerState
{
    /// <summary>Already in the folder when it was chosen; never uploaded.</summary>
    Baseline,

    /// <summary>Settled, with a book, waiting for its turn (or for a retry).</summary>
    Waiting,

    /// <summary>Settled, but its folder has no book yet (S3).</summary>
    NeedsBook,

    /// <summary>Prepared on the API and part way through its upload.</summary>
    Uploading,

    /// <summary>On the shelf.</summary>
    Shelved,

    /// <summary>The library already has it (the API's MD5 check).</summary>
    Duplicate,

    /// <summary>Longer than the plan keeps.</summary>
    TooLong,

    /// <summary>The plan had no room for it; uploads pause until it has.</summary>
    PlanFull,

    /// <summary>Out of attempts, or refused for good.</summary>
    Failed
}

public static class LedgerStates
{
    public static string ToDatabase(this LedgerState state) => state switch
    {
        LedgerState.Baseline => "baseline",
        LedgerState.Waiting => "waiting",
        LedgerState.NeedsBook => "needs_book",
        LedgerState.Uploading => "uploading",
        LedgerState.Shelved => "shelved",
        LedgerState.Duplicate => "duplicate",
        LedgerState.TooLong => "too_long",
        LedgerState.PlanFull => "plan_full",
        LedgerState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static LedgerState FromDatabase(string value) => value switch
    {
        "baseline" => LedgerState.Baseline,
        "waiting" => LedgerState.Waiting,
        "needs_book" => LedgerState.NeedsBook,
        "uploading" => LedgerState.Uploading,
        "shelved" => LedgerState.Shelved,
        "duplicate" => LedgerState.Duplicate,
        "too_long" => LedgerState.TooLong,
        "plan_full" => LedgerState.PlanFull,
        "failed" => LedgerState.Failed,
        _ => LedgerState.Failed
    };
}

/// <summary>One file the uploader has seen, keyed by its recordings folder and its path below it.</summary>
public sealed class LedgerEntry
{
    public required string Root { get; init; }
    public required string Path { get; init; }
    public long Size { get; set; }
    public DateTimeOffset LastWriteUtc { get; set; }
    public LedgerState State { get; set; }

    /// <summary>The folder below the root it is in ("" for the root), whose book it goes on.</summary>
    public string Folder => VideoFiles.FolderOf(Path);

    /// <summary>The clip's title: the file name without its extension.</summary>
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public Guid? ClipId { get; set; }
    public string? Md5 { get; set; }
    public Guid? CategoryId { get; set; }
    public string? UploadId { get; set; }
    public long? PartSize { get; set; }
    public int? PartCount { get; set; }

    /// <summary>ETags of the parts storage has, by part number.</summary>
    public Dictionary<int, string> Parts { get; set; } = [];

    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public double? LengthSeconds { get; set; }

    /// <summary>When a waiting file may be tried again after a failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public int Progress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ShelvedAt { get; set; }

    /// <summary>The same file: its size and last-write time, to the millisecond the ledger keeps, are unchanged.</summary>
    public bool Matches(ScannedFile file) =>
        file.Size == Size && file.LastWriteUtc.ToUnixTimeMilliseconds() == LastWriteUtc.ToUnixTimeMilliseconds();

    /// <summary>Forgets an upload in progress, so the file is prepared afresh.</summary>
    public void ForgetUpload()
    {
        ClipId = null;
        UploadId = null;
        PartSize = null;
        PartCount = null;
        Parts = [];
        Progress = 0;
    }
}

/// <summary>
/// The uploader's local record of every file it has seen (SQLite, <c>ledger.db</c>), so a rescan never hashes or
/// uploads a file twice: a file whose path, size and last-write time match a row is left alone. It also keeps each
/// upload's parts and their ETags, so an upload resumes after a restart. It stays on the device.
/// </summary>
public sealed class UploadLedger : IDisposable
{
    public const int RecentCount = 50;

    private const string Columns =
        "root, path, size, last_write, state, clip_id, md5, category_id, upload_id, part_size, part_count, parts, " +
        "attempts, last_error, length_seconds, next_attempt_at, progress, created_at, updated_at, shelved_at";

    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    public UploadLedger(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        _connection.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS file (
                root TEXT NOT NULL,
                path TEXT NOT NULL,
                size INTEGER NOT NULL,
                last_write INTEGER NOT NULL,
                state TEXT NOT NULL,
                clip_id TEXT,
                md5 TEXT,
                category_id TEXT,
                upload_id TEXT,
                part_size INTEGER,
                part_count INTEGER,
                parts TEXT,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                length_seconds REAL,
                next_attempt_at INTEGER,
                progress INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                shelved_at INTEGER,
                PRIMARY KEY (root, path)
            );
            CREATE INDEX IF NOT EXISTS file_state_idx ON file (state, created_at);
            """);
    }

    public LedgerEntry? Find(string root, string path)
    {
        lock (_lock)
        {
            return Query($"SELECT {Columns} FROM file WHERE root = $root AND path = $path",
                ("$root", root), ("$path", VideoFiles.Normalize(path))).FirstOrDefault();
        }
    }

    /// <summary>Whether the ledger has this exact file (path, size and last-write time).</summary>
    public bool Knows(string root, ScannedFile file) => Find(root, file.RelativePath)?.Matches(file) == true;

    /// <summary>Records files as already there, so they are never uploaded; files it has are left as they are.</summary>
    public int AddBaseline(string root, IEnumerable<ScannedFile> files, DateTimeOffset now)
    {
        int added = 0;
        lock (_lock)
        {
            using SqliteTransaction transaction = _connection.BeginTransaction();
            foreach (ScannedFile file in files)
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT OR IGNORE INTO file (root, path, size, last_write, state, created_at, updated_at)
                    VALUES ($root, $path, $size, $lastWrite, 'baseline', $now, $now)
                    """;
                command.Parameters.AddWithValue("$root", root);
                command.Parameters.AddWithValue("$path", file.RelativePath);
                command.Parameters.AddWithValue("$size", file.Size);
                command.Parameters.AddWithValue("$lastWrite", file.LastWriteUtc.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
                added += command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return added;
    }

    /// <summary>Writes the whole row, adding it when it is new.</summary>
    public void Save(LedgerEntry entry)
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO file ({Columns})
                VALUES ($root, $path, $size, $lastWrite, $state, $clipId, $md5, $categoryId, $uploadId, $partSize,
                        $partCount, $parts, $attempts, $lastError, $length, $nextAttempt, $progress, $created, $updated,
                        $shelved)
                ON CONFLICT (root, path) DO UPDATE SET
                    size = excluded.size, last_write = excluded.last_write, state = excluded.state,
                    clip_id = excluded.clip_id, md5 = excluded.md5, category_id = excluded.category_id,
                    upload_id = excluded.upload_id, part_size = excluded.part_size, part_count = excluded.part_count,
                    parts = excluded.parts, attempts = excluded.attempts, last_error = excluded.last_error,
                    length_seconds = excluded.length_seconds, next_attempt_at = excluded.next_attempt_at,
                    progress = excluded.progress, created_at = excluded.created_at, updated_at = excluded.updated_at,
                    shelved_at = excluded.shelved_at
                """;
            command.Parameters.AddWithValue("$root", entry.Root);
            command.Parameters.AddWithValue("$path", VideoFiles.Normalize(entry.Path));
            command.Parameters.AddWithValue("$size", entry.Size);
            command.Parameters.AddWithValue("$lastWrite", entry.LastWriteUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$state", entry.State.ToDatabase());
            command.Parameters.AddWithValue("$clipId", (object?)entry.ClipId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$md5", (object?)entry.Md5 ?? DBNull.Value);
            command.Parameters.AddWithValue("$categoryId", (object?)entry.CategoryId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$uploadId", (object?)entry.UploadId ?? DBNull.Value);
            command.Parameters.AddWithValue("$partSize", (object?)entry.PartSize ?? DBNull.Value);
            command.Parameters.AddWithValue("$partCount", (object?)entry.PartCount ?? DBNull.Value);
            command.Parameters.AddWithValue("$parts", entry.Parts.Count == 0
                ? DBNull.Value
                : JsonSerializer.Serialize(entry.Parts));
            command.Parameters.AddWithValue("$attempts", entry.Attempts);
            command.Parameters.AddWithValue("$lastError", (object?)entry.LastError ?? DBNull.Value);
            command.Parameters.AddWithValue("$length", (object?)entry.LengthSeconds ?? DBNull.Value);
            command.Parameters.AddWithValue("$nextAttempt",
                (object?)entry.NextAttemptAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
            command.Parameters.AddWithValue("$progress", entry.Progress);
            command.Parameters.AddWithValue("$created", entry.CreatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$updated", entry.UpdatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$shelved",
                (object?)entry.ShelvedAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public List<LedgerEntry> InState(string root, params LedgerState[] states)
    {
        lock (_lock)
        {
            string list = string.Join(", ", states.Select(state => $"'{state.ToDatabase()}'"));
            return Query($"SELECT {Columns} FROM file WHERE root = $root AND state IN ({list}) ORDER BY created_at, path",
                ("$root", root));
        }
    }

    /// <summary>
    /// The next file to upload: one already uploading first (to resume it), then the oldest waiting one whose retry
    /// time has come.
    /// </summary>
    public LedgerEntry? NextToUpload(string root, DateTimeOffset now)
    {
        lock (_lock)
        {
            return Query($"""
                SELECT {Columns} FROM file
                WHERE root = $root AND state IN ('uploading', 'waiting') AND
                      (next_attempt_at IS NULL OR next_attempt_at <= $now)
                ORDER BY state = 'uploading' DESC, created_at, path
                LIMIT 1
                """, ("$root", root), ("$now", now.ToUnixTimeMilliseconds())).FirstOrDefault();
        }
    }

    /// <summary>When the soonest waiting file may be tried again, if any is waiting on a retry.</summary>
    public DateTimeOffset? NextRetryAt(string root)
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                "SELECT min(next_attempt_at) FROM file WHERE root = $root AND state IN ('waiting', 'uploading') AND next_attempt_at IS NOT NULL";
            command.Parameters.AddWithValue("$root", root);
            object? value = command.ExecuteScalar();
            return value is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        }
    }

    /// <summary>The last <see cref="RecentCount"/> files that aren't baseline, newest first, from every folder (S4).</summary>
    public List<LedgerEntry> Recent(int count = RecentCount)
    {
        lock (_lock)
        {
            return Query($"SELECT {Columns} FROM file WHERE state <> 'baseline' ORDER BY created_at DESC, path LIMIT $count",
                ("$count", count));
        }
    }

    public int Count(string root, params LedgerState[] states) => InState(root, states).Count;

    public int ShelvedSince(string root, DateTimeOffset since)
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM file WHERE root = $root AND state = 'shelved' AND shelved_at >= $since";
            command.Parameters.AddWithValue("$root", root);
            command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    /// <summary>
    /// Gives files that failed one more try, at the app's start: they wait again with one attempt left, and those that
    /// fail again stay failed until the next start.
    /// </summary>
    public int RetryFailedOnce(string root, int maxAttempts, DateTimeOffset now)
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE file SET state = 'waiting', attempts = $attempts, next_attempt_at = NULL, updated_at = $now
                WHERE root = $root AND state = 'failed'
                """;
            command.Parameters.AddWithValue("$root", root);
            command.Parameters.AddWithValue("$attempts", Math.Max(0, maxAttempts - 1));
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>Files waiting for a book in a folder get it, and wait for their turn.</summary>
    public int AssignBook(string root, string folder, Guid categoryId, DateTimeOffset now)
    {
        int assigned = 0;
        foreach (LedgerEntry entry in InState(root, LedgerState.NeedsBook)
                     .Where(entry => string.Equals(entry.Folder, folder, StringComparison.OrdinalIgnoreCase)))
        {
            entry.CategoryId = categoryId;
            entry.State = LedgerState.Waiting;
            entry.UpdatedAt = now;
            Save(entry);
            assigned++;
        }

        return assigned;
    }

    /// <summary>Files that waited on a full library go back in the queue.</summary>
    public int RequeuePlanFull(string root, DateTimeOffset now)
    {
        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE file SET state = 'waiting', next_attempt_at = NULL, updated_at = $now
                WHERE root = $root AND state = 'plan_full'
                """;
            command.Parameters.AddWithValue("$root", root);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            return command.ExecuteNonQuery();
        }
    }

    private List<LedgerEntry> Query(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using SqliteDataReader reader = command.ExecuteReader();
        List<LedgerEntry> entries = [];
        while (reader.Read())
        {
            entries.Add(new LedgerEntry
            {
                Root = reader.GetString(0),
                Path = reader.GetString(1),
                Size = reader.GetInt64(2),
                LastWriteUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                State = LedgerStates.FromDatabase(reader.GetString(4)),
                ClipId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                Md5 = reader.IsDBNull(6) ? null : reader.GetString(6),
                CategoryId = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                UploadId = reader.IsDBNull(8) ? null : reader.GetString(8),
                PartSize = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                PartCount = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Parts = reader.IsDBNull(11)
                    ? []
                    : JsonSerializer.Deserialize<Dictionary<int, string>>(reader.GetString(11)) ?? [],
                Attempts = reader.GetInt32(12),
                LastError = reader.IsDBNull(13) ? null : reader.GetString(13),
                LengthSeconds = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                NextAttemptAt = reader.IsDBNull(15) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(15)),
                Progress = reader.GetInt32(16),
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(17)),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(18)),
                ShelvedAt = reader.IsDBNull(19) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(19))
            });
        }

        return entries;
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
