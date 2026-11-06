using Avalonia.Controls;
using Avalonia.Threading;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Matching;

namespace Reelshelf.Uploader.Views;

/// <summary>
/// S3's picker: which book a folder's clips go on. The answer is saved and the waiting clips upload at once. The books
/// are read again when it opens, so a game just added on the site is there.
/// </summary>
internal sealed class BookPickerWindow : Window
{
    private readonly AppController _controller;
    private readonly string _folder;
    private readonly ListBox _books = new() { MaxHeight = 320 };
    private readonly Button _use;

    public BookPickerWindow(AppController controller, string folder)
    {
        _controller = controller;
        _folder = folder;
        _use = Ui.Button(UploaderCopy.BookPicker.UseBook, Use, primary: true);
        _use.IsEnabled = false;
        _books.SelectionChanged += (_, _) => _use.IsEnabled = _books.SelectedItem is not null;

        Ui.Setup(this, UploaderCopy.BookPicker.WindowTitle, 400);
        Content = Ui.Column(14,
            Ui.Text(folder.Length == 0
                ? UploaderCopy.BookPicker.RootHeading
                : UploaderCopy.BookPicker.Heading(UploaderCopy.Folder(folder))),
            Ui.Panel(_books),
            Ui.Button(UploaderCopy.BookPicker.AddGame, () => controller.Open(AppLinks.AddGame), link: true),
            Ui.Buttons(_use));

        controller.Engine.BooksChanged += OnBooksChanged;
        Closed += (_, _) => controller.Engine.BooksChanged -= OnBooksChanged;
        Opened += async (_, _) =>
        {
            Render();
            await controller.Engine.RefreshBooksAsync(CancellationToken.None);
        };
        Activated += (_, _) => _ = controller.Engine.RefreshBooksAsync(CancellationToken.None);
    }

    private void OnBooksChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Render);

    private void Render()
    {
        Guid? selected = (_books.SelectedItem as ListBoxItem)?.Tag as Guid?;
        _books.Items.Clear();
        foreach (Book book in _controller.Engine.Books)
        {
            ListBoxItem item = new() { Content = BookChoice.Item(book), Tag = book.Id };
            _books.Items.Add(item);
            if (book.Id == selected)
            {
                _books.SelectedItem = item;
            }
        }
    }

    private void Use()
    {
        if ((_books.SelectedItem as ListBoxItem)?.Tag is Guid book)
        {
            _controller.Engine.ChooseBookForFolder(_folder, book);
            Close();
        }
    }
}
