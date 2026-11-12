using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Reelshelf.StorageCheck;

/// <summary>An <see cref="IHttpClientFactory"/> over one handler, for the worker's and the API's own classes.</summary>
public sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler, TimeSpan timeout) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = timeout };
}

/// <summary>The HTTP the check sends outside the AWS SDK: presigned URLs, CORS and the media hostname.</summary>
public static class StorageHttp
{
    /// <summary>
    /// What <c>IHttpClientFactory</c>'s default primary handler is, which both the worker's <c>media</c> client and the
    /// API's export client use: redirects followed, the environment's proxy, no decompression. The check's raw requests
    /// go through the same kind of handler, so they leave the machine as the real ones do.
    /// </summary>
    public static HttpMessageHandler StorageHandler() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    /// <summary>
    /// The handler for the media hostname. With <paramref name="connectHost"/>, every connection for
    /// <paramref name="mediaHost"/> goes to that host instead (DNS for the media hostname may still point at the old
    /// CDN), while the request keeps the media hostname's URL, so TLS sends it as SNI, the certificate is validated for
    /// it as usual, and it is the <c>Host</c> header: what a browser will do once DNS moves. No proxy then, since a
    /// proxy would resolve the media hostname itself. Redirects are not followed, so the HTTP to HTTPS redirect shows.
    /// </summary>
    public static HttpMessageHandler MediaHandler(string mediaHost, string? connectHost, int? connectPort)
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };
        if (connectHost is null)
        {
            return handler;
        }

        handler.UseProxy = false;
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            DnsEndPoint target = string.Equals(context.DnsEndPoint.Host, mediaHost, StringComparison.OrdinalIgnoreCase)
                ? new DnsEndPoint(connectHost, ConnectPort(context.DnsEndPoint.Port, connectPort))
                : context.DnsEndPoint;
            Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(target, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        return handler;
    }

    /// <summary>An explicit port replaces 443 (local tests); port 80 stays 80 for the redirect check.</summary>
    internal static int ConnectPort(int requested, int? configured) =>
        configured is { } port && requested == 443 ? port : requested;

    /// <summary>
    /// A response header exactly as it came over the wire (not re-serialized from its parsed form, which would reorder
    /// <c>Cache-Control</c>'s directives), or null.
    /// </summary>
    public static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.NonValidated.TryGetValues(name, out HeaderStringValues values) ||
        response.Content.Headers.NonValidated.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;
}
