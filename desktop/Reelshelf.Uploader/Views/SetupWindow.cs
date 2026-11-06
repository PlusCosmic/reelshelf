using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Files;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Windows;

namespace Reelshelf.Uploader.Views;

/// <summary>
/// S2, choosing the recordings folder: the folder, its subfolders with videos matched to books (each can be changed),
/// where clips that match nothing go, and "Start watching", which baselines what is already there.
/// </summary>
internal sealed class SetupWindow : Window
{
    private readonly AppController _controller;
    private readonly Dictionary<string, Guid> _choices = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _body = Ui.Stack(14);
    private readonly CheckBox _startWithWindows = new() { Content = UploaderCopy.Setup.AlsoStartWithWindows, IsChecked = true };
    private string? _folder;
    private Guid? _defaultBook;

    public SetupWindow(AppController controller)
    {
        _controller = controller;
        _folder = controller.Engine.Settings.RecordingsFolder ?? SuggestedFolder();
        _defaultBook = controller.Engine.Settings.DefaultBookId;
        foreach ((string folder, Guid book) in controller.Engine.Settings.FolderBooks)
        {
            _choices[folder] = book;
        }

        Ui.Setup(this, UploaderCopy.AppName, 480);
        Content = Ui.Column(14, Ui.Heading(UploaderCopy.Setup.Title), _body);
        controller.Engine.BooksChanged += OnBooksChanged;
        Closed += (_, _) => controller.Engine.BooksChanged -= OnBooksChanged;
        Opened += async (_, _) =>
        {
            Render();
            await controller.Engine.RefreshBooksAsync(CancellationToken.None);
        };
        // Coming back from adding a game on the site picks it up.
        Activated += (_, _) => _ = controller.Engine.RefreshBooksAsync(CancellationToken.None);
    }

    private void OnBooksChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Render);

    private void Render()
    {
        IReadOnlyList<Book> books = _controller.Engine.Books;
        _body.Children.Clear();

        Button change = Ui.Button(_folder is null ? UploaderCopy.Setup.ChooseFolder : UploaderCopy.Setup.ChangeFolder,
            () => _ = ChooseFolderAsync());
        Grid folderRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        folderRow.Children.Add(Ui.Text(_folder ?? "", "path"));
        Grid.SetColumn(change, 1);
        folderRow.Children.Add(change);
        _body.Children.Add(Ui.Stack(5, Ui.Label(UploaderCopy.Setup.FolderLabel), folderRow));

        if (books.Count == 0)
        {
            _body.Children.Add(Ui.Text(UploaderCopy.Setup.EmptyShelf));
            _body.Children.Add(Ui.Buttons(Ui.Button(UploaderCopy.Setup.AddGame, () => _controller.Open(AppLinks.AddGame))));
        }
        else if (_folder is not null)
        {
            List<string> folders = Directory.Exists(_folder) ? FolderScanner.FoldersWithVideos(_folder) : [];
            if (folders.Count == 0)
            {
                _body.Children.Add(Ui.Muted(UploaderCopy.Setup.NoSubfolders));
            }
            else
            {
                StackPanel rows = Ui.Stack(0);
                foreach (string folder in folders)
                {
                    rows.Children.Add(MatchRow(folder, books));
                }

                _body.Children.Add(Ui.Stack(6, Ui.Label(UploaderCopy.Setup.MatchedHeading), Ui.Panel(rows)));
            }

            _body.Children.Add(Ui.Stack(5, Ui.Label(UploaderCopy.Setup.DefaultBookLabel),
                BookChoice.DefaultBook(books, _defaultBook, book => _defaultBook = book)));
        }

        _body.Children.Add(Ui.Muted(UploaderCopy.Setup.Baseline));
        Button start = Ui.Button(UploaderCopy.Setup.StartWatching, () => _ = StartAsync(), primary: true);
        start.IsEnabled = books.Count > 0 && _folder is not null && Directory.Exists(_folder);
        _body.Children.Add(Ui.Buttons(_startWithWindows, start));
    }

    /// <summary>"Apex Legends\ → Apex Legends", or "No book" with "Choose…".</summary>
    private Control MatchRow(string folder, IReadOnlyList<Book> books)
    {
        BookMatch match = BookMatcher.Resolve(folder, books,
            _choices.TryGetValue(folder, out Guid chosen) ? chosen : null, null);
        Book? book = books.FirstOrDefault(candidate => candidate.Id == match.CategoryId);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,16,*,Auto"),
            Margin = new Thickness(10, 7)
        };
        row.Children.Add(Ui.Text(UploaderCopy.Folder(folder), "path"));
        TextBlock arrow = Ui.Muted(UploaderCopy.Setup.MatchArrow);
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);
        Control to = book is null ? Ui.Text(UploaderCopy.Setup.NoBook, "warn") : BookChoice.Item(book);
        Grid.SetColumn(to, 2);
        row.Children.Add(to);
        Button choose = Ui.Button(UploaderCopy.Setup.ChooseBook, () => { }, link: true);
        choose.ContextMenu = BookChoice.Menu(books, picked =>
        {
            _choices[folder] = picked.Id;
            Render();
        }, () => _controller.Open(AppLinks.AddGame));
        choose.Click += (_, _) => choose.ContextMenu.Open(choose);
        choose.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(choose, 3);
        row.Children.Add(choose);
        return row;
    }

    private async Task ChooseFolderAsync()
    {
        IReadOnlyList<IStorageFolder> picked = await _controller.PickFolderAsync(this, UploaderCopy.Setup.FolderPickerTitle);
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            _folder = path;
            Render();
        }
    }

    private async Task StartAsync()
    {
        if (_folder is null)
        {
            return;
        }

        foreach ((string folder, Guid book) in _choices)
        {
            _controller.Engine.Settings.FolderBooks[folder] = book;
        }

        _controller.Engine.SetDefaultBook(_defaultBook);
        _controller.Engine.SetRecordingsFolder(_folder);
        if (_startWithWindows.IsChecked == true)
        {
            await StartupTaskService.SetAsync(true);
        }

        Close();
    }

    /// <summary>NVIDIA's app saves into Videos\NVIDIA by default; otherwise the Videos folder itself.</summary>
    private static string? SuggestedFolder()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(videos))
        {
            return null;
        }

        string nvidia = Path.Combine(videos, "NVIDIA");
        return Directory.Exists(nvidia) ? nvidia : Directory.Exists(videos) ? videos : null;
    }
}
