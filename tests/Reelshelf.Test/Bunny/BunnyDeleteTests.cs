using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Reelshelf.Core;
using Reelshelf.Video.Bunny;
using Xunit;

namespace Reelshelf.Test.Bunny;

/// <summary>
/// Bunny's deletes, which the delete sweep, clip deletion and account deletion rely on: a video or collection Bunny
/// no longer knows about counts as deleted, and any other error is raised so the caller retries.
/// </summary>
public class BunnyDeleteTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DeleteVideo_SucceedsOnSuccessOrNotFound(HttpStatusCode status)
    {
        StubHandler handler = new(status);
        BunnyService bunny = Create(handler);
        Guid videoId = Guid.NewGuid();

        await bunny.DeleteVideoAsync(videoId);

        Assert.Equal(HttpMethod.Delete, handler.Request?.Method);
        Assert.Equal($"https://video.bunnycdn.com/library/123/videos/{videoId}", handler.Request?.RequestUri?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DeleteContainer_SucceedsOnSuccessOrNotFound(HttpStatusCode status)
    {
        StubHandler handler = new(status);
        Guid collectionId = Guid.NewGuid();

        await Create(handler).DeleteContainerAsync(collectionId);

        Assert.Equal($"https://video.bunnycdn.com/library/123/collections/{collectionId}", handler.Request?.RequestUri?.ToString());
    }

    [Fact]
    public async Task Deletes_ThrowOnAnyOtherError()
    {
        BunnyService bunny = Create(new StubHandler(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => bunny.DeleteVideoAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<HttpRequestException>(() => bunny.DeleteContainerAsync(Guid.NewGuid()));
    }

    private static BunnyService Create(StubHandler handler)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BunnyLibraryId"] = "123",
                ["BunnyAccessKey"] = "key"
            })
            .Build();
        return new BunnyService(new HttpClient(handler), configuration, new ClipsStatements(new NpgsqlConnection()),
            NullLogger<BunnyService>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
