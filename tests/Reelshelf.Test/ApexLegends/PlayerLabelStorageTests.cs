using Dapper;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.Core;
using Xunit;

namespace Reelshelf.Test.ApexLegends;

/// <summary>
/// Runs the baseline page's bulk label save against a real Postgres. Temporary tables with the columns it touches
/// stand in for <c>clip</c> and <c>legend_detection_label</c>, and shadow any real ones for this connection.
/// </summary>
public class PlayerLabelStorageTests
{
    [PostgresFact]
    public async Task SetPlayerLabels_UpsertsTheOwnersLegend_KeepsTeammates_AndSkipsMissingClips()
    {
        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(PostgresFactAttribute.ConnectionString!);
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("""
            CREATE TEMP TABLE clip (id uuid PRIMARY KEY);
            CREATE TEMP TABLE legend_detection_label (
                clip_id uuid PRIMARY KEY REFERENCES clip(id) ON DELETE CASCADE,
                player_legend text,
                teammate_legends text[] NOT NULL DEFAULT '{}',
                labelled_by uuid,
                labelled_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        Guid labelled = Guid.NewGuid();
        Guid unlabelled = Guid.NewGuid();
        Guid deleted = Guid.NewGuid();
        Guid admin = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO clip (id) VALUES (@Labelled), (@Unlabelled)",
            new { Labelled = labelled, Unlabelled = unlabelled });
        await connection.ExecuteAsync("""
            INSERT INTO legend_detection_label (clip_id, player_legend, teammate_legends)
            VALUES (@ClipId, 'Wraith', ARRAY['Lifeline', 'Octane'])
            """, new { ClipId = labelled });

        int saved = await new LegendDetectionStatements(connection).SetPlayerLabelsAsync(
            [labelled, unlabelled, deleted], ["Valkyrie", null, "Ash"], admin);

        Assert.Equal(2, saved);
        List<LabelRow> rows = (await connection.QueryAsync<LabelRow>(
            """
            SELECT clip_id AS ClipId, player_legend AS PlayerLegend, teammate_legends AS TeammateLegends,
                   labelled_by AS LabelledBy
            FROM legend_detection_label
            """)).ToList();
        Assert.Equal(2, rows.Count);
        LabelRow corrected = rows.Single(row => row.ClipId == labelled);
        Assert.Equal("Valkyrie", corrected.PlayerLegend);
        Assert.Equal(["Lifeline", "Octane"], corrected.TeammateLegends);
        Assert.Equal(admin, corrected.LabelledBy);
        LabelRow cantTell = rows.Single(row => row.ClipId == unlabelled);
        Assert.Null(cantTell.PlayerLegend);
        Assert.Empty(cantTell.TeammateLegends);

        Assert.Equal(1, await new LegendDetectionStatements(connection).DeleteLabelsAsync([unlabelled, deleted]));
        Assert.Equal([labelled], await connection.QueryAsync<Guid>("SELECT clip_id FROM legend_detection_label"));
    }

    private sealed class LabelRow
    {
        public Guid ClipId { get; set; }
        public string? PlayerLegend { get; set; }
        public string[] TeammateLegends { get; set; } = [];
        public Guid? LabelledBy { get; set; }
    }
}
