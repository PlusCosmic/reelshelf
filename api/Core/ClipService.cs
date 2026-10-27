using Npgsql;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Bunny.Models;
using Reelshelf.Core.Models;
using Reelshelf.Users;
using Reelshelf.Exceptions;
using Reelshelf.Games;
using Reelshelf.Plans;
using Reelshelf.Video;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Core;

public class ClipService(
    ClipVideoHosts videoHosts,
    NewUploadVideoHostPolicy newUploadHostPolicy,
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements,
    ClipProjection clipProjection,
    AccountPlanService planService,
    ClipLimitWarningService clipLimitWarningService,
    TimeProvider timeProvider,
    ClipMediaUrls mediaUrls,
    ITranscodeStorage transcodeStorage,
    ILogger<ClipService> logger)
{
    private const int ShareTokenByteLength = 32;
    private const int MaxShareTokenAttempts = 3;

    private static string NormalizeTag(string tag)
    {
        return tag.Trim().ToLowerInvariant();
    }

    private static string GenerateShareToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(ShareTokenByteLength);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Reserves a place in the owner's plan and creates the hosted video a clip's bytes will be uploaded into. Returns
    /// null when the owner already has this video (same MD5 for the game, or the same <paramref name="source"/> clip).
    /// <paramref name="durationSeconds"/> is checked against the plan's length cap when it is known up front (a
    /// Twitch import); a browser upload's length is checked when the upload completes.
    /// </summary>
    public async Task<CreateClipResponse?> CreateClip(Guid gameCategoryId, string videoTitle,
        Guid userId, DateTimeOffset createdAt, long fileSize, string? md5Hash = null, ClipSource? source = null,
        double? durationSeconds = null)
    {
        if (fileSize <= 0 || fileSize > AccountPlanService.MaxDeclaredFileSizeBytes)
        {
            throw new BadRequestException("File size must be between 1 byte and 1 TiB");
        }

        if (source is not null)
        {
            HashSet<string> imported = await clipsStatements.GetImportedSourceClipIds(userId, source.Provider, [source.ClipId]);
            if (imported.Count > 0)
            {
                return null; // Already imported
            }
        }


        // The host files the video by category (Bunny names its collection after the slug)
        GameCategory? gameCategory = await gameCategoryStatements.GetByIdAsync(gameCategoryId);
        if (gameCategory == null)
        {
            throw new BadRequestException("Invalid category");
        }

        // Check if a video with this MD5 hash already exists for this user and category
        if (!string.IsNullOrWhiteSpace(md5Hash))
        {
            bool exists = await clipsStatements.ClipExistsByMd5Hash(userId, gameCategoryId, md5Hash);
            if (exists)
            {
                return null; // Duplicate detected
            }
        }

        // Existing clips stay on the host they were created on; only new ones follow the policy.
        string videoHostName = newUploadHostPolicy.NewUploads;
        IClipVideoHost videoHost = videoHosts.For(videoHostName);

        // Cheap unlocked check first so a plainly over-limit request fails before taking the lock.
        await planService.EnsureCanCreateClip(userId, fileSize, durationSeconds);

        // Reserve quota before anything is created at the video host. The clip row is inserted with a placeholder
        // video id under a per-owner lock so concurrent requests cannot all pass the check on the same stale
        // usage figure, and so a request that will be rejected never spends a host operation, container
        // creation included. No network calls happen inside the lock, and disposing the lock returns the
        // pooled connection before the host calls below.
        Guid placeholderVideoId = Guid.NewGuid();
        ClipsStatements.ClipRow reserved;
        await using (ClipsStatements.OwnerStorageLock reservation = await clipsStatements.BeginOwnerStorageLock(userId))
        {
            await planService.EnsureCanCreateClip(userId, fileSize, durationSeconds);

            try
            {
                reserved = await clipsStatements.InsertClip(
                    userId,
                    videoHostName,
                    placeholderVideoId,
                    gameCategoryId,
                    md5Hash,
                    createdAt,
                    videoTitle,
                    fileSize: fileSize,
                    source: source);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Two imports of the same source clip raced past the check above; the first one wins.
                return null;
            }

            await clipsStatements.RecordClipCreation(userId, reserved.Id,
                forgetBefore: timeProvider.GetUtcNow() - TierLimits.NewClipWindow);
            await reservation.CommitAsync();
        }

        HostedVideo video;
        try
        {
            video = await videoHost.CreateVideoAsync(reserved.Id, userId, gameCategory, videoTitle);
        }
        catch
        {
            // Nothing billable exists for this clip yet: give the reservation back.
            await ReleaseReservationAsync(reserved.Id);
            throw;
        }

        ClipsStatements.ClipRow clip;
        try
        {
            clip = await clipsStatements.AttachVideo(reserved.Id, video);
        }
        catch
        {
            await DiscardUnattachedVideoAsync(reserved.Id, videoHost, video.VideoId);
            throw;
        }

        UploadTarget upload;
        try
        {
            upload = await videoHost.CreateUploadTargetAsync(video, videoTitle, fileSize);
        }
        catch
        {
            // Nothing can reach a video with no upload target (R2 starts its multipart upload here): discard it the
            // same way.
            await DiscardUnattachedVideoAsync(reserved.Id, videoHost, video.VideoId);
            throw;
        }

        // The clip now counts toward the plan; warn the owner once if this took them near its limit.
        await clipLimitWarningService.Evaluate(userId, await planService.GetPlan(userId));

        return new CreateClipResponse(clip.Id, video.VideoId, upload);
    }

    /// <summary>
    /// Server-side upload of a clip's bytes into the video <see cref="CreateClip"/> prepared for it, at the host
    /// the clip was created on.
    /// </summary>
    public async Task UploadVideo(Guid clipId, Stream content, long? contentLength, CancellationToken cancellationToken)
    {
        ClipsStatements.ClipRow clip = await clipsStatements.GetClipById(clipId)
                                       ?? throw new NotFoundException("Clip", clipId);
        await videoHosts.For(clip.VideoHost).UploadAsync(clip.VideoId, content, contentLength, cancellationToken);
    }

    /// <summary>
    /// A video was created at the host but could not be attached to its reserved row. Delete the video and the
    /// reservation; if the host's delete fails, keep the row and record the real video id on it so the
    /// abandoned-upload purge can finish the job rather than leaving a billable orphan.
    /// </summary>
    private async Task DiscardUnattachedVideoAsync(Guid clipId, IClipVideoHost videoHost, Guid videoId)
    {
        try
        {
            await videoHost.DeleteVideoAsync(videoId);
        }
        catch (Exception deleteError)
        {
            logger.LogWarning(deleteError, "Failed to delete video {VideoId} after attaching it to clip {ClipId} failed; keeping the row for the purge", videoId, clipId);
            try
            {
                await clipsStatements.SetClipVideoId(clipId, videoId);
            }
            catch (Exception recordError)
            {
                logger.LogError(recordError, "Could not record video {VideoId} on clip {ClipId}; the video may be orphaned", videoId, clipId);
            }

            return;
        }

        await ReleaseReservationAsync(clipId);
    }

    private async Task ReleaseReservationAsync(Guid clipId)
    {
        try
        {
            // Nothing was created at the host, so the attempt doesn't count toward the plan's new clips either.
            await clipsStatements.ForgetClipCreation(clipId);
            await clipsStatements.DeleteClip(clipId);
        }
        catch (Exception cleanupError)
        {
            // The abandoned-upload purge removes it after a day if this fails.
            logger.LogWarning(cleanupError, "Failed to release storage reservation for clip {ClipId}", clipId);
        }
    }

    public async Task<Clip?> GetClipById(Guid clipId, Guid userId)
    {
        ClipsStatements.ClipWithTagsRow? clipWithTags = await clipsStatements.GetClipWithTagsById(clipId);
        if (clipWithTags == null)
        {
            return null;
        }

        // Sign-up is open: only the owner, playlist members, or a clip with an active share may be read by id.
        if (clipWithTags.OwnerId != userId && !await clipsStatements.UserCanAccessClip(clipId, userId))
        {
            return null;
        }

        // Get the game category for the slug
        GameCategory? gameCategory = await gameCategoryStatements.GetByIdAsync(clipWithTags.GameCategoryId);
        if (gameCategory == null)
        {
            return null;
        }

        bool isViewed = await clipsStatements.IsClipViewed(userId, clipId);
        ClipsStatements.ClipShareRow? share = await clipsStatements.GetActiveShareByClipId(clipId);

        return clipProjection.ProjectClip(clipWithTags, gameCategory, isViewed, share != null);
    }

    public async Task<Clip?> AddTagToClip(Guid clipId, Guid userId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new BadRequestException("Tag cannot be empty");
        }

        tag = NormalizeTag(tag);
        if (tag.Length > 32)
        {
            tag = tag[..32];
        }


        ClipsStatements.ClipWithTagsRow? clipWithTags =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, userId);
        if (clipWithTags == null)
        {
            return null;
        }

        List<string> existingTags = !string.IsNullOrEmpty(clipWithTags.TagNames)
            ? clipWithTags.TagNames.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];

        if (existingTags.Contains(tag))
        {
            return await GetClipById(clipId, userId); // already tagged
        }

        if (existingTags.Count >= 5)
        {
            throw new BadRequestException("A clip can have a maximum of 5 tags");
        }

        ClipsStatements.TagRow? tagEntity = await clipsStatements.GetTagByName(tag);
        if (tagEntity == null)
        {
            tagEntity = await clipsStatements.InsertTag(tag);
        }

        await clipsStatements.InsertClipTag(clipId, tagEntity.Id);

        return await GetClipById(clipId, userId);
    }

    public async Task<Clip?> RemoveTagFromClip(Guid clipId, Guid userId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new BadRequestException("Tag cannot be empty");
        }

        tag = NormalizeTag(tag);


        ClipsStatements.ClipWithTagsRow? clipWithTags =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, userId);
        if (clipWithTags == null)
        {
            return null;
        }

        ClipsStatements.TagRow? tagEntity = await clipsStatements.GetTagByNameForClip(clipId, tag);
        if (tagEntity != null)
        {
            await clipsStatements.DeleteClipTag(clipId, tagEntity.Id);
        }

        return await GetClipById(clipId, userId);
    }

    public async Task<Clip?> UpdateClipTitle(Guid clipId, Guid userId, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new BadRequestException("Title cannot be empty");
        }

        if (newTitle.Length > 200)
        {
            newTitle = newTitle[..200];
        }


        ClipsStatements.ClipWithTagsRow? clip =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, userId);
        if (clip == null)
        {
            return null;
        }

        await clipsStatements.UpdateClipTitle(clipId, newTitle);
        return await GetClipById(clipId, userId);
    }

    public async Task<List<TopTag>> GetTopTags(Guid userId, Guid? gameCategoryId = null)
    {
        List<ClipsStatements.TopTagRow> topTagRows =
            await clipsStatements.GetTagsOrderedByUsageForOwner(userId, gameCategoryId);
        return topTagRows.Select(t => new TopTag(t.Name, t.Count)).ToList();
    }

    public async Task<List<LegendCount>> GetLegendCounts(Guid userId)
    {
        List<ClipsStatements.LegendCountRow> rows = await clipsStatements.GetLegendCountsForOwner(userId);
        return rows
            .Where(row => ApexLegendNames.Canonicalize(row.Legend) == row.Legend)
            .Select(row => new LegendCount(row.Legend, row.Count))
            .ToList();
    }

    public async Task<bool> MarkClipAsViewed(Guid clipId, Guid userId)
    {
        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
        if (clip == null || (clip.OwnerId != userId && !await clipsStatements.UserCanAccessClip(clipId, userId)))
        {
            return false;
        }

        bool alreadyViewed = await clipsStatements.IsClipViewed(userId, clipId);
        if (alreadyViewed)
        {
            return true;
        }

        await clipsStatements.InsertClipView(userId, clipId);
        return true;
    }

    /// <summary>
    /// A page of the caller's clips. A null <paramref name="gameCategoryId"/> spans the whole archive, so
    /// search and filters run in the database over every clip rather than over whatever the client holds.
    /// <paramref name="search"/> matches a clip's title, one of its tags, or its category name.
    /// <paramref name="legend"/>, spelled as in <see cref="ApexLegendNames"/>, keeps the Apex clips played as it.
    /// </summary>
    public async Task<PagedClipsResponse> GetClips(
        Guid userId, Guid? gameCategoryId, int page, int pageSize, List<string>? tags = null,
        string? search = null, bool unviewedOnly = false,
        ClipSortOrder sortOrder = ClipSortOrder.DateDescending,
        DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, string? legend = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Normalize tags for filtering (same as when adding tags)
        List<string>? normalizedTags = tags?.Select(NormalizeTag).ToList();

        ClipsStatements.PagedClipWithTagsRows clipsPage =
            await clipsStatements.GetClipsWithTags(
                userId,
                gameCategoryId,
                normalizedTags,
                search,
                startDate,
                endDate,
                unviewedOnly,
                sortOrder,
                userId,
                pageSize,
                (page - 1) * pageSize,
                legend);

        List<ClipsStatements.ClipWithTagsRow> pagedClips = clipsPage.Rows;
        List<Guid> clipIds = pagedClips.Select(c => c.Id).ToList();
        HashSet<Guid> viewedClipIds = await clipsStatements.GetViewedClipIds(userId, clipIds);
        HashSet<Guid> sharedClipIds = await clipsStatements.GetSharedClipIds(clipIds);

        List<Guid> categoryIds = pagedClips.Select(c => c.GameCategoryId).Distinct().ToList();
        Dictionary<Guid, GameCategory> categoriesById =
            (await gameCategoryStatements.GetByIdsAsync(categoryIds)).ToDictionary(category => category.Id);

        List<Clip> finalClips = clipProjection.ProjectClips(
            pagedClips,
            categoriesById,
            viewedClipIds,
            sharedClipIds);

        int totalPages = (int)Math.Ceiling((double)clipsPage.TotalCount / pageSize);
        return new PagedClipsResponse(finalClips, clipsPage.TotalCount, totalPages);
    }

    public async Task<bool> DeleteClip(Guid clipId, Guid userId)
    {
        ClipsStatements.ClipWithTagsRow? clip =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, userId);
        if (clip == null)
        {
            return false;
        }

        await ClipVideoDeletion.DeleteAsync(videoHosts, transcodeStorage, clipId, clip.VideoHost, clip.VideoId,
            clip.BunnyVideoId);
        await clipsStatements.DeleteClip(clipId);

        // The count went down; clear the "nearly full" marker if the owner is back under the threshold.
        await clipLimitWarningService.Evaluate(userId, await planService.GetPlan(userId));
        return true;
    }

    public async Task<ClipShareResponse?> CreateOrGetShare(Guid clipId, Guid userId)
    {
        ClipsStatements.ClipWithTagsRow? clip =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, userId);
        if (clip == null)
        {
            return null;
        }

        if (!clip.VideoStatus.IsPlayable())
        {
            throw new ConflictException("This clip is still processing and cannot be shared yet");
        }

        ClipsStatements.ClipShareRow? existing = await clipsStatements.GetActiveShareByClipId(clipId);
        if (existing != null)
        {
            return new ClipShareResponse($"/share/{existing.Token}", true);
        }

        for (int attempt = 1; attempt <= MaxShareTokenAttempts; attempt++)
        {
            string token = GenerateShareToken();

            try
            {
                ClipsStatements.ClipShareRow share = await clipsStatements.InsertClipShare(token, clipId, userId);
                logger.LogInformation("Created clip share {ShareId} for clip {ClipId}", share.Id, clipId);
                return new ClipShareResponse($"/share/{share.Token}", true);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                ClipsStatements.ClipShareRow? racedShare = await clipsStatements.GetActiveShareByClipId(clipId);
                if (racedShare != null)
                {
                    return new ClipShareResponse($"/share/{racedShare.Token}", true);
                }

                logger.LogWarning(ex, "Share token collision while creating share for clip {ClipId}; attempt {Attempt}", clipId, attempt);
            }
        }

        throw new InvalidOperationException("Failed to generate a unique share token");
    }

    public async Task<SharedClipResponse?> GetSharedClip(string token)
    {
        ClipsStatements.SharedClipRow? sharedClip = await clipsStatements.GetSharedClipByToken(token);
        if (sharedClip == null || !sharedClip.VideoStatus.IsPlayable())
        {
            return null;
        }

        logger.LogInformation("Resolved clip share {ShareId} for clip {ClipId}", sharedClip.ShareId, sharedClip.ClipId);

        return new SharedClipResponse(
            sharedClip.Title ?? "Untitled",
            sharedClip.GameName,
            sharedClip.Length ?? 0,
            sharedClip.DateUploaded ?? sharedClip.CreatedAt,
            mediaUrls.Thumbnail(sharedClip.ToVideoRef()),
            mediaUrls.Sources(sharedClip.ToVideoRef()));
    }
}
