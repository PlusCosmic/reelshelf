using System.Text;
using Dapper;
using Npgsql;
using Reelshelf.Bunny.Models;

namespace Reelshelf.Core;

public class ClipsStatements(NpgsqlConnection connection)
{
    // Queries
    public async Task<ClipCollectionRow?> GetCollectionByOwnerAndCategory(Guid ownerId, Guid gameCategoryId)
    {
        const string sql = """
            SELECT id, owner_id, collection_id, game_category_id
            FROM clip_collection
            WHERE owner_id = @ownerId AND game_category_id = @gameCategoryId
            LIMIT 1
            """;

        return await connection.QuerySingleOrDefaultAsync<ClipCollectionRow>(sql, new { ownerId, gameCategoryId });
    }

    /// <summary>
    /// Every collection the owner has, keyed by category. One row per category, so this is cheap enough
    /// to fetch whole rather than per clip when projecting a page that spans categories.
    /// </summary>
    public async Task<List<ClipCollectionRow>> GetCollectionsByOwner(Guid ownerId)
    {
        const string sql = """
            SELECT id, owner_id, collection_id, game_category_id
            FROM clip_collection
            WHERE owner_id = @ownerId
            """;

        return (await connection.QueryAsync<ClipCollectionRow>(sql, new { ownerId })).ToList();
    }

    public async Task<ClipCollectionRow> InsertCollection(Guid ownerId, Guid collectionId, Guid gameCategoryId)
    {
        const string sql = """
            INSERT INTO clip_collection (owner_id, collection_id, game_category_id)
            VALUES (@ownerId, @collectionId, @gameCategoryId)
            RETURNING id, owner_id, collection_id, game_category_id
            """;

        return await connection.QuerySingleAsync<ClipCollectionRow>(sql, new { ownerId, collectionId, gameCategoryId });
    }

