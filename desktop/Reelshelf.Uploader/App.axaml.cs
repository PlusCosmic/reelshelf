using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Reelshelf.Uploader;

public sealed class App : Application
{
    private AppController? _controller;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _controller = new AppController(this, desktop);
            _controller.Start();
            desktop.Exit += (_, _) => _controller.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
