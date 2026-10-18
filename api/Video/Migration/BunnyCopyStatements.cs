using Dapper;
using Npgsql;

namespace Reelshelf.Video.Migration;

/// <summary>A migrated clip's old Bunny video (<c>clip.bunny_video_id</c>), waiting for the delete sweep.</summary>
public sealed record BunnyCopy(Guid ClipId, Guid BunnyVideoId);

/// <summary>An owner's Bunny collection for a game category (a <c>clip_collection</c> row).</summary>
public sealed record OwnedBunnyCollection(Guid Id, Guid OwnerId, Guid CollectionId);

/// <summary>What the Bunny delete sweep (<see cref="BunnyCopySweeper"/>) reads and changes.</summary>
public interface IBunnyCopyStore
{
    /// <summary>
    /// Up to <paramref name="limit"/> Bunny copies whose clips moved more than <paramref name="keepFor"/> ago, oldest
    /// move first, leaving out <paramref name="skipClipIds"/> and any copy a clip still plays from.
    /// </summary>
    Task<List<BunnyCopy>> GetExpiredCopiesAsync(TimeSpan keepFor, IReadOnlyCollection<Guid> skipClipIds, int limit);

    /// <summary>
    /// Locks the clip, checks the copy is still due and that no clip plays from it, runs
    /// <paramref name="deleteAtHost"/> on it and clears <c>bunny_video_id</c>, all in one transaction, so a clip
    /// switched back meanwhile keeps its video. False, without calling the host, when the copy is no longer due or
    /// another pass holds the clip. When the host call throws, nothing changes.
    /// </summary>
    Task<bool> DeleteCopyAsync(BunnyCopy copy, TimeSpan keepFor, Func<Guid, Task> deleteAtHost);

    /// <summary>
    /// Up to <paramref name="limit"/> Bunny collections that no clip on Bunny and no remaining Bunny copy refers to,
    /// leaving out <paramref name="skipIds"/>.
    /// </summary>
    Task<List<OwnedBunnyCollection>> GetUnusedCollectionsAsync(IReadOnlyCollection<Guid> skipIds, int limit);

    /// <summary>
    /// Under the owner's upload lock, deletes the collection's row if nothing refers to it yet, runs
    /// <paramref name="deleteAtHost"/> on it, and commits. False, without calling the host, when a clip refers to it
    /// again. When the host call throws, the row stays.
    /// </summary>
    Task<bool> DeleteCollectionAsync(OwnedBunnyCollection collection, Func<Guid, Task> deleteAtHost);
}

public class BunnyCopyStatements(NpgsqlConnection connection) : IBunnyCopyStore
{
    /// <summary>
    /// A copy is due once its clip moved more than @KeepFor ago. The NOT EXISTS is the guard that the sweep never
    /// deletes a Bunny video a clip still plays from, whichever clip that is (one switched back by hand included).
    /// </summary>
    private const string CopyIsDue = """
        clip.bunny_video_id IS NOT NULL
        AND clip.migrated_at < now() - @KeepFor
        AND NOT EXISTS (
            SELECT 1 FROM clip playing
            WHERE playing.video_host = 'bunny' AND playing.video_id = clip.bunny_video_id)
        """;

    /// <summary>
    /// A collection holds the owner's Bunny videos for one game category, so it's in use while any of the owner's
    /// clips in that category is on Bunny (a reserved upload included) or still has a Bunny copy.
    /// </summary>
    private const string CollectionIsUnused = """
        NOT EXISTS (
            SELECT 1 FROM clip
            WHERE clip.owner_id = clip_collection.owner_id
              AND clip.game_category_id = clip_collection.game_category_id
              AND (clip.video_host = 'bunny' OR clip.bunny_video_id IS NOT NULL))
        """;

