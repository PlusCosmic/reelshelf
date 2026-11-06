using System.Net;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.ApiClient.Models;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Core.Uploads;

/// <summary>When uploads may go, and how fast: the full-screen game rule, a pause, and the speed limit.</summary>
public interface IUploadGate
{
    /// <summary>Returns once uploading may carry on (straight away unless a game or a pause holds it).</summary>
    Task WaitUntilOpenAsync(CancellationToken cancellationToken);

    /// <summary>The speed for the next part, in bytes a second; null for no limit.</summary>
    long? BytesPerSecond { get; }
}

/// <summary>A gate that is always open, at full speed.</summary>
public sealed class OpenGate : IUploadGate
{
    public static readonly OpenGate Instance = new();

    public Task WaitUntilOpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public long? BytesPerSecond => null;
}

/// <summary>How sending a clip's parts went.</summary>
public enum PartsOutcome
{
    /// <summary>Every part is in storage, with its ETag in the ledger.</summary>
    Uploaded,

    /// <summary>The upload is gone (expired, or its clip was deleted): start again.</summary>
    UploadGone,

    /// <summary>The network or a server failed after the part's retries: this attempt failed.</summary>
    Failed,

    /// <summary>The API refused the token or this version of the app; stop until that is dealt with.</summary>
    Stopped
}

