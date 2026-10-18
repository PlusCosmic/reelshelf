using System.Web;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Exceptions;
using Reelshelf.Games;
using Reelshelf.Video;
using Reelshelf.Video.R2;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// Browser uploads to R2: the API starts a multipart upload, presigns parts, lists them for resuming and completes
/// it, checking the file before its transcode is queued. R2 is <see cref="FakeR2Client"/> and ffprobe a stand-in,
/// so the <see cref="PostgresFactAttribute"/> tests check what reaches the clip row and the queue.
/// </summary>
public class R2MultipartUploadTests
{
    private const long MiB = 1024 * 1024;

    // 20 MiB is three parts: 8, 8 and 4 MiB.
    private const long FileSize = 20 * MiB;

    private static readonly UploadProbeResult Video = new(HasVideo: true, DurationSeconds: 31.5, Width: 1920, Height: 1080);

    [Theory]
    [InlineData(1, 8 * MiB, 1)]
    [InlineData(8 * MiB, 8 * MiB, 1)]
    [InlineData(8 * MiB + 1, 8 * MiB, 2)]
    [InlineData(20 * MiB, 8 * MiB, 3)]
    // The largest file that fits 10,000 parts of 8 MiB, and one byte more.
    [InlineData(10_000 * 8 * MiB, 8 * MiB, 10_000)]
    [InlineData(10_000 * 8 * MiB + 1, 9 * MiB, 8_889)]
    public void Parts_AreSizedSoTheFileFitsInTenThousand(long fileSize, long partSize, int partCount)
    {
        Assert.Equal(partSize, MultipartUploadParts.PartSizeFor(fileSize));
        Assert.Equal(partCount, MultipartUploadParts.PartCountFor(fileSize));
    }

    [Fact]
    public void TheLargestDeclarableFile_FitsInTheMostParts()
    {
        long largest = Reelshelf.Storage.StorageQuota.MaxDeclaredFileSizeBytes;

        Assert.True(MultipartUploadParts.PartCountFor(largest) <= MultipartUploadParts.MaxParts);
        Assert.True(MultipartUploadParts.PartSizeFor(largest) % MiB == 0);
        Assert.True(MultipartUploadParts.PartSizeFor(largest) < 5L * 1024 * MiB);
    }

    [Fact]
    public void PresignedPart_IsAPutForThatPartOfThatUpload()
    {
        using R2Storage storage = new FakeR2Client().Storage();
        Guid clipId = Guid.NewGuid();

        Uri url = new(storage.PresignUploadPart(R2ObjectKeys.Upload(clipId), "upload-1", 7, TimeSpan.FromHours(1)));

        Assert.Equal($"/bucket/uploads/{clipId}", url.AbsolutePath);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("upload-1", query["uploadId"]);
        Assert.Equal("7", query["partNumber"]);
        Assert.Equal("3600", query["X-Amz-Expires"]);
        Assert.Equal("host", query["X-Amz-SignedHeaders"]);
    }

    [Theory]
    [InlineData(false, 30, 1920, 1080, "The file has no video in it.")]
    [InlineData(true, 30, 0, 0, "The video has no picture size.")]
    [InlineData(true, 0, 1920, 1080, "The video has no length.")]
    [InlineData(true, double.NaN, 1920, 1080, "The video has no length.")]
    [InlineData(true, 30, 1920, 1080, null)]
    // HDR, variable frame rate, odd sizes and long clips are the worker's to handle, not reasons to refuse.
    [InlineData(true, 7200, 3840, 2160, null)]
    public void Validation_RefusesOnlyClearlyUnusableFiles(bool hasVideo, double duration, int width, int height,
        string? problem)
    {
        Assert.Equal(problem, UploadValidation.Problem(new UploadProbeResult(hasVideo, duration, width, height)));
    }