    public async Task<List<BunnyCopy>> GetExpiredCopiesAsync(TimeSpan keepFor, IReadOnlyCollection<Guid> skipClipIds, int limit)
    {
        const string sql = $"""
            SELECT clip.id AS clip_id, clip.bunny_video_id
            FROM clip
            WHERE {CopyIsDue}
              AND NOT (clip.id = ANY(@Skip))
            ORDER BY clip.migrated_at, clip.id
            LIMIT @Limit
            """;
        return (await connection.QueryAsync<BunnyCopyRow>(sql, new { KeepFor = keepFor, Skip = skipClipIds.ToArray(), Limit = limit }))
            .Select(row => new BunnyCopy(row.ClipId, row.BunnyVideoId))
            .ToList();
    }

    public async Task<bool> DeleteCopyAsync(BunnyCopy copy, TimeSpan keepFor, Func<Guid, Task> deleteAtHost)
    {
        const string lockDue = $"""
            SELECT clip.id
            FROM clip
            WHERE clip.id = @ClipId AND clip.bunny_video_id = @BunnyVideoId
              AND {CopyIsDue}
            FOR UPDATE SKIP LOCKED
            """;
        const string clear = "UPDATE clip SET bunny_video_id = NULL WHERE id = @ClipId";

        return await InTransactionAsync(async transaction =>
        {
            Guid? locked = await connection.QuerySingleOrDefaultAsync<Guid?>(lockDue,
                new { copy.ClipId, copy.BunnyVideoId, KeepFor = keepFor }, transaction);
            if (locked is null)
            {
                return false;
            }

            await deleteAtHost(copy.BunnyVideoId);
            await connection.ExecuteAsync(clear, new { copy.ClipId }, transaction);
            return true;
        });
    }

    public async Task<List<OwnedBunnyCollection>> GetUnusedCollectionsAsync(IReadOnlyCollection<Guid> skipIds, int limit)
    {
        const string sql = $"""
            SELECT id, owner_id, collection_id
            FROM clip_collection
            WHERE {CollectionIsUnused}
              AND NOT (clip_collection.id = ANY(@Skip))
            ORDER BY id
            LIMIT @Limit
            """;
        return (await connection.QueryAsync<CollectionRow>(sql, new { Skip = skipIds.ToArray(), Limit = limit }))
            .Select(row => new OwnedBunnyCollection(row.Id, row.OwnerId, row.CollectionId))
            .ToList();
    }

    public async Task<bool> DeleteCollectionAsync(OwnedBunnyCollection collection, Func<Guid, Task> deleteAtHost)
    {
        const string delete = $"""
            DELETE FROM clip_collection
            WHERE clip_collection.id = @Id AND {CollectionIsUnused}
            """;

        return await InTransactionAsync(async transaction =>
        {
            // The lock an upload reserves its clip under (ClipsStatements.BeginOwnerStorageLock) before it looks the
            // collection up. Holding it until the collection is gone at Bunny means an upload either reserved its
            // clip first, so the collection is still in use, or finds no collection and makes a new one; a video is
            // never filed into a collection that is being deleted. Unlike the uploads, this holds the lock over the
            // one Bunny call, which only delays that owner's next upload, and only once per collection.
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext(@key))",
                new { key = $"clip-storage:{collection.OwnerId}" }, transaction);
            if (await connection.ExecuteAsync(delete, new { collection.Id }, transaction) == 0)
            {
                return false;
            }

            await deleteAtHost(collection.CollectionId);
            return true;
        });
    }

    /// <summary>Commits when <paramref name="work"/> returns true and rolls back otherwise, or when it throws.</summary>
    private async Task<bool> InTransactionAsync(Func<NpgsqlTransaction, Task<bool>> work)
    {
        bool openedConnection = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            openedConnection = true;
        }

        try
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            if (!await work(transaction))
            {
                await transaction.RollbackAsync();
                return false;
            }

            await transaction.CommitAsync();
            return true;
        }
        finally
        {
            if (openedConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed class BunnyCopyRow
    {
        public Guid ClipId { get; set; }
        public Guid BunnyVideoId { get; set; }
    }

    private sealed class CollectionRow
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid CollectionId { get; set; }
    }
}
