using System.Security;
using System.Security.Cryptography;
using System.Text;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Platform;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Reelshelf.Uploader.Windows;

/// <summary>What a toast's button (or the toast itself) asks the app to do.</summary>
public enum ToastAction
{
    OpenLibrary,
    ChooseBook,
    SeePlans,
    OpenStore,
    SignIn
}

/// <summary>
/// N1–N5 as Windows toast notifications (<c>ToastNotificationManager</c>, which a packaged app can use without
/// registering anything else). N2 is tagged by folder, so its count updates in place. Clicks come back through
/// <see cref="Activated"/> while the app runs, which it does whenever it shows one. Windows itself holds toasts back
/// while a game is full screen.
/// </summary>
public sealed class ToastNotifier : INotifier
{
    private const string Group = "reelshelf";
    private readonly global::Windows.UI.Notifications.ToastNotifier? _notifier;
    private readonly Action<string> _log;

    public ToastNotifier(Action<string> log)
    {
        _log = log;
        try
        {
            _notifier = ToastNotificationManager.CreateToastNotifier();
        }
        catch (Exception error)
        {
            // An unpackaged development build has no identity to show toasts with.
            _log($"Toasts are unavailable: {error.GetType().Name}");
        }
    }

    /// <summary>A toast or one of its buttons was clicked: the action, and the folder for N2.</summary>
    public event Action<ToastAction, string?>? Activated;

    public void ClipsShelved(IReadOnlyList<ShelvedBook> books)
    {
        int clips = books.Sum(book => book.Clips);
        Show("shelved", UploaderCopy.Notifications.ShelvedTitle(clips),
            UploaderCopy.Notifications.ShelvedBooks(books.Select(book => (book.BookName, book.Clips))),
            ToastAction.OpenLibrary, null, (UploaderCopy.Notifications.OpenLibrary, ToastAction.OpenLibrary));
    }

    public void NeedsBook(string folder, int waiting)
    {
        string title = folder.Length == 0
            ? UploaderCopy.Notifications.NeedsBookRootTitle
            : UploaderCopy.Notifications.NeedsBookTitle(folder);
        string detail = folder.Length == 0
            ? UploaderCopy.Notifications.NeedsBookRootDetail(waiting)
            : UploaderCopy.Notifications.NeedsBookDetail(waiting, UploaderCopy.Folder(folder));
        Show($"book-{Tag(folder)}", title, detail, ToastAction.ChooseBook, folder,
            (UploaderCopy.Notifications.ChooseBook, ToastAction.ChooseBook), dismiss: UploaderCopy.Notifications.NotNow);
    }

    public void LibraryFull(LibraryFullReason reason)
    {
        string detail = reason.MonthlyLimit
            ? UploaderCopy.Notifications.MonthlyLimitDetail(reason.UploadsFrom?.ToLocalTime())
            : UploaderCopy.Notifications.LibraryFullDetail(reason.ClipLimit);
        Show("full", UploaderCopy.Notifications.LibraryFullTitle, detail, ToastAction.SeePlans, null,
            (UploaderCopy.Notifications.SeePlans, ToastAction.SeePlans));
    }

    public void UpdateRequired() => Show("update", UploaderCopy.Notifications.UpdateTitle,
        UploaderCopy.Notifications.UpdateDetail, ToastAction.OpenStore, null,
        (UploaderCopy.Notifications.OpenStore, ToastAction.OpenStore));

    public void SignedOut() => Show("signed-out", UploaderCopy.Notifications.SignedOutTitle,
        UploaderCopy.Notifications.SignedOutDetail, ToastAction.SignIn, null,
        (UploaderCopy.Notifications.SignIn, ToastAction.SignIn));

    private void Show(string tag, string title, string detail, ToastAction onClick, string? folder,
        (string Label, ToastAction Action) button, string? dismiss = null)
    {
        if (_notifier is null)
        {
            return;
        }

        StringBuilder xml = new();
        xml.Append($"<toast launch=\"{Arguments(onClick, folder)}\" activationType=\"foreground\">");
        xml.Append("<visual><binding template=\"ToastGeneric\">");
        xml.Append($"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(detail)}</text>");
        xml.Append("</binding></visual><actions>");
        xml.Append($"<action content=\"{SecurityElement.Escape(button.Label)}\" arguments=\"{Arguments(button.Action, folder)}\" activationType=\"foreground\"/>");
        if (dismiss is not null)
        {
            xml.Append($"<action content=\"{SecurityElement.Escape(dismiss)}\" arguments=\"dismiss\" activationType=\"system\"/>");
        }

        xml.Append("</actions></toast>");

        try
        {
            XmlDocument document = new();
            document.LoadXml(xml.ToString());
            ToastNotification toast = new(document) { Tag = tag, Group = Group };
            toast.Activated += (_, args) =>
            {
                string arguments = (args as ToastActivatedEventArgs)?.Arguments ?? "";
                if (Parse(arguments) is var (action, chosenFolder))
                {
                    Activated?.Invoke(action, chosenFolder);
                }
            };
            _notifier.Show(toast);
        }
        catch (Exception error)
        {
            _log($"A notification couldn't be shown: {error.GetType().Name}");
        }
    }

    private static string Arguments(ToastAction action, string? folder) =>
        SecurityElement.Escape($"action={action}" + (folder is null ? "" : $"&folder={Uri.EscapeDataString(folder)}"))!;

    internal static (ToastAction Action, string? Folder)? Parse(string arguments)
    {
        Dictionary<string, string> values = arguments.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        return values.TryGetValue("action", out string? name) && Enum.TryParse(name, out ToastAction action)
            ? (action, values.GetValueOrDefault("folder"))
            : null;
    }

    /// <summary>A toast tag is at most 64 characters, so a folder is tagged by a hash of its name.</summary>
    private static string Tag(string folder) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..16];
}