    [Fact]
    public void Validation_RefusesAnUploadOfAnotherSize()
    {
        Assert.Null(UploadValidation.SizeProblem(FileSize, FileSize));
        Assert.NotNull(UploadValidation.SizeProblem(FileSize, FileSize + 1));
        Assert.NotNull(UploadValidation.SizeProblem(FileSize, FileSize - 1));
    }

    [Fact]
    public async Task Probe_ReadsARealVideo_AndRefusesAFileThatIsNot()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"upload-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string video = Path.Combine(directory, "clip.mp4");
            await RunFfmpegAsync($"-f lavfi -i testsrc=size=320x240:rate=30 -t 2 -pix_fmt yuv420p \"{video}\"");
            string text = Path.Combine(directory, "notes.mp4");
            await File.WriteAllTextAsync(text, "not a video");

            FfprobeUploadProbe probe = new();
            UploadProbeResult result = await probe.ProbeAsync(new Uri(video), CancellationToken.None);

            Assert.True(result.HasVideo);
            Assert.Equal((320, 240), (result.Width, result.Height));
            Assert.InRange(result.DurationSeconds, 1.9, 2.1);
            await Assert.ThrowsAsync<UnreadableUploadException>(() =>
                probe.ProbeAsync(new Uri(text), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [PostgresFact]
    public async Task UploadTarget_StartsAMultipartUploadSizedForTheFile()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        Guid clipId = Guid.NewGuid();

        UploadTarget target = await test.Host.CreateUploadTargetAsync(
            new HostedVideo(clipId, Guid.Empty, null!), "Clip", FileSize);

        Assert.Equal(UploadTarget.S3Multipart, target.Protocol);
        Assert.Equal($"/api/clips/videos/{clipId}/upload", target.Endpoint);
        Assert.NotNull(target.Multipart);
        Assert.Equal((8 * MiB, 3), (target.Multipart.PartSize, target.Multipart.PartCount));
        Assert.Equal(R2ObjectKeys.Upload(clipId), test.R2.Uploads[target.Multipart.UploadId].Key);
    }

    [PostgresFact]
    public async Task Complete_QueuesOneTranscode_AndRepeatingItIsAccepted()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB, 8 * MiB, 4 * MiB);

