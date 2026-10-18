using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reelshelf.Worker;

/// <summary>
/// A <c>migrate</c> job's input: a Bunny HLS master playlist, copied into a local MP4 with
/// <see cref="EncodeArguments.HlsRemux"/> before the job follows the <c>transcode</c> path. ffmpeg reads the playlist
/// and segments itself, so its log decides what went wrong: a 403, 404 or 410 means the video is gone and input that
/// isn't HLS will never be, so both fail as not retryable; network errors and 5xx are retried like other storage
/// reads, then fail as retryable. ffmpeg skips a segment it can't fetch and still exits cleanly, so a remux whose log
/// shows a skipped segment counts as a failed read rather than a clip with a hole in it. The whole remux, retries
/// included, is bounded by <see cref="WorkerSettings.RemuxTimeout"/>.
/// </summary>
public sealed partial class HlsRemux(FfmpegRunner ffmpeg, IOptions<WorkerSettings> options, ILogger<HlsRemux> logger)
{
    private const int Attempts = 4;
    private const int LoggedLines = 5;

    private readonly WorkerSettings settings = options.Value;

    /// <summary>The delay before retry <paramref name="attempt"/> (1-based), as for <see cref="MediaTransfer"/>; tests shorten it.</summary>
    internal Func<int, TimeSpan> RetryDelay { get; init; } = attempt => TimeSpan.FromSeconds(2 << (attempt - 1));

    /// <summary>How one remux went, from its exit and what it logged.</summary>
    internal enum Outcome
    {
        Complete,

        /// <summary>The playlist or a segment answered 403, 404 or 410: the video isn't there.</summary>
        Gone,

        /// <summary>ffmpeg read the playlist but it isn't HLS (or not media).</summary>
        Unreadable,

        /// <summary>Network errors, 5xx or a skipped segment: worth another try.</summary>
        Transient
    }

    /// <summary>
    /// Remuxes the playlist at <paramref name="playlistUrl"/> into <paramref name="path"/> and returns its size.
    /// Throws <see cref="JobFailedException"/> when it can't; cancelling stops ffmpeg and throws as usual.
    /// </summary>
    public async Task<long> RemuxAsync(string playlistUrl, string path, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.RemuxTimeout);
        try
        {
            return await RemuxWithRetriesAsync(playlistUrl, path, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new JobFailedException(
                $"Remuxing the HLS source took longer than {settings.RemuxTimeout} (Worker:RemuxTimeout)", retryable: true);
        }
    }

    private async Task<long> RemuxWithRetriesAsync(string playlistUrl, string path, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            List<string> lines = [];
            bool exitedCleanly = true;
            try
            {
                await ffmpeg.RunFfmpegAsync(EncodeArguments.HlsRemux(playlistUrl, path), cancellationToken,
                    onStderrLine: line =>
                    {
                        lock (lines)
                        {
                            lines.Add(line);
                        }
                    });
            }
            catch (FfmpegException)
            {
                exitedCleanly = false;
            }

            Outcome outcome;
            string detail;
            lock (lines)
            {
                outcome = Classify(exitedCleanly, lines);
                detail = string.Join('\n', lines.TakeLast(LoggedLines)).Trim();
            }

            switch (outcome)
            {
                case Outcome.Complete:
                    long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (size == 0)
                    {
                        throw new JobFailedException("The HLS source remuxed to an empty file", retryable: false);
                    }

                    if (size > settings.MaxSourceBytes)
                    {
                        throw new JobFailedException(
                            $"The source is larger than the {settings.MaxSourceBytes}-byte limit (Worker:MaxSourceBytes)",
                            retryable: false);
                    }

                    return size;
                case Outcome.Gone:
                    throw new JobFailedException($"The HLS source is gone: {detail}", retryable: false);
                case Outcome.Unreadable:
                    throw new JobFailedException($"The source can't be read as HLS: {detail}", retryable: false);
            }

            if (attempt >= Attempts)
            {
                throw new JobFailedException($"Could not read the HLS source: {detail}", retryable: true);
            }

            TimeSpan delay = RetryDelay(attempt);
            logger.LogWarning("Could not read the HLS source (attempt {Attempt}), retrying in {Delay}: {Error}",
                attempt, delay, detail);
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>What a remux that exited (cleanly or not) after logging <paramref name="lines"/> amounts to.</summary>
    internal static Outcome Classify(bool exitedCleanly, IReadOnlyCollection<string> lines)
    {
        if (lines.Any(line => GoneStatus().IsMatch(line)))
        {
            return Outcome.Gone;
        }

        // ffmpeg logs these and carries on, leaving a gap, so a clean exit after them isn't a complete copy.
        bool incomplete = lines.Any(line => IncompleteRead().IsMatch(line));
        if (incomplete)
        {
            return Outcome.Transient;
        }

        if (exitedCleanly)
        {
            return Outcome.Complete;
        }

        return lines.Any(line => line.Contains("Invalid data found when processing input", StringComparison.Ordinal))
            ? Outcome.Unreadable
            : Outcome.Transient;
    }

    /// <summary>ffmpeg's http protocol logs "HTTP error 404 Not Found"; opening the input fails with "Server returned 404 Not Found".</summary>
    [GeneratedRegex(@"\b(HTTP error|Server returned) (403|404|410)\b")]
    private static partial Regex GoneStatus();

    [GeneratedRegex(@"Failed to open segment|failed too many times|Failed to reload playlist|\bHTTP error \d{3}\b|\bServer returned [45]")]
    private static partial Regex IncompleteRead();
}
