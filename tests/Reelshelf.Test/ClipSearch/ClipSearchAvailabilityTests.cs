using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.ClipSearch;
using Reelshelf.ClipSummary;
using Reelshelf.Exceptions;
using Reelshelf.Test.Billing;
using Xunit;

namespace Reelshelf.Test.ClipSearch;

/// <summary>Who can search by description: plans with transcripts, where a model and an embedding key are set.</summary>
public class ClipSearchAvailabilityTests
{
    private static readonly ClipSearchRequest Request = new("funny clips", null, null);

    [Fact]
    public async Task APlanWithoutTranscripts_IsToldItIsntAvailable()
    {
        ClipSearchService service = Service(parserConfigured: true, embeddingConfigured: true);
        AuthenticatedUser user = User(AccountTier.Plus);

        Assert.Equal(new ClipSearchAvailability(false, ClipSearchAvailability.PlanReason),
            await service.GetAvailabilityAsync(user));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SearchAsync(user, Request, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WithoutAModelOrAnEmbeddingKey_ItIsntAvailable(bool parserConfigured, bool embeddingConfigured)
    {
        ClipSearchService service = Service(parserConfigured, embeddingConfigured);
        AuthenticatedUser user = User(AccountTier.Pro);

        Assert.Equal(new ClipSearchAvailability(false, ClipSearchAvailability.NotConfiguredReason),
            await service.GetAvailabilityAsync(user));
        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            service.SearchAsync(user, Request, CancellationToken.None));
    }

    [Fact]
    public async Task ProAndUnlimited_CanSearch_WhenItIsConfigured()
    {
        ClipSearchService service = Service(parserConfigured: true, embeddingConfigured: true);

        Assert.True((await service.GetAvailabilityAsync(User(AccountTier.Pro))).Available);
        Assert.True((await service.GetAvailabilityAsync(User(AccountTier.Unlimited))).Available);
    }

    [Fact]
    public async Task ASearchTooLongOrEmpty_IsRefused()
    {
        ClipSearchService service = Service(parserConfigured: true, embeddingConfigured: true);
        AuthenticatedUser user = User(AccountTier.Pro);

        await Assert.ThrowsAsync<BadRequestException>(() => service.SearchAsync(user,
            new ClipSearchRequest(new string('a', ClipSearchService.MaxQueryLength + 1), null, null),
            CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(() => service.SearchAsync(user,
            new ClipSearchRequest("   ", null, null), CancellationToken.None));
    }

    private static AuthenticatedUser User(AccountTier? grantedTier) =>
        new(Guid.NewGuid(), "player", null, null, null, false, UserRole.Editor, grantedTier, []);

    // Everything after the checks is left null: a search refused before it reaches the database touches none of it.
    private static ClipSearchService Service(bool parserConfigured, bool embeddingConfigured) =>
        new(new FakeParsers(parserConfigured),
            new FakeSummarizers(embeddingConfigured),
            new FixedOptions<ClipSearchOptions>(new ClipSearchOptions()),
            new FixedOptions<ClipSummaryOptions>(new ClipSummaryOptions()),
            new AccountTierService(new FakeStore(), null!),
            null!, null!, null!, null!,
            TimeProvider.System,
            NullLogger<ClipSearchService>.Instance);

    private sealed class FakeParsers(bool configured) : IClipQueryParserFactory
    {
        public bool IsConfigured => configured;
        public IClipQueryParser Create() => throw new NotSupportedException();
    }

    private sealed class FakeSummarizers(bool embeddingConfigured) : IClipSummarizerFactory
    {
        public bool IsConfigured(string provider) => embeddingConfigured;
        public bool IsEmbeddingConfigured => embeddingConfigured;
        public IClipSummarizer Create(string provider, string model, string? reasoningEffort) =>
            throw new NotSupportedException();
        public IClipEmbedder CreateEmbedder(string model) => throw new NotSupportedException();
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
