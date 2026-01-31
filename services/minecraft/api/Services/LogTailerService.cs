using System.Text;
using System.Threading.Channels;
using Nucleus.Minecraft.Models;

namespace Nucleus.Minecraft.Services;

/// <summary>
/// Provides log tailing functionality for Minecraft servers.
/// Supports reading recent log lines and streaming new log entries.
/// </summary>
public class LogTailerService(ILogger<LogTailerService> logger)
{
    public record LogEntry(string Text, DateTimeOffset Timestamp, LogLevel Level);

    private static string GetLogFilePath(MinecraftServer server) =>
        Path.Combine(server.PersistenceLocation, "logs", "latest.log");

    /// <summary>
    /// Gets recent log lines by reading the tail of the log file.
    /// Used to provide initial context when a client connects.
    /// </summary>
    public async Task<List<LogEntry>> GetRecentLinesAsync(MinecraftServer server, int count = 100)
    {
        string logFilePath = GetLogFilePath(server);

        if (!File.Exists(logFilePath))
            return new List<LogEntry>();

        try
        {
            string[] allLines = await File.ReadAllLinesAsync(logFilePath);
            return allLines
                .TakeLast(count)
                .Select(ParseLogLine)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read recent log lines for server {ServerId}", server.Id);
            return new List<LogEntry>();
        }
    }

    /// <summary>
    /// Creates a channel that streams new log entries for a specific server.
    /// The returned task completes when streaming stops (cancelled or error).
    /// </summary>
    public async Task StreamLogsAsync(MinecraftServer server, Channel<LogEntry> channel, CancellationToken ct)
    {
        string logFilePath = GetLogFilePath(server);

        while (!ct.IsCancellationRequested)
        {
            if (!File.Exists(logFilePath))
            {
                logger.LogWarning("Log file not found for server {ServerId}: {Path}", server.Id, logFilePath);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                continue;
            }

            try
            {
                await TailLogFileAsync(logFilePath, channel, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error tailing log file for server {ServerId}, retrying", server.Id);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        channel.Writer.TryComplete();
    }

    private async Task TailLogFileAsync(string logFilePath, Channel<LogEntry> channel, CancellationToken ct)
    {
        await using FileStream fs = new(
            logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // Start from end of file
        fs.Seek(0, SeekOrigin.End);

        using StreamReader reader = new(fs, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);

            if (line != null)
            {
                LogEntry entry = ParseLogLine(line);
                await channel.Writer.WriteAsync(entry, ct);
            }
            else
            {
                // No new content, wait a bit before checking again
                await Task.Delay(100, ct);
            }
        }
    }

    private static LogEntry ParseLogLine(string line)
    {
        // Minecraft log format: [HH:mm:ss] [Thread/LEVEL]: Message
        // Example: [12:34:56] [Server thread/INFO]: Player joined the game
        LogLevel level = LogLevel.Information;

        if (line.Contains("/WARN]") || line.Contains("/WARNING]"))
            level = LogLevel.Warning;
        else if (line.Contains("/ERROR]") || line.Contains("/SEVERE]") || line.Contains("/FATAL]"))
            level = LogLevel.Error;
        else if (line.Contains("/DEBUG]"))
            level = LogLevel.Debug;

        return new LogEntry(line, DateTimeOffset.UtcNow, level);
    }
}
