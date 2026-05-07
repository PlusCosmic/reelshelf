using Nucleus.Clips.ApexLegends.Models;

namespace Nucleus.Clips.ApexLegends.LegendDetection;

public class ApexDetectionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ApexDetectionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ApexStatements apexStatements = scope.ServiceProvider.GetRequiredService<ApexStatements>();
                IApexDetectionQueueService queueService = scope.ServiceProvider.GetRequiredService<IApexDetectionQueueService>();
                List<ApexStatements.ApexClipDetectionRow> pendingTasks = await apexStatements.GetApexClipDetectionsByStatus((int)ClipDetectionStatus.InProgress);

                foreach (ApexStatements.ApexClipDetectionRow task in pendingTasks)
                {
                    if (task.TaskId == null)
                    {
                        continue;
                    }

                    Guid taskId = task.TaskId.Value;

                    DetectionResult? result = await queueService.GetTaskResultAsync(taskId);
                    if (result == null)
                    {
                        continue;
                    }

                    await apexStatements.SetApexClipDetectionStatus(task.ClipId, (int)result.GetStatus());
                    await apexStatements.SetApexClipDetectionPrimaryDetection(task.ClipId, result.BestOverall != null ? (int)result.BestOverall.CharacterName.GetLegendByName() : (int)ApexLegend.None);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to process apex clip detection tasks");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}