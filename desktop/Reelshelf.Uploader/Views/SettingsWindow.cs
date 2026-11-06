using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Settings;
using Reelshelf.Uploader.Windows;

namespace Reelshelf.Uploader.Views;

/// <summary>
/// S5, settings: the recordings folder, the default book, what happens during a full-screen game, the speed limit,
/// starting with Windows, signing out and the log folder. Changing the folder keeps the ledger, so a folder used before
/// doesn't upload twice. Signing out keeps the folder settings and the ledger.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly StackPanel _body = Ui.Stack(6);
    private StartupTaskService.StartupState _startup = StartupTaskService.StartupState.Unavailable;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        Ui.Setup(this, UploaderCopy.Settings.WindowTitle, 480);
        Content = Ui.Column(10, _body);
        controller.Engine.BooksChanged += OnBooksChanged;
        Closed += (_, _) => controller.Engine.BooksChanged -= OnBooksChanged;
        Opened += async (_, _) =>
        {
            _startup = await StartupTaskService.GetAsync();
            Render();
            await controller.Engine.RefreshBooksAsync(CancellationToken.None);
        };
    }

    private UploaderSettings Settings => _controller.Engine.Settings;

    private void OnBooksChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Render);

    private void Render()
    {
        _body.Children.Clear();

        string folder = Settings.RecordingsFolder ?? "";
        int matched = _controller.Engine.FolderMatches().Count(match => match.Book is not null);
        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.FolderLabel,
            UploaderCopy.Settings.FolderDetail(folder, matched),
            Ui.Button(UploaderCopy.Settings.Change, () => _ = ChangeFolderAsync())));

        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.DefaultBookLabel, null,
            BookChoice.DefaultBook(_controller.Engine.Books, Settings.DefaultBookId, _controller.Engine.SetDefaultBook)));

        ToggleButton wait = Segment(UploaderCopy.Settings.Wait, Settings.FullScreen == FullScreenMode.Wait);
        ToggleButton slow = Segment(UploaderCopy.Settings.UploadSlowly, Settings.FullScreen == FullScreenMode.UploadSlowly);
        wait.Click += (_, _) => SetFullScreen(FullScreenMode.Wait);
        slow.Click += (_, _) => SetFullScreen(FullScreenMode.UploadSlowly);
        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.FullScreenLabel,
            Settings.FullScreen == FullScreenMode.Wait
                ? UploaderCopy.Settings.FullScreenWait
                : UploaderCopy.Settings.FullScreenSlow,
            Ui.Row(0, wait, slow)));

        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.SpeedLabel, UploaderCopy.Settings.SpeedDetail,
            SpeedLimit()));

        ToggleSwitch startWithWindows = new()
        {
            IsChecked = _startup == StartupTaskService.StartupState.On,
            IsEnabled = _startup is StartupTaskService.StartupState.On or StartupTaskService.StartupState.Off,
            OnContent = null,
            OffContent = null
        };
        startWithWindows.IsCheckedChanged += async (_, _) =>
        {
            _startup = await StartupTaskService.SetAsync(startWithWindows.IsChecked == true);
            Render();
        };
        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.StartWithWindows,
            _startup == StartupTaskService.StartupState.Blocked ? UploaderCopy.Settings.StartWithWindowsBlocked : null,
            startWithWindows));

        ToggleSwitch notify = new() { IsChecked = Settings.NotifyShelved, OnContent = null, OffContent = null };
        notify.IsCheckedChanged += (_, _) =>
            _controller.Engine.UpdateSettings(settings => settings.NotifyShelved = notify.IsChecked == true);
        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.NotifyShelved, null, notify));

        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.SignedIn,
            Settings.DeviceName is { } name ? UploaderCopy.Settings.DeviceName(name) : null,
            Ui.Button(UploaderCopy.Settings.SignOut, () => _ = _controller.SignOutAsync())));

        _body.Children.Add(Ui.SettingRow(UploaderCopy.Settings.Version(AppController.Version), null,
            Ui.Button(UploaderCopy.Settings.OpenLogFolder, () =>
            {
                Directory.CreateDirectory(_controller.LogFolder);
                _controller.Open(_controller.LogFolder);
            }, link: true)));
    }

    private ComboBox SpeedLimit()
    {
        List<int?> choices = [null, .. UploaderSettings.SpeedLimits.Select(limit => (int?)limit)];
        ComboBox box = new() { MinWidth = 120 };
        foreach (int? choice in choices)
        {
            box.Items.Add(new ComboBoxItem
            {
                Content = choice is { } mbps ? UploaderCopy.Settings.Mbps(mbps) : UploaderCopy.Settings.NoLimit,
                Tag = choice
            });
        }

        box.SelectedIndex = Math.Max(0, choices.IndexOf(Settings.SpeedLimitMbps));
        box.SelectionChanged += (_, _) => _controller.Engine.UpdateSettings(settings =>
            settings.SpeedLimitMbps = (box.SelectedItem as ComboBoxItem)?.Tag as int?);
        return box;
    }

    private static ToggleButton Segment(string label, bool on) => new() { Content = label, IsChecked = on };

    private void SetFullScreen(FullScreenMode mode)
    {
        _controller.Engine.UpdateSettings(settings => settings.FullScreen = mode);
        Render();
    }

    private async Task ChangeFolderAsync()
    {
        IReadOnlyList<IStorageFolder> picked =
            await _controller.PickFolderAsync(this, UploaderCopy.Setup.FolderPickerTitle);
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            _controller.Engine.SetRecordingsFolder(path);
            Render();
        }
    }
}
