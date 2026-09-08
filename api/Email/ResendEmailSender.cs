using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Reelshelf.Email;

/// <summary>
/// Sends through Resend's HTTP API (<c>Resend:ApiKey</c>, <c>Email:From</c>). Registered only when an API key is
/// configured; <see cref="LoggingEmailSender"/> stands in otherwise so local development never sends mail.
/// </summary>
public sealed class ResendEmailSender(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public const string HttpClientName = "resend";

    private readonly string _from = configuration["Email:From"] ?? "Reelshelf <no-reply@clips.pluscosmic.dev>";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "emails",
                new
                {
                    from = _from,
                    to = new[] { message.To },
                    subject = message.Subject,
                    text = message.Text,
                    html = message.Html
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Resend rejected \"{Subject}\" with {Status}: {Body}",
                    message.Subject, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Mail is best effort: an outage must not fail the request that triggered it.
            logger.LogWarning(ex, "Failed to send \"{Subject}\" via Resend", message.Subject);
        }
    }

    public static void Configure(HttpClient client, string apiKey)
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}

/// <summary>Used when no Resend key is configured: records what would have been sent.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email not sent (no Resend:ApiKey configured). To {To}: {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}
