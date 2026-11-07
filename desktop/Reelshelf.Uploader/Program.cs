using System.IO.Pipes;
using Avalonia;

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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

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
