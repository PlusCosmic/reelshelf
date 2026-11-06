using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Platform;
using Reelshelf.Uploader.Core.Settings;
using Reelshelf.Uploader.Core.Uploads;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>A clock tests move by hand. Waiting moves it on at once, so loops run without real time passing.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 6, 20, 47, 0, TimeSpan.Zero);

    public List<TimeSpan> Delays { get; } = [];

    /// <summary>Called after each wait, so a test can change the world while the code under test waits.</summary>
    public Action<TimeSpan>? OnDelay { get; set; }

    public void Advance(TimeSpan by) => UtcNow += by;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        UtcNow += delay;
        OnDelay?.Invoke(delay);
        return Task.CompletedTask;
    }
}

internal sealed class MemoryCredentials(string? token = "rsd_test") : ICredentialStore
{
    public string? Token { get; private set; } = token;

    public string? ReadToken() => Token;

    public void WriteToken(string value) => Token = value;

    public void DeleteToken() => Token = null;
}

internal sealed class FakeGame : IGameActivity
{
    public bool IsGameRunning { get; set; }
}

internal sealed class RecordingNotifier : INotifier
{
    public List<string> Sent { get; } = [];

    public void ClipsShelved(IReadOnlyList<ShelvedBook> books) =>
        Sent.Add($"N1 {string.Join(", ", books.Select(book => $"{book.BookName}:{book.Clips}"))}");

    public void NeedsBook(string folder, int waiting) => Sent.Add($"N2 {folder}:{waiting}");

    public void LibraryFull(LibraryFullReason reason) => Sent.Add($"N3 {reason.ClipLimit}:{reason.MonthlyLimit}");

    public void UpdateRequired() => Sent.Add("N4");

    public void SignedOut() => Sent.Add("N5");
}

