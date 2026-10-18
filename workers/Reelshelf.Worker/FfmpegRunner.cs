using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace Reelshelf.Worker;

/// <summary>An ffmpeg or ffprobe run that exited with an error.</summary>
public sealed class FfmpegException(string message) : Exception(message);

/// <summary>What the installed ffmpeg can do, checked once at startup.</summary>
public sealed record FfmpegCapabilities(bool SvtAv1, bool Opus, bool ZScale, bool Tonemap)
{
    /// <summary>HDR sources can be tone-mapped; without these filters they are refused as unsupported input.</summary>
    public bool CanToneMap => ZScale && Tonemap;
}

/// <summary>
/// Runs ffmpeg and ffprobe as child processes, under <c>nice</c> when <see cref="WorkerSettings.Niceness"/> is set,
/// with the arguments passed as a list (no shell). Cancelling kills the process.
/// </summary>
public sealed class FfmpegRunner(IOptions<WorkerSettings> options)
{
    private const int StderrLines = 30;

    private readonly WorkerSettings settings = options.Value;

    /// <summary>The command that runs <paramref name="tool"/>: the tool itself, or <c>nice -n N tool</c>.</summary>
    public static (string FileName, IReadOnlyList<string> Arguments) Command(
        string tool,
        IReadOnlyList<string> arguments,
        int niceness)
    {
        if (niceness <= 0)
        {
            return (tool, arguments);
        }

        return ("nice", ["-n", niceness.ToString(CultureInfo.InvariantCulture), tool, .. arguments]);
    }

    /// <summary>
    /// Runs ffmpeg. With <paramref name="onProgress"/> and an expected output length, the arguments must include
    /// <c>-progress pipe:1</c>, and it is called with the encoded share (0 to 1) as ffmpeg reports it.
    /// </summary>
    public async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        double expectedSeconds = 0,
        Action<double>? onProgress = null)
    {
        Action<string>? onLine = null;
        if (onProgress is not null && expectedSeconds > 0)
        {
            onLine = line =>
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out long micros) && micros >= 0)
                {
                    onProgress(Math.Clamp(micros / 1_000_000.0 / expectedSeconds, 0, 1));
                }
            };
        }

        await RunAsync(settings.FfmpegPath, arguments, onLine, cancellationToken);
    }

    /// <summary>ffprobe's description of <paramref name="path"/>. Throws <see cref="FfmpegException"/> when it can't read it.</summary>
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        await RunAsync(settings.FfprobePath, EncodeArguments.Probe(path), line => output.AppendLine(line), cancellationToken);
        try
        {
            return MediaInfo.Parse(output.ToString());
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new FfmpegException($"ffprobe wrote unreadable output: {exception.Message}");
        }
    }

    /// <summary>Which of the encoders and filters the worker uses this ffmpeg has.</summary>
    public async Task<FfmpegCapabilities> CapabilitiesAsync(CancellationToken cancellationToken)
    {
        HashSet<string> encoders = await ListNamesAsync("-encoders", cancellationToken);
        HashSet<string> filters = await ListNamesAsync("-filters", cancellationToken);
        return new FfmpegCapabilities(encoders.Contains("libsvtav1"), encoders.Contains("libopus"),
            filters.Contains("zscale"), filters.Contains("tonemap"));
    }

    private async Task<HashSet<string>> ListNamesAsync(string list, CancellationToken cancellationToken)
    {
        // Lines look like " V....D libsvtav1   SVT-AV1 ..." (encoders) or " ... zscale  V->V  ..." (filters).
        HashSet<string> names = new(StringComparer.Ordinal);
        await RunAsync(settings.FfmpegPath, ["-hide_banner", list], line =>
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                names.Add(parts[1]);
            }
        }, cancellationToken, nice: false);
        return names;
    }

    private async Task RunAsync(
        string tool,
        IReadOnlyList<string> arguments,
        Action<string>? onStdoutLine,
        CancellationToken cancellationToken,
        bool nice = true)
    {
        (string fileName, IReadOnlyList<string> args) = Command(tool, arguments, nice ? settings.Niceness : 0);
        ProcessStartInfo startInfo = new(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // SVT-AV1 logs its configuration to stderr at info level; keep stderr for errors.
        startInfo.Environment["SVT_LOG"] = "1";

        using Process process = new() { StartInfo = startInfo };
        Queue<string> stderr = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stderr)
            {
                stderr.Enqueue(e.Data);
                while (stderr.Count > StderrLines)
                {
                    stderr.Dequeue();
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new FfmpegException($"Could not start {fileName}: {exception.Message}");
        }

        process.BeginErrorReadLine();
        await using CancellationTokenRegistration kill = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        });

        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            onStdoutLine?.Invoke(line);
        }

        await process.WaitForExitAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string tail;
            lock (stderr)
            {
                tail = string.Join('\n', stderr).Trim();
            }

            throw new FfmpegException(
                $"{Path.GetFileName(tool)} {DescribeExit(process.ExitCode)}{(tail.Length > 0 ? ": " + tail : "")}");
        }
    }

    /// <summary>
    /// How a tool ended. A process killed by a signal reports 128 plus the signal's number, and a crash leaves
    /// nothing on stderr, so naming the signal is what tells a segfault in ffmpeg or an encoder library apart from
    /// an encode error.
    /// </summary>
    internal static string DescribeExit(int exitCode) =>
        (exitCode - 128) switch
        {
            4 => $"crashed with SIGILL (exit code {exitCode}): an instruction this CPU doesn't support",
            6 => $"crashed with SIGABRT (exit code {exitCode})",
            7 => $"crashed with SIGBUS (exit code {exitCode})",
            9 => $"was killed with SIGKILL (exit code {exitCode}), usually for running out of memory",
            11 => $"crashed with SIGSEGV (exit code {exitCode}), a bug in ffmpeg or an encoder library",
            _ => $"exited with code {exitCode}"
        };
}
