using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Reelshelf.Worker.Test;

/// <summary>
/// The API's worker endpoints and a storage bucket on one loopback <see cref="HttpListener"/>: it hands out queued
/// jobs, records every report and stores PUT uploads with their headers. Events are kept in arrival order, so a test
/// can check that frames were uploaded before thumbnails were reported ready.
/// </summary>
public sealed class FakeWorkerApi : IAsyncDisposable
{
    public const string Secret = "worker-secret-0123456789abcdef-0123456789";

    private readonly HttpListener listener = new();
    private readonly ConcurrentQueue<WorkerJob> jobs = new();
    private readonly Task serving;
    private readonly string storageDirectory;

    public FakeWorkerApi(string storageDirectory)
    {
        this.storageDirectory = storageDirectory;
        Directory.CreateDirectory(storageDirectory);
        int port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(BaseUrl);
        listener.Start();
        serving = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }

    /// <summary>Progress reports answer 409, as when the job was reclaimed by another worker.</summary>
    public bool LoseLeaseOnProgress { get; set; }

    /// <summary>"progress", "complete", "fail" or "upload:{name}", in order.</summary>
    public ConcurrentQueue<string> Events { get; } = new();

    public ConcurrentQueue<JsonElement> Progress { get; } = new();

    public JsonElement? Completion { get; private set; }

    public JsonElement? Failure { get; private set; }

    public ConcurrentDictionary<string, Upload> Uploads { get; } = new();

    /// <summary>Requests to the worker API without the secret.</summary>
    public int Unauthorized { get; private set; }

    public sealed record Upload(string Path, string? ContentType, string? CacheControl, long Length);

    /// <summary>Queues a job whose source is <paramref name="sourcePath"/> and with an upload URL per file name.</summary>
    public WorkerJob Enqueue(string type, string sourcePath, IEnumerable<string> outputs, string inputJson = "{}")
    {
        File.Copy(sourcePath, Path.Combine(storageDirectory, "source"), overwrite: true);
        using JsonDocument input = JsonDocument.Parse(inputJson);
        WorkerJob job = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, 1, 1, input.RootElement.Clone(),
            $"{BaseUrl}storage/source?X-Amz-Signature=abc",
            outputs.ToDictionary(name => name, name => $"{BaseUrl}storage/out/{name}?X-Amz-Signature=def"));
        jobs.Enqueue(job);
        return job;
    }

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            try
            {
                await HandleAsync(context);
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        string path = request.Url!.AbsolutePath;

        if (path.StartsWith("/storage/", StringComparison.Ordinal))
        {
            if (request.Headers["Authorization"] is not null)
            {
                // Storage must never see the worker secret.
                response.StatusCode = 400;
                return;
            }

            if (request.HttpMethod == "GET" && path == "/storage/source")
            {
                await using FileStream source = File.OpenRead(Path.Combine(storageDirectory, "source"));
                response.ContentLength64 = source.Length;
                await source.CopyToAsync(response.OutputStream);
                return;
            }

            if (request.HttpMethod == "PUT" && path.StartsWith("/storage/out/", StringComparison.Ordinal))
            {
                string name = path["/storage/out/".Length..];
                string target = Path.Combine(storageDirectory, "out-" + name);
                await using (FileStream file = File.Create(target))
                {
                    await request.InputStream.CopyToAsync(file);
                }

                Uploads[name] = new Upload(target, request.Headers["Content-Type"], request.Headers["Cache-Control"],
                    new FileInfo(target).Length);
                Events.Enqueue($"upload:{name}");
                response.StatusCode = 200;
                return;
            }

            response.StatusCode = 404;
            return;
        }

        if (request.Headers["Authorization"] != $"Bearer {Secret}")
        {
            Unauthorized++;
            response.StatusCode = 401;
            return;
        }

        if (path == "/api/worker/claim")
        {
            if (jobs.TryDequeue(out WorkerJob? job))
            {
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(job, WorkerApiClient.JsonOptions);
                response.ContentType = "application/json";
                response.ContentLength64 = body.Length;
                await response.OutputStream.WriteAsync(body);
                return;
            }

            response.StatusCode = 204;
            return;
        }

        string report = path[(path.LastIndexOf('/') + 1)..];
        JsonElement json;
        using (StreamReader reader = new(request.InputStream, Encoding.UTF8))
        using (JsonDocument document = JsonDocument.Parse(await reader.ReadToEndAsync()))
        {
            json = document.RootElement.Clone();
        }

        Events.Enqueue(report);
        switch (report)
        {
            case "progress":
                Progress.Enqueue(json);
                response.StatusCode = LoseLeaseOnProgress ? 409 : 204;
                return;
            case "complete":
                Completion = json;
                break;
            case "fail":
                Failure = json;
                break;
        }

        response.StatusCode = 204;
    }

    private static int FreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        listener.Stop();
        listener.Close();
        await serving;
    }
}
