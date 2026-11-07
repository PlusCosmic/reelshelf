using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Billing;
using Reelshelf.Email;
using Reelshelf.FeatureFlags;
using Reelshelf.Plans;
using Reelshelf.Test.Plans;
using Reelshelf.Games;
using Reelshelf.Users;
using Reelshelf.Video;
using Xunit;

namespace Reelshelf.Test.Users;

public class AccountDeletionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (AccountDeletionService Service, FakeStore Store, FakeVideoHost Host, FakeSender Sender, AccountDeletionSignal Signal) Build(
        string? email = "harry@example.com", IAccountSubscriptions? subscriptions = null, IExportStorage? exports = null,
        IPostHogPeople? posthog = null)
    {
        FakeStore store = new() { Email = email };
        FakeVideoHost host = new();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IClipVideoHost>(VideoHostNames.R2, host);
        ClipVideoHosts hosts = new(services.BuildServiceProvider());
        FakeSender sender = new();
        AccountDeletionSignal signal = new();
        AccountDeletionService service = new(store, hosts, sender, signal, subscriptions ?? new NoAccountSubscriptions(),
            exports ?? new UnconfiguredExportStorage(), posthog ?? new NoPostHogPeople(),
            NullLogger<AccountDeletionService>.Instance);
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
    public async Task Process_DeletesEveryVideoThenTheAccount()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(5);
        await service.Request(UserId);

        bool deleted = await service.Process(UserId, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(store.Clips);
        Assert.True(store.AccountDeleted);
        Assert.Equal(5, host.DeletedVideos.Count);
    }

    [Fact]
    public async Task Process_CancelsTheSubscriptionsBeforeDeletingAnything()
    {
        FakeSubscriptions subscriptions = new();
        var (service, store, host, _, _) = Build(subscriptions: subscriptions);
        store.AddClips(2);
        await service.Request(UserId);

        Assert.True(await service.Process(UserId, CancellationToken.None));

        Assert.Equal([UserId], subscriptions.Cancelled);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_DeletesTheAccountsExports()
    {
        ClipExportBuilderTests.FakeStorage exports = new();
        exports.Objects[ExportParts.Key(UserId, Guid.NewGuid(), 1)] = [1];
        exports.Objects[ExportParts.Key(Guid.NewGuid(), Guid.NewGuid(), 1)] = [2];
        var (service, store, _, _, _) = Build(exports: exports);
        await service.Request(UserId);

        Assert.True(await service.Process(UserId, CancellationToken.None));

        Assert.DoesNotContain(exports.Objects.Keys, key => key.StartsWith(ExportParts.AccountPrefix(UserId)));
        Assert.Single(exports.Objects);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_DeletesThePostHogPersonBeforeTheAccount()
    {
        FakePostHogPeople posthog = new();
        var (service, store, _, _, _) = Build(posthog: posthog);
        await service.Request(UserId);

        Assert.True(await service.Process(UserId, CancellationToken.None));

        Assert.Equal([UserId], posthog.Deleted);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenDeletingThePostHogPersonFails_KeepsTheAccountForTheNextPass()
    {
        var (service, store, _, _, _) = Build(posthog: new FakePostHogPeople { Fail = true });
        await service.Request(UserId);

        Assert.False(await service.Process(UserId, CancellationToken.None));

        Assert.False(store.AccountDeleted);
    }

    private sealed class FakePostHogPeople : IPostHogPeople
    {
        public bool Fail { get; init; }
        public List<Guid> Deleted { get; } = [];

        public Task IdentifyAsync(IReadOnlyList<FeatureFlagSubject> subjects, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("PostHog is down");
            }

            Deleted.Add(accountId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Process_WhenCancellingFails_DeletesNothingAndTriesAgainNextPass()
    {
        FakeSubscriptions subscriptions = new() { Fail = true };
        var (service, store, host, _, _) = Build(subscriptions: subscriptions);
        store.AddClips(2);
        await service.Request(UserId);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.Equal(2, store.Clips.Count);
        Assert.Empty(host.DeletedVideos);
        Assert.False(store.AccountDeleted);

        subscriptions.Fail = false;
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenAVideoDeleteFails_KeepsThatClipAndTheAccountForTheNextPass()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(3);
        OwnedClipVideo failing = store.Clips[1];
        host.FailingVideos.Add(failing.VideoId);
        await service.Request(UserId);

        bool deleted = await service.Process(UserId, CancellationToken.None);

        Assert.False(deleted);
        Assert.Equal(failing, Assert.Single(store.Clips));
        Assert.False(store.AccountDeleted);

        host.FailingVideos.Clear();
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Empty(store.Clips);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenAClipAppearsMidPass_LeavesTheAccountForTheNextPass()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(2);
        await service.Request(UserId);

        // An upload already in flight reserves a clip after the clip list was read.
        OwnedClipVideo late = new(Guid.NewGuid(), Guid.NewGuid(), VideoHostNames.R2);
        store.OnClipsDeleted = () => store.Clips.Add(late);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.False(store.AccountDeleted);

        store.OnClipsDeleted = null;
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Contains(late.VideoId, host.DeletedVideos);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhileAClipIsHeldForReview_CancelsTheSubscriptionsButDeletesNothing()
    {
        FakeSubscriptions subscriptions = new();
        ClipExportBuilderTests.FakeStorage exports = new();
        exports.Objects[ExportParts.Key(UserId, Guid.NewGuid(), 1)] = [1];
        var (service, store, host, _, _) = Build(subscriptions: subscriptions, exports: exports);
        store.AddClips(3);
        await service.Request(UserId);
        // A child-safety report filed after the account asked to be deleted.
        store.HeldClips.Add(store.Clips[1].ClipId);

        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.False(await service.Process(UserId, CancellationToken.None));

        Assert.Equal([UserId, UserId], subscriptions.Cancelled);
        Assert.Equal(3, store.Clips.Count);
        Assert.Empty(host.DeletedVideos);
        Assert.Single(exports.Objects);
        Assert.False(store.AccountDeleted);

        // Dismissed: the next pass deletes everything.
        store.HeldClips.Clear();
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.Empty(store.Clips);
        Assert.Equal(3, host.DeletedVideos.Count);
        Assert.Empty(exports.Objects);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_AfterTheHeldClipIsRemoved_WaitsForItsEvidenceThenDeletesTheRest()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(2);
        await service.Request(UserId);
        OwnedClipVideo reported = store.Clips[0];
        store.HeldClips.Add(reported.ClipId);
        Assert.False(await service.Process(UserId, CancellationToken.None));

        // An admin removes it: the row goes, and its video waits for the evidence processor to copy it.
        store.Clips.Remove(reported);
        store.HeldClips.Clear();
        store.EvidencePending = true;
        Assert.False(await service.Process(UserId, CancellationToken.None));
        Assert.Single(store.Clips);
        Assert.Empty(host.DeletedVideos);

        store.EvidencePending = false;
        Assert.True(await service.Process(UserId, CancellationToken.None));
        Assert.DoesNotContain(reported.VideoId, host.DeletedVideos);
        Assert.Single(host.DeletedVideos);
        Assert.True(store.AccountDeleted);
    }

    [Fact]
    public async Task Process_WhenAReportIsFiledMidPass_StopsBeforeTheNextBatchAndKeepsTheHeldClip()
    {
        var (service, store, host, _, _) = Build();
        store.AddClips(50);
        await service.Request(UserId);
        OwnedClipVideo late = store.Clips[^1];
        // Filed while the first batch of videos goes, just after the check at the start of the pass.
        store.AfterHoldCheck = checks =>
        {
            if (checks == 0)
            {
                store.HeldClips.Add(late.ClipId);
            }
        };

        Assert.False(await service.Process(UserId, CancellationToken.None));

        Assert.Equal(20, host.DeletedVideos.Count);
        Assert.Equal(30, store.Clips.Count);
        Assert.Contains(late, store.Clips);
        Assert.DoesNotContain(late.VideoId, host.DeletedVideos);
        Assert.False(store.AccountDeleted);
    }

    private sealed class FakeSubscriptions : IAccountSubscriptions
    {
        public bool Fail { get; set; }
        public List<Guid> Cancelled { get; } = [];

        public Task CancelAllAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Stripe is down");
            }

            Cancelled.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IAccountDeletionStore
    {
        public string? Email { get; init; }
        public bool Marked { get; private set; }
        public bool AccountDeleted { get; private set; }
        public List<OwnedClipVideo> Clips { get; } = [];
        public Action? OnClipsDeleted { get; set; }

        /// <summary>Clips under an open child-safety or terrorism report.</summary>
        public HashSet<Guid> HeldClips { get; } = [];

        /// <summary>A removed clip's video still waits to be copied as evidence.</summary>
        public bool EvidencePending { get; set; }

        /// <summary>Called after each hold check has been answered, with how many checks came before it.</summary>
        public Action<int>? AfterHoldCheck { get; set; }

        private int _holdChecks;

        public void AddClips(int count, string videoHost = VideoHostNames.R2)
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

        public Task<bool> IsHeldForReview(Guid userId)
        {
            bool held = EvidencePending || Clips.Any(clip => HeldClips.Contains(clip.ClipId));
            AfterHoldCheck?.Invoke(_holdChecks++);
            return Task.FromResult(held);
        }

        public Task DeleteClips(Guid userId, IReadOnlyCollection<Guid> clipIds)
        {
            Clips.RemoveAll(clip => clipIds.Contains(clip.ClipId) && !HeldClips.Contains(clip.ClipId));
            OnClipsDeleted?.Invoke();
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAccountIfEmpty(Guid userId)
        {
            if (!Marked || Clips.Count > 0 || EvidencePending)
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
        public HashSet<Guid> FailingVideos { get; } = [];

        public Task DeleteVideoAsync(Guid videoId)
        {
            if (FailingVideos.Contains(videoId))
            {
                throw new HttpRequestException("Storage is down");
            }

            lock (_lock)
            {
                DeletedVideos.Add(videoId);
            }

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
