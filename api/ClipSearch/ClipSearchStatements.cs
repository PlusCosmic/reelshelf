using System.Text;
using Dapper;
using Npgsql;
using Pgvector;
using Reelshelf.Core;
using Reelshelf.Video;

namespace Reelshelf.ClipSearch;

public class ClipSearchStatements(NpgsqlConnection connection)
{
    /// <summary>A clip a search found, with its summary's distance from the phrase when there was one.</summary>
    public sealed class ClipSearchRow : ClipsStatements.ClipWithTagsRow
    {
        public double? Distance { get; set; }
    }

    /// <summary>What a search narrows the owner's clips to, as SQL reads it.</summary>
    public sealed record SearchFilters(
        DateTimeOffset? From,
        DateTimeOffset? Before,
        IReadOnlyList<Guid> GameCategoryIds,
        IReadOnlyList<string> MoodTags,
        IReadOnlyList<string> People);

    /// <summary>
    /// The phrase's embedding, made by <paramref name="Model"/>; only summaries embedded by the same model are compared
    /// with it.
    /// </summary>
    public sealed record SearchPhrase(Vector Embedding, string Model, double MaxDistance);

    /// <summary>
    /// The owner's clips outside the bin (<see cref="ClipsStatements.ClipFilter"/>) that match every filter given,
    /// judged by each clip's latest succeeded summary for its mood tags and people. With a <paramref name="phrase"/>
    /// only clips whose summary was embedded by the phrase's model and lies within its distance match, closest first;
    /// without one, clips with no summary can match too, newest first. Dates run from <c>From</c> up to, but not
    /// including, <c>Before</c>.
    /// </summary>
    public async Task<List<ClipSearchRow>> SearchAsync(
        Guid ownerId,
        SearchFilters filters,
        SearchPhrase? phrase,
        int limit)
    {
        // ClipFilter's end date is inclusive; a microsecond before the next day is the last moment Postgres stores.
        (StringBuilder where, DynamicParameters parameters) = ClipsStatements.ClipFilter(ownerId, null, null, null,
            filters.From, filters.Before?.AddMicroseconds(-1), false, null, null, binned: false);
        parameters.Add("limit", limit);

        if (filters.GameCategoryIds.Count > 0)
        {
            where.Append("\nAND c.game_category_id = ANY(@searchGameIds)");
            parameters.Add("searchGameIds", filters.GameCategoryIds.ToArray());
        }

        if (filters.MoodTags.Count > 0)
        {
            where.Append("\nAND summary.mood_tags @> @searchMoodTags");
            parameters.Add("searchMoodTags", filters.MoodTags.ToArray());
        }

        // A name matches when it is part of one the summary heard, ignoring case, so "Sam" finds "Sammy".
        for (int i = 0; i < filters.People.Count; i++)
        {
            where.Append($"""

                AND EXISTS (SELECT 1 FROM unnest(summary.people) heard
                            WHERE strpos(lower(heard), lower(@searchPerson{i})) > 0)
                """);
            parameters.Add($"searchPerson{i}", filters.People[i]);
        }

        string distance = "NULL::float8";
        string orderBy = "c.created_at DESC, c.id DESC";
        if (phrase is not null)
        {
            distance = "(summary.embedding <=> @searchEmbedding)";
            where.Append($"""

                AND summary.embedding IS NOT NULL
                AND summary.embedding_model = @searchEmbeddingModel
                AND {distance} <= @searchMaxDistance
                """);
            parameters.Add("searchEmbedding", phrase.Embedding);
            parameters.Add("searchEmbeddingModel", phrase.Model);
            parameters.Add("searchMaxDistance", phrase.MaxDistance);
            orderBy = $"{distance}, {orderBy}";
        }

        string sql = $"""
            WITH found AS (
                SELECT
                    c.id,
                    c.owner_id,
                    c.video_id,
                    c.game_category_id,
                    c.md5_hash,
                    c.created_at,
                    c.title,
                    c.length,
                    c.thumbnail_file_name,
                    c.date_uploaded,
                    c.storage_size,
                    c.video_state,
                    c.video_host,
                    c.video_version,
                    c.video_codecs,
                    c.video_height,
                    c.encode_progress,
                    c.binned_at,
                    {ClipVideoColumns.ThumbnailsReadySql("c")},
                    {distance} AS distance
                FROM clip c
                LEFT JOIN LATERAL (
                    SELECT run.mood_tags, run.people, run.embedding, run.embedding_model
                    FROM clip_summary_run run
                    WHERE run.clip_id = c.id AND run.status = 'succeeded'
                    ORDER BY run.completed_at DESC NULLS LAST, run.created_at DESC
                    LIMIT 1
                ) summary ON true
                WHERE {where}
                ORDER BY {orderBy}
                LIMIT @limit
            )
            SELECT
                found.*,
                (SELECT STRING_AGG(t.name, ',' ORDER BY t.name)
                 FROM clip_tag ct
                 INNER JOIN tag t ON ct.tag_id = t.id
                 WHERE ct.clip_id = found.id) AS tag_names,
                {ClipsStatements.PlayerLegendSql("found")} AS player_legend
            FROM found
            ORDER BY found.distance NULLS LAST, found.created_at DESC, found.id DESC
            """;

        return (await connection.QueryAsync<ClipSearchRow>(sql, parameters)).ToList();
    }
}
