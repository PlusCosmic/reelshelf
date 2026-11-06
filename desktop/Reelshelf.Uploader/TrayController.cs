using Avalonia.Controls;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.State;
using Reelshelf.Uploader.Views;

namespace Reelshelf.Uploader;

/// <summary>
/// The tray icon and its menu (T1–T8): the state's two lines, its main action, "Open your library", "Pause for…",
/// "Settings" and "Quit". The icon's badge shows the state's colour and its tooltip repeats the first line. A right
/// click opens the menu; Avalonia's tray can't open it on a left click, so a left click opens recent uploads (S4).
/// </summary>
internal sealed class TrayController : IDisposable
{
    private readonly AppController _controller;
    private readonly TrayIcon _icon;
    private readonly Dictionary<TrayBadge, WindowIcon> _icons;

    public TrayController(AppController controller)
    {
        _controller = controller;
        _icons = Enum.GetValues<TrayBadge>().ToDictionary(badge => badge, badge =>
            new WindowIcon(Ui.Asset($"Tray/tray-{badge.ToString().ToLowerInvariant()}-32.png")));
        _icon = new TrayIcon { Icon = _icons[TrayBadge.None], IsVisible = true };
        _icon.Clicked += (_, _) => _controller.ShowRecent();
        TrayIcon.SetIcons(Avalonia.Application.Current!, [_icon]);
    }

    public void Show(UploaderState state)
    {
        _icon.Icon = _icons[state.Badge];
        _icon.ToolTipText = state.Tooltip;

        NativeMenu menu = [];
        menu.Add(new NativeMenuItem(state.Title) { IsEnabled = false });
        menu.Add(new NativeMenuItem(state.Detail) { IsEnabled = false });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(state.ActionLabel, () => _controller.TrayAction(state.Action)));
        if (state.Kind != UploaderStateKind.UpdateRequired)
        {
            menu.Add(Item(UploaderCopy.Tray.OpenLibrary, () => _controller.Open(AppLinks.Library)));
            if (state.Kind != UploaderStateKind.Paused)
            {
                NativeMenu pause =
                [
                    Item(UploaderCopy.Tray.PauseHour, () => _controller.Engine.Pause(PauseLength.OneHour)),
                    Item(UploaderCopy.Tray.PauseTomorrow, () => _controller.Engine.Pause(PauseLength.UntilTomorrow)),
                    Item(UploaderCopy.Tray.PauseUntilResumed, () => _controller.Engine.Pause(PauseLength.UntilResumed))
                ];
                menu.Add(new NativeMenuItem(UploaderCopy.Tray.PauseFor) { Menu = pause });
            }
        }

        menu.Add(Item(UploaderCopy.Tray.Settings, _controller.ShowSettings));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(UploaderCopy.Tray.Quit, Quit));
        _icon.Menu = menu;
    }

    private void Quit()
    {
        int uploads = _controller.Engine.UploadsInProgress;
        if (uploads == 0)
        {
            _controller.Quit();
            return;
        }

        new ConfirmWindow(UploaderCopy.Tray.QuitWhileUploading(uploads), UploaderCopy.Tray.QuitConfirm,
            UploaderCopy.Tray.Cancel, _controller.Quit).Show();
    }

    private static NativeMenuItem Item(string header, Action onClick)
    {
        NativeMenuItem item = new(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
