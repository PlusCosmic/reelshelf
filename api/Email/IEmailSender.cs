namespace Reelshelf.Email;

public sealed record EmailMessage(string To, string Subject, string Text, string Html);

public interface IEmailSender
{
    /// <summary>Sends one message. Failures are logged by the implementation and never thrown to callers.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
