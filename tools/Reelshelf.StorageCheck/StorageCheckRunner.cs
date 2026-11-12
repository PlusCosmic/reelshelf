using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Moderation;
using Reelshelf.Video;
using Reelshelf.Video.R2;

namespace Reelshelf.StorageCheck;

/// <summary>What the runner talks to; <see cref="Real"/> for a run, fakes for tests.</summary>
public sealed class CheckServices
{
    /// <summary>The S3 client for a bucket's options, as <see cref="R2Storage"/>'s public constructor makes it.</summary>
    public required Func<R2Options, AmazonS3Client> S3Client { get; init; }

    /// <summary>Presigned URLs, CORS and the worker's and the export's own HTTP clients.</summary>
    public required HttpMessageHandler StorageHandler { get; init; }

    /// <summary>The media hostname, with the connect-host override when there is one.</summary>
    public required HttpMessageHandler MediaHandler { get; init; }

    public Func<string, ICdnPurger>? Purger { get; init; }

    /// <summary>The API's upload probe, or null with <see cref="ProbeSkipReason"/>.</summary>
    public Reelshelf.Video.R2.IUploadProbe? Probe { get; init; }

    public string ProbeSkipReason { get; init; } = "no ffprobe";

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public static CheckServices Real(CheckSettings settings)
    {
        string mediaHost = new Uri(settings.Store.MediaBaseUrl).Host;
        string? ffprobe = FindOnPath(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return new CheckServices
        {
            S3Client = options => new AmazonS3Client(R2Storage.Credentials(options), R2Storage.ClientConfig(options)),
            StorageHandler = StorageHttp.StorageHandler(),
            MediaHandler = StorageHttp.MediaHandler(mediaHost, settings.MediaConnectHost, settings.MediaConnectPort),
            Purger = settings.DistributionId is { } id ? _ => new CloudFrontCdnPurger(settings.Store, id) : null,
            Probe = ffprobe is null ? null : new FfprobeUploadProbe(),
            ProbeSkipReason = "ffprobe isn't on PATH; the ranged reads above stand in for it"
        };
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(folder => Path.Combine(folder, name))
        .FirstOrDefault(File.Exists);
}

/// <summary>
/// Runs every check in order, then cleans up whatever happened, then prints the summary. The checks run one after
/// another on purpose: later ones read what earlier ones wrote, and the order is the order a clip's life takes.
/// </summary>
public sealed partial class StorageCheckRunner : IDisposable
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitCleanupIncomplete = 3;
    public const int ExitInterrupted = 130;

    private const int Kibibyte = 1024;

    /// <summary>Two parts: one of exactly the part size and a short last one, as the smallest real multipart upload.</summary>
    private static readonly long SourceSize = MultipartUploadParts.MinPartSize + 64 * Kibibyte;

    private readonly CheckSettings _settings;
    private readonly CheckServices _services;
    private readonly CheckReport _report;
    private readonly RunNamespace _run;
    private readonly AmazonS3Client _mediaClient;
    private readonly R2Storage _media;
    private readonly AmazonS3Client? _evidenceClient;
    private readonly R2Storage? _evidence;
    private readonly HttpClient _raw;
    private readonly HttpClient _cdn;
    private readonly string _workDirectory;
    private CancellationToken _cancellation;

    public StorageCheckRunner(CheckSettings settings, CheckServices services, TextWriter output, RunNamespace? run = null)
    {
        _settings = settings;
        _services = services;
        _run = run ?? new RunNamespace();
        _report = new CheckReport(output, [settings.Store.SecretAccessKey, settings.Store.AccessKeyId]);
        _mediaClient = services.S3Client(settings.Store);
        _media = new R2Storage(settings.Store, _mediaClient, NullLogger<R2Storage>.Instance);
        if (settings.EvidenceBucket is { } evidenceBucket)
        {
            R2Options evidenceOptions = settings.Store.WithBucket(evidenceBucket);
            _evidenceClient = services.S3Client(evidenceOptions);
            _evidence = new R2Storage(evidenceOptions, _evidenceClient, NullLogger<R2Storage>.Instance);
        }

        _raw = new HttpClient(services.StorageHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
        _cdn = new HttpClient(services.MediaHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
        _workDirectory = Path.Combine(Path.GetTempPath(), $"reelshelf-storage-check-{_run.ClipId:N}");
    }

    public CheckReport Report => _report;
    public RunNamespace Run => _run;

    /// <summary>The checks, then cleanup (never cancelled by <paramref name="cancellation"/>), then the summary.</summary>
    public async Task<int> RunAsync(CancellationToken cancellation)
    {
        _cancellation = cancellation;
        PrintHeader();

        bool interrupted = false;
        try
        {
            Directory.CreateDirectory(_workDirectory);
            await ChecksAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            interrupted = true;
            _report.Line();
            _report.Line("Interrupted: the remaining checks are skipped; cleaning up.");
        }
        catch (Exception error)
        {
            _report.Add("run", "the run stopped early", Outcome.Fail, CheckReport.Describe(error));
        }
        finally
        {
            try
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        CleanupOutcome cleanup = await CleanupAsync();
        _report.Summary();
        PrintCleanup(cleanup);

        if (!cleanup.Verified)
        {
            return ExitCleanupIncomplete;
        }

        if (interrupted)
        {
            return ExitInterrupted;
        }

        return _report.AnyFailed ? ExitFailed : ExitOk;
    }

    private void PrintHeader()
    {
        _report.Line("Reelshelf storage preflight");
        _report.Line($"Run ids (every key this run writes starts with one of them):");
        foreach (string line in _run.Describe())
        {
            _report.Line($"  {line}");
        }

        R2Options store = _settings.Store;
        _report.Line();
        _report.Line(store.ServiceUrl is { } endpoint
            ? $"Target: bucket {store.Bucket} at {endpoint} (signed for {store.SigningRegion})"
            : $"Target: bucket {store.Bucket} on AWS S3 in {store.Region}");
        _report.Line($"Media: {store.MediaBaseUrl}" + (_settings.MediaConnectHost is { } host
            ? $", fetched from {host}{(_settings.MediaConnectPort is { } port ? $":{port}" : "")}"
            : ""));
        _report.Line($"Evidence bucket: {_settings.EvidenceBucket ?? "(not set; its checks are skipped)"}");
        _report.Line($"CloudFront distribution: {(_settings.DistributionId is null ? "(not set; the purge is skipped)" : "set")}");
        _report.Line($"Site origin: {_settings.SiteOrigin}; an origin CORS must refuse: {_settings.OtherOrigin}");
        _report.Line("Settings read: " + string.Join(", ", _settings.Sources.Select(source => $"{source.Name} ({source.Source})")));
        if (_settings.Ignored.Count > 0)
        {
            _report.Line("Ignored, as the API will once they are removed: " + string.Join(", ", _settings.Ignored));
        }

        _report.Line("Everything else in the environment is ignored.");
    }

    private async Task<CleanupOutcome> CleanupAsync()
    {
        _report.Heading("Cleanup");
        List<RunBucket> buckets =
        [
            new("media", _media, _mediaClient, _settings.Store.Bucket, _run.MediaKeys, _run.MediaPrefixes, _run.OwnsMediaKey)
        ];
        if (_evidence is not null && _evidenceClient is not null)
        {
            buckets.Add(new RunBucket("evidence", _evidence, _evidenceClient, _settings.EvidenceBucket!, [],
                _run.EvidencePrefixes, _run.OwnsEvidenceKey));
        }

        // Its own time limit, not the run's token: an interrupted run still cleans up.
        using CancellationTokenSource limit = new(TimeSpan.FromMinutes(3));
        Task<CleanupOutcome> work = new RunCleanup(buckets).RunAsync(delay => _services.Delay(delay, CancellationToken.None));
        Task finished = await Task.WhenAny(work, Task.Delay(Timeout.InfiniteTimeSpan, limit.Token));
        if (finished != work)
        {
            return new CleanupOutcome(false, [], [], ["cleanup took longer than 3 minutes"], [], 0);
        }

        try
        {
            return await work;
        }
        catch (Exception error)
        {
            return new CleanupOutcome(false, [], [], [CheckReport.Describe(error)], [], 0);
        }
    }

    private void PrintCleanup(CleanupOutcome cleanup)
    {
        _report.Line();
        foreach (string note in cleanup.Unverifiable)
        {
            _report.Line($"Cleanup note: {note}");
        }

        if (cleanup.Verified)
        {
            _report.Line($"Cleanup: verified. Nothing of this run is left in {(_evidence is null ? "the bucket" : "either bucket")} " +
                         $"(listed again and found none; cleanup itself removed {cleanup.Removed} objects or open uploads the checks had left).");
            return;
        }

        _report.Line("CLEANUP INCOMPLETE. Delete these by hand (see README.md):");
        foreach (string left in cleanup.LeftObjects.Concat(cleanup.LeftUploads))
        {
            _report.Line($"  left: {left}");
        }

        foreach (string error in cleanup.Errors)
        {
            _report.Line($"  error: {error}");
        }

        if (cleanup.LeftObjects.Count == 0 && cleanup.LeftUploads.Count == 0)
        {
            _report.Line("  Nothing could be listed, so check every key of this run:");
            foreach (string line in _run.Describe())
            {
                _report.Line($"  {line}");
            }
        }
    }

    // The checks' helpers.

    private async Task<bool> Check(string id, string title, Func<Task<string>> body)
    {
        _cancellation.ThrowIfCancellationRequested();
        try
        {
            string detail = await body();
            _report.Add(id, title, Outcome.Pass, detail);
            return true;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            _report.Add(id, title, Outcome.Skip, "interrupted");
            throw;
        }
        catch (Exception error)
        {
            _report.Add(id, title, Outcome.Fail, CheckReport.Describe(error));
            return false;
        }
    }

    private void Skip(string id, string title, string reason) => _report.Add(id, title, Outcome.Skip, reason);

    private void Note(string id, string title, string detail) => _report.Add(id, title, Outcome.Note, detail);

    private static void Expect(bool condition, string failure)
    {
        if (!condition)
        {
            throw new CheckFailedException(failure);
        }
    }

    private static byte[] RandomBytes(long length) => RandomNumberGenerator.GetBytes(checked((int)length));

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];

    private static string Mib(long bytes) =>
        bytes < 1024 * Kibibyte ? $"{bytes / 1024.0:0.#} KiB" : $"{bytes / 1024.0 / 1024.0:0.##} MiB";

    /// <summary>Sends a request and reads the whole body, which is never more than a few MiB of the run's own.</summary>
    private async Task<Fetched> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                _cancellation);
            byte[] body = await response.Content.ReadAsByteArrayAsync(_cancellation);
            return new Fetched(response.StatusCode, body, Headers(response));
        }
    }

