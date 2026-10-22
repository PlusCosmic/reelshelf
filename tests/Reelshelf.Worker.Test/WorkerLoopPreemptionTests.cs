using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Reelshelf.Worker.Test;

public sealed partial class WorkerLoopTests
{
    [SvtAv1Fact]
    public async Task Preemption_DuringTheEncode_ReleasesTheJobAtOnce_WithoutTheGrace()
    {
        ManualPreemption preemption = new();
        await services.DisposeAsync();
        services = BuildServices(new Dictionary<string, string?> { ["Worker:ShutdownGrace"] = "00:01:00" },
            collection => collection.AddSingleton<IPreemptionNotice>(preemption));
        string source = await GenerateAsync("long.mp4", "testsrc2=size=1280x720:rate=30:duration=60", audio: false,
            "-c:v", "libx264", "-preset", "ultrafast");
        WorkerJob job = api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);

        WorkerLoop loop = services.GetRequiredService<WorkerLoop>();
        await loop.StartAsync();
        Task<bool> run = loop.RunOnceAsync(CancellationToken.None);
        while (api.Progress.IsEmpty && !run.IsCompleted)
        {
            await Task.Delay(50);
        }

        Stopwatch elapsed = Stopwatch.StartNew();
        preemption.Preempt();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(20)));

        // The minute's grace isn't waited out: a preempted VM has seconds left.
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"The release took {elapsed.Elapsed}");
        Assert.Null(api.Completion);
        Assert.Null(api.Failure);
        JsonElement release = Assert.IsType<JsonElement>(api.Release);
        Assert.Equal(job.LeaseId, release.GetProperty("lease_id").GetGuid());
        Assert.Empty(Directory.EnumerateFileSystemEntries(WorkDirectory));

        // And nothing more is claimed.
        api.Enqueue(JobTypes.Transcode, source, TranscodeOutputs);
        int claims = api.Claims;
        Assert.False(await loop.RunOnceAsync(CancellationToken.None));
        Assert.Equal(claims, api.Claims);
    }

    private sealed class ManualPreemption : IPreemptionNotice
    {
        private readonly CancellationTokenSource source = new();

        public CancellationToken Preempted => source.Token;

        public void Preempt() => source.Cancel();
    }
}
