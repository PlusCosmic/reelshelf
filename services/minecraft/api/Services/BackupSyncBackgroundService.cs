using Nucleus.Minecraft.Data;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

public class BackupSyncBackgroundService(
    ILogger<BackupSyncBackgroundService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration)
    : BackgroundService
{
    private readonly TimeSpan _syncInterval = TimeSpan.FromHours(
        double.TryParse(configuration["Backblaze:SyncIntervalHours"], out double hours) ? hours : 1
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Skip if B2 is not configured
        if (string.IsNullOrWhiteSpace(configuration["Backblaze:BucketName"]))
        {
            logger.LogInformation("Backblaze B2 not configured - backup sync service disabled");
            return;
        }

        logger.LogInformation("Backup sync service starting with interval: {Interval}", _syncInterval);

        // Wait a bit before the first sync to let the app fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using PeriodicTimer timer = new(_syncInterval);

        await SyncBackupsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncBackupsAsync(stoppingToken);
        }
    }

    private async Task SyncBackupsAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting scheduled backup sync for all active servers");

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BackupService backupService = scope.ServiceProvider.GetRequiredService<BackupService>();
            MinecraftStatements statements = scope.ServiceProvider.GetRequiredService<MinecraftStatements>();

            List<MinecraftServer> servers = await statements.GetAllActiveServersAsync();

            if (servers.Count == 0)
            {
                logger.LogInformation("No active servers found, skipping backup sync");
                return;
            }

            int totalUploaded = 0;
            int totalSkipped = 0;
            long totalBytesUploaded = 0;
            int totalDeleted = 0;
            long totalBytesDeleted = 0;

            foreach (MinecraftServer server in servers)
            {
                stoppingToken.ThrowIfCancellationRequested();

                logger.LogInformation("Syncing backups for server {ServerName} ({ServerId})", server.Name, server.Id);

                BackupSyncResult result = await backupService.SyncBackupsAsync(server, stoppingToken);

                if (result.Success)
                {
                    totalUploaded += result.FilesUploaded;
                    totalSkipped += result.FilesSkipped;
                    totalBytesUploaded += result.BytesUploaded;

                    // Clean up old local backups after successful sync
                    var (filesDeleted, bytesDeleted) = backupService.CleanupOldBackups(server);
                    totalDeleted += filesDeleted;
                    totalBytesDeleted += bytesDeleted;

                    logger.LogDebug(
                        "Server {ServerName}: {Uploaded} uploaded, {Skipped} skipped, {Deleted} deleted",
                        server.Name, result.FilesUploaded, result.FilesSkipped, filesDeleted);
                }
                else
                {
                    logger.LogWarning("Backup sync failed for server {ServerName}: {Message}", server.Name, result.Message);
                }
            }

            logger.LogInformation(
                "Scheduled backup sync completed for {ServerCount} servers: {Uploaded} uploaded, {Skipped} skipped, {Deleted} deleted, {BytesUp:N0} bytes up, {BytesDel:N0} bytes freed",
                servers.Count, totalUploaded, totalSkipped, totalDeleted, totalBytesUploaded, totalBytesDeleted);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Backup sync cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run scheduled backup sync");
        }
    }
}
