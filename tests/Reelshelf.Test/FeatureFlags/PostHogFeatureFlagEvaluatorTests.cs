using System.Reflection;
using PostHog;
using PostHog.Features;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;
using Xunit;

namespace Reelshelf.Test.FeatureFlags;

public class PostHogFeatureFlagEvaluatorTests
{
    private static readonly FeatureFlagSubject Subject = new(Guid.NewGuid(), UserRole.Admin, AccountTier.Free, null);

    [Fact]
    public async Task EvaluatesOnlyLocallyWithTheSubjectsProperties()
    {
        AllFeatureFlagsOptions? asked = null;
        IPostHogClient client = FakePostHog.Answering((_, options) =>
        {
            asked = options;
            return new Dictionary<string, FeatureFlag>();
        });

        await new PostHogFeatureFlagEvaluator(client).GetEnabledAsync(Subject, ["beta"], CancellationToken.None);

        Assert.NotNull(asked);
        Assert.True(asked.OnlyEvaluateLocally);
        Assert.True(asked.DisableGeoIp);
        Assert.Equal(["beta"], asked.FlagKeysToEvaluate);
        Assert.Equal(Subject.PersonProperties(), asked.PersonProperties);
    }

    [Fact]
    public async Task AnswersTheAskedFlagsThatAreOn()
    {
        IPostHogClient client = FakePostHog.Answering((distinctId, _) =>
        {
            Assert.Equal(Subject.DistinctId, distinctId);
            return new Dictionary<string, FeatureFlag>
            {
                ["beta"] = new() { Key = "beta", IsEnabled = true },
                ["off"] = new() { Key = "off", IsEnabled = false },
                ["not-asked"] = new() { Key = "not-asked", IsEnabled = true }
            };
        });

        IReadOnlySet<string> enabled = await new PostHogFeatureFlagEvaluator(client)
            .GetEnabledAsync(Subject, ["beta", "off"], CancellationToken.None);

        Assert.Equal(["beta"], enabled);
    }

    /// <summary>An IPostHogClient whose GetAllFeatureFlagsAsync answers as told; anything else fails the test.</summary>
    private class FakePostHog : DispatchProxy
    {
        private Func<string, AllFeatureFlagsOptions?, IReadOnlyDictionary<string, FeatureFlag>> _answer = null!;

        public static IPostHogClient Answering(
            Func<string, AllFeatureFlagsOptions?, IReadOnlyDictionary<string, FeatureFlag>> answer)
        {
            IPostHogClient client = Create<IPostHogClient, FakePostHog>();
            ((FakePostHog)(object)client)._answer = answer;
            return client;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IPostHogClient.GetAllFeatureFlagsAsync))
            {
                return Task.FromResult(_answer((string)args![0]!, (AllFeatureFlagsOptions?)args[1]));
            }

            throw new NotSupportedException($"Only GetAllFeatureFlagsAsync is expected, not {targetMethod?.Name}.");
        }
    }
}
