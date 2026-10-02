using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Email;
using Reelshelf.Users;
using Xunit;

namespace Reelshelf.Test.Users;

public class AccountDeletionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (AccountDeletionService Service, FakeStore Store, FakeVideoHost Host, FakeSender Sender, AccountDeletionSignal Signal) Build(
        string? email = "harry@example.com")
    {
        FakeStore store = new() { Email = email };
        FakeVideoHost host = new();
        FakeSender sender = new();
        AccountDeletionSignal signal = new();
        AccountDeletionService service = new(store, host, sender, signal, NullLogger<AccountDeletionService>.Instance);
        return (service, store, host, sender, signal);
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
        OwnedClipVideo late = new(Guid.NewGuid(), Guid.NewGuid());
        store.OnClipsDeleted = () => store.Clips.Add(late);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.False(store.AccountDeleted);

        store.OnClipsDeleted = null;
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Contains(late.VideoId, host.DeletedVideos);
        Assert.True(store.AccountDeleted);
    }

    private sealed class FakeStore : IAccountDeletionStore
    {
        public string? Email { get; init; }
        public bool Marked { get; private set; }
        public bool AccountDeleted { get; private set; }
        public List<OwnedClipVideo> Clips { get; } = [];
        public List<Guid> Collections { get; } = [];
        public Action? OnClipsDeleted { get; set; }

        public void AddClips(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Clips.Add(new OwnedClipVideo(Guid.NewGuid(), Guid.NewGuid()));
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

        public Task<List<Guid>> GetBunnyCollections(Guid userId) => Task.FromResult(Collections.ToList());

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

        public Task DeleteCollectionAsync(Guid collectionId)
        {
            if (FailingCollections.Contains(collectionId))
            {
                throw new HttpRequestException("Bunny is down");
            }

            DeletedCollections.Add(collectionId);
            return Task.CompletedTask;
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
