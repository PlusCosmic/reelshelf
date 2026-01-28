using Dapper;
using Npgsql;
using Nucleus.Clips.ApexLegends.Models;

namespace Nucleus.Clips.ApexLegends;

public class ApexStatements(NpgsqlConnection connection)
{
    public async Task InsertApexClipDetection(Guid clipId, int status)
    {
        const string sql = """

                                       INSERT INTO apex_clip_detection (clip_id, status, primary_detection, secondary_detection)
                                       VALUES (@clipId, @status, 27, 27)
                           """;
        await connection.ExecuteAsync(sql, new { clipId, status });
    }

    public async Task SetApexClipDetectionTaskId(Guid clipId, Guid taskId)
    {
        const string sql = """
                                       UPDATE apex_clip_detection
                                       SET task_id = @taskId
                                       WHERE clip_id = @clipId
                           """;
        await connection.ExecuteAsync(sql, new { clipId, taskId });
    }

    public async Task SetApexClipDetectionPrimaryDetection(Guid clipId, int detection)
    {
        const string sql = """
                                       UPDATE apex_clip_detection
                                       SET primary_detection = @detection
                                       WHERE clip_id = @clipId
                           """;
        await connection.ExecuteAsync(sql, new { clipId, detection });
    }

    public async Task SetApexClipDetectionSecondaryDetection(Guid clipId, int detection)
    {
        const string sql = """
                                       UPDATE apex_clip_detection
                                       SET secondary_detection = @detection
                                       WHERE clip_id = @clipId
                           """;
        await connection.ExecuteAsync(sql, new { clipId, detection });
    }

    public async Task SetApexClipDetectionStatus(Guid clipId, int status)
    {
        const string sql = """
                                       UPDATE apex_clip_detection
                                       SET status = @status
                                       WHERE clip_id = @clipId
                           """;
        await connection.ExecuteAsync(sql, new { clipId, status });
    }

    public async Task<ApexClipDetectionRow?> GetApexClipDetection(Guid clipId)
    {
        const string sql = """
                                       SELECT clip_id, task_id, status, primary_detection, secondary_detection
                                       FROM apex_clip_detection
                                       WHERE clip_id = @clipId
                           """;
        return await connection.QuerySingleOrDefaultAsync<ApexClipDetectionRow>(sql, new { clipId });
    }

    public async Task<List<ApexClipDetectionRow>> GetAllApexClipDetections()
    {
        const string sql = """
                                       SELECT clip_id, task_id, status, primary_detection, secondary_detection
                                       FROM apex_clip_detection
                           """;
        return (await connection.QueryAsync<ApexClipDetectionRow>(sql)).ToList();
    }

    public async Task<List<ApexClipDetectionRow>> GetApexClipDetectionsByClipIds(List<Guid> clipIds)
    {
        if (clipIds == null || clipIds.Count == 0)
        {
            return [];
        }

        const string sql = """
                                       SELECT clip_id, task_id, status, primary_detection, secondary_detection
                                       FROM apex_clip_detection
                                       WHERE clip_id = ANY(@clipIds)
                           """;
        return (await connection.QueryAsync<ApexClipDetectionRow>(sql, new { clipIds })).ToList();
    }

    public async Task<List<ApexClipDetectionRow>> GetApexClipDetectionsByStatus(int status)
    {
        const string sql = """
                                       SELECT clip_id, task_id, status, primary_detection, secondary_detection
                                       FROM apex_clip_detection
                                       WHERE status = @status
                           """;
        return (await connection.QueryAsync<ApexClipDetectionRow>(sql, new { status })).ToList();
    }

    public async Task DeleteApexClipDetection(Guid clipId)
    {
        const string sql = "DELETE FROM apex_clip_detection WHERE clip_id = @clipId";
        await connection.ExecuteAsync(sql, new { clipId });
    }

    /// <summary>
    /// Gets minimal clip data for all clips in a specific game category.
    /// Used for Apex detection processing.
    /// </summary>
    public async Task<List<ClipForDetectionRow>> GetClipsForCategory(Guid gameCategoryId)
    {
        const string sql = "SELECT id, video_id FROM clip WHERE game_category_id = @gameCategoryId";
        return (await connection.QueryAsync<ClipForDetectionRow>(sql, new { gameCategoryId })).ToList();
    }

    // Database Models (PascalCase properties auto-mapped to snake_case via DefaultTypeMap.MatchNamesWithUnderscores)
    public class ApexClipDetectionRow
    {
        public Guid ClipId { get; set; }
        public Guid? TaskId { get; set; }
        public int Status { get; set; }
        public int PrimaryDetection { get; set; }
        public int SecondaryDetection { get; set; }

        public ClipDetectionStatus GetStatus()
        {
            return (ClipDetectionStatus)Status;
        }

        public ApexLegend GetPrimaryDetection()
        {
            return (ApexLegend)PrimaryDetection;
        }

        public ApexLegend GetSecondaryDetection()
        {
            return (ApexLegend)SecondaryDetection;
        }
    }

    public class ClipForDetectionRow
    {
        public Guid Id { get; set; }
        public Guid VideoId { get; set; }
    }
}