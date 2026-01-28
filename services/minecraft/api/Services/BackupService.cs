using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

public class BackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly IAmazonS3? _s3Client;
    private readonly string? _bucketName;
    private readonly string _bucketPrefixBase;
    private readonly int _localRetentionCount;

    public BackupService(IConfiguration configuration, ILogger<BackupService> logger)
    {
        _logger = logger;
        _bucketPrefixBase = configuration["Backblaze:BucketPrefix"] ?? "minecraft-backups/";
        _localRetentionCount = int.TryParse(configuration["Backblaze:LocalRetentionCount"], out int count) ? count : 3;

        string? keyId = configuration["Backblaze:KeyId"];
        string? appKey = configuration["Backblaze:ApplicationKey"];
        _bucketName = configuration["Backblaze:BucketName"];
        string? endpoint = configuration["Backblaze:Endpoint"];

        if (!string.IsNullOrWhiteSpace(keyId) &&
            !string.IsNullOrWhiteSpace(appKey) &&
            !string.IsNullOrWhiteSpace(_bucketName) &&
            !string.IsNullOrWhiteSpace(endpoint))
        {
            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true
            };

            _s3Client = new AmazonS3Client(keyId, appKey, config);
            _logger.LogInformation("BackupService initialized with B2 bucket: {Bucket}", _bucketName);
        }
        else
        {
            _logger.LogWarning("Backblaze B2 not configured - backup sync will be disabled");
        }
    }

    public bool IsConfigured => _s3Client != null && _bucketName != null;

    private static string GetBackupsPath(MinecraftServer server) =>
        Path.Combine(server.PersistenceLocation, "simplebackups");

    private string GetBucketPrefix(MinecraftServer server) =>
        $"{_bucketPrefixBase}{server.ContainerName}/";

    public async Task<BackupSyncResult> SyncBackupsAsync(MinecraftServer server, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new BackupSyncResult(
                Success: false,
                Message: "Backblaze B2 is not configured",
                FilesUploaded: 0,
                FilesSkipped: 0,
                BytesUploaded: 0
            );
        }

        string backupsPath = GetBackupsPath(server);
        string bucketPrefix = GetBucketPrefix(server);

        if (!Directory.Exists(backupsPath))
        {
            _logger.LogWarning("Backups directory does not exist: {Path}", backupsPath);
            return new BackupSyncResult(
                Success: false,
                Message: $"Backups directory not found: {backupsPath}",
                FilesUploaded: 0,
                FilesSkipped: 0,
                BytesUploaded: 0
            );
        }

        try
        {
            HashSet<string> existingKeys = await GetExistingKeysAsync(bucketPrefix, cancellationToken);
            string[] localFiles = Directory.GetFiles(backupsPath, "*", SearchOption.AllDirectories);

            int uploaded = 0;
            int skipped = 0;
            long bytesUploaded = 0;

            foreach (string localFile in localFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = Path.GetRelativePath(backupsPath, localFile).Replace('\\', '/');
                string key = bucketPrefix + relativePath;

                if (existingKeys.Contains(key))
                {
                    _logger.LogDebug("Skipping existing file: {Key}", key);
                    skipped++;
                    continue;
                }

                FileInfo fileInfo = new(localFile);
                await UploadFileAsync(localFile, key, cancellationToken);

                _logger.LogInformation("Uploaded: {File} ({Size:N0} bytes)", relativePath, fileInfo.Length);
                uploaded++;
                bytesUploaded += fileInfo.Length;
            }

            string message = $"Sync complete: {uploaded} uploaded, {skipped} skipped";
            _logger.LogInformation(message);

            return new BackupSyncResult(
                Success: true,
                Message: message,
                FilesUploaded: uploaded,
                FilesSkipped: skipped,
                BytesUploaded: bytesUploaded
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync backups");
            return new BackupSyncResult(
                Success: false,
                Message: $"Sync failed: {ex.Message}",
                FilesUploaded: 0,
                FilesSkipped: 0,
                BytesUploaded: 0
            );
        }
    }

    private async Task<HashSet<string>> GetExistingKeysAsync(string bucketPrefix, CancellationToken cancellationToken)
    {
        HashSet<string> keys = new();

        string? continuationToken = null;
        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = bucketPrefix,
                ContinuationToken = continuationToken
            };

            ListObjectsV2Response response = await _s3Client!.ListObjectsV2Async(request, cancellationToken);

            foreach (S3Object obj in response.S3Objects ?? [])
            {
                keys.Add(obj.Key);
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken != null);

        _logger.LogDebug("Found {Count} existing files in B2", keys.Count);
        return keys;
    }

    private async Task UploadFileAsync(string localPath, string key, CancellationToken cancellationToken)
    {
        using var transferUtility = new TransferUtility(_s3Client);

        var request = new TransferUtilityUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            FilePath = localPath,
            ContentType = GetContentType(localPath),
            PartSize = 50 * 1024 * 1024 // 50MB parts for multipart upload
        };

        await transferUtility.UploadAsync(request, cancellationToken);
    }

    private static string GetContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".zip" => "application/zip",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".log" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

    public async Task<BackupListResult> GetBackupStatusAsync(MinecraftServer server, CancellationToken cancellationToken = default)
    {
        string backupsPath = GetBackupsPath(server);
        string bucketPrefix = GetBucketPrefix(server);

        List<BackupFileInfo> localFiles = new();
        List<BackupFileInfo> remoteFiles = new();

        if (Directory.Exists(backupsPath))
        {
            foreach (string file in Directory.GetFiles(backupsPath, "*", SearchOption.AllDirectories))
            {
                FileInfo info = new(file);
                string relativePath = Path.GetRelativePath(backupsPath, file).Replace('\\', '/');
                localFiles.Add(new BackupFileInfo(relativePath, info.Length, info.LastWriteTimeUtc));
            }
        }

        if (IsConfigured)
        {
            string? continuationToken = null;
            do
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = bucketPrefix,
                    ContinuationToken = continuationToken
                };

                ListObjectsV2Response response = await _s3Client!.ListObjectsV2Async(request, cancellationToken);

                foreach (S3Object obj in response.S3Objects ?? [])
                {
                    string relativePath = obj.Key.StartsWith(bucketPrefix)
                        ? obj.Key[bucketPrefix.Length..]
                        : obj.Key;
                    remoteFiles.Add(new BackupFileInfo(
                        relativePath,
                        obj.Size ?? 0,
                        obj.LastModified ?? DateTime.MinValue
                    ));
                }

                continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
            } while (continuationToken != null);
        }

        HashSet<string> remoteKeys = remoteFiles.Select(f => f.Path).ToHashSet();
        int pendingCount = localFiles.Count(f => !remoteKeys.Contains(f.Path));

        return new BackupListResult(
            IsConfigured: IsConfigured,
            LocalFiles: localFiles.OrderByDescending(f => f.LastModified).ToList(),
            RemoteFiles: remoteFiles.OrderByDescending(f => f.LastModified).ToList(),
            PendingSyncCount: pendingCount
        );
    }

    public (int FilesDeleted, long BytesDeleted) CleanupOldBackups(MinecraftServer server)
    {
        string backupsPath = GetBackupsPath(server);

        if (!Directory.Exists(backupsPath))
        {
            _logger.LogDebug("Backups directory does not exist, nothing to clean up: {Path}", backupsPath);
            return (0, 0);
        }

        // Get all top-level entries (files and directories) as backup units
        var backupEntries = Directory.GetFileSystemEntries(backupsPath)
            .Select(path =>
            {
                bool isDirectory = Directory.Exists(path);
                DateTime lastModified = isDirectory
                    ? Directory.GetLastWriteTimeUtc(path)
                    : File.GetLastWriteTimeUtc(path);
                long size = isDirectory
                    ? GetDirectorySize(path)
                    : new FileInfo(path).Length;

                return new { Path = path, LastModified = lastModified, Size = size, IsDirectory = isDirectory };
            })
            .OrderByDescending(e => e.LastModified)
            .ToList();

        if (backupEntries.Count <= _localRetentionCount)
        {
            _logger.LogDebug(
                "Local backup count ({Count}) is within retention limit ({Limit}), no cleanup needed",
                backupEntries.Count, _localRetentionCount);
            return (0, 0);
        }

        var entriesToDelete = backupEntries.Skip(_localRetentionCount).ToList();
        int filesDeleted = 0;
        long bytesDeleted = 0;

        foreach (var entry in entriesToDelete)
        {
            try
            {
                if (entry.IsDirectory)
                {
                    Directory.Delete(entry.Path, recursive: true);
                }
                else
                {
                    File.Delete(entry.Path);
                }

                filesDeleted++;
                bytesDeleted += entry.Size;
                _logger.LogInformation(
                    "Deleted old backup: {Path} ({Size:N0} bytes)",
                    Path.GetFileName(entry.Path), entry.Size);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old backup: {Path}", entry.Path);
            }
        }

        _logger.LogInformation(
            "Backup cleanup complete: deleted {Count} backups ({Bytes:N0} bytes freed)",
            filesDeleted, bytesDeleted);

        return (filesDeleted, bytesDeleted);
    }

    private static long GetDirectorySize(string path)
    {
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }
}

public record BackupSyncResult(
    bool Success,
    string Message,
    int FilesUploaded,
    int FilesSkipped,
    long BytesUploaded,
    int FilesDeleted = 0,
    long BytesDeleted = 0
);

public record BackupFileInfo(
    string Path,
    long Size,
    DateTime LastModified
);

public record BackupListResult(
    bool IsConfigured,
    List<BackupFileInfo> LocalFiles,
    List<BackupFileInfo> RemoteFiles,
    int PendingSyncCount
);