/// <summary>
/// Sends a prepared clip's parts with the protocol <c>frontend/src/utils/s3MultipartUpload.ts</c> uses, one part at a
/// time: part URLs are asked for 10 at a time, each part is PUT to its presigned URL (never with the device token),
/// retried after 1, 2, 4 and 8 seconds before the attempt fails, and its ETag is saved in the ledger as soon as it is
/// in, so a restart resumes from there.
/// </summary>
public sealed class MultipartUploader(
    UploaderApi api,
    HttpClient storage,
    UploadLedger ledger,
    IClock clock,
    TokenBucket bucket,
    IUploadGate gate)
{
    public const int UrlBatch = 10;

    public static readonly IReadOnlyList<TimeSpan> PartRetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    /// <summary>A part URL this close to expiring is asked for again rather than risked.</summary>
    private static readonly TimeSpan UrlMargin = TimeSpan.FromMinutes(5);

    private readonly Dictionary<int, (string Url, DateTimeOffset ExpiresAt)> _urls = [];

    /// <summary>The status (401 or 426) that made the last call answer <see cref="PartsOutcome.Stopped"/>.</summary>
    public HttpStatusCode? StoppedBy { get; private set; }

    /// <summary>Raised after each part with the bytes sent so far and the file's size.</summary>
    public event Action<LedgerEntry, long, long>? Progress;

    /// <summary>
    /// Takes the parts storage already holds as sent, for an upload that was under way before a restart: the API's list
    /// is the truth, and a part of the wrong size is sent again.
    /// </summary>
    public async Task<PartsOutcome> ReconcileAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        ApiResult<UploadedParts> listed = await api.ListPartsAsync(entry.ClipId!.Value, entry.UploadId!, cancellationToken);
        if (listed.Status is HttpStatusCode.NotFound)
        {
            return PartsOutcome.UploadGone;
        }

        if (listed.Status is HttpStatusCode.Unauthorized or HttpStatusCode.UpgradeRequired)
        {
            StoppedBy = listed.Status;
            return PartsOutcome.Stopped;
        }

        if (!listed.IsSuccess)
        {
            return PartsOutcome.Failed;
        }

        Dictionary<int, string> held = [];
        foreach (UploadedPart part in listed.Value?.Parts ?? [])
        {
            if (part.PartNumber is not { } number || number < 1 || number > entry.PartCount ||
                string.IsNullOrEmpty(part.Etag))
            {
                continue;
            }

            (long start, long end) = PartRange(entry, number);
            if (part.Size == end - start)
            {
                held[number] = part.Etag;
            }
        }

        entry.Parts = held;
        entry.Progress = Percent(entry);
        entry.UpdatedAt = clock.UtcNow;
        ledger.Save(entry);
        return PartsOutcome.Uploaded;
    }

    public async Task<PartsOutcome> SendPartsAsync(LedgerEntry entry, string fullPath, CancellationToken cancellationToken)
    {
        _urls.Clear();
        int partCount = entry.PartCount ?? 0;
        List<int> pending = Enumerable.Range(1, partCount).Where(number => !entry.Parts.ContainsKey(number)).ToList();
        Progress?.Invoke(entry, SentBytes(entry), entry.Size);

        for (int index = 0; index < pending.Count; index++)
        {
            // A clip already uploading finishes its part, then waits for the game like the rest.
            await gate.WaitUntilOpenAsync(cancellationToken);
            bucket.SetRate(gate.BytesPerSecond);

            int number = pending[index];
            PartsOutcome? outcome = await SendPartAsync(entry, fullPath, number, pending.Skip(index), cancellationToken);
            if (outcome is { } stopped)
            {
                return stopped;
            }

            entry.Progress = Percent(entry);
            entry.UpdatedAt = clock.UtcNow;
            ledger.Save(entry);
            Progress?.Invoke(entry, SentBytes(entry), entry.Size);
        }

        return PartsOutcome.Uploaded;
    }

    /// <summary>Sends one part, retrying it; null once it is in, else why the attempt stops.</summary>
    private async Task<PartsOutcome?> SendPartAsync(LedgerEntry entry, string fullPath, int number,
        IEnumerable<int> upcoming, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            (string? url, PartsOutcome? refused) = await PartUrlAsync(entry, number, upcoming, cancellationToken);
            if (refused is { } urlOutcome && urlOutcome != PartsOutcome.Failed)
            {
                return urlOutcome;
            }

            HttpStatusCode? status = null;
            string? etag = null;
            if (url is not null)
            {
                (long start, long end) = PartRange(entry, number);
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Put, url);
                    request.Content = new StreamContent(
                        new ThrottledStream(new FileSliceStream(fullPath, start, end - start), bucket));
                    request.Content.Headers.ContentLength = end - start;
                    using HttpResponseMessage response =
                        await storage.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    status = response.StatusCode;
                    etag = response.Headers.ETag?.Tag ??
                           (response.Headers.TryGetValues("ETag", out IEnumerable<string>? values)
                               ? values.FirstOrDefault()
                               : null);
                }
                catch (HttpRequestException)
                {
                    status = null;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    status = null;
                }
                catch (IOException)
                {
                    // The file went away or can't be read now; another try may find it.
                    status = null;
                }

                if (status is { } ok && (int)ok is >= 200 and < 300 && !string.IsNullOrEmpty(etag))
                {
                    entry.Parts[number] = etag;
                    return null;
                }

                // The upload itself is gone or the request is wrong; another try won't change that.
                if (status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                {
                    return PartsOutcome.UploadGone;
                }

                // An expired or refused URL is asked for afresh on the next try.
                if (status is HttpStatusCode.Forbidden)
                {
                    _urls.Remove(number);
                }
            }

            if (attempt >= PartRetryDelays.Count)
            {
                return PartsOutcome.Failed;
            }

            await clock.Delay(PartRetryDelays[attempt], cancellationToken);
        }
    }

    /// <summary>The part's URL, asking for it with the next few parts' when there is none still fresh.</summary>
    private async Task<(string? Url, PartsOutcome? Refused)> PartUrlAsync(LedgerEntry entry, int number,
        IEnumerable<int> upcoming, CancellationToken cancellationToken)
    {
        if (Fresh(number) is { } cached)
        {
            return (cached, null);
        }

        List<int> batch = upcoming.Where(part => part == number || Fresh(part) is null).Take(UrlBatch).ToList();
        if (!batch.Contains(number))
        {
            batch.Insert(0, number);
        }

        ApiResult<PresignedUploadParts> presigned =
            await api.PresignPartsAsync(entry.ClipId!.Value, entry.UploadId!, batch, cancellationToken);
        if (presigned.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return (null, PartsOutcome.UploadGone);
        }

        if (presigned.Status is HttpStatusCode.Unauthorized or HttpStatusCode.UpgradeRequired)
        {
            StoppedBy = presigned.Status;
            return (null, PartsOutcome.Stopped);
        }

        if (!presigned.IsSuccess || presigned.Value is null)
        {
            return (null, PartsOutcome.Failed);
        }

        DateTimeOffset expiresAt = presigned.Value.ExpiresAt ?? clock.UtcNow.AddMinutes(30);
        foreach (PresignedUploadPart part in presigned.Value.Parts ?? [])
        {
            if (part.PartNumber is { } partNumber && !string.IsNullOrEmpty(part.Url))
            {
                _urls[partNumber] = (part.Url, expiresAt);
            }
        }

        return (Fresh(number), Fresh(number) is null ? PartsOutcome.Failed : null);
    }

    private string? Fresh(int number) =>
        _urls.TryGetValue(number, out (string Url, DateTimeOffset ExpiresAt) cached) &&
        cached.ExpiresAt - clock.UtcNow > UrlMargin
            ? cached.Url
            : null;

    public static (long Start, long End) PartRange(LedgerEntry entry, int number)
    {
        long partSize = entry.PartSize ?? entry.Size;
        long start = (number - 1) * partSize;
        return (start, Math.Min(start + partSize, entry.Size));
    }

    private static long SentBytes(LedgerEntry entry) =>
        entry.Parts.Keys.Sum(number =>
        {
            (long start, long end) = PartRange(entry, number);
            return end - start;
        });

    private static int Percent(LedgerEntry entry) =>
        entry.Size <= 0 ? 0 : (int)Math.Min(100, SentBytes(entry) * 100 / entry.Size);
}
