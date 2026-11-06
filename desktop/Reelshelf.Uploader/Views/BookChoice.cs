using Avalonia.Controls;
using Avalonia.Layout;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Matching;

namespace Reelshelf.Uploader.Views;

/// <summary>Lists of the account's books as the windows offer them: a book's cloth colour beside its name.</summary>
internal static class BookChoice
{
    /// <summary>A book as a list item: its swatch and name.</summary>
    public static Control Item(Book book) => Ui.Row(8, Ui.Swatch(book.ClothColor), Ui.Text(book.Name));

    /// <summary>
    /// The default-book list (S2, S5): "Ask me each time" first, then every book. Calls back with the book, or null for
    /// asking each time.
    /// </summary>
    public static ComboBox DefaultBook(IReadOnlyList<Book> books, Guid? selected, Action<Guid?> onChange)
    {
        ComboBox box = new() { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.Items.Add(new ComboBoxItem { Content = UploaderCopy.Setup.AskEachTime, Tag = null });
        foreach (Book book in books)
        {
            box.Items.Add(new ComboBoxItem { Content = Item(book), Tag = book.Id });
        }

        box.SelectedIndex = selected is { } id && books.FirstOrDefault(book => book.Id == id) is { } chosen
            ? books.ToList().IndexOf(chosen) + 1
            : 0;
        box.SelectionChanged += (_, _) => onChange((box.SelectedItem as ComboBoxItem)?.Tag as Guid?);
        return box;
    }

    /// <summary>
    /// The books to pick from for one folder (S2's "Choose…"), with "Add a game on reelshelf.app" at the end, which
    /// opens the site.
    /// </summary>
    public static ContextMenu Menu(IReadOnlyList<Book> books, Action<Book> onChoose, Action onAddGame)
    {
        ContextMenu menu = new();
        foreach (Book book in books)
        {
            MenuItem item = new() { Header = Item(book) };
            item.Click += (_, _) => onChoose(book);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        MenuItem add = new() { Header = UploaderCopy.Setup.AddGame };
        add.Click += (_, _) => onAddGame();
        menu.Items.Add(add);
        return menu;
    }
}