/// <summary>A clip prepared on the fake API, with its open upload.</summary>
internal sealed class FakeClip
{
    public required Guid Id { get; init; }
    public required string UploadId { get; init; }
    public required long Size { get; init; }
    public required int PartCount { get; init; }
    public Dictionary<int, (string ETag, long Size)> Parts { get; } = [];
    public bool Completed { get; set; }
    public bool Deleted { get; set; }
    public bool UploadGone { get; set; }
    public required Guid CategoryId { get; init; }
    public required string Title { get; init; }
    public string? Md5 { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// The Reelshelf API and R2 in one handler: books, the plan, preparing clips, part URLs, part PUTs, the part list and
/// completion, with hooks for the answers tests need (409, 403, 422, 503, other protocols, failing parts).
/// </summary>
internal sealed class FakeReelshelf : HttpMessageHandler
{
    public static readonly Uri ApiBase = new("https://api.reelshelf.test");
    public const string StorageHost = "r2.reelshelf.test";

    public List<Book> Books { get; } = [];

    public int? MaxClipSeconds { get; set; } = 60;
    public int? ClipLimit { get; set; } = 100;
    public int ClipCount { get; set; }
    public int? NewClipLimit { get; set; } = 200;
    public int NewClips { get; set; }

    /// <summary>The part size the API gives each upload.</summary>
    public long PartSize { get; set; } = 1024;

    public string Protocol { get; set; } = "s3-multipart";

    public Dictionary<Guid, FakeClip> Clips { get; } = [];

    public List<string> Requests { get; } = [];

    /// <summary>Answers for preparing, in turn; once used up, clips are prepared normally.</summary>
    public Queue<(HttpStatusCode Status, string? Title, string? Detail)> PrepareAnswers { get; } = [];

    /// <summary>Answers for completing, in turn; once used up, completing succeeds.</summary>
    public Queue<(HttpStatusCode Status, string? Detail)> CompleteAnswers { get; } = [];

    /// <summary>Answers for part PUTs, in turn (null throws a network error); once used up, parts go in.</summary>
    public Queue<HttpStatusCode?> PutAnswers { get; } = [];

    /// <summary>Every API call fails with a network error while set.</summary>
    public bool ApiDown { get; set; }

    /// <summary>Called on every part PUT that goes in, with its number.</summary>
    public Action<int>? OnPart { get; set; }

    public int Puts { get; private set; }

    public string? LastAuthorization { get; private set; }
    public string? LastClient { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        Requests.Add($"{request.Method} {path}");
        if (request.RequestUri.Host == StorageHost)
        {
            return await PutPartAsync(request, cancellationToken);
        }

        LastAuthorization = request.Headers.Authorization?.ToString();
        LastClient = request.Headers.TryGetValues(ReelshelfClientHandler.ClientHeader, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
        if (ApiDown)
        {
            throw new HttpRequestException("The network is down");
        }

        string[] segments = path.Trim('/').Split('/');
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        JsonNode? body = request.Content is null
            ? null
            : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken) is { Length: > 0 } text ? text : "null");

        return (request.Method.Method, segments) switch
        {
            ("GET", ["api", "games", "categories"]) => Json(Books.Select(book => new
            {
                id = book.Id, name = book.Name, slug = book.Slug, is_custom = false, cloth_color = book.ClothColor
            })),
            ("GET", ["api", "me", "plan"]) => Json(new
            {
                tier = "free", clip_count = ClipCount, clip_limit = ClipLimit, new_clips_last30_days = NewClips,
                new_clip_limit = NewClipLimit, max_clip_seconds = MaxClipSeconds, storage_used_bytes = 0,
                has_transcripts = false, billing_enabled = false, can_manage_billing = false, clips_over_limit = 0,
                can_change_plan = false
            }),
            ("POST", ["api", "clips", "categories", var category, "videos"]) => Prepare(Guid.Parse(category), query),
            ("DELETE", ["api", "clips", "videos", var clip]) => Delete(Guid.Parse(clip)),
            ("POST", ["api", "clips", "videos", var clip, "upload", "parts"]) => Presign(Guid.Parse(clip), body!),
            ("GET", ["api", "clips", "videos", var clip, "upload", "parts"]) => ListParts(Guid.Parse(clip)),
            ("POST", ["api", "clips", "videos", var clip, "upload", "complete"]) => Complete(Guid.Parse(clip), body!),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private HttpResponseMessage Prepare(Guid categoryId, System.Collections.Specialized.NameValueCollection query)
    {
        if (PrepareAnswers.TryDequeue(out (HttpStatusCode Status, string? Title, string? Detail) answer))
        {
            return Problem(answer.Status, answer.Title, answer.Detail);
        }

        if (Books.All(book => book.Id != categoryId))
        {
            return Problem(HttpStatusCode.NotFound, "Resource Not Found", "No such book");
        }

        long size = long.Parse(query["fileSize"]!);
        Guid id = Guid.NewGuid();
        int partCount = (int)Math.Max(1, (size + PartSize - 1) / PartSize);
        Clips[id] = new FakeClip
        {
            Id = id, UploadId = $"upload-{Clips.Count + 1}", Size = size, PartCount = partCount,
            CategoryId = categoryId, Title = query["videoTitle"]!, Md5 = query["md5Hash"],
            CreatedAt = DateTimeOffset.TryParse(query["createdAt"], out DateTimeOffset createdAt) ? createdAt : null
        };
        return Json(new
        {
            clip_id = id,
            video_id = id,
            upload = new
            {
                protocol = Protocol,
                endpoint = $"/api/clips/videos/{id}/upload",
                headers = new { },
                metadata = new { },
                expires_at = DateTimeOffset.UtcNow.AddDays(1),
                multipart = new { upload_id = Clips[id].UploadId, part_size = PartSize, part_count = partCount }
            }
        });
    }

    private HttpResponseMessage Delete(Guid clipId)
    {
        if (Clips.TryGetValue(clipId, out FakeClip? clip))
        {
            clip.Deleted = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Presign(Guid clipId, JsonNode body)
    {
        if (!Clips.TryGetValue(clipId, out FakeClip? clip) || clip.Deleted || clip.UploadGone)
        {
            return Problem(HttpStatusCode.NotFound, "Resource Not Found", "This upload is no longer open");
        }

        return Json(new
        {
            expires_at = DateTimeOffset.UtcNow.AddYears(5),
            parts = body["part_numbers"]!.AsArray().Select(number => (int)number!).Select(number => new
            {
                part_number = number,
                url = $"https://{StorageHost}/{clipId}/{number}"
            })
        });
    }

    private HttpResponseMessage ListParts(Guid clipId)
    {
        if (!Clips.TryGetValue(clipId, out FakeClip? clip) || clip.Deleted || clip.UploadGone)
        {
            return Problem(HttpStatusCode.NotFound, "Resource Not Found", "This upload is no longer open");
        }

        return Json(new
        {
            parts = clip.Parts.OrderBy(part => part.Key).Select(part => new
            {
                part_number = part.Key, etag = part.Value.ETag, size = part.Value.Size
            })
        });
    }

    private HttpResponseMessage Complete(Guid clipId, JsonNode body)
    {
        if (CompleteAnswers.TryDequeue(out (HttpStatusCode Status, string? Detail) answer))
        {
            return Problem(answer.Status, null, answer.Detail);
        }

        if (!Clips.TryGetValue(clipId, out FakeClip? clip) || clip.Deleted || clip.UploadGone)
        {
            return Problem(HttpStatusCode.NotFound, "Resource Not Found", "This upload is no longer open");
        }

        JsonArray parts = body["parts"]!.AsArray();
        bool everyPart = parts.Count == clip.PartCount && parts.All(part =>
            clip.Parts.TryGetValue((int)part!["part_number"]!, out (string ETag, long Size) held) &&
            held.ETag == (string)part["etag"]!);
        if (!everyPart)
        {
            return Problem(HttpStatusCode.BadRequest, "Bad Request", "The parts don't match the upload");
        }

        clip.Completed = true;
        return Json(new { clip_id = clipId, status = "uploaded" });
    }

    private async Task<HttpResponseMessage> PutPartAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Puts++;
        if (request.Headers.Authorization is not null)
        {
            throw new InvalidOperationException("A part PUT carried the device token");
        }

        string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        Guid clipId = Guid.Parse(segments[0]);
        int number = int.Parse(segments[1]);
        byte[] bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        if (PutAnswers.TryDequeue(out HttpStatusCode? answer))
        {
            if (answer is null)
            {
                throw new HttpRequestException("The connection was reset");
            }

            return new HttpResponseMessage(answer.Value);
        }

        FakeClip clip = Clips[clipId];
        string etag = $"\"etag-{number}-{bytes.Length}\"";
        clip.Parts[number] = (etag, bytes.Length);
        OnPart?.Invoke(number);
        HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Problem(HttpStatusCode status, string? title, string? detail) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { status = (int)status, title, detail }), Encoding.UTF8,
            "application/problem+json")
    };
}

/// <summary>A recordings folder, a ledger and settings in a temporary directory, with the fake API and storage.</summary>
internal sealed class TestUploader : IDisposable
{
    public TestUploader()
    {
        Directory.CreateDirectory(Recordings);
        Ledger = new UploadLedger(Path.Combine(Home, "ledger.db"));
        Handler = new ReelshelfClientHandler(() => Credentials.ReadToken(), "1.0.3", Server);
        ReelshelfApi = new ReelshelfApi(FakeReelshelf.ApiBase, Handler);
        Api = new UploaderApi(ReelshelfApi);
        Storage = new HttpClient(Server, disposeHandler: false);
    }

    public string Home { get; } = Path.Combine(Path.GetTempPath(), $"reelshelf-uploader-test-{Guid.NewGuid():N}");

    public string Recordings => Path.Combine(Home, "Videos", "NVIDIA");

    public FakeClock Clock { get; } = new();

    public FakeReelshelf Server { get; } = new();

    public MemoryCredentials Credentials { get; } = new();

    public FakeGame Game { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    public UploadLedger Ledger { get; }

    public ReelshelfClientHandler Handler { get; }

    public ReelshelfApi ReelshelfApi { get; }

    public UploaderApi Api { get; }

    public HttpClient Storage { get; }

    public SettingsStore SettingsStore => new(Path.Combine(Home, "settings.json"));

    public Book AddBook(string name)
    {
        Book book = new(Guid.NewGuid(), name, name.ToLowerInvariant().Replace(' ', '-'));
        Server.Books.Add(book);
        return book;
    }

    /// <summary>Writes a file below the recordings folder and gives it a last-write time in the past.</summary>
    public string WriteFile(string relativePath, int size, DateTimeOffset? lastWrite = null)
    {
        string path = Path.Combine(Recordings, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = new byte[size];
        new Random(relativePath.GetHashCode()).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, (lastWrite ?? Clock.UtcNow.AddMinutes(-1)).UtcDateTime);
        return path;
    }

    public UploaderEngine Engine(Action<UploaderSettings>? configure = null)
    {
        UploaderSettings settings = SettingsStore.Load();
        configure?.Invoke(settings);
        SettingsStore.Save(settings);
        return new UploaderEngine(Api, Storage, Ledger, SettingsStore, Credentials, Game, Notifier, Clock,
            canOpen: _ => true);
    }

    public UploadPipeline Pipeline(IUploadGate? gate = null, TokenBucket? bucket = null) =>
        new(Api, Ledger, new MultipartUploader(Api, Storage, Ledger, Clock, bucket ?? new TokenBucket(Clock),
            gate ?? OpenGate.Instance), Clock);

    /// <summary>A settled file as the monitor adds it: waiting on a book.</summary>
    public LedgerEntry Waiting(string relativePath, Guid bookId, int size = 2500)
    {
        string path = WriteFile(relativePath, size);
        FileInfo info = new(path);
        LedgerEntry entry = new()
        {
            Root = Path.GetFullPath(Recordings),
            Path = relativePath,
            Size = info.Length,
            LastWriteUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            State = LedgerState.Waiting,
            CategoryId = bookId,
            CreatedAt = Clock.UtcNow,
            UpdatedAt = Clock.UtcNow
        };
        Ledger.Save(entry);
        return entry;
    }

    public string FullPath(LedgerEntry entry) => Path.Combine(Recordings, entry.Path.Replace('/', Path.DirectorySeparatorChar));

    public static readonly PlanLimits Plan = new(60, 100, 200, 0, 0);

    public void Dispose()
    {
        Storage.Dispose();
        ReelshelfApi.Dispose();
        Ledger.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
