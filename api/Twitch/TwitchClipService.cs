using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Exceptions;
using Reelshelf.Games;
using Reelshelf.Users;

namespace Reelshelf.Twitch;

/// <summary>
/// Lists a user's own Twitch clips and copies chosen ones into their library. The copy runs server-side:
/// Twitch hands out a temporary download URL, the API streams that file straight into the Bunny video that
/// <see cref="ClipService.CreateClip"/> reserved, and from there the clip behaves like any browser upload.
/// </summary>
public sealed class TwitchClipService(
    TwitchApiClient twitch,
    IProviderTokenStore tokenStore,
    ClipService clipService,
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<TwitchClipService> logger)
{
    public const string DownloadHttpClientName = "twitch-clip-download";

    /// <summary>Refresh a token this close to expiry rather than risk a 401 mid-import.</summary>
    private static readonly TimeSpan RefreshLeeway = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GameCacheDuration = TimeSpan.FromHours(12);

    /// <summary>
    /// Used to reserve storage when Twitch does not report the file size. Clips are at most a minute long and
    /// encoded well under this rate, so the reservation is a ceiling and the real size replaces it after the copy.
    /// </summary>
    private const long FallbackBytesPerSecond = 1_000_000;

    public async Task<TwitchClipsResponse> GetClips(Guid userId, int first, string? cursor, int? days, CancellationToken cancellationToken)
    {
        TwitchAccess access = await ResolveAccess(userId, cancellationToken);
        if (access.State != TwitchClipsState.Ready)
        {
            return new TwitchClipsResponse(access.State, access.Login, [], null);
        }

        DateTimeOffset? startedAt = days is { } window and > 0
            ? timeProvider.GetUtcNow().AddDays(-window)
            : null;

        TwitchPage<TwitchClip> page = await WithFreshToken(access,
            token => twitch.GetClipsAsync(token, access.ProviderUserId, first, cursor, startedAt, null, cancellationToken),
            cancellationToken);

        HashSet<string> imported = await clipsStatements.GetImportedSourceClipIds(
            userId, AuthProvider.Twitch, page.Data.Select(clip => clip.Id).ToList());
        Dictionary<string, TwitchGame> games = await ResolveGames(access, page.Data, cancellationToken);
        Dictionary<long, Guid> categoriesByIgdbId = (await gameCategoryStatements.GetUserCategoriesAsync(userId))
            .Where(category => category.IgdbId is not null)
            .GroupBy(category => category.IgdbId!.Value)
            .ToDictionary(group => group.Key, group => group.First().Id);

        List<TwitchClipSummary> clips = page.Data.Select(clip =>
        {
            TwitchGame? game = clip.GameId is not null ? games.GetValueOrDefault(clip.GameId) : null;
            Guid? suggested = game?.IgdbId is { } igdbId ? categoriesByIgdbId.GetValueOrDefault(igdbId) : null;
            return new TwitchClipSummary(
                clip.Id,
                clip.Title,
                clip.Url,
                clip.ThumbnailUrl,
                clip.Duration,
                clip.ViewCount,
                clip.CreatedAt,
                clip.CreatorName,
                game?.Name,
                game?.IgdbId,
                suggested == Guid.Empty ? null : suggested,
                imported.Contains(clip.Id));
        }).ToList();

        return new TwitchClipsResponse(TwitchClipsState.Ready, access.Login, clips, page.Pagination?.Cursor);
    }

    /// <summary>
    /// Copies one clip into the library. Returns null when the owner already imported it. Storage is reserved
    /// for the download's reported size before any bytes move, and released again if the copy fails.
    /// </summary>
    public async Task<ImportedTwitchClipResponse?> ImportClip(Guid userId, ImportTwitchClipRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClipId))
        {
            throw new BadRequestException("A Twitch clip id is required");
        }

        TwitchAccess access = await ResolveAccess(userId, cancellationToken);
        if (access.State != TwitchClipsState.Ready)
        {
            throw new TwitchReauthorizationRequiredException(access.State);
        }

        ClipSource source = new(AuthProvider.Twitch, request.ClipId);
        HashSet<string> imported = await clipsStatements.GetImportedSourceClipIds(userId, AuthProvider.Twitch, [request.ClipId]);
        if (imported.Count > 0)
        {
            return null;
        }

        // The clip's own metadata supplies the title and capture time; the download call supplies the file.
        TwitchPage<TwitchClip> lookup = await WithFreshToken(access,
            token => twitch.GetClipsByIdAsync(token, [request.ClipId], cancellationToken), cancellationToken);
        TwitchClip clip = lookup.Data.FirstOrDefault(candidate => candidate.Id == request.ClipId)
                          ?? throw new NotFoundException("Twitch clip", request.ClipId);

        if (!string.Equals(clip.BroadcasterId, access.ProviderUserId, StringComparison.Ordinal))
        {
            throw new BadRequestException("Only clips from your own Twitch channel can be imported");
        }

        IReadOnlyList<TwitchClipDownload> downloads = await WithFreshToken(access,
            token => twitch.GetClipDownloadsAsync(token, access.ProviderUserId, [request.ClipId], cancellationToken),
            cancellationToken);
        string downloadUrl = downloads.FirstOrDefault(d => d.ClipId == request.ClipId)?.LandscapeDownloadUrl
                             ?? downloads.FirstOrDefault(d => d.ClipId == request.ClipId)?.PortraitDownloadUrl
                             ?? throw new ConflictException("Twitch has no downloadable file for this clip yet. Try again later.");

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Twitch returned an unexpected download URL");
        }

        string title = string.IsNullOrWhiteSpace(request.Title) ? clip.Title : request.Title.Trim();
        if (title.Length > 200)
        {
            title = title[..200];
        }

        using HttpClient downloader = httpClientFactory.CreateClient(DownloadHttpClientName);
        using HttpResponseMessage download = await downloader.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        download.EnsureSuccessStatusCode();

        long? reportedLength = download.Content.Headers.ContentLength;
        long reservedSize = reportedLength is > 0
            ? reportedLength.Value
            : Math.Max(1, (long)Math.Ceiling(clip.Duration) * FallbackBytesPerSecond);

        CreateClipResponse? created = await clipService.CreateClip(
            request.CategoryId, title, userId, clip.CreatedAt, reservedSize, md5Hash: null, source: source);
        if (created is null)
        {
            return null;
        }

        try
        {
            await using Stream body = await download.Content.ReadAsStreamAsync(cancellationToken);
            await using CountingStream counted = new(body);
            await clipService.UploadVideo(created.VideoId, counted, reportedLength, cancellationToken);

            if (reportedLength is null && counted.BytesRead > 0)
            {
                // Replace the ceiling with what actually landed so the reservation matches the file.
                await clipsStatements.UpdateClipFileSize(created.ClipId, counted.BytesRead);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Copying Twitch clip {TwitchClipId} into clip {ClipId} failed; releasing the reservation", request.ClipId, created.ClipId);
            try
            {
                await clipService.DeleteClip(created.ClipId, userId);
            }
            catch (Exception cleanupError)
            {
                // The abandoned-upload purge removes a never-filled video after a day.
                logger.LogWarning(cleanupError, "Failed to release clip {ClipId} after a Twitch import error", created.ClipId);
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new ServiceUnavailableException("The clip could not be copied from Twitch. Try again in a moment.", ex);
        }

        return new ImportedTwitchClipResponse(created.ClipId, created.VideoId, request.ClipId, request.CategoryId, clip.CreatedAt);
    }

    /// <summary>
    /// Works out whether the account can use the Twitch clip features and, when it can, a token ready to use.
    /// A token missing the clips scope counts as "needs authorization": the user linked Twitch before the app
    /// asked for it and has to go through the consent screen once more.
    /// </summary>
    private async Task<TwitchAccess> ResolveAccess(Guid userId, CancellationToken cancellationToken)
    {
        ProviderIdentityTokens? identity = await tokenStore.GetIdentityTokens(userId, AuthProvider.Twitch);
        if (identity is null)
        {
            return TwitchAccess.NotLinked;
        }

        if (identity.Tokens is null || !identity.Tokens.HasScope(TwitchScopes.ManageClips))
        {
            return TwitchAccess.NeedsAuthorization(identity);
        }

        ProviderTokens tokens = identity.Tokens;
        if (tokens.IsExpiring(timeProvider.GetUtcNow(), RefreshLeeway))
        {
            ProviderTokens? refreshed = await Refresh(identity, cancellationToken);
            if (refreshed is null)
            {
                return TwitchAccess.NeedsAuthorization(identity);
            }

            tokens = refreshed;
        }

        return TwitchAccess.Ready(identity, tokens);
    }

    /// <summary>Runs a Helix call; on a 401 refreshes the token once and retries, otherwise asks the user to reconnect.</summary>
    private async Task<T> WithFreshToken<T>(TwitchAccess access, Func<string, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call(access.Tokens!.AccessToken);
        }
        catch (TwitchUnauthorizedException)
        {
            ProviderTokens? refreshed = await Refresh(access.Identity!, cancellationToken);
            if (refreshed is null)
            {
                throw new TwitchReauthorizationRequiredException(TwitchClipsState.NeedsAuthorization);
            }

            access.Tokens = refreshed;
            try
            {
                return await call(refreshed.AccessToken);
            }
            catch (TwitchUnauthorizedException)
            {
                await tokenStore.UpdateIdentityTokens(access.Identity!.IdentityId, null);
                throw new TwitchReauthorizationRequiredException(TwitchClipsState.NeedsAuthorization);
            }
        }
        catch (TwitchForbiddenException ex)
        {
            logger.LogInformation(ex, "Twitch refused a clip call for identity {IdentityId}", access.Identity?.IdentityId);
            throw new TwitchReauthorizationRequiredException(TwitchClipsState.NeedsAuthorization);
        }
        catch (TwitchRateLimitedException)
        {
            throw new ServiceUnavailableException("Twitch is rate limiting requests right now. Try again in a minute.");
        }
    }

    private async Task<ProviderTokens?> Refresh(ProviderIdentityTokens identity, CancellationToken cancellationToken)
    {
        if (identity.Tokens?.RefreshToken is not { } refreshToken)
        {
            await tokenStore.UpdateIdentityTokens(identity.IdentityId, null);
            return null;
        }

        TwitchTokenResponse? response = await twitch.RefreshTokenAsync(refreshToken, cancellationToken);
        if (response is null)
        {
            // Twitch will not renew this grant; forget it so the UI asks the user to reconnect.
            await tokenStore.UpdateIdentityTokens(identity.IdentityId, null);
            return null;
        }

        ProviderTokens refreshed = new(
            response.AccessToken,
            response.RefreshToken ?? refreshToken,
            timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn),
            response.Scope is { Count: > 0 } ? response.Scope : identity.Tokens.Scopes);
        await tokenStore.UpdateIdentityTokens(identity.IdentityId, refreshed);
        return refreshed;
    }

    private async Task<Dictionary<string, TwitchGame>> ResolveGames(TwitchAccess access, IEnumerable<TwitchClip> clips, CancellationToken cancellationToken)
    {
        Dictionary<string, TwitchGame> games = new(StringComparer.Ordinal);
        List<string> missing = [];
        foreach (string gameId in clips.Select(clip => clip.GameId).OfType<string>().Where(id => id.Length > 0).Distinct())
        {
            if (cache.TryGetValue(GameCacheKey(gameId), out TwitchGame? cached) && cached is not null)
            {
                games[gameId] = cached;
            }
            else
            {
                missing.Add(gameId);
            }
        }

        if (missing.Count == 0)
        {
            return games;
        }

        try
        {
            IReadOnlyList<TwitchGame> fetched = await WithFreshToken(access,
                token => twitch.GetGamesAsync(token, missing, cancellationToken), cancellationToken);
            foreach (TwitchGame game in fetched)
            {
                games[game.Id] = game;
                cache.Set(GameCacheKey(game.Id), game, GameCacheDuration);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TwitchReauthorizationRequiredException)
        {
            // Game names are a convenience for pre-selecting a category; the clip list is still useful without them.
            logger.LogWarning(ex, "Could not resolve Twitch games {GameIds}", string.Join(',', missing));
        }

        return games;
    }

    private static string GameCacheKey(string gameId) => $"twitch:game:{gameId}";

    private sealed class TwitchAccess
    {
        public TwitchClipsState State { get; private init; }
        public ProviderIdentityTokens? Identity { get; private init; }
        public ProviderTokens? Tokens { get; set; }

        public string ProviderUserId => Identity?.ProviderUserId ?? throw new InvalidOperationException("No Twitch identity");
        public string? Login => Identity?.Username;

        public static readonly TwitchAccess NotLinked = new() { State = TwitchClipsState.NotLinked };

        public static TwitchAccess NeedsAuthorization(ProviderIdentityTokens identity) =>
            new() { State = TwitchClipsState.NeedsAuthorization, Identity = identity };

        public static TwitchAccess Ready(ProviderIdentityTokens identity, ProviderTokens tokens) =>
            new() { State = TwitchClipsState.Ready, Identity = identity, Tokens = tokens };
    }

    /// <summary>Counts bytes passing through so an unknown Content-Length can be replaced with the real size.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>The Twitch clip feature is unavailable until the user (re)connects Twitch from the add-clips page.</summary>
public sealed class TwitchReauthorizationRequiredException(TwitchClipsState state)
    : DomainException(
        state == TwitchClipsState.NotLinked
            ? "Link your Twitch account to import clips."
            : "Reconnect Twitch to allow Reelshelf to download your clips.",
        StatusCodes.Status403Forbidden)
{
    public TwitchClipsState State { get; } = state;
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum TwitchClipsState
{
    /// <summary>The account has no Twitch identity.</summary>
    NotLinked,

    /// <summary>Twitch is linked but the stored token is missing, revoked, or lacks the clips scope.</summary>
    NeedsAuthorization,

    Ready
}

public sealed record TwitchClipsResponse(
    TwitchClipsState State,
    string? TwitchLogin,
    List<TwitchClipSummary> Clips,
    string? Cursor);

public sealed record TwitchClipSummary(
    string Id,
    string Title,
    string Url,
    string? ThumbnailUrl,
    double DurationSeconds,
    long ViewCount,
    DateTimeOffset CreatedAt,
    string? CreatorName,
    string? GameName,
    long? IgdbId,
    Guid? SuggestedCategoryId,
    bool AlreadyImported);

public sealed record ImportTwitchClipRequest(string ClipId, Guid CategoryId, string? Title = null);

public sealed record ImportedTwitchClipResponse(
    Guid ClipId,
    Guid VideoId,
    string TwitchClipId,
    Guid CategoryId,
    DateTimeOffset CreatedAt);
