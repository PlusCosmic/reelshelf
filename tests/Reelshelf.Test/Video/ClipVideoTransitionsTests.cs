using Dapper;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// The table of moves <c>clip.video_state</c> may make (<see cref="ClipVideoTransitions"/>), and each statement that
/// writes the column keeping to it against a real Postgres, in a schema of the test's own: in particular the moves
/// that used to happen (#322): progress reviving a failed clip, a
/// transcode failure relabelling a failed upload, and a retried upload reviving a rejected one.
/// </summary>
public class ClipVideoTransitionsTests
{
    private const ClipVideoStatus AwaitingUpload = ClipVideoStatus.AwaitingUpload;
    private const ClipVideoStatus Uploaded = ClipVideoStatus.Uploaded;
    private const ClipVideoStatus Processing = ClipVideoStatus.Processing;
    private const ClipVideoStatus Ready = ClipVideoStatus.Ready;
    private const ClipVideoStatus EncodeFailed = ClipVideoStatus.EncodeFailed;
    private const ClipVideoStatus UploadFailed = ClipVideoStatus.UploadFailed;

    [Fact]
    public void OurOwnPipelinesMoves_LeaveOnlyTheStatesTheyName()
    {
        Assert.Equal(["uploaded", "processing"],
            ClipVideoTransitions.FromStates(ClipVideoChange.TranscodeProgress, Processing));
        Assert.Equal(["uploaded", "processing"],
            ClipVideoTransitions.FromStates(ClipVideoChange.TranscodeFailed, EncodeFailed));
        Assert.Equal(["processing"], ClipVideoTransitions.FromStates(ClipVideoChange.TranscodeReleased, Uploaded));
        Assert.Equal(["awaiting_upload"], ClipVideoTransitions.FromStates(ClipVideoChange.SourceUploaded, Uploaded));
        Assert.Equal(["awaiting_upload"], ClipVideoTransitions.FromStates(ClipVideoChange.UploadFailed, UploadFailed));
        Assert.Equal(["awaiting_upload"], ClipVideoTransitions.FromStates(ClipVideoChange.Attach, Processing));

        // A change never leads anywhere but its own state.
        Assert.Empty(ClipVideoTransitions.FromStates(ClipVideoChange.TranscodeProgress, Ready));
    }

    [PostgresFact]
    public async Task AttachVideo_TakesOnlyAReservedClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid reserved = await test.AddClipAsync("awaiting_upload");
        Guid ready = await test.AddClipAsync("ready", encodeProgress: 100, length: 30);

