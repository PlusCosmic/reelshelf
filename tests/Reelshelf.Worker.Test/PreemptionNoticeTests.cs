using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Reelshelf.Worker.Test;

/// <summary><see cref="GcePreemptionWatcher"/> against a scripted metadata server, and which notice the settings pick.</summary>
public sealed class PreemptionNoticeTests
{
    [Fact]
    public async Task Gce_ReadsOnce_ThenWaitsForAChange_AndFiresOnTrue()
    {
        ScriptedMetadata metadata = new(("FALSE", "etag-1"), ("TRUE", "etag-2"));
        await using ServiceProvider services = Build("gce", metadata);
        GcePreemptionWatcher watcher = Assert.IsType<GcePreemptionWatcher>(services.GetRequiredService<IPreemptionNotice>());

        await watcher.StartAsync(CancellationToken.None);
        await Task.Delay(Timeout.Infinite, watcher.Preempted).ContinueWith(_ => { }).WaitAsync(TimeSpan.FromSeconds(10));
        await watcher.StopAsync(CancellationToken.None);

        Assert.True(watcher.Preempted.IsCancellationRequested);
        Assert.Equal(
        [
            "http://metadata.test/computeMetadata/v1/instance/preempted",
            "http://metadata.test/computeMetadata/v1/instance/preempted?wait_for_change=true&timeout_sec=300&last_etag=etag-1"
        ], metadata.Requests);
        Assert.All(metadata.Flavors, flavor => Assert.Equal("Google", flavor));
    }

    [Fact]
    public async Task Gce_KeepsWaiting_WhileTheAnswerIsFalse()
    {
        ScriptedMetadata metadata = new(("FALSE", "etag-1"), ("FALSE", "etag-2"), ("FALSE", "etag-3"));
        await using ServiceProvider services = Build("gce", metadata);
        GcePreemptionWatcher watcher = Assert.IsType<GcePreemptionWatcher>(services.GetRequiredService<IPreemptionNotice>());

        await watcher.StartAsync(CancellationToken.None);
        await metadata.Exhausted.WaitAsync(TimeSpan.FromSeconds(10));
        await watcher.StopAsync(CancellationToken.None);

        Assert.False(watcher.Preempted.IsCancellationRequested);
        Assert.EndsWith("last_etag=etag-2", metadata.Requests[2]);
    }

    [Fact]
    public async Task Default_IsNoNotice()
    {
        await using ServiceProvider services = Build(null, new ScriptedMetadata());
        Assert.IsType<NoPreemptionNotice>(services.GetRequiredService<IPreemptionNotice>());
        Assert.DoesNotContain(services.GetServices<IHostedService>(), service => service is GcePreemptionWatcher);
    }

    [Fact]
    public void AnUnknownNotice_FailsTheSettings()
    {
        WorkerSettings settings = new() { ApiBaseUrl = "https://reelshelf.example", Secret = FakeWorkerApi.Secret, PreemptionNotice = "aws" };
        Assert.Equal("Worker:PreemptionNotice must be empty or gce", settings.Problem());
    }

    private static ServiceProvider Build(string? notice, ScriptedMetadata metadata)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:ApiBaseUrl"] = "https://reelshelf.example",
            ["Worker:Secret"] = FakeWorkerApi.Secret,
            ["Worker:PreemptionNotice"] = notice,
            ["Worker:GceMetadataUrl"] = "http://metadata.test/computeMetadata/v1/"
        }).Build();
        ServiceCollection collection = new();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
        collection.AddTranscodeWorker(configuration);
        collection.AddHttpClient(GcePreemptionWatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => metadata);
        return collection.BuildServiceProvider();
    }

    /// <summary>
    /// Answers each request with the next scripted value and ETag (unquoted, as GCE sends it); once they run out, a
    /// request hangs until cancelled, like a wait with no change.
    /// </summary>
    private sealed class ScriptedMetadata(params (string Value, string ETag)[] answers) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<(string Value, string ETag)> remaining = new(answers);
        private readonly TaskCompletionSource exhausted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Requests { get; } = [];

        public List<string?> Flavors { get; } = [];

        public Task Exhausted => exhausted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.RequestUri!.ToString());
                Flavors.Add(request.Headers.TryGetValues("Metadata-Flavor", out var values) ? values.Single() : null);
            }

            if (!remaining.TryDequeue(out var answer))
            {
                exhausted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StringContent(answer.Value) };
            response.Headers.TryAddWithoutValidation("ETag", answer.ETag);
            return response;
        }
    }
}
