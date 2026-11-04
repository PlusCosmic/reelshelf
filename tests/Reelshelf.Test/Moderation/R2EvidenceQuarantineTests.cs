using Reelshelf.Moderation;
using Reelshelf.Test.Video;
using Reelshelf.Video.R2;
using Xunit;

namespace Reelshelf.Test.Moderation;

public class R2EvidenceQuarantineTests
{
    private static readonly Guid ClipId = Guid.NewGuid();
    private static readonly Guid EvidenceId = Guid.NewGuid();

    [Fact]
    public async Task Quarantine_MovesEveryObjectOfTheClip_AndAbortsItsOpenUpload()
    {
        FakeR2Client r2 = new();
        Guid other = Guid.NewGuid();
        string[] clipKeys = [$"clips/{ClipId}/v1/av1.mp4", $"clips/{ClipId}/v1/thumbnail.jpg", $"uploads/{ClipId}"];
        foreach (string key in clipKeys.Append($"clips/{other}/v1/av1.mp4").Append($"uploads/{other}"))
        {
            r2.Objects[key] = 1;
        }

        string upload = r2.Open($"uploads/{ClipId}");
        using R2Storage storage = r2.Storage();
        R2EvidenceQuarantine quarantine = new(storage);

        await quarantine.QuarantineAsync(EvidenceId, ClipId, CancellationToken.None);

        Assert.Equal(
            Sorted([.. clipKeys.Select(key => $"quarantine/{EvidenceId}/{key}"), $"clips/{other}/v1/av1.mp4",
                $"uploads/{other}"]),
            Sorted(r2.Objects.Keys));
        Assert.Equal([upload], r2.Aborted);

        await using Stream? av1 = await quarantine.OpenAsync(EvidenceId, $"clips/{ClipId}/v1/av1.mp4",
            CancellationToken.None);
        Assert.Equal($"quarantine/{EvidenceId}/clips/{ClipId}/v1/av1.mp4", await new StreamReader(av1!).ReadToEndAsync());
        Assert.Null(await quarantine.OpenAsync(EvidenceId, $"clips/{ClipId}/v2/av1.mp4", CancellationToken.None));

        await quarantine.DeleteAsync(EvidenceId);
        Assert.Equal(Sorted([$"clips/{other}/v1/av1.mp4", $"uploads/{other}"]), Sorted(r2.Objects.Keys));
    }

    [Fact]
    public async Task QuarantiningAgain_MovesLateUploads_WithoutOverwritingWhatIsAlreadyQuarantined()
    {
        FakeR2Client r2 = new();
        r2.Objects[$"clips/{ClipId}/v1/av1.mp4"] = 1;
        using R2Storage storage = r2.Storage();
        R2EvidenceQuarantine quarantine = new(storage);
        await quarantine.QuarantineAsync(EvidenceId, ClipId, CancellationToken.None);

        // A worker uploads with a URL it held at removal: once to a new version, once over a key it already wrote.
        r2.Objects[$"clips/{ClipId}/v2/av1.mp4"] = 2;
        r2.Objects[$"clips/{ClipId}/v1/av1.mp4"] = 3;
        r2.Copies.Clear();
        await quarantine.QuarantineAsync(EvidenceId, ClipId, CancellationToken.None);

        Assert.Equal([($"clips/{ClipId}/v2/av1.mp4", $"quarantine/{EvidenceId}/clips/{ClipId}/v2/av1.mp4")], r2.Copies);
        Assert.Equal(1, r2.Objects[$"quarantine/{EvidenceId}/clips/{ClipId}/v1/av1.mp4"]);
        Assert.DoesNotContain(r2.Objects.Keys, key => key.StartsWith("clips/", StringComparison.Ordinal));
    }

    private static string[] Sorted(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();
}
