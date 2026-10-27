using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;
using Xunit;

namespace Reelshelf.Test.FeatureFlags;

public class FeatureFlagServiceTests
{
    private static readonly AuthenticatedUser User = new(Guid.NewGuid(), "ada", null, null, null, true,
        UserRole.Editor, AccountTier.Pro, []);

    [Fact]
    public async Task AFlagIsOnWhenTheEvaluatorSaysSo()
    {
        FakeEvaluator evaluator = new() { Enabled = ["beta"] };
        FeatureFlagService flags = Service(evaluator);

        Assert.True(await flags.IsEnabledAsync("beta", User));
        Assert.False(await flags.IsEnabledAsync("other", User));
    }

    [Fact]
    public async Task AnOverrideWinsWithoutAskingTheEvaluator()
    {
        FakeEvaluator evaluator = new() { Enabled = ["off-here"] };
        FeatureFlagService flags = Service(evaluator, new() { ["on-here"] = true, ["off-here"] = false });

        Assert.True(await flags.IsEnabledAsync("on-here", User));
        Assert.False(await flags.IsEnabledAsync("off-here", User));
        Assert.Empty(evaluator.Calls);
    }

    [Fact]
    public async Task TheSubjectIsWorkedOutOncePerScope()
    {
        FakeSubjects subjects = new();
        FeatureFlagService flags = Service(new FakeEvaluator(), subjects: subjects);

        await flags.IsEnabledAsync("a", User);
        await flags.IsEnabledAsync("b", User);

        Assert.Equal(1, subjects.Lookups);
    }

    [Fact]
    public async Task AFailingEvaluatorLeavesTheFlagOff()
    {
        FeatureFlagService flags = Service(new FakeEvaluator { Throws = true });

        Assert.False(await flags.IsEnabledAsync("beta", User));
    }

    [Fact]
    public async Task AnAccountThatIsGoneHasEveryFlagOff()
    {
        FakeEvaluator evaluator = new() { Enabled = ["beta"] };
        FeatureFlagService flags = Service(evaluator, subjects: new FakeSubjects { Missing = true });

        Assert.False(await flags.IsEnabledAsync("beta", Guid.NewGuid()));
        Assert.Empty(evaluator.Calls);
    }

    [Fact]
    public async Task TheSubjectCarriesRoleTierAndGrantedTier()
    {
        FakeEvaluator evaluator = new();
        await Service(evaluator).IsEnabledAsync("beta", User);

        FeatureFlagSubject subject = Assert.Single(evaluator.Calls).Subject;
        Assert.Equal(User.Id.ToString(), subject.DistinctId);
        Assert.Equal(new Dictionary<string, object?>
        {
            ["role"] = "editor",
            ["tier"] = "pro",
            ["granted_tier"] = "pro"
        }, subject.PersonProperties());
    }

    [Fact]
    public void NoGrantedTierIsSentAsNone()
    {
        FeatureFlagSubject subject = new(Guid.NewGuid(), UserRole.Admin, AccountTier.Free, null);

        Assert.Equal("none", subject.PersonProperties()["granted_tier"]);
        Assert.Equal("admin", subject.PersonProperties()["role"]);
    }

    internal static FeatureFlagService Service(IFeatureFlagEvaluator evaluator,
        Dictionary<string, bool>? overrides = null, IFeatureFlagSubjects? subjects = null)
    {
        FeatureFlagOptions options = new() { Overrides = overrides ?? [] };
        return new FeatureFlagService(evaluator, subjects ?? new FakeSubjects(),
            new StaticOptionsMonitor(options), NullLogger<FeatureFlagService>.Instance);
    }

    internal sealed class FakeEvaluator : IFeatureFlagEvaluator
    {
        public HashSet<string> Enabled { get; init; } = [];
        public bool Throws { get; init; }
        public List<(FeatureFlagSubject Subject, List<string> Keys)> Calls { get; } = [];

        public Task<IReadOnlySet<string>> GetEnabledAsync(FeatureFlagSubject subject,
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            Calls.Add((subject, keys.ToList()));
            if (Throws)
            {
                throw new HttpRequestException("PostHog is down");
            }

            return Task.FromResult<IReadOnlySet<string>>(keys.Where(Enabled.Contains).ToHashSet());
        }
    }

    internal sealed class FakeSubjects : IFeatureFlagSubjects
    {
        public bool Missing { get; init; }
        public int Lookups { get; private set; }

        public Task<FeatureFlagSubject> ForUser(AuthenticatedUser user)
        {
            Lookups++;
            return Task.FromResult(new FeatureFlagSubject(user.Id, user.Role, AccountTier.Pro, user.GrantedTier));
        }

        public Task<FeatureFlagSubject?> ForAccount(Guid accountId)
        {
            Lookups++;
            return Task.FromResult<FeatureFlagSubject?>(Missing
                ? null
                : new FeatureFlagSubject(accountId, UserRole.Editor, AccountTier.Free, null));
        }
    }

    private sealed class StaticOptionsMonitor(FeatureFlagOptions value) : IOptionsMonitor<FeatureFlagOptions>
    {
        public FeatureFlagOptions CurrentValue => value;
        public FeatureFlagOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FeatureFlagOptions, string?> listener) => null;
    }
}