        CompletedUpload completed = await test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None);
        CompletedUpload again = await test.Host.CompleteUploadAsync(await test.ClipAsync(clip.Id), uploadId, Parts(3),
            CancellationToken.None);
        // A completion repeated before the clip row moved on finds the joined object rather than the upload.
        CompletedUpload raced = await test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None);

        Assert.Equal(ClipVideoStatus.Uploaded, completed.Status);
        Assert.Equal(ClipVideoStatus.Uploaded, again.Status);
        Assert.Equal(ClipVideoStatus.Uploaded, raced.Status);
        Assert.Equal("uploaded", (await test.ClipAsync(clip.Id)).VideoState);
        Assert.Equal([("transcode", "pending", $"uploads/{clip.Id}")], await test.RunsAsync(clip.Id));
        Assert.Equal(FileSize, test.R2.Objects[R2ObjectKeys.Upload(clip.Id)]);
        // The probe read the joined object through a presigned GET: once for the first completion and once for the
        // raced one, which queued nothing more. The second, which saw the clip uploaded, didn't probe.
        Assert.Equal(2, test.Probe.Probed.Count);
        Assert.Equal($"/bucket/uploads/{clip.Id}", test.Probe.Probed[0].AbsolutePath);
    }

    [PostgresFact]
    public async Task Complete_RefusesAFileThatIsNotAVideo()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB, 8 * MiB, 4 * MiB);
        test.Probe.Next = Video with { HasVideo = false };

        UnprocessableContentException refused = await Assert.ThrowsAsync<UnprocessableContentException>(() =>
            test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None));

        Assert.Equal("The file has no video in it.", refused.Message);
        Assert.Equal("upload_failed", (await test.ClipAsync(clip.Id)).VideoState);
        Assert.Empty(await test.RunsAsync(clip.Id));
        Assert.Empty(test.R2.Objects);
    }

    [PostgresFact]
    public async Task Complete_RefusesAnUnreadableFile()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB, 8 * MiB, 4 * MiB);
        test.Probe.Throw = new UnreadableUploadException("moov atom not found");

        await Assert.ThrowsAsync<UnprocessableContentException>(() =>
            test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None));

        Assert.Equal("upload_failed", (await test.ClipAsync(clip.Id)).VideoState);
        Assert.Empty(await test.RunsAsync(clip.Id));
    }

    [PostgresFact]
    public async Task Complete_RefusesAFileLargerThanTheReservation_WithoutProbingIt()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        // The last part is a byte bigger than the declared size allows, which would get past the quota.
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB, 8 * MiB, 4 * MiB + 1);

        await Assert.ThrowsAsync<UnprocessableContentException>(() =>
            test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None));

        Assert.Equal("upload_failed", (await test.ClipAsync(clip.Id)).VideoState);
        Assert.Empty(test.Probe.Probed);
        Assert.Empty(test.R2.Objects);
    }

    [PostgresFact]
    public async Task Complete_WhenTheProbeCantFinish_LeavesTheClipWaiting_AndCanBeRetried()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB, 8 * MiB, 4 * MiB);
        test.Probe.Throw = new TimeoutException("slow");

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None));
        Assert.Equal("awaiting_upload", (await test.ClipAsync(clip.Id)).VideoState);

        test.Probe.Throw = null;
        CompletedUpload completed = await test.Host.CompleteUploadAsync(await test.ClipAsync(clip.Id), uploadId,
            Parts(3), CancellationToken.None);

        Assert.Equal(ClipVideoStatus.Uploaded, completed.Status);
        Assert.Single(await test.RunsAsync(clip.Id));
    }

    [PostgresFact]
    public async Task Complete_NeedsEveryPartWithItsETag()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB, 8 * MiB);

        // Part 3 was never uploaded: naming only two parts is refused before R2 is asked, and naming the third makes
        // R2 refuse it. Neither fails the clip, which can still be finished.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            test.Host.CompleteUploadAsync(clip, uploadId, Parts(2), CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            test.Host.CompleteUploadAsync(clip, uploadId, Parts(3), CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(() => test.Host.CompleteUploadAsync(clip, uploadId,
            [new(1, "\"etag-1\""), new(1, "\"etag-1\""), new(3, "\"etag-3\"")], CancellationToken.None));

        Assert.Equal("awaiting_upload", (await test.ClipAsync(clip.Id)).VideoState);
        Assert.Equal(2, test.R2.Uploads[uploadId].Parts.Count);
    }

    [PostgresFact]
    public async Task Complete_OfAnUploadThatIsGone_IsNotFound()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        ClipsStatements.ClipRow clip = await test.AddClipAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            test.Host.CompleteUploadAsync(clip, "upload-gone", Parts(3), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            test.Host.ListPartsAsync(clip, "upload-gone", CancellationToken.None));
    }

    [PostgresFact]
    public async Task ListParts_ReportsWhatIsUploaded_ForResuming()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB);
        test.R2.PutPart(uploadId, 3, 4 * MiB);

        List<UploadedPart> parts = await test.Host.ListPartsAsync(clip, uploadId, CancellationToken.None);

        Assert.Equal([new UploadedPart(1, "\"etag-1\"", 8 * MiB), new UploadedPart(3, "\"etag-3\"", 4 * MiB)], parts);
    }

    [PostgresFact]
    public async Task PresignParts_OnlyForThePartsTheFileHas()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        ClipsStatements.ClipRow clip = await test.AddClipAsync();

        PresignedUploadParts presigned = test.Host.PresignParts(clip, "upload-1", [1, 2, 3]);

        Assert.Equal([1, 2, 3], presigned.Parts.Select(part => part.PartNumber));
        Assert.All(presigned.Parts, part => Assert.Contains("uploadId=upload-1", part.Url));
        Assert.Throws<BadRequestException>(() => test.Host.PresignParts(clip, "upload-1", [0]));
        Assert.Throws<BadRequestException>(() => test.Host.PresignParts(clip, "upload-1", [4]));
        Assert.Throws<BadRequestException>(() => test.Host.PresignParts(clip, "upload-1", [1, 1]));
        Assert.Throws<BadRequestException>(() => test.Host.PresignParts(clip, "upload-1", []));
        Assert.Throws<BadRequestException>(() => test.Host.PresignParts(clip, "", [1]));
        Assert.Throws<BadRequestException>(() =>
            test.Host.PresignParts(clip, "upload-1", Enumerable.Range(1, 101).ToList()));
    }

    [PostgresFact]
    public async Task PresignParts_RefusesAClipNoLongerWaiting()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        ClipsStatements.ClipRow clip = await test.AddClipAsync("upload_failed");

        await Assert.ThrowsAsync<ConflictException>(() =>
            test.Host.CompleteUploadAsync(clip, "upload-1", Parts(3), CancellationToken.None));
        Assert.Throws<ConflictException>(() => test.Host.PresignParts(clip, "upload-1", [1]));
        await Assert.ThrowsAsync<ConflictException>(() =>
            test.Host.ListPartsAsync(clip, "upload-1", CancellationToken.None));
    }

    [PostgresFact]
    public async Task Endpoints_FindOnlyTheOwnersR2Clip()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        ClipsStatements.ClipRow clip = await test.AddClipAsync();
        ClipsStatements.ClipRow bunnyClip = await test.AddClipAsync(videoHost: VideoHostNames.Bunny);
        ClipsStatements clips = new(test.DataSource.CreateConnection());
        ClipVideoHosts hosts = test.Hosts();

        (ClipsStatements.ClipRow found, IMultipartUploadHost host) =
            await MultipartUploadEndpoints.FindAsync(clips, hosts, clip.Id, clip.OwnerId);

        Assert.Equal(clip.Id, found.Id);
        Assert.IsType<R2VideoHost>(host);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            MultipartUploadEndpoints.FindAsync(clips, hosts, clip.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            MultipartUploadEndpoints.FindAsync(clips, hosts, bunnyClip.Id, bunnyClip.OwnerId));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            MultipartUploadEndpoints.FindAsync(clips, hosts, Guid.NewGuid(), clip.OwnerId));
    }

    [PostgresFact]
    public async Task Delete_AbortsTheOpenUpload()
    {
        await using TestUploads test = await TestUploads.CreateAsync();
        (ClipsStatements.ClipRow clip, string uploadId) = await test.AddUploadedPartsAsync(8 * MiB);

        await test.Host.DeleteVideoAsync(clip.Id);

        Assert.Equal([uploadId], test.R2.Aborted);
        Assert.Empty(test.R2.Uploads);
    }

    private static List<CompletedUploadPart> Parts(int count) =>
        Enumerable.Range(1, count).Select(number => new CompletedUploadPart(number, $"\"etag-{number}\"")).ToList();

    private static async Task RunFfmpegAsync(string arguments)
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y {arguments}")
            {
                RedirectStandardError = true
            })!;
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }

    private sealed class FakeProbe : IUploadProbe
    {
        public UploadProbeResult Next { get; set; } = Video;
        public Exception? Throw { get; set; }
        public List<Uri> Probed { get; } = [];

        public Task<UploadProbeResult> ProbeAsync(Uri source, CancellationToken cancellationToken)
        {
            Probed.Add(source);
            return Throw is null ? Task.FromResult(Next) : Task.FromException<UploadProbeResult>(Throw);
        }
    }

    private sealed class TestUploads : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly string _schema;
        private readonly R2Storage _storage;

        private TestUploads(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema)
        {
            DataSource = dataSource;
            _connection = connection;
            _schema = schema;
            _storage = R2.Storage();

            // One closed connection shared by the statements, as the request scope shares one in the API.
            NpgsqlConnection scoped = dataSource.CreateConnection();
            TranscodeStatements transcodeStatements = new(scoped);
            // Enqueueing touches only the statements.
            TranscodeQueue queue = new(transcodeStatements, null!, null!, null!,
                new FixedOptions<TranscodingOptions>(new TranscodingOptions()), NullLogger<TranscodeQueue>.Instance);
            Host = new R2VideoHost(_storage, new ClipsStatements(scoped), transcodeStatements, queue, Probe,
                TimeProvider.System, NullLogger<R2VideoHost>.Instance);
        }

        public NpgsqlDataSource DataSource { get; }
        public FakeR2Client R2 { get; } = new();
        public FakeProbe Probe { get; } = new();
        public R2VideoHost Host { get; }

        public static async Task<TestUploads> CreateAsync()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"r2_upload_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);
            return new TestUploads(dataSource, connection, schema);
        }

        /// <summary>The routing the endpoints use, with this R2 host and a Bunny that takes no multipart uploads.</summary>
        public ClipVideoHosts Hosts()
        {
            ServiceCollection services = new();
            services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, Host);
            services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.Bunny, new NoUploadsHost());
            return new ClipVideoHosts(services.BuildServiceProvider());
        }

        public async Task<ClipsStatements.ClipRow> AddClipAsync(string videoState = "awaiting_upload",
            string videoHost = VideoHostNames.R2)
        {
            Guid clipId = Guid.NewGuid();
            await _connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host, file_size)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, 'Clip', @VideoState, @VideoHost, @FileSize)
                """, new
            {
                ClipId = clipId, OwnerId = Guid.NewGuid(), GameCategoryId = Guid.NewGuid(), VideoState = videoState,
                VideoHost = videoHost, FileSize
            });
            return await ClipAsync(clipId);
        }

        /// <summary>A clip waiting for its upload, with an open upload holding parts of these sizes from 1 up.</summary>
        public async Task<(ClipsStatements.ClipRow Clip, string UploadId)> AddUploadedPartsAsync(params long[] sizes)
        {
            ClipsStatements.ClipRow clip = await AddClipAsync();
            string uploadId = R2.Open(R2ObjectKeys.Upload(clip.Id));
            for (int index = 0; index < sizes.Length; index++)
            {
                R2.PutPart(uploadId, index + 1, sizes[index]);
            }

            return (clip, uploadId);
        }

        public async Task<ClipsStatements.ClipRow> ClipAsync(Guid clipId) =>
            (await new ClipsStatements(DataSource.CreateConnection()).GetClipById(clipId))!;

        public async Task<List<(string Type, string Status, string? SourceKey)>> RunsAsync(Guid clipId) =>
            (await _connection.QueryAsync<(string, string, string?)>(
                "SELECT type, status, input->>'source_key' FROM transcode_run WHERE clip_id = @ClipId",
                new { ClipId = clipId })).ToList();

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            }
            finally
            {
                _storage.Dispose();
                await _connection.DisposeAsync();
                await DataSource.DisposeAsync();
            }
        }
    }

    private sealed class NoUploadsHost : IClipVideoHost
    {
        public Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title) =>
            throw new NotSupportedException();

        public Task<UploadTarget> CreateUploadTargetAsync(HostedVideo video, string title, long fileSize) =>
            throw new NotSupportedException();

        public Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<HostedVideoInfo?> GetVideoAsync(Guid videoId) => throw new NotSupportedException();

        public Task DeleteVideoAsync(Guid videoId) => throw new NotSupportedException();

        public Task DeleteContainerAsync(Guid containerId) => throw new NotSupportedException();
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