    /// <summary>A GET, optionally ranged and with an <c>Origin</c>.</summary>
    private Task<Fetched> GetAsync(HttpClient client, string url, string? range = null, string? origin = null)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        if (range is not null)
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return SendAsync(client, request);
    }

    private static Dictionary<string, string> Headers(HttpResponseMessage response)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in response.Headers.NonValidated.Select(pair => pair.Key)
                     .Concat(response.Content.Headers.NonValidated.Select(pair => pair.Key)))
        {
            headers[name] = StorageHttp.Header(response, name) ?? "";
        }

        return headers;
    }

    /// <summary>A response the check has read whole.</summary>
    private sealed record Fetched(HttpStatusCode Status, byte[] Body, Dictionary<string, string> Headers)
    {
        public string? this[string name] => Headers.GetValueOrDefault(name);

        /// <summary>The S3 error code of an XML error body, or null.</summary>
        public string? ErrorCode
        {
            get
            {
                if (Body.Length == 0 || Body.Length > 64 * Kibibyte)
                {
                    return null;
                }

                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                    System.Text.Encoding.UTF8.GetString(Body), "<Code>([^<]{1,100})</Code>");
                return match.Success ? match.Groups[1].Value : null;
            }
        }

        public string Describe() => CheckReport.Status(Status, ErrorCode);
    }

    public void Dispose()
    {
        _raw.Dispose();
        _cdn.Dispose();
        _media.Dispose();
        _evidence?.Dispose();
    }
}
