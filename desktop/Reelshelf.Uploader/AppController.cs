using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Reelshelf.Uploader.ApiClient;
using Reelshelf.Uploader.Core;
using Reelshelf.Uploader.Core.Api;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Logging;
using Reelshelf.Uploader.Core.Platform;
using Reelshelf.Uploader.Core.Settings;
using Reelshelf.Uploader.Core.State;
using Reelshelf.Uploader.Views;
using Reelshelf.Uploader.Windows;

namespace Reelshelf.Uploader;

/// <summary>
/// Puts the uploader together and runs it: the engine from Core with the Windows implementations of its platform
/// parts, the tray, and the windows S1–S5. Decides nothing about uploads itself; it shows what the engine says.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly App _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly CancellationTokenSource _stopping = new();
    private readonly UploaderPaths _paths = UploaderPaths.Default();
    private readonly DailyFileLog _log;
    private readonly WindowsCredentialStore _credentials = new();
    private readonly WindowsGameActivity _game = new();
    private readonly ToastNotifier _toasts;
    private readonly ReelshelfApi _api;
    private readonly UploadLedger _ledger;
    private readonly HttpClient _storage = new() { Timeout = TimeSpan.FromMinutes(10) };
    private TrayController? _tray;
    private Task? _engineRun;

    private SignInWindow? _signIn;
    private SetupWindow? _setup;
    private SettingsWindow? _settings;
    private RecentWindow? _recent;
    private BookPickerWindow? _picker;

    public AppController(App app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _desktop = desktop;
        _log = new DailyFileLog(_paths.Logs);
        _toasts = new ToastNotifier(_log.Write);
        _ledger = new UploadLedger(_paths.Ledger);
        ReelshelfClientHandler handler = new(ReadToken, Version);
        _api = new ReelshelfApi(AppLinks.Api, handler);
        Api = new UploaderApi(_api);
        Engine = new UploaderEngine(Api, _storage, _ledger, new SettingsStore(_paths.Settings), _credentials, _game,
            _toasts, SystemClock.Instance);
    }

    /// <summary>The app's version, as <c>X-Reelshelf-Client</c> and S5 give it.</summary>
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}"
            : "0.0.0";

    public UploaderEngine Engine { get; }

    public UploaderApi Api { get; }

    public string LogFolder => _paths.Logs;

    public void Start()
    {
        _log.Write($"Starting the uploader {Version} against {AppLinks.Api}");
        _tray = new TrayController(this);
        Engine.StateChanged += (_, state) => Dispatcher.UIThread.Post(() => _tray?.Show(state));
        Engine.SignedOut += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _log.Write("The API refused the device token; showing sign-in");
            ShowSignIn();
        });
        Engine.BookNeeded += (_, folder) => _log.Write($"A folder needs a book ({folder.Length} characters)");
        _toasts.Activated += (action, folder) => Dispatcher.UIThread.Post(() => OnToast(action, folder));
        _tray.Show(Engine.State);
        ListenForSecondLaunch();

        if (!Engine.IsSignedIn)
        {
            ShowSignIn();
        }
        else if (!Engine.IsSetUp)
        {
            ShowSetup();
        }

        _engineRun = Task.Run(() => RunEngineAsync(_stopping.Token));
    }

    private async Task RunEngineAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Engine.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _log.Write($"The uploader stopped: {error.GetType().Name}: {error.Message}");
        }
    }

    // ---- Windows ----

    public void ShowSignIn()
    {
        if (_signIn is null)
        {
            _signIn = new SignInWindow(this);
            _signIn.Closed += (_, _) => _signIn = null;
        }

        _signIn.Show();
        _signIn.Activate();
    }

    /// <summary>S1 finished: the token is in Credential Manager.</summary>
    public void SignedIn(string deviceName)
    {
        _log.Write("Signed in");
        Engine.UpdateSettings(settings => settings.DeviceName = deviceName);
        Engine.OnSignedIn();
        _signIn?.Close();
        if (!Engine.IsSetUp)
        {
            ShowSetup();
        }
    }

    public void ShowSetup()
    {
        if (_setup is null)
        {
            _setup = new SetupWindow(this);
            _setup.Closed += (_, _) => _setup = null;
        }

        _setup.Show();
        _setup.Activate();
    }

    public void ShowSettings()
    {
        if (!Engine.IsSignedIn)
        {
            ShowSignIn();
            return;
        }

        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
        }

        _settings.Show();
        _settings.Activate();
    }

    public void ShowRecent()
    {
        if (_recent is null)
        {
            _recent = new RecentWindow(this);
            _recent.Closed += (_, _) => _recent = null;
        }

        _recent.Show();
        _recent.Activate();
    }

    public void ShowBookPicker(string folder)
    {
        _picker?.Close();
        _picker = new BookPickerWindow(this, folder);
        _picker.Closed += (_, _) => _picker = null;
        _picker.Show();
        _picker.Activate();
    }

    /// <summary>T5's "Choose a book…": the first folder waiting for one.</summary>
    public void ChooseBookForWaitingFolder()
    {
        if (Engine.Inputs().NeedsBook is var (folder, _))
        {
            ShowBookPicker(folder);
        }
    }

    public async Task<IReadOnlyList<Avalonia.Platform.Storage.IStorageFolder>> PickFolderAsync(Window owner, string title)
    {
        return await owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
    }

    // ---- Actions ----

    public void Open(Uri uri) => Open(uri.ToString());

    public void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Write($"Couldn't open a link: {error.GetType().Name}");
        }
    }

    public void TrayAction(TrayAction action)
    {
        switch (action)
        {
            case Core.State.TrayAction.ShowRecent:
                ShowRecent();
                break;
            case Core.State.TrayAction.UploadNow:
                Engine.UploadNow();
                break;
            case Core.State.TrayAction.Resume:
                Engine.Resume();
                break;
            case Core.State.TrayAction.ChooseBook:
                ChooseBookForWaitingFolder();
                break;
            case Core.State.TrayAction.SeePlans:
                Open(AppLinks.Plans);
                break;
            case Core.State.TrayAction.TryNow:
                Engine.TryNow();
                break;
            case Core.State.TrayAction.OpenStore:
                Open(AppLinks.Store);
                break;
        }
    }

    private void OnToast(ToastAction action, string? folder)
    {
        switch (action)
        {
            case ToastAction.OpenLibrary:
                Open(AppLinks.Library);
                break;
            case ToastAction.ChooseBook:
                ShowBookPicker(folder ?? "");
                break;
            case ToastAction.SeePlans:
                Open(AppLinks.Plans);
                break;
            case ToastAction.OpenStore:
                Open(AppLinks.Store);
                break;
            case ToastAction.SignIn:
                ShowSignIn();
                break;
        }
    }

    public async Task SignOutAsync()
    {
        _log.Write("Signing out");
        await Engine.SignOutAsync(CancellationToken.None);
        _settings?.Close();
        ShowSignIn();
    }

    /// <summary>Quit: stops watching until the app next starts. Uploads carry on from where they were next time.</summary>
    public void Quit()
    {
        _log.Write("Quitting");
        _stopping.Cancel();
        _tray?.Dispose();
        _desktop.Shutdown();
    }

    // ---- Second launches ----

    private void ListenForSecondLaunch()
    {
        _ = Task.Run(async () =>
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    await using NamedPipeServerStream pipe = new(Program.PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stopping.Token);
                    using StreamReader reader = new(pipe);
                    if (await reader.ReadLineAsync(_stopping.Token) == Program.ShowSettingsMessage)
                    {
                        Dispatcher.UIThread.Post(ShowSettings);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        });
    }

    private string? ReadToken()
    {
        try
        {
            return _credentials.ReadToken();
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            _log.Write($"Credential Manager couldn't be read: {error.NativeErrorCode}");
            return null;
        }
    }

    public void Log(string message) => _log.Write(message);

    public void Dispose()
    {
        _stopping.Cancel();
        try
        {
            _engineRun?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        Engine.Dispose();
        _game.Dispose();
        _api.Dispose();
        _storage.Dispose();
        _ledger.Dispose();
        _tray?.Dispose();
    }
}
