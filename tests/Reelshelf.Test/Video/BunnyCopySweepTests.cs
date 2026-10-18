using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Video.Migration;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>The Bunny delete sweep (<see cref="BunnyCopySweeper"/>) against a fake store and a fake Bunny.</summary>
public class BunnyCopySweepTests
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    private static (BunnyCopySweeper Sweeper, FakeStore Store, FakeBunnyHost Bunny) Build()
    {
        FakeStore store = new();
        FakeBunnyHost bunny = new();
        BunnyCopySweeper sweeper = new(store, bunny.Hosts(), NullLogger<BunnyCopySweeper>.Instance) { Pause = TimeSpan.Zero };
        return (sweeper, store, bunny);
    }

    [Fact]
    public async Task Run_DeletesEveryDueCopyInBatches_ClearsIt_ThenTheUnusedCollections()
    {
        var (sweeper, store, bunny) = Build();
        List<BunnyCopy> copies = store.AddCopies(BunnyCopySweeper.BatchSize * 2 + 3);
        OwnedBunnyCollection collection = store.AddCollection();

        BunnyCopySweepResult result = await sweeper.RunAsync(KeepFor, CancellationToken.None);

        Assert.Equal(new BunnyCopySweepResult(copies.Count, 1, 0), result);
        Assert.Equal(copies.Select(copy => copy.BunnyVideoId).Order(), bunny.DeletedVideos.Order());
        Assert.Empty(store.Copies);
        Assert.Equal([collection.CollectionId], bunny.DeletedCollections);
        Assert.Empty(store.Collections);
        Assert.Equal(KeepFor, store.KeepForSeen);
    }

    [Fact]
    public async Task Run_WhenBunnyFailsOnACopy_LogsIt_CarriesOn_AndRetriesItOnTheNextPass()
    {
        var (sweeper, store, bunny) = Build();
        List<BunnyCopy> copies = store.AddCopies(3);
        bunny.FailingVideos.Add(copies[0].BunnyVideoId);

        BunnyCopySweepResult first = await sweeper.RunAsync(KeepFor, CancellationToken.None);

        Assert.Equal(new BunnyCopySweepResult(2, 0, 1), first);
        Assert.Equal(copies[0], Assert.Single(store.Copies));

        bunny.FailingVideos.Clear();
        Assert.Equal(new BunnyCopySweepResult(1, 0, 0), await sweeper.RunAsync(KeepFor, CancellationToken.None));
        Assert.Empty(store.Copies);
    }

    [Fact]
    public async Task Run_NeverDeletesACopyAClipStillPlaysFrom()
    {
        var (sweeper, store, bunny) = Build();
        BunnyCopy playing = store.AddCopies(1)[0];
        // A clip was switched back to Bunny after the copy was listed: the store's re-check under the clip's lock
        // refuses it.
        store.PlayingFromBunny.Add(playing.BunnyVideoId);

        BunnyCopySweepResult result = await sweeper.RunAsync(KeepFor, CancellationToken.None);

        Assert.Equal(new BunnyCopySweepResult(0, 0, 0), result);
        Assert.Empty(bunny.DeletedVideos);
        Assert.Equal(playing, Assert.Single(store.Copies));
    }

    [Fact]
    public async Task Run_WhenACollectionDeleteFails_KeepsItForTheNextPass()
    {
        var (sweeper, store, bunny) = Build();
        OwnedBunnyCollection failing = store.AddCollection();
        OwnedBunnyCollection other = store.AddCollection();
        bunny.FailingCollections.Add(failing.CollectionId);

        Assert.Equal(new BunnyCopySweepResult(0, 1, 1), await sweeper.RunAsync(KeepFor, CancellationToken.None));
        Assert.Equal([other.CollectionId], bunny.DeletedCollections);
        Assert.Equal(failing, Assert.Single(store.Collections));
    }

    [Fact]
    public async Task Service_DoesNothingUnlessDeleteBunnyIsOn()
    {
        var (sweeper, store, bunny) = Build();
        store.AddCopies(2);
        store.AddCollection();
        ServiceCollection services = new();
        services.AddSingleton(sweeper);
        using ServiceProvider provider = services.BuildServiceProvider();
        MigrationOptions options = new() { DeleteBunnyAfter = TimeSpan.FromDays(3) };
        BunnyCopySweepService service = new(provider.GetRequiredService<IServiceScopeFactory>(), new FixedOptions(options),
            NullLogger<BunnyCopySweepService>.Instance);

        Assert.Null(await service.RunPassAsync(CancellationToken.None));
        Assert.Empty(bunny.DeletedVideos);
        Assert.Empty(bunny.DeletedCollections);

        options.DeleteBunny = true;
        Assert.Equal(new BunnyCopySweepResult(2, 1, 0), await service.RunPassAsync(CancellationToken.None));
        Assert.Equal(TimeSpan.FromDays(3), store.KeepForSeen);
    }

    [Fact]
    public void Options_KeepBunnyCopiesForAWeekByDefault_AndTheSweepIsOff()
    {
        MigrationOptions options = new();

        Assert.False(options.DeleteBunny);
        Assert.Equal(TimeSpan.FromDays(7), options.EffectiveDeleteBunnyAfter);
    }

    private sealed class FixedOptions(MigrationOptions value) : IOptionsMonitor<MigrationOptions>
    {
        public MigrationOptions CurrentValue => value;
        public MigrationOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<MigrationOptions, string?> listener) => null;
    }

    /// <summary>
    /// Holds due copies and unused collections the way the statements select them, and re-checks a copy against
    /// <see cref="PlayingFromBunny"/> before calling the host, as <see cref="BunnyCopyStatements"/> does.
    /// </summary>
    private sealed class FakeStore : IBunnyCopyStore
    {
        public List<BunnyCopy> Copies { get; } = [];
        public List<OwnedBunnyCollection> Collections { get; } = [];
        public HashSet<Guid> PlayingFromBunny { get; } = [];
        public TimeSpan? KeepForSeen { get; private set; }

        public List<BunnyCopy> AddCopies(int count)
        {
            List<BunnyCopy> added = Enumerable.Range(0, count).Select(_ => new BunnyCopy(Guid.NewGuid(), Guid.NewGuid())).ToList();
            Copies.AddRange(added);
            return added;
        }

        public OwnedBunnyCollection AddCollection()
        {
            OwnedBunnyCollection collection = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            Collections.Add(collection);
            return collection;
        }

        public Task<List<BunnyCopy>> GetExpiredCopiesAsync(TimeSpan keepFor, IReadOnlyCollection<Guid> skipClipIds, int limit)
        {
            KeepForSeen = keepFor;
            return Task.FromResult(Copies.Where(copy => !skipClipIds.Contains(copy.ClipId)).Take(limit).ToList());
        }

        public async Task<bool> DeleteCopyAsync(BunnyCopy copy, TimeSpan keepFor, Func<Guid, Task> deleteAtHost)
        {
            if (!Copies.Contains(copy) || PlayingFromBunny.Contains(copy.BunnyVideoId))
            {
                return false;
            }

            await deleteAtHost(copy.BunnyVideoId);
            Copies.Remove(copy);
            return true;
        }

        public Task<List<OwnedBunnyCollection>> GetUnusedCollectionsAsync(IReadOnlyCollection<Guid> skipIds, int limit) =>
            Task.FromResult(Collections.Where(collection => !skipIds.Contains(collection.Id)).Take(limit).ToList());

        public async Task<bool> DeleteCollectionAsync(OwnedBunnyCollection collection, Func<Guid, Task> deleteAtHost)
        {
            if (!Collections.Contains(collection))
            {
                return false;
            }

            await deleteAtHost(collection.CollectionId);
            Collections.Remove(collection);
            return true;
        }
    }
}
