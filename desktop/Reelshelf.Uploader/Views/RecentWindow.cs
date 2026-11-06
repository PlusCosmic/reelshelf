using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.State;

namespace Reelshelf.Uploader.Views;

/// <summary>
/// S4, recent uploads: the last 50 files the app has handled, newest first, from the ledger. A clip on the shelf opens
/// on reelshelf.app; one that didn't upload can be tried again.
/// </summary>
internal sealed class RecentWindow : Window
{
    private readonly AppController _controller;
    private readonly StackPanel _rows = Ui.Stack(0);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _dirty = true;

    public RecentWindow(AppController controller)
    {
        _controller = controller;
        Ui.Setup(this, UploaderCopy.Recent.WindowTitle, 480);
        Content = Ui.Column(14,
            new ScrollViewer { Content = _rows, MaxHeight = 460 },
            Ui.Buttons(
                Ui.Button(UploaderCopy.Recent.OpenFolder, OpenFolder),
                Ui.Button(UploaderCopy.Recent.OpenLibrary, () => controller.Open(AppLinks.Library), primary: true)));

        // The ledger changes with every part; redraw at most once a second.
        controller.Engine.LedgerChanged += OnLedgerChanged;
        _refresh.Tick += (_, _) =>
        {
            if (_dirty)
            {
                _dirty = false;
                Render();
            }
        };
        Opened += (_, _) =>
        {
            Render();
            _refresh.Start();
        };
        Closed += (_, _) =>
        {
            _refresh.Stop();
            controller.Engine.LedgerChanged -= OnLedgerChanged;
        };
    }

    private void OnLedgerChanged(object? sender, EventArgs e) => _dirty = true;

    private void Render()
    {
        _rows.Children.Clear();
        List<LedgerEntry> entries = _controller.Engine.Recent();
        if (entries.Count == 0)
        {
            _rows.Children.Add(Ui.Muted(UploaderCopy.Recent.Empty));
            return;
        }

        (string Path, int Percent)? current = _controller.Engine.CurrentUpload;
        foreach (LedgerEntry entry in entries)
        {
            int? percent = current is var (path, value) && path == entry.Path ? value : null;
            _rows.Children.Add(Row(RecentUpload.From(entry, _controller.Engine.Books, DateTimeOffset.UtcNow, percent)));
        }
    }

    private Control Row(RecentUpload upload)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("9,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(0, 9)
        };
        Border swatch = Ui.Swatch(upload.ClothColor);
        swatch.Height = 30;
        Grid.SetRowSpan(swatch, 2);
        row.Children.Add(swatch);

        TextBlock title = Ui.Text(upload.Title);
        title.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        title.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
        title.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        TextBlock meta = Ui.Muted(upload.Meta);
        meta.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(meta, 1);
        Grid.SetRow(meta, 1);
        row.Children.Add(meta);

        Control status = upload.CanTryAgain
            ? Ui.Row(6, Ui.Text(upload.Status, "bad"), Ui.Button(UploaderCopy.Recent.TryAgain,
                () => _controller.Engine.TryNow(upload.Entry), link: true))
            : Ui.Text(upload.Status, upload.Entry.State is LedgerState.TooLong or LedgerState.NeedsBook ? "warn" : "muted");
        status.HorizontalAlignment = HorizontalAlignment.Right;
        status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(status, 2);
        Grid.SetRowSpan(status, 2);
        row.Children.Add(status);

        if (upload.CanOpen)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            row.PointerPressed += (_, _) =>
                _controller.Open(AppLinks.Clip(upload.BookSlug!, upload.Entry.ClipId!.Value));
        }

        return new Border
        {
            Child = row,
            BorderBrush = (Avalonia.Media.IBrush?)Application.Current!.FindResource("RsLine"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private void OpenFolder()
    {
        if (_controller.Engine.Settings.RecordingsFolder is { } folder)
        {
            _controller.Open(folder);
        }
    }
}
