using Newtonsoft.Json;
using Nucleus.Clips.ApexLegends.Models;
using StackExchange.Redis;

namespace Nucleus.Clips.ApexLegends.LegendDetection;

public interface IApexDetectionQueueService
{
    Task<Guid> QueueDetectionAsync(Guid clipId, List<string> screenshotUrls);
    Task<DetectionResult?> GetTaskResultAsync(Guid taskId);
}

public class ApexDetectionQueueService(IConnectionMultiplexer redis, ILogger<ApexDetectionQueueService> logger, ApexStatements apexStatements) : IApexDetectionQueueService
{
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<DetectionResult?> GetTaskResultAsync(Guid taskId)
    {
        RedisValue resultJson = await _db.StringGetAsync($"result:{taskId.ToString()}");
        return resultJson.IsNullOrEmpty ? null : JsonConvert.DeserializeObject<DetectionResult>(resultJson.ToString());
    }

    public async Task<Guid> QueueDetectionAsync(Guid clipId, List<string> screenshotUrls)
    {
        Guid taskId = Guid.NewGuid();

        var taskMessage = new
        {
            task_id = taskId.ToString(),
            clip_id = clipId.ToString(),
            screenshot_urls = screenshotUrls
        };

        await _db.ListLeftPushAsync("apex_detection_queue", JsonConvert.SerializeObject(taskMessage));
        await apexStatements.SetApexClipDetectionTaskId(clipId, taskId);
        await apexStatements.SetApexClipDetectionStatus(clipId, (int)ClipDetectionStatus.InProgress);
        logger.LogInformation("Queued task {TaskId} for clip {ClipId}", taskId, clipId);
        return taskId;
    }
}