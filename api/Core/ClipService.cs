using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;
using Reelshelf.Bunny;
using Reelshelf.Bunny.Models;
using Reelshelf.Core.Models;
using Reelshelf.Discord;
using Reelshelf.Exceptions;
using Reelshelf.Games;

namespace Reelshelf.Core;

public class ClipService(
    BunnyService bunnyService,
    ClipsStatements clipsStatements,
    DiscordStatements discordStatements,
    GameCategoryStatements gameCategoryStatements,
    ClipProjection clipProjection,
    IConfiguration configuration,
    ILogger<ClipService> logger)
{
    private const int ShareTokenByteLength = 32;
    private const int MaxShareTokenAttempts = 3;

    private static string NormalizeTag(string tag)
    {
        return tag.Trim().ToLowerInvariant();
    }

    private string BuildEmbedUrl(Guid videoId)
    {
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");
        return $"https://player.mediadelivery.net/embed/{libraryId}/{videoId}?autoplay=false";
    }

    private static bool IsPlayable(int? videoStatus)
    {
        return videoStatus is (int)BunnyVideoStatus.Finished or (int)BunnyVideoStatus.ResolutionFinished;
    }

    private static string GenerateShareToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(ShareTokenByteLength);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public async Task<CreateClipResponse?> CreateClip(Guid gameCategoryId, string videoTitle,
        string discordUserId, DateTimeOffset createdAt, string? md5Hash = null)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        // Get the category to get the slug for Bunny CDN collection naming
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

        // Get or create collection
        ClipsStatements.ClipCollectionRow? clipCollection =
            await clipsStatements.GetCollectionByOwnerAndCategory(userId, gameCategoryId);
        if (clipCollection == null)
        {
            BunnyCollection bunnyCollection = await bunnyService.CreateCollectionAsync(gameCategory.Slug, userId);
            clipCollection = await clipsStatements.InsertCollection(userId, bunnyCollection.Guid, gameCategoryId);
        }

        BunnyVideo video = await bunnyService.CreateVideoAsync(clipCollection.CollectionId, videoTitle);

        await clipsStatements.InsertClip(
            userId,
            video.Guid,
            gameCategoryId,
            md5Hash,
            createdAt,
            video.Title,
            video.Length,
            video.ThumbnailFileName,
            video.DateUploaded,
            video.StorageSize,
            video.Status,
            video.EncodeProgress);

        long expiration = DateTimeOffset.Now.AddHours(1).ToUnixTimeSeconds();
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");
        string secretKey = configuration["BunnyAccessKey"]
                           ?? throw new InvalidOperationException("Bunny access key not configured");
        byte[] signature = Encoding.UTF8.GetBytes(libraryId + secretKey + expiration + video.Guid);
        byte[] hash = SHA256.HashData(signature);
        string? hashString = Convert.ToHexString(hash);
        return new CreateClipResponse(
            hashString ?? throw new InvalidOperationException("Hash computation failed"),
            expiration,
            libraryId,
            video.Guid,
            video.CollectionId);
    }

    public async Task<Clip?> GetClipById(Guid clipId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        ClipsStatements.ClipWithTagsRow? clipWithTags = await clipsStatements.GetClipWithTagsById(clipId);
        if (clipWithTags == null)
        {
            return null;
        }

        // Get the game category for the slug
        GameCategory? gameCategory = await gameCategoryStatements.GetByIdAsync(clipWithTags.GameCategoryId);
        if (gameCategory == null)
        {
            return null;
        }

        // Get clip collection to retrieve CollectionId
        ClipsStatements.ClipCollectionRow? clipCollection =
            await clipsStatements.GetCollectionByOwnerAndCategory(clipWithTags.OwnerId, clipWithTags.GameCategoryId);
        if (clipCollection == null)
        {
            return null;
        }

        bool isViewed = await clipsStatements.IsClipViewed(userId, clipId);
        ClipsStatements.ClipShareRow? share = await clipsStatements.GetActiveShareByClipId(clipId);

        return clipProjection.ProjectClip(clipWithTags, gameCategory, clipCollection, isViewed, share != null);
    }

    public async Task<Clip?> AddTagToClip(Guid clipId, string discordUserId, string tag)
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

        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");

        ClipsStatements.ClipWithTagsRow? clipWithTags =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, discordUser.Id);
        if (clipWithTags == null)
        {
            return null;
        }

        List<string> existingTags = !string.IsNullOrEmpty(clipWithTags.TagNames)
            ? clipWithTags.TagNames.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];

        if (existingTags.Contains(tag))
        {
            return await GetClipById(clipId, discordUserId); // already tagged
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

        return await GetClipById(clipId, discordUserId);
    }

    public async Task<Clip?> RemoveTagFromClip(Guid clipId, string discordUserId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new BadRequestException("Tag cannot be empty");
        }

        tag = NormalizeTag(tag);

        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");

        ClipsStatements.ClipWithTagsRow? clipWithTags =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, discordUser.Id);
        if (clipWithTags == null)
        {
            return null;
        }

        ClipsStatements.TagRow? tagEntity = await clipsStatements.GetTagByNameForClip(clipId, tag);
        if (tagEntity != null)
        {
            await clipsStatements.DeleteClipTag(clipId, tagEntity.Id);
        }

        return await GetClipById(clipId, discordUserId);
    }

    public async Task<Clip?> UpdateClipTitle(Guid clipId, string discordUserId, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new BadRequestException("Title cannot be empty");
        }

        if (newTitle.Length > 200)
        {
            newTitle = newTitle[..200];
        }

        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");

        ClipsStatements.ClipWithTagsRow? clip =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, discordUser.Id);
        if (clip == null)
        {
            return null;
        }

        await clipsStatements.UpdateClipTitle(clipId, newTitle);
        return await GetClipById(clipId, discordUserId);
    }

    public async Task<List<TopTag>> GetTopTags()
    {
        List<ClipsStatements.TopTagRow> topTagRows = await clipsStatements.GetAllTagsOrderedByUsage();
        return topTagRows.Select(t => new TopTag(t.Name, t.Count)).ToList();
    }

    public async Task<bool> MarkClipAsViewed(Guid clipId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;

        ClipsStatements.ClipRow? clip = await clipsStatements.GetClipById(clipId);
        if (clip == null)
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

    public async Task<PagedClipsResponse> GetClipsForCategory(Guid gameCategoryId,
        string discordUserId, int page, int pageSize, List<string>? tags = null, string? titleSearch = null,
        bool unviewedOnly = false, ClipSortOrder sortOrder = ClipSortOrder.DateDescending,
        DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");
        Guid userId = discordUser.Id;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Get the game category for the slug
        GameCategory? gameCategory = await gameCategoryStatements.GetByIdAsync(gameCategoryId);
        if (gameCategory == null)
        {
            return new PagedClipsResponse([], 0, 0);
        }

        ClipsStatements.ClipCollectionRow? clipCollection =
            await clipsStatements.GetCollectionByOwnerAndCategory(userId, gameCategoryId);
        if (clipCollection == null)
        {
            return new PagedClipsResponse([], 0, 0);
        }

        // Normalize tags for filtering (same as when adding tags)
        List<string>? normalizedTags = tags?.Select(NormalizeTag).ToList();

        ClipsStatements.PagedClipWithTagsRows clipsPage =
            await clipsStatements.GetClipsWithTagsByOwnerAndCategory(
                userId,
                gameCategoryId,
                normalizedTags,
                titleSearch,
                startDate,
                endDate,
                unviewedOnly,
                sortOrder,
                userId,
                pageSize,
                (page - 1) * pageSize);

        List<ClipsStatements.ClipWithTagsRow> pagedClips = clipsPage.Rows;
        List<Guid> clipIds = pagedClips.Select(c => c.Id).ToList();
        HashSet<Guid> viewedClipIds = await clipsStatements.GetViewedClipIds(userId, clipIds);
        HashSet<Guid> sharedClipIds = await clipsStatements.GetSharedClipIds(clipIds);

        int totalPages = (int)Math.Ceiling((double)clipsPage.TotalCount / pageSize);

        List<Clip> finalClips = clipProjection.ProjectClips(
            pagedClips,
            gameCategory,
            clipCollection,
            viewedClipIds,
            sharedClipIds);

        PagedClipsResponse pagedClipsResponse = new(finalClips, clipsPage.TotalCount, totalPages);
        return pagedClipsResponse;
    }

    public async Task<bool> DeleteClip(Guid clipId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId) ?? throw new UnauthorizedException("User not found");

        ClipsStatements.ClipWithTagsRow? clip =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, discordUser.Id);
        if (clip == null)
        {
            return false;
        }

        await bunnyService.DeleteVideoAsync(clip.VideoId);
        await clipsStatements.DeleteClip(clipId);
        return true;
    }

    public async Task<ClipShareResponse?> CreateOrGetShare(Guid clipId, string discordUserId)
    {
        DiscordStatements.DiscordUserRow discordUser = await discordStatements.GetUserByDiscordId(discordUserId)
                                                       ?? throw new UnauthorizedException("User not found");

        ClipsStatements.ClipWithTagsRow? clip =
            await clipsStatements.GetClipWithTagsByIdAndOwner(clipId, discordUser.Id);
        if (clip == null)
        {
            return null;
        }

        if (!IsPlayable(clip.VideoStatus))
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
                ClipsStatements.ClipShareRow share = await clipsStatements.InsertClipShare(token, clipId, discordUser.Id);
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
        if (sharedClip == null || !IsPlayable(sharedClip.VideoStatus))
        {
            return null;
        }

        logger.LogInformation("Resolved clip share {ShareId} for clip {ClipId}", sharedClip.ShareId, sharedClip.ClipId);

        return new SharedClipResponse(
            sharedClip.Title ?? "Untitled",
            sharedClip.GameName,
            sharedClip.Length ?? 0,
            sharedClip.DateUploaded ?? sharedClip.CreatedAt,
            BuildEmbedUrl(sharedClip.VideoId));
    }
}
