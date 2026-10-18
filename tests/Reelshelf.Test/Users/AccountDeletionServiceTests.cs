using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Email;
using Reelshelf.Games;
using Reelshelf.Users;
using Reelshelf.Video;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Users;

public class AccountDeletionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (AccountDeletionService Service, FakeStore Store, FakeVideoHost Host, FakeSender Sender, AccountDeletionSignal Signal) Build(
        string? email = "harry@example.com")
    {
        var (service, store, host, _, sender, signal, _) = BuildWithTwoHosts(email);
        return (service, store, host, sender, signal);
    }

    /// <summary>Bunny (the default for clips and collections the tests add) and a second host without containers.</summary>
    private static (AccountDeletionService Service, FakeStore Store, FakeVideoHost Bunny, FakeVideoHost Other, FakeSender Sender, AccountDeletionSignal Signal, FakeTranscodeStorage Storage) BuildWithTwoHosts(
        string? email = "harry@example.com")
    {
        FakeStore store = new() { Email = email };
        FakeVideoHost bunny = new();
        FakeVideoHost other = new();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.Bunny, bunny);
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, other);
        ClipVideoHosts hosts = new(services.BuildServiceProvider());
        FakeSender sender = new();
        AccountDeletionSignal signal = new();
        FakeTranscodeStorage storage = new();
        AccountDeletionService service = new(store, hosts, storage, sender, signal, NullLogger<AccountDeletionService>.Instance);
        return (service, store, bunny, other, sender, signal, storage);
    }

    [Theory]
    [InlineData("delete my account", true)]
    [InlineData("  Delete My Account ", true)]
    [InlineData("delete", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsConfirmed_RequiresThePhrase(string? confirmation, bool expected)
    {
        Assert.Equal(expected, AccountDeletionService.IsConfirmed(confirmation));
    }

    [Fact]
    public async Task Request_MarksTheAccountEmailsTheOwnerAndWakesTheWorker()
    {
        var (service, store, _, sender, signal) = Build();

        await service.Request(UserId);

        Assert.True(store.Marked);
        EmailMessage message = Assert.Single(sender.Sent);
        Assert.Equal("harry@example.com", message.To);
        Assert.Contains("being deleted", message.Subject);
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task Request_WhenAlreadyMarked_SendsNothing()
    {
        var (service, store, _, sender, _) = Build();
        await service.Request(UserId);

        await service.Request(UserId);

        Assert.Single(sender.Sent);
        Assert.True(store.Marked);
    }

    [Fact]
    public async Task Request_WithoutAnEmail_StillMarks()
    {
        var (service, store, _, sender, _) = Build(email: null);

        await service.Request(UserId);

        Assert.True(store.Marked);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Process_DeletesEveryVideoAndCollectionThenTheAccount()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(5);
        store.Collections.AddRange([Guid.NewGuid(), Guid.NewGuid()]);
        await service.Request(UserId);

        bool deleted = await service.Process(UserId, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(store.Clips);
        Assert.True(store.AccountDeleted);
        Assert.Equal(5, host.DeletedVideos.Count);
        Assert.Equal(store.Collections.Order(), host.DeletedCollections.Order());
    }

    [Fact]
    public async Task Process_WhenAVideoDeleteFails_KeepsThatClipAndTheAccountForTheNextPass()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(3);
        store.Collections.Add(Guid.NewGuid());
        OwnedClipVideo failing = store.Clips[1];
        host.FailingVideos.Add(failing.VideoId);
        await service.Request(UserId);

        bool deleted = await service.Process(UserId, CancellationToken.None);

        Assert.False(deleted);
        Assert.Equal(failing, Assert.Single(store.Clips));
        Assert.False(store.AccountDeleted);
        Assert.Empty(host.DeletedCollections);

        host.FailingVideos.Clear();
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Empty(store.Clips);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenACollectionDeleteFails_KeepsTheAccount()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(1);
        Guid collection = Guid.NewGuid();
        store.Collections.Add(collection);
        host.FailingCollections.Add(collection);
        await service.Request(UserId);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.Empty(store.Clips);
        Assert.False(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenAClipAppearsMidPass_LeavesTheAccountForTheNextPass()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(2);
        await service.Request(UserId);

        // An upload already in flight reserves a clip after the clip list was read.
        OwnedClipVideo late = new(Guid.NewGuid(), Guid.NewGuid(), VideoHostNames.Bunny);
        store.OnClipsDeleted = () => store.Clips.Add(late);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.False(store.AccountDeleted);

        store.OnClipsDeleted = null;
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Contains(late.VideoId, host.DeletedVideos);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_DeletesEachVideoAndContainerAtItsOwnHost()
    {
        var (service, store, bunny, other, _, _, storage) = BuildWithTwoHosts();
        store.AddClips(2);
        store.AddClips(3, VideoHostNames.R2);
        Guid bunnyCollection = Guid.NewGuid();
        store.Collections.Add(bunnyCollection);
        Guid otherContainer = Guid.NewGuid();
        store.OtherContainers.Add(new OwnedVideoContainer(VideoHostNames.R2, otherContainer));
        List<Guid> bunnyVideos = store.Clips.Where(clip => clip.VideoHost == VideoHostNames.Bunny).Select(clip => clip.VideoId).ToList();
        List<Guid> bunnyClips = store.Clips.Where(clip => clip.VideoHost == VideoHostNames.Bunny).Select(clip => clip.ClipId).ToList();
        List<Guid> otherVideos = store.Clips.Where(clip => clip.VideoHost == VideoHostNames.R2).Select(clip => clip.VideoId).ToList();
        await service.Request(UserId);

        Assert.True(await service.Process(UserId, CancellationToken.None));

        Assert.Equal(bunnyVideos.Order(), bunny.DeletedVideos.Order());
        Assert.Equal(otherVideos.Order(), other.DeletedVideos.Order());
        Assert.Equal([bunnyCollection], bunny.DeletedCollections);
        Assert.Equal([otherContainer], other.DeletedCollections);
        Assert.True(store.AccountDeleted);
        // A Bunny clip's R2 copy, which a migrate job may have written, goes too; an R2 clip's host deletes its own.
        Assert.Equal(bunnyClips.Select(clipId => (clipId, 1)).Order(), storage.DeletedVersions.Order());
    }

    [Fact]
    public async Task Process_DeletesAMigratedClipsPendingBunnyCopy_BeforeTheCollections()
    {
        var (service, store, bunny, r2, _, _, _) = BuildWithTwoHosts();
        Guid clipId = Guid.NewGuid();
        Guid bunnyCopy = Guid.NewGuid();
        // Moved to R2 within the grace period: its old Bunny video still waits for the sweep.
        store.Clips.Add(new OwnedClipVideo(clipId, clipId, VideoHostNames.R2, bunnyCopy));
        Guid collection = Guid.NewGuid();
        store.Collections.Add(collection);
        await service.Request(UserId);

        Assert.True(await service.Process(UserId, CancellationToken.None));

        Assert.Equal([clipId], r2.DeletedVideos);
        Assert.Equal([bunnyCopy], bunny.DeletedVideos);
        Assert.Equal([collection], bunny.DeletedCollections);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenThePendingBunnyCopyFailsToDelete_KeepsTheClipForTheNextPass()
    {
        var (service, store, bunny, _, _, _, _) = BuildWithTwoHosts();
        Guid clipId = Guid.NewGuid();
        Guid bunnyCopy = Guid.NewGuid();
        OwnedClipVideo clip = new(clipId, clipId, VideoHostNames.R2, bunnyCopy);
        store.Clips.Add(clip);
        bunny.FailingVideos.Add(bunnyCopy);
        await service.Request(UserId);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.Equal(clip, Assert.Single(store.Clips));

        bunny.FailingVideos.Clear();
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Equal([bunnyCopy], bunny.DeletedVideos);
    }

    private sealed class FakeTranscodeStorage : ITranscodeStorage
    {
        private readonly Lock _lock = new();
        public List<(Guid ClipId, int Version)> DeletedVersions { get; } = [];

        public TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion, TimeSpan lifetime) =>
            TranscodeJobUrls.None;

        public Task DeleteSourceAsync(Guid clipId) => Task.CompletedTask;

        public Task DeleteVersionAsync(Guid clipId, int videoVersion)
        {
            lock (_lock)
            {
                DeletedVersions.Add((clipId, videoVersion));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IAccountDeletionStore
    {
        public string? Email { get; init; }
        public bool Marked { get; private set; }
        public bool AccountDeleted { get; private set; }
        public List<OwnedClipVideo> Clips { get; } = [];
        public List<Guid> Collections { get; } = [];
        public List<OwnedVideoContainer> OtherContainers { get; } = [];
        public Action? OnClipsDeleted { get; set; }

        public void AddClips(int count, string videoHost = VideoHostNames.Bunny)
        {
            for (int i = 0; i < count; i++)
            {
                Clips.Add(new OwnedClipVideo(Guid.NewGuid(), Guid.NewGuid(), videoHost));
            }
        }

        public Task<AccountDeletionRequest?> MarkForDeletion(Guid userId)
        {
            if (Marked)
            {
                return Task.FromResult<AccountDeletionRequest?>(null);
            }

            Marked = true;
            return Task.FromResult<AccountDeletionRequest?>(new AccountDeletionRequest(Email));
        }

        public Task<List<Guid>> GetAccountsPendingDeletion()
        {
            return Task.FromResult(Marked && !AccountDeleted ? new List<Guid> { UserId } : []);
        }

        public Task<List<OwnedClipVideo>> GetClipVideos(Guid userId) => Task.FromResult(Clips.ToList());

        public Task DeleteClips(Guid userId, IReadOnlyCollection<Guid> clipIds)
        {
            Clips.RemoveAll(clip => clipIds.Contains(clip.ClipId));
            OnClipsDeleted?.Invoke();
            return Task.CompletedTask;
        }

        public Task<List<OwnedVideoContainer>> GetVideoContainers(Guid userId) => Task.FromResult(
            Collections.Select(id => new OwnedVideoContainer(VideoHostNames.Bunny, id)).Concat(OtherContainers).ToList());

        public Task<bool> DeleteAccountIfEmpty(Guid userId)
        {
            if (!Marked || Clips.Count > 0)
            {
                return Task.FromResult(false);
            }

            AccountDeleted = true;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeVideoHost : IClipVideoHost
    {
        private readonly Lock _lock = new();
        public List<Guid> DeletedVideos { get; } = [];
        public List<Guid> DeletedCollections { get; } = [];
        public HashSet<Guid> FailingVideos { get; } = [];
        public HashSet<Guid> FailingCollections { get; } = [];

        public Task DeleteVideoAsync(Guid videoId)
        {
            if (FailingVideos.Contains(videoId))
            {
                throw new HttpRequestException("Bunny is down");
            }

            lock (_lock)
            {
                DeletedVideos.Add(videoId);
            }

            return Task.CompletedTask;
        }

        public Task DeleteContainerAsync(Guid containerId)
        {
            if (FailingCollections.Contains(containerId))
            {
                throw new HttpRequestException("Bunny is down");
            }

            DeletedCollections.Add(containerId);
            return Task.CompletedTask;
        }

        public Task<HostedVideo> CreateVideoAsync(Guid clipId, Guid ownerId, GameCategory gameCategory, string title)
        {
            throw new NotSupportedException();
        }

        public Task<UploadTarget> CreateUploadTargetAsync(HostedVideo video, string title, long fileSize)
        {
            throw new NotSupportedException();
        }

        public Task UploadAsync(Guid videoId, Stream content, long? contentLength, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<HostedVideoInfo?> GetVideoAsync(Guid videoId)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
