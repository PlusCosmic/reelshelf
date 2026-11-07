using System.Net;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.ApiClient.Models;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.Files;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Media;
using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Core.Uploads;

/// <summary>What one go at uploading a file came to.</summary>
public enum UploadOutcome
{
    Shelved,
    Duplicate,
    TooLong,

    /// <summary>The plan has no room (403 at preparation); uploads pause until it has.</summary>
    PlanFull,

    /// <summary>Its book is no longer on the shelf; the file waits for one.</summary>
    NeedsBook,

    /// <summary>Failed this time; it waits for its retry (<see cref="LedgerEntry.NextAttemptAt"/>).</summary>
    RetryLater,

    /// <summary>Out of attempts, or refused for good.</summary>
    Failed,

    /// <summary>The API asked for an upload protocol this version can't do; only a newer app can (T8).</summary>
    Unsupported,

    /// <summary>The token was refused (401); the file waits until the app is signed in again.</summary>
    SignedOut,

    /// <summary>The API refused this version (426); the file waits for an update.</summary>
    UpdateRequired
}

/// <summary>The account's plan as uploading needs it.</summary>
public sealed record PlanLimits(int? MaxClipSeconds, int? ClipLimit, int? NewClipLimit, int ClipCount, int NewClips)
{
    /// <summary>The server lets a clip run a second over the plan's length.</summary>
    public const double LengthToleranceSeconds = 1;

    public static PlanLimits From(AccountPlanResponse plan) => new(plan.MaxClipSeconds, plan.ClipLimit,
        plan.NewClipLimit, plan.ClipCount ?? 0, plan.NewClipsLast30Days ?? 0);

    public bool IsTooLong(double seconds) => MaxClipSeconds is { } max && seconds > max + LengthToleranceSeconds;

    /// <summary>Whether the plan has room for another clip now.</summary>
    public bool HasRoom => (ClipLimit is not { } clips || ClipCount < clips) &&
                           (NewClipLimit is not { } monthly || NewClips < monthly);

    public bool MonthlyLimitReached => NewClipLimit is { } monthly && NewClips >= monthly &&
                                       (ClipLimit is not { } clips || ClipCount < clips);
}

