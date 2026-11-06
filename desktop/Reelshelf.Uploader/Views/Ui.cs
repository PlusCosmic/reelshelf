using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Reelshelf.Uploader.Core.Copy;

namespace Reelshelf.Uploader.Views;

/// <summary>
/// The building blocks every window uses, styled by App.axaml's classes. Windows are built in code from these so every
/// word comes from <see cref="UploaderCopy"/> and none is written in XAML.
/// </summary>
internal static class Ui
{
    public static readonly Thickness WindowPadding = new(22, 20, 22, 22);

    public static WindowIcon Icon() =>
        new(AssetLoader.Open(new Uri("avares://Reelshelf.Uploader/Assets/Icons/reelshelf-64.png")));

    public static Bitmap Asset(string path) => new(AssetLoader.Open(new Uri($"avares://Reelshelf.Uploader/Assets/{path}")));

    /// <summary>A window of the uploader's: its title, icon and size, not resizable, centred.</summary>
    public static T Setup<T>(T window, string title, double width) where T : Window
    {
        window.Title = title;
        window.Icon = Icon();
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.CanResize = false;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window;
    }

    public static StackPanel Column(double spacing = 14, params Control[] children)
    {
        StackPanel panel = new() { Spacing = spacing, Margin = WindowPadding };
        panel.Children.AddRange(children);
        return panel;
    }

    public static StackPanel Stack(double spacing, params Control[] children)
    {
        StackPanel panel = new() { Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal, Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    /// <summary>Buttons along the bottom, to the right.</summary>
    public static StackPanel Buttons(params Control[] buttons)
    {
        StackPanel panel = Row(8, buttons);
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        return panel;
    }

    public static TextBlock Text(string text, string? style = null)
    {
        TextBlock block = new() { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style is not null)
        {
            block.Classes.AddRange(style.Split(' '));
        }

        return block;
    }

    public static TextBlock Heading(string text) => Text(text, "heading");

    public static TextBlock Muted(string text) => Text(text, "muted");

    public static TextBlock Label(string text) => Text(text, "label");

    public static Button Button(string label, Action onClick, bool primary = false, bool link = false)
    {
        Button button = new() { Content = label };
        if (primary)
        {
            button.Classes.Add("primary");
        }

        if (link)
        {
            button.Classes.Add("link");
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>A labelled row: the label and its detail on the left, a control on the right (S5).</summary>
    public static Grid SettingRow(string label, string? detail, Control control)
    {
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
        StackPanel text = Stack(2, Text(label));
        if (detail is not null)
        {
            text.Children.Add(Muted(detail));
        }

        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>A book's cloth colour as a small swatch, as the site's spines show it.</summary>
    public static Border Swatch(string? clothColor)
    {
        IBrush brush = clothColor is not null && Color.TryParse(clothColor, out Color color)
            ? new SolidColorBrush(color)
            : Brushes.Gray;
        return new Border { Width = 9, Height = 14, CornerRadius = new CornerRadius(1), Background = brush };
    }

    public static Border Panel(Control child) => new Border { Child = child }.WithClass("panel");

    public static T WithClass<T>(this T control, string name) where T : StyledElement
    {
        control.Classes.Add(name);
        return control;
    }
}
