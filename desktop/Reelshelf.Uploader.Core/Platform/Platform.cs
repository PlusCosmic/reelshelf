namespace Reelshelf.Uploader.Core.Platform;

/// <summary>Where the device token is kept: Windows Credential Manager in the app, memory in tests.</summary>
public interface ICredentialStore
{
    string? ReadToken();

    void WriteToken(string token);

    void DeleteToken();
}

/// <summary>Whether a full-screen game is in the foreground (<c>SHQueryUserNotificationState</c> in the app).</summary>
public interface IGameActivity
{
    bool IsGameRunning { get; }
}

/// <summary>The clock, so waits and timestamps can be driven by tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
}

/// <summary>One book's share of a "clips are on the shelf" notification.</summary>
public sealed record ShelvedBook(string BookName, int Clips);

/// <summary>
/// The notifications N1–N5 from screens.html. The app shows them as Windows toasts; their words come from
/// <see cref="Copy.UploaderCopy.Notifications"/>.
/// </summary>
public interface INotifier
{
    /// <summary>N1: clips reached the shelf since the last one.</summary>
    void ClipsShelved(IReadOnlyList<ShelvedBook> books);

    /// <summary>N2: a folder has no book; <paramref name="folder"/> is its name ("" for the recordings folder itself).</summary>
    void NeedsBook(string folder, int waiting);

    /// <summary>N3: preparing a clip answered that the library is full.</summary>
    void LibraryFull(LibraryFullReason reason);

    /// <summary>N4: the API answered 426.</summary>
    void UpdateRequired();

    /// <summary>N5: the API refused the token.</summary>
    void SignedOut();
}

/// <summary>Why new clips wait: the plan's clip limit, or its limit on clips added in 30 days.</summary>
public sealed record LibraryFullReason(int? ClipLimit, bool MonthlyLimit, DateTimeOffset? UploadsFrom);

/// <summary>A notifier that shows nothing, until the app has one.</summary>
public sealed class SilentNotifier : INotifier
{
    public void ClipsShelved(IReadOnlyList<ShelvedBook> books) { }

    public void NeedsBook(string folder, int waiting) { }

    public void LibraryFull(LibraryFullReason reason) { }

    public void UpdateRequired() { }

    public void SignedOut() { }
}
