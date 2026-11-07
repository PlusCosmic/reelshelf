using System.Net;
using System.Net.Http.Headers;

namespace Reelshelf.Uploader.ApiClient;

/// <summary>
/// Sits in front of every request to the Reelshelf API: adds the device token as <c>Authorization: Bearer</c> when
/// there is one, names the app in <c>X-Reelshelf-Client: uploader/&lt;version&gt;</c>, and tells the app when the API
/// refuses the token (401, <see cref="SignedOut"/>) or this version of the app (426, <see cref="UpdateRequired"/>).
/// Never used for the presigned storage URLs parts go to, which must carry no token.
/// </summary>
public sealed class ReelshelfClientHandler : DelegatingHandler
{
    public const string ClientHeader = "X-Reelshelf-Client";

    private readonly Func<string?> _token;
    private readonly string _clientName;

    /// <param name="token">The current device token, or null while signed out.</param>
    /// <param name="version">The app's version, such as <c>1.0.3</c>.</param>
    public ReelshelfClientHandler(Func<string?> token, string version, HttpMessageHandler? inner = null)
        : base(inner ?? new HttpClientHandler())
    {
        _token = token;
        _clientName = ClientName(version);
    }

    /// <summary>A request carrying the token got 401: the token was revoked, or the account can't sign in.</summary>
    public event EventHandler? SignedOut;

    /// <summary>The API answered 426: this version is older than it serves.</summary>
    public event EventHandler? UpdateRequired;

    public static string ClientName(string version) => $"uploader/{version}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Remove(ClientHeader);
        request.Headers.TryAddWithoutValidation(ClientHeader, _clientName);
        string? token = _token();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token))
        {
            SignedOut?.Invoke(this, EventArgs.Empty);
        }
        else if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            UpdateRequired?.Invoke(this, EventArgs.Empty);
        }

        return response;
    }
}