        ClipsStatements.ClipRow attached = await test.Clips.AttachVideo(reserved,
            new HostedVideo(Guid.NewGuid(), Report(AwaitingUpload, 0)));
        Assert.Equal("awaiting_upload", attached.VideoState);

        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Clips.AttachVideo(ready,
            new HostedVideo(Guid.NewGuid(), Report(AwaitingUpload, 0))));
        Assert.Equal(("ready", 100, 30, "Clip"), await test.RowAsync(ready));
    }

    [PostgresFact]
    public async Task SetTranscodeProgress_DoesNotReviveAFailedOrWaitingClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid uploaded = await test.AddClipAsync("uploaded", encodeProgress: 0);
        Guid encodeFailed = await test.AddClipAsync("encode_failed", encodeProgress: 70);
        Guid uploadFailed = await test.AddClipAsync("upload_failed", encodeProgress: 0);
        Guid awaiting = await test.AddClipAsync("awaiting_upload", encodeProgress: 0);
        Guid ready = await test.AddClipAsync("ready", encodeProgress: 100);

        foreach (Guid clipId in new[] { uploaded, encodeFailed, uploadFailed, awaiting, ready })
        {
            await test.Clips.SetTranscodeProgress(clipId, 55);
        }

        Assert.Equal(("processing", 55), await test.ProgressAsync(uploaded));
        // The percentage is written only with the state.
        Assert.Equal(("encode_failed", 70), await test.ProgressAsync(encodeFailed));
        Assert.Equal(("upload_failed", 0), await test.ProgressAsync(uploadFailed));
        Assert.Equal(("awaiting_upload", 0), await test.ProgressAsync(awaiting));
        Assert.Equal(("ready", 100), await test.ProgressAsync(ready));
    }

    [PostgresFact]
    public async Task SetTranscodeFailed_LeavesAFailedUploadAndAReadyClip()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid processing = await test.AddClipAsync("processing");
        Guid uploaded = await test.AddClipAsync("uploaded");
        Guid uploadFailed = await test.AddClipAsync("upload_failed");
        Guid ready = await test.AddClipAsync("ready");

        foreach (Guid clipId in new[] { processing, uploaded, uploadFailed, ready })
        {
            await test.Clips.SetTranscodeFailed(clipId);
        }

        Assert.Equal("encode_failed", await test.StateAsync(processing));
        Assert.Equal("encode_failed", await test.StateAsync(uploaded));
        Assert.Equal("upload_failed", await test.StateAsync(uploadFailed));
        Assert.Equal("ready", await test.StateAsync(ready));
    }

    [PostgresFact]
    public async Task SetSourceUploaded_MovesOnlyAClipWaitingForItsBytes()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid awaiting = await test.AddClipAsync("awaiting_upload");
        Guid uploadFailed = await test.AddClipAsync("upload_failed");
        Guid processing = await test.AddClipAsync("processing");

        Assert.True(await test.Clips.SetSourceUploaded(awaiting));
        // A rejected upload's file is gone: it stays failed, as the browser path's check says.
        Assert.False(await test.Clips.SetSourceUploaded(uploadFailed));
        Assert.False(await test.Clips.SetSourceUploaded(processing));

        Assert.Equal("uploaded", await test.StateAsync(awaiting));
        Assert.Equal("upload_failed", await test.StateAsync(uploadFailed));
        Assert.Equal("processing", await test.StateAsync(processing));
    }

    [PostgresFact]
    public async Task SetUploadFailed_AndSetTranscodeReleased_KeepToTheirStates()
    {
        await using TestDatabase test = await TestDatabase.CreateAsync();
        Guid awaiting = await test.AddClipAsync("awaiting_upload");
        Guid uploaded = await test.AddClipAsync("uploaded");
        Guid processing = await test.AddClipAsync("processing", encodeProgress: 40);
        Guid encodeFailed = await test.AddClipAsync("encode_failed", encodeProgress: 40);

        await test.Clips.SetUploadFailed(awaiting);
        await test.Clips.SetUploadFailed(uploaded);
        await test.Clips.SetTranscodeReleased(processing);
        await test.Clips.SetTranscodeReleased(encodeFailed);

        Assert.Equal("upload_failed", await test.StateAsync(awaiting));
        Assert.Equal("uploaded", await test.StateAsync(uploaded));
        Assert.Equal(("uploaded", 0), await test.ProgressAsync(processing));
        Assert.Equal(("encode_failed", 40), await test.ProgressAsync(encodeFailed));
    }

    private static HostedVideoInfo Report(ClipVideoStatus status, int encodeProgress) =>
        new("Host title", 12, status, encodeProgress, 1000, DateTimeOffset.UtcNow, "thumbnail.jpg");

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestDatabase(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        {
            _dataSource = dataSource;
            _schema = schema;
            Connection = connection;
            Clips = new ClipsStatements(dataSource.CreateConnection());
        }

        public NpgsqlConnection Connection { get; }

        public ClipsStatements Clips { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"transitions_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            return new TestDatabase(dataSource, connection, schema);
        }

        public async Task<Guid> AddClipAsync(string videoState, int? encodeProgress = null, int? length = null)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, encode_progress, length)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, 'Clip', @VideoState, @EncodeProgress, @Length)
                """, new
            {
                ClipId = clipId, OwnerId = Guid.NewGuid(), GameCategoryId = Guid.NewGuid(), VideoState = videoState,
                EncodeProgress = encodeProgress, Length = length
            });
            return clipId;
        }

        public Task<string> StateAsync(Guid clipId) =>
            Connection.QuerySingleAsync<string>("SELECT video_state FROM clip WHERE id = @clipId", new { clipId });

        public Task<(string, int?)> ProgressAsync(Guid clipId) =>
            Connection.QuerySingleAsync<(string, int?)>(
                "SELECT video_state, encode_progress FROM clip WHERE id = @clipId", new { clipId });

        public Task<(string, int?, int?, string?)> RowAsync(Guid clipId) =>
            Connection.QuerySingleAsync<(string, int?, int?, string?)>(
                "SELECT video_state, encode_progress, length, title FROM clip WHERE id = @clipId", new { clipId });

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await _dataSource.DisposeAsync();
            await using NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
        }
    }
}
