using Dapper;
using Npgsql;

namespace Nucleus.Clips.Core;

public class ClipsBackfillStatements(NpgsqlConnection connection)
{
    public async Task<List<ClipBackfillRow>> GetClipsNeedingBackfillAsync(int limit = 100)
    {
        const string sql = """
            SELECT id, video_id
            FROM clip
            WHERE title IS NULL OR length IS NULL OR thumbnail_file_name IS NULL
            ORDER BY created_at ASC
            LIMIT @Limit
            """;
        var results = await connection.QueryAsync<ClipBackfillRow>(sql, new { Limit = limit });
        return results.ToList();
    }

    public async Task UpdateClipMetadataAsync(
        Guid clipId,
        string title,
        int length,
        string thumbnailFileName,
        DateTimeOffset dateUploaded,
        long storageSize,
        int videoStatus,
        int encodeProgress)
    {
        const string sql = """
            UPDATE clip
            SET title = @Title,
                length = @Length,
                thumbnail_file_name = @ThumbnailFileName,
                date_uploaded = @DateUploaded,
                storage_size = @StorageSize,
                video_status = @VideoStatus,
                encode_progress = @EncodeProgress
            WHERE id = @ClipId
            """;
        await connection.ExecuteAsync(sql, new
        {
            ClipId = clipId,
            Title = title,
            Length = length,
            ThumbnailFileName = thumbnailFileName,
            DateUploaded = dateUploaded,
            StorageSize = storageSize,
            VideoStatus = videoStatus,
            EncodeProgress = encodeProgress
        });
    }
}

public record ClipBackfillRow
{
    public Guid Id { get; init; }
    public Guid VideoId { get; init; }
}
