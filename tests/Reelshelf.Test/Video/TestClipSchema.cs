using Dapper;
using Npgsql;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// A minimal <c>clip</c> table with the columns the clip statements read, plus the video pipeline's migrations, for
/// <see cref="PostgresFactAttribute"/> tests that run in a schema of their own.
/// </summary>
internal static class TestClipSchema
{
    public static async Task CreateAsync(NpgsqlConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE clip (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL,
                video_id uuid NOT NULL,
                game_category_id uuid NOT NULL,
                md5_hash text,
                created_at timestamptz NOT NULL DEFAULT now(),
                reserved_at timestamptz NOT NULL DEFAULT now(),
                title text,
                length integer,
                thumbnail_file_name text,
                date_uploaded timestamptz,
                storage_size bigint,
                video_status integer,
                video_state text NOT NULL DEFAULT 'awaiting_upload',
                video_host text NOT NULL DEFAULT 'bunny',
                encode_progress integer,
                file_size bigint
            )
            """);
        await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V40__transcode_run.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V42__clip_video_version.sql")));
        await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V44__bunny_migration.sql")));
    }

    public static string MigrationPath(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Reelshelf.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "migrations", "db", "migrations", fileName);
    }
}
