using Reelshelf.Uploader.Core.Copy;

namespace Reelshelf.Uploader.Core.State;

/// <summary>The tray states T1–T8 of screens.html.</summary>
public enum UploaderStateKind
{
    Watching,
    Uploading,
    GameRunning,
    Paused,
    NeedsBook,
    LibraryFull,
    Unreachable,
    UpdateRequired
}

/// <summary>The colour of the tray icon's badge: none while watching, green while uploading, amber when it needs you, red when it's stopped.</summary>
public enum TrayBadge
{
    None,
    Green,
    Amber,
    Red
}

/// <summary>What the tray's main action (the menu's first item) does.</summary>
public enum TrayAction
{
    ShowRecent,
    UploadNow,
    Resume,
    ChooseBook,
    SeePlans,
    TryNow,
    OpenStore
}

/// <summary>How long a pause lasts (the tray's "Pause for…").</summary>
public enum PauseLength
{
    OneHour,
    UntilTomorrow,
    UntilResumed
}

public sealed record PauseInfo(PauseLength Length, DateTimeOffset? Until);

/// <summary>
/// Everything the tray's state is worked out from. The engine fills it in; <see cref="UploaderState.From"/> alone
/// decides what the tray shows, so the shell never works out state itself.
/// </summary>
public sealed record StateInputs
{
    public bool UpdateRequired { get; init; }

    /// <summary>The plan refused a clip; null while it has room.</summary>
    public LibraryFullInfo? LibraryFull { get; init; }

    /// <summary>When the API will be tried again after it couldn't be reached; null while it can.</summary>
    public DateTimeOffset? UnreachableRetryAt { get; init; }

    /// <summary>The first folder waiting for a book and how many clips wait in it; null when none does.</summary>
    public (string Folder, int Waiting)? NeedsBook { get; init; }

    public PauseInfo? Paused { get; init; }

    /// <summary>A full-screen game is running and uploads wait for it.</summary>
    public bool GameHoldsUploads { get; init; }

    /// <summary>Clips with a book, waiting or uploading.</summary>
    public int Waiting { get; init; }

    /// <summary>The clip uploading now, and how far it has got.</summary>
    public (string Title, int Percent)? Current { get; init; }

    /// <summary>The recordings folder as the tray names it, such as <c>Videos\NVIDIA</c>.</summary>
    public string FolderName { get; init; } = "";

    public int ShelvedToday { get; init; }
}

public sealed record LibraryFullInfo(int? ClipLimit, bool MonthlyLimit, DateTimeOffset? UploadsFrom);

/// <summary>
/// The tray's state: which of T1–T8 applies, by the priority in screens.html (T8 &gt; T6 &gt; T7 &gt; T5 &gt; T4 &gt;
/// T3 &gt; T2 &gt; T1), with the menu's two lines, its main action and the badge.
/// </summary>
public sealed record UploaderState(
    UploaderStateKind Kind,
    string Title,
    string Detail,
    string ActionLabel,
    TrayAction Action,
    TrayBadge Badge,
    int? Percent = null)
{
    /// <summary>The screen id, T1 to T8.</summary>
    public string Id => $"T{(int)Kind + 1}";

    /// <summary>The tooltip repeats the menu's first line.</summary>
    public string Tooltip => Title;

    public static UploaderState From(StateInputs inputs, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;

        if (inputs.UpdateRequired)
        {
            return new UploaderState(UploaderStateKind.UpdateRequired, UploaderCopy.Tray.UpdateRequired.Title,
                UploaderCopy.Tray.UpdateRequired.Detail, UploaderCopy.Tray.UpdateRequired.Action, TrayAction.OpenStore,
                TrayBadge.Red);
        }

        if (inputs.LibraryFull is { } full)
        {
            return full.MonthlyLimit
                ? new UploaderState(UploaderStateKind.LibraryFull, UploaderCopy.Tray.LibraryFull.MonthlyTitle,
                    UploaderCopy.Tray.LibraryFull.MonthlyDetail(
                        full.UploadsFrom is { } from ? TimeZoneInfo.ConvertTime(from, zone) : null, inputs.Waiting),
                    UploaderCopy.Tray.LibraryFull.Action, TrayAction.SeePlans, TrayBadge.Amber)
                : new UploaderState(UploaderStateKind.LibraryFull, UploaderCopy.Tray.LibraryFull.Title,
                    UploaderCopy.Tray.LibraryFull.Detail(full.ClipLimit, inputs.Waiting),
                    UploaderCopy.Tray.LibraryFull.Action, TrayAction.SeePlans, TrayBadge.Amber);
        }

        if (inputs.UnreachableRetryAt is { } retryAt)
        {
            TimeSpan wait = retryAt - now;
            return new UploaderState(UploaderStateKind.Unreachable, UploaderCopy.Tray.Unreachable.Title,
                UploaderCopy.Tray.Unreachable.Detail(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, inputs.Waiting),
                UploaderCopy.Tray.Unreachable.Action, TrayAction.TryNow, TrayBadge.Red);
        }

        if (inputs.NeedsBook is var (folder, waiting))
        {
            return new UploaderState(UploaderStateKind.NeedsBook,
                folder.Length == 0
                    ? UploaderCopy.Tray.NeedsBook.RootTitle
                    : UploaderCopy.Tray.NeedsBook.Title(folder),
                UploaderCopy.Tray.NeedsBook.Detail(waiting), UploaderCopy.Tray.NeedsBook.Action, TrayAction.ChooseBook,
                TrayBadge.Amber);
        }

        if (inputs.Paused is { } paused)
        {
            string title = paused.Length switch
            {
                PauseLength.UntilResumed => UploaderCopy.Tray.Paused.UntilResumed,
                PauseLength.UntilTomorrow => UploaderCopy.Tray.Paused.UntilTomorrow,
                _ => UploaderCopy.Tray.Paused.Until(TimeZoneInfo.ConvertTime(paused.Until ?? now, zone))
            };
            return new UploaderState(UploaderStateKind.Paused, title, UploaderCopy.Tray.Paused.Detail,
                UploaderCopy.Tray.Paused.Action, TrayAction.Resume, TrayBadge.Amber);
        }

        if (inputs.GameHoldsUploads && inputs.Waiting > 0)
        {
            return new UploaderState(UploaderStateKind.GameRunning, UploaderCopy.Tray.GameRunning.Title,
                UploaderCopy.Tray.GameRunning.Detail(inputs.Waiting), UploaderCopy.Tray.GameRunning.Action,
                TrayAction.UploadNow, TrayBadge.Green);
        }

        if (inputs.Current is var (current, percent))
        {
            return new UploaderState(UploaderStateKind.Uploading,
                UploaderCopy.Tray.Uploading.Title(Math.Max(1, inputs.Waiting)),
                UploaderCopy.Tray.Uploading.Detail(current, percent), UploaderCopy.Tray.Uploading.Action,
                TrayAction.ShowRecent, TrayBadge.Green, percent);
        }

        return new UploaderState(UploaderStateKind.Watching, UploaderCopy.Tray.Watching.Title,
            UploaderCopy.Tray.Watching.Detail(inputs.FolderName, inputs.ShelvedToday),
            UploaderCopy.Tray.Watching.Action, TrayAction.ShowRecent, TrayBadge.None);
    }

    /// <summary>The recordings folder as the tray names it: its last two folders, <c>Videos\NVIDIA</c>.</summary>
    public static string ShortFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        string[] parts = path.TrimEnd('\\', '/').Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\\', parts.TakeLast(2));
    }
}
