using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Reelshelf.FeatureFlags;

/// <summary>
/// Keeps PostHog's people in step with the accounts (ADR-0009): each account is a person keyed by its id, with only the
/// properties flags target, so PostHog's UI lists those properties and shows who a flag matches. Flags are still
/// evaluated in the API from the database; this copy is only for PostHog's UI. Both calls throw on failure.
/// </summary>
public interface IPostHogPeople
{
    /// <summary>Sets each subject's person properties, creating the person if it doesn't exist yet.</summary>
    Task IdentifyAsync(IReadOnlyList<FeatureFlagSubject> subjects, CancellationToken cancellationToken);

    /// <summary>Deletes the account's person; a person that doesn't exist counts as deleted.</summary>
    Task DeleteAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>Where PostHog isn't configured nothing is sent, so there is nothing to keep in step or delete.</summary>
public sealed class NoPostHogPeople : IPostHogPeople
{
    public Task IdentifyAsync(IReadOnlyList<FeatureFlagSubject> subjects, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// PostHog's capture and persons APIs over plain HTTP. The SDK's own identify logs and swallows failures, so the sync
/// couldn't tell a person it failed to send from one it sent.
/// </summary>
public sealed class PostHogPeopleClient(HttpClient http, PostHogSettings settings) : IPostHogPeople
{
    public const string HttpClientName = "PostHogPeople";

    /// <summary>Events per <c>/batch/</c> request; PostHog takes up to 20 MB, far more than this needs.</summary>
    public const int BatchSize = 100;

    public async Task IdentifyAsync(IReadOnlyList<FeatureFlagSubject> subjects, CancellationToken cancellationToken)
    {
        foreach (FeatureFlagSubject[] chunk in subjects.Chunk(BatchSize))
        {
            var body = new
            {
                api_key = settings.ProjectToken,
                batch = chunk.Select(subject => new
                {
                    @event = "$identify",
                    distinct_id = subject.DistinctId,
                    properties = new Dictionary<string, object?> { ["$set"] = subject.PersonProperties() }
                })
            };

            using HttpResponseMessage response = await http.PostAsJsonAsync(
                new Uri(settings.HostUrl, "/batch/"), body, cancellationToken);
            await EnsureSuccess(response, "identify", cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post,
            new Uri(settings.ApiHostUrl,
                $"/api/projects/{Uri.EscapeDataString(settings.ProjectId ?? throw new InvalidOperationException("PostHog:ProjectId isn't set."))}/persons/bulk_delete/"))
        {
            Content = JsonContent.Create(new { distinct_ids = new[] { accountId.ToString() } })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SecretKey);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, "delete the person", cancellationToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string what,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"PostHog refused to {what}: {(int)response.StatusCode} {Truncate(detail)}", null, response.StatusCode);
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
