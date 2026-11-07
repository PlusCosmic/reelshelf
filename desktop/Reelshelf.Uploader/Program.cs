using System.IO.Pipes;
using Avalonia;
using Reelshelf.Uploader.Core.Logging;
using Reelshelf.Uploader.Core.Settings;

namespace Reelshelf.Uploader;

internal static class Program
{
    /// <summary>One uploader per Windows session.</summary>
    private const string MutexName = @"Local\ReelshelfUploader";

    /// <summary>A second launch asks the running uploader, over this pipe, to show its settings (S5).</summary>
    internal const string PipeName = "ReelshelfUploader";

    internal const string ShowSettingsMessage = "show-settings";

    [STAThread]
    public static int Main(string[] args)
    {
        using Mutex mutex = new(initiallyOwned: true, MutexName, out bool first);
        if (!first)
        {
            SignalRunningInstance();
            return 0;
        }

        LogCrashes();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    /// <summary>Writes an exception that ends the app, or a task's that nobody observed, to the app's own log.</summary>
    private static void LogCrashes()
    {
        DailyFileLog log = new(UploaderPaths.Default().Logs);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(log, e.IsTerminating ? "The app crashed" : "Unhandled exception", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Write(log, "A background task failed", e.Exception);
    }

    private static void Write(DailyFileLog log, string what, object error)
    {
        try
        {
            log.Write($"{what}: {error}");
        }
        catch (Exception)
        {
            // Nothing more can be done while crashing; Windows' event log still has it.
        }
    }

    private static void SignalRunningInstance()
    {
        try
        {
            using NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using StreamWriter writer = new(pipe);
            writer.WriteLine(ShowSettingsMessage);
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The running uploader is starting up or shutting down; nothing more to do.
        }
    }
}
