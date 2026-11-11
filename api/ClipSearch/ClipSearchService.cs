using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pgvector;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.ClipSummary;
using Reelshelf.Core;
using Reelshelf.Core.Models;
using Reelshelf.Exceptions;
using Reelshelf.Games;
using Reelshelf.Plans;

namespace Reelshelf.ClipSearch;

/// <summary>
/// Natural-language search over the caller's own clips (ADR-0005): a model reads the search into filters and a phrase,
/// SQL narrows the clips by the filters and ranks them by how close each one's summary is to the phrase. Only for
/// plans with transcripts, since only their clips have summaries; summaries themselves are never shown.
/// </summary>
public sealed class ClipSearchService(
    IClipQueryParserFactory parsers,
    IClipSummarizerFactory summarizers,
    IOptionsMonitor<ClipSearchOptions> options,
    IOptionsMonitor<ClipSummaryOptions> summaryOptions,
    AccountTierService tiers,
    GameCategoryStatements categories,
    ClipSearchStatements statements,
    ClipsStatements clips,
    ClipProjection projection,
    TimeProvider time,
    ILogger<ClipSearchService> logger)
{
    public const int MaxQueryLength = 300;

    public async Task<ClipSearchAvailability> GetAvailabilityAsync(AuthenticatedUser user)
    {
        if (!TierLimits.HasTranscripts(await tiers.GetTier(user)))
        {
            return new ClipSearchAvailability(false, ClipSearchAvailability.PlanReason);
        }

        return parsers.IsConfigured && summarizers.IsEmbeddingConfigured
            ? new ClipSearchAvailability(true, null)
            : new ClipSearchAvailability(false, ClipSearchAvailability.NotConfiguredReason);
    }

    public async Task<ClipSearchResponse> SearchAsync(
        AuthenticatedUser user,
        ClipSearchRequest request,
        CancellationToken cancellationToken)
    {
        ClipSearchAvailability availability = await GetAvailabilityAsync(user);
        if (availability.Reason == ClipSearchAvailability.PlanReason)
        {
            throw new ForbiddenException("Searching by description comes with the Pro and Unlimited plans.");
        }

        if (!availability.Available)
        {
            throw new ServiceUnavailableException("Searching by description isn't available right now.");
        }

        string query = request.Query?.Trim() ?? "";
        if (query.Length > MaxQueryLength)
        {
            throw new BadRequestException($"Keep the search under {MaxQueryLength} characters.");
        }

        if (query.Length == 0 && request.Filters is null)
        {
            throw new BadRequestException("Say what you're looking for.");
        }

        TimeZoneInfo zone = FindTimeZone(request.TimeZone);
        List<GameCategory> library = await categories.GetLibraryCategoriesAsync(user.Id);
        List<ClipQueryGame> games = library.Select(game => new ClipQueryGame(game.Id, game.Name)).ToList();

        ClipQueryFilters filters = request.Filters is { } sent
            ? ClipQueryFilters.Clean(sent.StartDate, sent.EndDate, sent.GameCategoryIds, sent.MoodTags, sent.People,
                sent.Phrase)
            : await ParseAsync(new ClipQueryInput(query, Today(zone), games), cancellationToken);

        // Only the caller's own games: an id from anywhere else would narrow to clips they can't have.
        Dictionary<Guid, GameCategory> libraryById = library.ToDictionary(game => game.Id);
        List<GameCategory> chosenGames = filters.GameCategoryIds
            .Where(libraryById.ContainsKey)
            .Select(id => libraryById[id])
            .ToList();

        ClipSearchStatements.SearchPhrase? phrase = filters.Phrase is { } text
            ? await EmbedAsync(text, cancellationToken)
            : null;
        List<ClipSearchStatements.ClipSearchRow> rows = await statements.SearchAsync(
            user.Id,
            new ClipSearchStatements.SearchFilters(
                StartOfDay(filters.StartDate, zone),
                StartOfDay(filters.EndDate?.AddDays(1), zone),
                chosenGames.Select(game => game.Id).ToList(),
                filters.MoodTags,
                filters.People),
            phrase,
            Math.Clamp(options.CurrentValue.MaxResults, 1, 200));

        List<Guid> clipIds = rows.Select(row => row.Id).ToList();
        HashSet<Guid> viewed = await clips.GetViewedClipIds(user.Id, clipIds);
        HashSet<Guid> shared = await clips.GetSharedClipIds(clipIds);
        List<Clip> found = projection.ProjectClips(rows, libraryById, viewed, shared);

        return new ClipSearchResponse(
            query,
            new ClipSearchInterpretation(
                filters.StartDate,
                filters.EndDate,
                chosenGames.Select(game => new ClipSearchGame(game.Id, game.Name)).ToList(),
                filters.MoodTags.ToList(),
                filters.People.ToList(),
                filters.Phrase),
            found);
    }

    private async Task<ClipQueryFilters> ParseAsync(ClipQueryInput input, CancellationToken cancellationToken)
    {
        try
        {
            return await parsers.Create().ParseAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or ClientResultException or HttpRequestException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Clip search couldn't read a search");
            throw new ServiceUnavailableException("Search couldn't read that just now. Try again in a moment.", ex);
        }
    }

    private async Task<ClipSearchStatements.SearchPhrase> EmbedAsync(string phrase, CancellationToken cancellationToken)
    {
        string model = summaryOptions.CurrentValue.EmbeddingModel;
        try
        {
            ClipEmbedding embedding = await summarizers.CreateEmbedder(model).EmbedAsync(phrase, cancellationToken);
            return new ClipSearchStatements.SearchPhrase(new Vector(embedding.Vector), model,
                options.CurrentValue.MaxDistance);
        }
        catch (Exception ex) when (ex is ClientResultException or HttpRequestException or InvalidOperationException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Clip search couldn't embed a phrase");
            throw new ServiceUnavailableException("Search couldn't read that just now. Try again in a moment.", ex);
        }
    }

    private DateOnly Today(TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
    }

    /// <summary>Midnight at the start of <paramref name="day"/> where the searcher is.</summary>
    internal static DateTimeOffset? StartOfDay(DateOnly? day, TimeZoneInfo zone)
    {
        if (day is not { } date)
        {
            return null;
        }

        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A midnight skipped by a clock change starts the day an hour later.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>The searcher's time zone by its IANA name, else UTC.</summary>
    internal static TimeZoneInfo FindTimeZone(string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.Utc;
    }
}

/// <summary>
/// A search: the words typed, read by a model, or with <c>Filters</c> set, a search already read with some of its
/// filters taken away, which isn't read again. <c>TimeZone</c> is the searcher's IANA time zone (UTC when unknown), so
/// "yesterday" and "last month" are their days.
/// </summary>
public sealed record ClipSearchRequest(string? Query, ClipSearchFilters? Filters, string? TimeZone);

/// <summary>Dates are whole days, both inclusive.</summary>
public sealed record ClipSearchFilters(
    DateOnly? StartDate,
    DateOnly? EndDate,
    List<Guid>? GameCategoryIds,
    List<string>? MoodTags,
    List<string>? People,
    string? Phrase);

/// <summary>
/// How a search was read: the filters it applied and the phrase clips were ranked by, for the page to show and to take
/// filters away from. Dates are whole days, both inclusive.
/// </summary>
public sealed record ClipSearchInterpretation(
    DateOnly? StartDate,
    DateOnly? EndDate,
    List<ClipSearchGame> Games,
    List<string> MoodTags,
    List<string> People,
    string? Phrase);

public sealed record ClipSearchGame(Guid Id, string Name);

/// <summary>The clips found, closest to the phrase first (or newest first without one), at most <c>ClipSearch:MaxResults</c>.</summary>
public sealed record ClipSearchResponse(string Query, ClipSearchInterpretation Interpretation, List<Clip> Clips);

/// <summary>
/// Whether the caller can search by description. <c>Reason</c> is <c>plan</c> when their plan has no transcripts, or
/// <c>not_configured</c> when this deployment has no model for it.
/// </summary>
public sealed record ClipSearchAvailability(bool Available, string? Reason)
{
    public const string PlanReason = "plan";
    public const string NotConfiguredReason = "not_configured";
}
