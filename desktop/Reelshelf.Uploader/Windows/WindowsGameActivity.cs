using System.Runtime.InteropServices;
using Reelshelf.Uploader.Core.Platform;

namespace Reelshelf.Uploader.Windows;

/// <summary>
/// Whether a full-screen game is in the foreground, from <c>SHQueryUserNotificationState</c>, polled every 10 seconds:
/// <c>QUNS_RUNNING_D3D_FULL_SCREEN</c> (a Direct3D game in exclusive full screen) and <c>QUNS_BUSY</c> (a full-screen
/// app, which includes borderless games) count as a game.
/// </summary>
public sealed partial class WindowsGameActivity : IGameActivity, IDisposable
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(10);

    private readonly Timer _timer;
    private volatile bool _running;

    public WindowsGameActivity()
    {
        _running = Query();
        _timer = new Timer(_ => Poll(), null, PollEvery, PollEvery);
    }

    public bool IsGameRunning => _running;

    /// <summary>Raised when a game starts or stops.</summary>
    public event EventHandler<bool>? Changed;

    private void Poll()
    {
        bool running = Query();
        if (running != _running)
        {
            _running = running;
            Changed?.Invoke(this, running);
        }
    }

    public static bool Query()
    {
        try
        {
            return SHQueryUserNotificationState(out NotificationState state) == 0 &&
                   state is NotificationState.Busy or NotificationState.RunningD3DFullScreen;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void Dispose() => _timer.Dispose();

    /// <summary><c>QUERY_USER_NOTIFICATION_STATE</c>.</summary>
    internal enum NotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out NotificationState state);
}