/// <summary>
/// Uploads one file, one step after another: its length against the plan (from the <c>mvhd</c> box, MP4 and MOV only),
/// its MD5, preparing the clip, the multipart upload and completing it. Every answer the API can give is turned into
/// the file's next state in the ledger.
/// </summary>
public sealed class UploadPipeline(
    UploaderApi api,
    UploadLedger ledger,
    MultipartUploader uploader,
    IClock clock)
{
    public const string MultipartProtocol = "s3-multipart";

    /// <summary>Tries per file before it fails until the next start.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The longest wait between tries.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>A completion the API couldn't check just now (503) is asked again after this.</summary>
    public static readonly TimeSpan CheckRetry = TimeSpan.FromMinutes(1);

    /// <summary>The title the API gives a refused clip whose plan is full (<c>PlanLimitExceededException</c>).</summary>
    public const string PlanLimitTitle = "Plan Limit Reached";

    /// <summary>The detail of the last 422 or 403, for the log and S4.</summary>
    public string? LastProblem { get; private set; }

    /// <summary>The wait before the next try after <paramref name="attempts"/> failed ones: 15 s doubling to 5 minutes.</summary>
    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 15 * Math.Pow(2, Math.Max(0, attempts - 1))));

    public async Task<UploadOutcome> UploadAsync(LedgerEntry entry, string fullPath, PlanLimits plan,
        CancellationToken cancellationToken)
    {
        LastProblem = null;

        // An upload under way before a restart carries on from the parts storage has.
        if (entry.State == LedgerState.Uploading && entry.ClipId is not null && entry.UploadId is not null)
        {
            switch (await uploader.ReconcileAsync(entry, cancellationToken))
            {
                case PartsOutcome.UploadGone:
                    await GiveUpUploadAsync(entry, cancellationToken);
                    break;
                case PartsOutcome.Stopped:
                    return Stopped(entry, uploader.StoppedBy);
                case PartsOutcome.Failed:
                    return RetryLater(entry, Backoff(entry.Attempts + 1), countAttempt: true);
            }
        }

        if (entry.ClipId is null)
        {
            UploadOutcome? refused = await PrepareAsync(entry, fullPath, plan, cancellationToken);
            if (refused is { } outcome)
            {
                return outcome;
            }
        }

        switch (await uploader.SendPartsAsync(entry, fullPath, cancellationToken))
        {
            case PartsOutcome.UploadGone:
                await GiveUpUploadAsync(entry, cancellationToken);
                return RetryLater(entry, TimeSpan.Zero, countAttempt: true);
            case PartsOutcome.Stopped:
                return Stopped(entry, uploader.StoppedBy);
            case PartsOutcome.Failed:
                return RetryLater(entry, Backoff(entry.Attempts + 1), countAttempt: true);
        }

        return await CompleteAsync(entry, cancellationToken);
    }

    /// <summary>Checks, hashes and prepares the file; null once it has a clip to upload into.</summary>
    private async Task<UploadOutcome?> PrepareAsync(LedgerEntry entry, string fullPath, PlanLimits plan,
        CancellationToken cancellationToken)
    {
        if (entry.CategoryId is not { } categoryId)
        {
            return Finish(entry, LedgerState.NeedsBook, UploadOutcome.NeedsBook);
        }

        if (VideoFiles.HasReadableLength(fullPath))
        {
            entry.LengthSeconds ??= Mp4Duration.TryRead(fullPath);
            if (entry.LengthSeconds is { } length && plan.IsTooLong(length))
            {
                return Finish(entry, LedgerState.TooLong, UploadOutcome.TooLong);
            }
        }

        try
        {
            entry.Md5 ??= await FileHashes.Md5Async(fullPath, cancellationToken);
        }
        catch (IOException error)
        {
            entry.LastError = error.Message;
            return RetryLater(entry, Backoff(entry.Attempts + 1), countAttempt: true);
        }

        ApiResult<CreateClipResponse> prepared = await api.PrepareAsync(categoryId, entry.Title, entry.Size,
            ClipDates.DeriveCreatedAt(entry.LastWriteUtc, entry.Path), entry.Md5, cancellationToken);
        switch (prepared.Status)
        {
            case HttpStatusCode.Conflict:
                return Finish(entry, LedgerState.Duplicate, UploadOutcome.Duplicate);
            case HttpStatusCode.Forbidden when prepared.Title == PlanLimitTitle:
                LastProblem = prepared.Detail;
                return Finish(entry, LedgerState.PlanFull, UploadOutcome.PlanFull, prepared.Detail);
            case HttpStatusCode.NotFound:
                // The book was taken off the shelf.
                entry.CategoryId = null;
                return Finish(entry, LedgerState.NeedsBook, UploadOutcome.NeedsBook);
            case HttpStatusCode.Unauthorized or HttpStatusCode.UpgradeRequired:
                return Stopped(entry, prepared.Status);
        }

        if (prepared.IsTransient)
        {
            return RetryLater(entry, Backoff(entry.Attempts + 1), countAttempt: true);
        }

        if (!prepared.IsSuccess || prepared.Value?.ClipId is not { } clipId)
        {
            return Finish(entry, LedgerState.Failed, UploadOutcome.Failed, prepared.Detail ?? $"Preparing answered {prepared.Status}");
        }

        UploadTarget? target = prepared.Value.Upload;
        if (target?.Protocol != MultipartProtocol || target.Multipart?.UploadId is not { } uploadId ||
            target.Multipart.PartSize is not { } partSize || target.Multipart.PartCount is not { } partCount)
        {
            // Only a newer app can upload there; give the reservation back.
            await api.DeleteClipAsync(clipId, cancellationToken);
            return Finish(entry, LedgerState.Failed, UploadOutcome.Unsupported,
                $"The API asked for the {target?.Protocol ?? "unknown"} upload protocol");
        }

        entry.ClipId = clipId;
        entry.UploadId = uploadId;
        entry.PartSize = partSize;
        entry.PartCount = partCount;
        entry.Parts = [];
        entry.Progress = 0;
        entry.State = LedgerState.Uploading;
        entry.UpdatedAt = clock.UtcNow;
        ledger.Save(entry);
        return null;
    }

    private async Task<UploadOutcome> CompleteAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        ApiResult<CompletedUpload> completed =
            await api.CompleteAsync(entry.ClipId!.Value, entry.UploadId!, entry.Parts, cancellationToken);
        if (completed.IsSuccess)
        {
            entry.ShelvedAt = clock.UtcNow;
            entry.Progress = 100;
            return Finish(entry, LedgerState.Shelved, UploadOutcome.Shelved);
        }

        switch (completed.Status)
        {
            case HttpStatusCode.UnprocessableEntity:
                // The server's probe refused the file, and the clip with it.
                LastProblem = completed.Detail;
                entry.ForgetUpload();
                return IsAboutLength(completed.Detail)
                    ? Finish(entry, LedgerState.TooLong, UploadOutcome.TooLong, completed.Detail)
                    : Finish(entry, LedgerState.Failed, UploadOutcome.Failed, completed.Detail);
            case HttpStatusCode.ServiceUnavailable:
                // The check couldn't finish; completing again picks up the joined file.
                return RetryLater(entry, CheckRetry, countAttempt: false);
            case HttpStatusCode.NotFound:
                await GiveUpUploadAsync(entry, cancellationToken);
                return RetryLater(entry, TimeSpan.Zero, countAttempt: true);
            case HttpStatusCode.BadRequest:
                // The parts don't match what storage has; list them again on the next try.
                return RetryLater(entry, Backoff(entry.Attempts + 1), countAttempt: true);
            case HttpStatusCode.Unauthorized or HttpStatusCode.UpgradeRequired:
                return Stopped(entry, completed.Status);
        }

        return RetryLater(entry, Backoff(entry.Attempts + 1), countAttempt: true);
    }

    /// <summary>The probe's length refusal (<c>TierLimits.LengthProblem</c>) rather than an unreadable file.</summary>
    public static bool IsAboutLength(string? detail) =>
        detail is not null &&
        (detail.Contains("Your plan keeps clips up to", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("This clip is", StringComparison.OrdinalIgnoreCase));

    /// <summary>The upload can't go on: deletes the clip waiting on it, so the file is prepared afresh.</summary>
    private async Task GiveUpUploadAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        if (entry.ClipId is { } clipId)
        {
            await api.DeleteClipAsync(clipId, cancellationToken);
        }

        entry.ForgetUpload();
        entry.State = LedgerState.Waiting;
        entry.UpdatedAt = clock.UtcNow;
        ledger.Save(entry);
    }

    private UploadOutcome RetryLater(LedgerEntry entry, TimeSpan wait, bool countAttempt)
    {
        if (countAttempt)
        {
            entry.Attempts++;
        }

        if (entry.Attempts >= MaxAttempts)
        {
            return Finish(entry, LedgerState.Failed, UploadOutcome.Failed, entry.LastError);
        }

        entry.NextAttemptAt = clock.UtcNow + wait;
        // A prepared clip keeps uploading from where it is; one not yet prepared waits its turn.
        entry.State = entry.ClipId is null ? LedgerState.Waiting : LedgerState.Uploading;
        entry.UpdatedAt = clock.UtcNow;
        ledger.Save(entry);
        return UploadOutcome.RetryLater;
    }

    /// <summary>Signed out or too old: the file stays as it is until the app can carry on.</summary>
    private UploadOutcome Stopped(LedgerEntry entry, HttpStatusCode? status)
    {
        entry.UpdatedAt = clock.UtcNow;
        ledger.Save(entry);
        return status == HttpStatusCode.UpgradeRequired ? UploadOutcome.UpdateRequired : UploadOutcome.SignedOut;
    }

    private UploadOutcome Finish(LedgerEntry entry, LedgerState state, UploadOutcome outcome, string? error = null)
    {
        entry.State = state;
        entry.LastError = error ?? entry.LastError;
        entry.NextAttemptAt = null;
        entry.UpdatedAt = clock.UtcNow;
        ledger.Save(entry);
        return outcome;
    }
}