    /// <summary>
    /// Wraps free-text search in a literal "contains" pattern. The user typed a substring, not a pattern,
    /// so the LIKE metacharacters and the escape character itself are escaped first — otherwise a search
    /// for "_" or "%" matches every clip. Pairs with ESCAPE '\' on each ILIKE.
    /// </summary>
    public static string? ToContainsPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        string escaped = search
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        return $"%{escaped}%";
    }

    /// <summary>
    /// A page of the owner's clips, newest first by default. A null <paramref name="gameCategoryId"/>
    /// spans every category, which is what the library grid and archive-wide search need.
    /// </summary>
    public async Task<PagedClipWithTagsRows> GetClipsWithTags(
        Guid ownerId,
        Guid? gameCategoryId,
        List<string>? tags = null,
        string? search = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        bool unviewedOnly = false,
        ClipSortOrder sortOrder = ClipSortOrder.DateDescending,
        Guid? viewedByUserId = null,
        int limit = 50,
        int offset = 0)
    {
        DynamicParameters parameters = new();
        parameters.Add("ownerId", ownerId);
        parameters.Add("gameCategoryId", gameCategoryId);
        parameters.Add("search", ToContainsPattern(search));
        parameters.Add("startDate", startDate);
        parameters.Add("endDate", endDate);
        parameters.Add("viewedByUserId", viewedByUserId);
        parameters.Add("limit", limit);
        parameters.Add("offset", offset);

        StringBuilder where = new("""
            c.owner_id = @ownerId
            AND (@gameCategoryId::uuid IS NULL OR c.game_category_id = @gameCategoryId)
            AND (@search::text IS NULL OR c.title ILIKE @search ESCAPE '\' OR EXISTS (
                SELECT 1
                FROM clip_tag search_ct
                INNER JOIN tag search_t ON search_ct.tag_id = search_t.id
                WHERE search_ct.clip_id = c.id AND search_t.name ILIKE @search ESCAPE '\'
            ) OR EXISTS (
                SELECT 1
                FROM game_category search_gc
                WHERE search_gc.id = c.game_category_id AND search_gc.name ILIKE @search ESCAPE '\'
            ))
            AND (@startDate::timestamptz IS NULL OR c.created_at >= @startDate)
            AND (@endDate::timestamptz IS NULL OR c.created_at <= @endDate)
            """);

        // If a tags filter is provided, require exact tag-name matches through relational predicates.
        if (tags != null && tags.Any())
        {
            for (int i = 0; i < tags.Count; i++)
            {
                where.Append($"""

                    AND EXISTS (
                        SELECT 1
                        FROM clip_tag filter_ct
                        INNER JOIN tag filter_t ON filter_ct.tag_id = filter_t.id
                        WHERE filter_ct.clip_id = c.id AND filter_t.name = @tag{i}
                    )
                    """);
                parameters.Add($"tag{i}", tags[i]);
            }
        }

        if (unviewedOnly)
        {
            where.Append("""

                AND NOT EXISTS (
                    SELECT 1
                    FROM clip_view cv
                    WHERE cv.clip_id = c.id AND cv.user_id = @viewedByUserId
                )
            """);
        }

        string orderBy = sortOrder == ClipSortOrder.DateAscending
            ? "created_at ASC, id ASC"
            : "created_at DESC, id DESC";

        string sql = $"""
            WITH filtered AS (
                SELECT
                    c.id,
                    c.owner_id,
                    c.video_id,
                    c.game_category_id,
                    c.md5_hash,
                    c.created_at,
                    c.title,
                    c.length,
                    c.thumbnail_file_name,
                    c.date_uploaded,
                    c.storage_size,
                    c.video_status,
                    c.encode_progress
                FROM clip c
                WHERE {where}
            ),
            tagged AS (
                SELECT
                    f.id,
                    f.owner_id,
                    f.video_id,
                    f.game_category_id,
                    f.md5_hash,
                    f.created_at,
                    f.title,
                    f.length,
                    f.thumbnail_file_name,
                    f.date_uploaded,
                    f.storage_size,
                    f.video_status,
                    f.encode_progress,
                    STRING_AGG(t.name, ',' ORDER BY t.name) as tag_names
                FROM filtered f
                LEFT JOIN clip_tag ct ON f.id = ct.clip_id
                LEFT JOIN tag t ON ct.tag_id = t.id
                GROUP BY f.id, f.owner_id, f.video_id, f.game_category_id, f.md5_hash, f.created_at, f.title, f.length, f.thumbnail_file_name, f.date_uploaded, f.storage_size, f.video_status, f.encode_progress
            )
            SELECT *
            FROM tagged
            ORDER BY {orderBy}
            LIMIT @limit OFFSET @offset;

            SELECT COUNT(*)
            FROM clip c
            WHERE {where};
            """;

        using SqlMapper.GridReader results = await connection.QueryMultipleAsync(sql, parameters);
        List<ClipWithTagsRow> rows = (await results.ReadAsync<ClipWithTagsRow>()).ToList();
        int totalCount = await results.ReadSingleAsync<int>();
        return new PagedClipWithTagsRows(rows, totalCount);
    }

    public async Task<HashSet<Guid>> GetViewedClipIds(Guid userId, List<Guid> clipIds)
    {
        if (!clipIds.Any())
        {
            return new HashSet<Guid>();
        }

        const string sql = """

                                       SELECT clip_id
                                       FROM clip_view
                                       WHERE user_id = @userId AND clip_id = ANY(@clipIds)
                           """;

        return (await connection.QueryAsync<Guid>(sql, new { userId, clipIds = clipIds.ToArray() })).ToHashSet();
    }

    public async Task<HashSet<Guid>> GetSharedClipIds(List<Guid> clipIds)
    {
        if (!clipIds.Any())
        {
            return new HashSet<Guid>();
        }

        const string sql = """
            SELECT clip_id
            FROM clip_share
            WHERE revoked_at IS NULL AND clip_id = ANY(@clipIds)
            """;

        return (await connection.QueryAsync<Guid>(sql, new { clipIds = clipIds.ToArray() })).ToHashSet();
    }

    public async Task<bool> ClipExistsByMd5Hash(Guid ownerId, Guid gameCategoryId, string md5Hash)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM clip
                WHERE owner_id = @ownerId AND game_category_id = @gameCategoryId AND md5_hash = @md5Hash
            )
            """;

        return await connection.QuerySingleAsync<bool>(sql, new { ownerId, gameCategoryId, md5Hash });
    }

    public async Task<ClipRow> InsertClip(Guid ownerId, Guid videoId, Guid gameCategoryId, string? md5Hash,
        DateTimeOffset createdAt, string? title = null, int? length = null, string? thumbnailFileName = null,
        DateTimeOffset? dateUploaded = null, long? storageSize = null, int? videoStatus = null, int? encodeProgress = null,
        long? fileSize = null, ClipSource? source = null)
    {
        const string sql = """
            INSERT INTO clip (owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress, file_size, source_provider, source_clip_id)
            VALUES (@ownerId, @videoId, @gameCategoryId, @md5Hash, @createdAt, @title, @length, @thumbnailFileName, @dateUploaded, @storageSize, @videoStatus, @encodeProgress, @fileSize, @sourceProvider, @sourceClipId)
            RETURNING id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress, file_size
            """;

        return await connection.QuerySingleAsync<ClipRow>(sql, new
        {
            ownerId, videoId, gameCategoryId, md5Hash, createdAt, title, length, thumbnailFileName, dateUploaded,
            storageSize, videoStatus, encodeProgress, fileSize,
            sourceProvider = source?.Provider,
            sourceClipId = source?.ClipId
        });
    }

    /// <summary>Corrects the declared size once the real byte count of a server-side copy is known.</summary>
    public async Task UpdateClipFileSize(Guid clipId, long fileSize)
    {
        await connection.ExecuteAsync("UPDATE clip SET file_size = @fileSize WHERE id = @clipId", new { clipId, fileSize });
    }

    /// <summary>Which of <paramref name="sourceClipIds"/> the owner has already imported from <paramref name="provider"/>.</summary>
    public async Task<HashSet<string>> GetImportedSourceClipIds(Guid ownerId, string provider, IReadOnlyCollection<string> sourceClipIds)
    {
        if (sourceClipIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT source_clip_id
            FROM clip
            WHERE owner_id = @ownerId AND source_provider = @provider AND source_clip_id = ANY(@sourceClipIds)
            """;

        IEnumerable<string> imported = await connection.QueryAsync<string>(sql,
            new { ownerId, provider, sourceClipIds = sourceClipIds.ToArray() });
        return imported.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Bytes of storage attributed to a single clip row.
    /// Counts the larger of the client-declared file size (known at creation, before anything is uploaded)
    /// and Bunny's reported storage size (trusted, but only available after encoding), so an under-declared
    /// size stops mattering once Bunny reports the real one.
    /// Every place that reports storage must use this expression, or the numbers disagree across the UI.
    /// </summary>
    public const string StorageBytesExpression = "GREATEST(COALESCE(file_size, 0), COALESCE(storage_size, 0))";

    /// <summary>
    /// Total bytes of clip storage attributed to an owner.
    /// </summary>
    public async Task<long> GetStorageUsedBytesByOwner(Guid ownerId)
    {
        string sql = $"""
            SELECT COALESCE(SUM({StorageBytesExpression}), 0)
            FROM clip
            WHERE owner_id = @ownerId
            """;

        return await connection.QuerySingleAsync<long>(sql, new { ownerId });
    }

    /// <summary>
    /// Clip count, unviewed count, duration and storage per category for an owner, computed over every
    /// clip row rather than over a page of clips. Storage uses <see cref="StorageBytesExpression"/> so a
    /// library topline built from these rows matches the storage meter exactly.
    /// </summary>
    public async Task<List<CategoryTotalsRow>> GetCategoryTotalsByOwner(Guid ownerId)
    {
        string sql = $"""
            SELECT
                c.game_category_id,
                COUNT(*) AS clip_count,
                COUNT(*) FILTER (WHERE cv.clip_id IS NULL) AS unviewed_count,
                COALESCE(SUM(COALESCE(c.length, 0)), 0) AS duration_seconds,
                COALESCE(SUM({StorageBytesExpression}), 0) AS storage_bytes
            FROM clip c
            LEFT JOIN clip_view cv ON cv.clip_id = c.id AND cv.user_id = @ownerId
            WHERE c.owner_id = @ownerId
            GROUP BY c.game_category_id
            """;

        return (await connection.QueryAsync<CategoryTotalsRow>(sql, new { ownerId })).ToList();
    }

    /// <summary>
    /// Opens a transaction holding a per-owner advisory lock so a storage check and the clip insert that
    /// follows it cannot interleave with another request for the same owner. Dispose without committing to roll back.
    /// Disposing also closes the connection if this call opened it, so the pooled connection is returned
    /// before the caller goes on to do external I/O rather than being held for the rest of the request.
    /// </summary>
    public async Task<OwnerStorageLock> BeginOwnerStorageLock(Guid ownerId)
    {
        bool openedConnection = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            openedConnection = true;
        }

        NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtext(@key))",
            new { key = $"clip-storage:{ownerId}" },
            transaction);
        return new OwnerStorageLock(connection, transaction, openedConnection);
    }

    public sealed class OwnerStorageLock(NpgsqlConnection connection, NpgsqlTransaction transaction, bool openedConnection)
        : IAsyncDisposable
    {
        public Task CommitAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            if (openedConnection)
            {
                // Return the pooled connection now; later Dapper calls on this scope reopen it per call.
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Clips whose video was never uploaded or failed with nothing stored: still in a pre-upload Bunny status
    /// (or Bunny's general Failed state) with zero storage, reserved at the API before <paramref name="reservedBefore"/>.
    /// These hold quota and their MD5 for their owner until removed.
    /// (created_at is the client-supplied capture time and says nothing about when the upload started.)
    /// </summary>
    public async Task<List<ClipRow>> GetAbandonedClips(DateTimeOffset reservedBefore)
    {
        const string sql = """
            SELECT id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress, file_size
            FROM clip
            WHERE reserved_at < @reservedBefore
              AND COALESCE(storage_size, 0) = 0
              AND (video_status IS NULL OR video_status IN (@Queued, @PresignedUploadStarted, @PresignedUploadFailed, @Failed))
            """;
        return (await connection.QueryAsync<ClipRow>(sql, new
        {
            reservedBefore,
            Queued = (int)BunnyVideoStatus.Queued,
            PresignedUploadStarted = (int)BunnyVideoStatus.PresignedUploadStarted,
            PresignedUploadFailed = (int)BunnyVideoStatus.PresignedUploadFailed,
            Failed = (int)BunnyVideoStatus.Failed
        })).ToList();
    }

    public async Task<ClipWithTagsRow?> GetClipWithTagsById(Guid clipId)
    {
        const string sql = """
            SELECT
                c.id,
                c.owner_id,
                c.video_id,
                c.game_category_id,
                c.md5_hash,
                c.created_at,
                c.title,
                c.length,
                c.thumbnail_file_name,
                c.date_uploaded,
                c.storage_size,
                c.video_status,
                c.encode_progress,
                STRING_AGG(t.name, ',') as tag_names
            FROM clip c
            LEFT JOIN clip_tag ct ON c.id = ct.clip_id
            LEFT JOIN tag t ON ct.tag_id = t.id
            WHERE c.id = @clipId
            GROUP BY c.id, c.owner_id, c.video_id, c.game_category_id, c.md5_hash, c.created_at, c.title, c.length, c.thumbnail_file_name, c.date_uploaded, c.storage_size, c.video_status, c.encode_progress
            """;

        return await connection.QuerySingleOrDefaultAsync<ClipWithTagsRow>(sql, new { clipId });
    }

    public async Task<bool> IsClipViewed(Guid userId, Guid clipId)
    {
        const string sql = """

                                       SELECT EXISTS(
                                           SELECT 1 FROM clip_view
                                           WHERE user_id = @userId AND clip_id = @clipId
                                       )
                           """;

        return await connection.QuerySingleAsync<bool>(sql, new { userId, clipId });
    }

    public async Task<TagRow?> GetTagByName(string name)
    {
        const string sql = "SELECT id, name FROM tag WHERE name = @name LIMIT 1";
        return await connection.QuerySingleOrDefaultAsync<TagRow>(sql, new { name });
    }

    public async Task<TagRow> InsertTag(string name)
    {
        const string sql = """

                                       INSERT INTO tag (name)
                                       VALUES (@name)
                                       RETURNING id, name
                           """;

        return await connection.QuerySingleAsync<TagRow>(sql, new { name });
    }

    public async Task<int> GetTagCountForClip(Guid clipId)
    {
        const string sql = "SELECT COUNT(*) FROM clip_tag WHERE clip_id = @clipId";
        return await connection.QuerySingleAsync<int>(sql, new { clipId });
    }

    public async Task<bool> ClipHasTag(Guid clipId, Guid tagId)
    {
        const string sql = """

                                       SELECT EXISTS(
                                           SELECT 1 FROM clip_tag
                                           WHERE clip_id = @clipId AND tag_id = @tagId
                                       )
                           """;

        return await connection.QuerySingleAsync<bool>(sql, new { clipId, tagId });
    }

    public async Task InsertClipTag(Guid clipId, Guid tagId)
    {
        const string sql = """

                                       INSERT INTO clip_tag (clip_id, tag_id)
                                       VALUES (@clipId, @tagId)
                           """;

        await connection.ExecuteAsync(sql, new { clipId, tagId });
    }

    public async Task DeleteClipTag(Guid clipId, Guid tagId)
    {
        const string sql = "DELETE FROM clip_tag WHERE clip_id = @clipId AND tag_id = @tagId";
        await connection.ExecuteAsync(sql, new { clipId, tagId });
    }

    /// <summary>Tags used on the owner's own clips, most used first. Other users' tags are private to them.</summary>
    public async Task<List<TopTagRow>> GetTagsOrderedByUsageForOwner(Guid ownerId, Guid? gameCategoryId = null)
    {
        const string sql = """
            SELECT t.name, COUNT(ct.clip_id)::int AS count
            FROM tag t
            JOIN clip_tag ct ON ct.tag_id = t.id
            JOIN clip c ON c.id = ct.clip_id
            WHERE c.owner_id = @ownerId
              AND (@gameCategoryId::uuid IS NULL OR c.game_category_id = @gameCategoryId)
            GROUP BY t.name
            ORDER BY count DESC, t.name ASC
            """;

        return (await connection.QueryAsync<TopTagRow>(sql, new { ownerId, gameCategoryId })).ToList();
    }

    public async Task<ClipViewRow> InsertClipView(Guid userId, Guid clipId)
    {
        const string sql = """

                                       INSERT INTO clip_view (user_id, clip_id, viewed_at)
                                       VALUES (@userId, @clipId, @viewedAt)
                                       RETURNING user_id, clip_id, viewed_at
                           """;

        return await connection.QuerySingleAsync<ClipViewRow>(sql, new { userId, clipId, viewedAt = DateTime.UtcNow });
    }

    public async Task<ClipRow?> GetClipById(Guid clipId)
    {
        const string sql =
            "SELECT id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress FROM clip WHERE id = @clipId LIMIT 1";
        return await connection.QuerySingleOrDefaultAsync<ClipRow>(sql, new { clipId });
    }

    public async Task<ClipRow?> GetClipByVideoId(Guid videoId)
    {
        const string sql =
            "SELECT id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress FROM clip WHERE video_id = @videoId LIMIT 1";
        return await connection.QuerySingleOrDefaultAsync<ClipRow>(sql, new { videoId });
    }

    /// <summary>
    /// Whether <paramref name="userId"/> may view a clip: they own it, it is in a playlist they created or
    /// collaborate on, or it has an active public share.
    /// </summary>
    /// <summary>
    /// Whether <paramref name="userId"/> may read the clip by id: the owner, or a current creator/collaborator
    /// of a playlist containing it. Share links are deliberately not considered here; they are only honoured
    /// through the token endpoint, so knowing a clip's UUID never substitutes for holding the token.
    /// </summary>
    public async Task<bool> UserCanAccessClip(Guid clipId, Guid userId)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM clip c WHERE c.id = @clipId AND c.owner_id = @userId
            ) OR EXISTS (
                SELECT 1
                FROM playlist_clips pc
                JOIN playlists p ON p.id = pc.playlist_id
                WHERE pc.clip_id = @clipId
                  AND (p.creator_user_id = @userId
                       OR EXISTS (SELECT 1 FROM playlist_collaborators col
                                  WHERE col.playlist_id = p.id AND col.user_id = @userId))
            )
            """;

        return await connection.QuerySingleAsync<bool>(sql, new { clipId, userId });
    }

    public async Task<ClipShareRow?> GetActiveShareByClipId(Guid clipId)
    {
        const string sql = """
            SELECT id, token, clip_id, owner_id, created_at, revoked_at
            FROM clip_share
            WHERE clip_id = @clipId AND revoked_at IS NULL
            LIMIT 1
            """;

        return await connection.QuerySingleOrDefaultAsync<ClipShareRow>(sql, new { clipId });
    }

    public async Task<ClipShareRow> InsertClipShare(string token, Guid clipId, Guid ownerId)
    {
        const string sql = """
            INSERT INTO clip_share (token, clip_id, owner_id)
            VALUES (@token, @clipId, @ownerId)
            RETURNING id, token, clip_id, owner_id, created_at, revoked_at
            """;

        return await connection.QuerySingleAsync<ClipShareRow>(sql, new { token, clipId, ownerId });
    }

    public async Task<SharedClipRow?> GetSharedClipByToken(string token)
    {
        const string sql = """
            SELECT
                cs.id as share_id,
                cs.token,
                cs.clip_id,
                c.owner_id,
                c.video_id,
                c.game_category_id,
                c.created_at,
                c.title,
                c.length,
                c.date_uploaded,
                c.video_status,
                gc.name as game_name
            FROM clip_share cs
            INNER JOIN clip c ON c.id = cs.clip_id
            INNER JOIN game_category gc ON gc.id = c.game_category_id
            WHERE cs.token = @token AND cs.revoked_at IS NULL
            LIMIT 1
            """;

        return await connection.QuerySingleOrDefaultAsync<SharedClipRow>(sql, new { token });
    }

    public async Task<ClipWithTagsRow?> GetClipWithTagsByIdAndOwner(Guid clipId, Guid ownerId)
    {
        const string sql = """
            SELECT
                c.id,
                c.owner_id,
                c.video_id,
                c.game_category_id,
                c.md5_hash,
                c.created_at,
                c.title,
                c.length,
                c.thumbnail_file_name,
                c.date_uploaded,
                c.storage_size,
                c.video_status,
                c.encode_progress,
                STRING_AGG(t.name, ',') as tag_names
            FROM clip c
            LEFT JOIN clip_tag ct ON c.id = ct.clip_id
            LEFT JOIN tag t ON ct.tag_id = t.id
            WHERE c.id = @clipId AND c.owner_id = @ownerId
            GROUP BY c.id, c.owner_id, c.video_id, c.game_category_id, c.md5_hash, c.created_at, c.title, c.length, c.thumbnail_file_name, c.date_uploaded, c.storage_size, c.video_status, c.encode_progress
            """;

        return await connection.QuerySingleOrDefaultAsync<ClipWithTagsRow>(sql, new { clipId, ownerId });
    }

    public async Task DeleteClip(Guid clipId)
    {
        const string sql = "DELETE FROM clip WHERE id = @clipId";
        await connection.ExecuteAsync(sql, new { clipId });
    }

    public async Task UpdateClipTitle(Guid clipId, string title)
    {
        const string sql = """
            UPDATE clip
            SET title = @title
            WHERE id = @clipId
            """;
        await connection.ExecuteAsync(sql, new { clipId, title });
    }

    public async Task<TagRow?> GetTagByNameForClip(Guid clipId, string tagName)
    {
        const string sql = """

                                       SELECT t.id, t.name
                                       FROM tag t
                                       INNER JOIN clip_tag ct ON t.id = ct.tag_id
                                       WHERE ct.clip_id = @clipId AND t.name = @tagName
                                       LIMIT 1
                           """;

        return await connection.QuerySingleOrDefaultAsync<TagRow>(sql, new { clipId, tagName });
    }

    public async Task<List<TagRow>> GetTagsForClip(Guid clipId)
    {
        const string sql = """

                                       SELECT t.id, t.name
                                       FROM tag t
                                       INNER JOIN clip_tag ct ON t.id = ct.tag_id
                                       WHERE ct.clip_id = @clipId
                                       ORDER BY t.name
                           """;

        return (await connection.QueryAsync<TagRow>(sql, new { clipId })).ToList();
    }

    public async Task<List<ClipRow>> GetAllClipsForCategory(Guid gameCategoryId)
    {
        const string sql = "SELECT id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress FROM clip WHERE game_category_id = @gameCategoryId";
        return (await connection.QueryAsync<ClipRow>(sql, new { gameCategoryId })).ToList();
    }

    public async Task<List<ClipRow>> GetClipsNeedingStatusUpdate()
    {
        const string sql = """
            SELECT id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress
            FROM clip
            WHERE video_status IS NULL OR video_status NOT IN (@FinishedStatus, @ResolutionFinishedStatus)
            """;
        return (await connection.QueryAsync<ClipRow>(sql, new
        {
            FinishedStatus = (int)BunnyVideoStatus.Finished,
            ResolutionFinishedStatus = (int)BunnyVideoStatus.ResolutionFinished
        })).ToList();
    }

    /// <summary>
    /// Replaces the placeholder video id on a reserved clip row with the Bunny video created for it.
    /// </summary>
    public async Task<ClipRow> AttachBunnyVideo(Guid clipId, Guid videoId, string? title, int? length,
        string? thumbnailFileName, DateTimeOffset? dateUploaded, long? storageSize, int? videoStatus, int? encodeProgress)
    {
        const string sql = """
            UPDATE clip
            SET video_id = @videoId,
                title = @title,
                length = @length,
                thumbnail_file_name = @thumbnailFileName,
                date_uploaded = @dateUploaded,
                storage_size = @storageSize,
                video_status = @videoStatus,
                encode_progress = @encodeProgress
            WHERE id = @clipId
            RETURNING id, owner_id, video_id, game_category_id, md5_hash, created_at, title, length, thumbnail_file_name, date_uploaded, storage_size, video_status, encode_progress, file_size
            """;

        return await connection.QuerySingleAsync<ClipRow>(sql,
            new { clipId, videoId, title, length, thumbnailFileName, dateUploaded, storageSize, videoStatus, encodeProgress });
    }

    /// <summary>
    /// Records the real Bunny video id on a reserved row when full attachment failed, so the abandoned-upload
    /// purge can still find and delete the video instead of it being orphaned at Bunny.
    /// </summary>
    public async Task SetClipVideoId(Guid clipId, Guid videoId)
    {
        await connection.ExecuteAsync("UPDATE clip SET video_id = @videoId WHERE id = @clipId", new { clipId, videoId });
    }

    public async Task UpdateClipMetadata(Guid clipId, string? title, int? length, string? thumbnailFileName,
        DateTimeOffset? dateUploaded, long? storageSize, int? videoStatus, int? encodeProgress)
    {
        const string sql = """
            UPDATE clip
            SET title = @title,
                length = @length,
                thumbnail_file_name = @thumbnailFileName,
                date_uploaded = @dateUploaded,
                storage_size = @storageSize,
                video_status = @videoStatus,
                encode_progress = @encodeProgress
            WHERE id = @clipId
            """;
        await connection.ExecuteAsync(sql, new { clipId, title, length, thumbnailFileName, dateUploaded, storageSize, videoStatus, encodeProgress });
    }

    public class ClipRow
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid VideoId { get; set; }
        public Guid GameCategoryId { get; set; }
        public string? Md5Hash { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? Title { get; set; }
        public int? Length { get; set; }
        public string? ThumbnailFileName { get; set; }
        public DateTimeOffset? DateUploaded { get; set; }
        public long? StorageSize { get; set; }
        public int? VideoStatus { get; set; }
        public int? EncodeProgress { get; set; }
        public long? FileSize { get; set; }
    }

    public class CategoryTotalsRow
    {
        public Guid GameCategoryId { get; set; }
        public long ClipCount { get; set; }
        public long UnviewedCount { get; set; }
        public long DurationSeconds { get; set; }
        public long StorageBytes { get; set; }
    }

    public class TagRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ClipTagRow
    {
        public Guid ClipId { get; set; }
        public Guid TagId { get; set; }
    }

    public class ClipCollectionRow
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid CollectionId { get; set; }
        public Guid GameCategoryId { get; set; }
    }

    public class ClipViewRow
    {
        public Guid UserId { get; set; }
        public Guid ClipId { get; set; }
        public DateTime ViewedAt { get; set; }
    }

    public class ClipShareRow
    {
        public Guid Id { get; set; }
        public string Token { get; set; } = string.Empty;
        public Guid ClipId { get; set; }
        public Guid OwnerId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
    }

    public class SharedClipRow
    {
        public Guid ShareId { get; set; }
        public string Token { get; set; } = string.Empty;
        public Guid ClipId { get; set; }
        public Guid OwnerId { get; set; }
        public Guid VideoId { get; set; }
        public Guid GameCategoryId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? Title { get; set; }
        public int? Length { get; set; }
        public DateTimeOffset? DateUploaded { get; set; }
        public int? VideoStatus { get; set; }
        public string GameName { get; set; } = string.Empty;
    }

    public class ClipWithTagsRow
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid VideoId { get; set; }
        public Guid GameCategoryId { get; set; }
        public string? Md5Hash { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? TagNames { get; set; } // comma-separated tags
        public string? Title { get; set; }
        public int? Length { get; set; }
        public string? ThumbnailFileName { get; set; }
        public DateTimeOffset? DateUploaded { get; set; }
        public long? StorageSize { get; set; }
        public int? VideoStatus { get; set; }
        public int? EncodeProgress { get; set; }
    }

    public record PagedClipWithTagsRows(List<ClipWithTagsRow> Rows, int TotalCount);

    public class TopTagRow
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}

/// <summary>Where an imported clip came from: the provider name and its id there. Local uploads have none.</summary>
public sealed record ClipSource(string Provider, string ClipId);
