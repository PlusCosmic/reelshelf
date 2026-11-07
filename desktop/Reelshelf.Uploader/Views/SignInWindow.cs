using Avalonia.Controls;
using Avalonia.Threading;
using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.Platform;
using Reelshelf.Uploader.Core.SignIn;
using Reelshelf.Uploader.Windows;

namespace Reelshelf.Uploader.Views;

/// <summary>
/// S1, signing in: asks the API for a code, opens reelshelf.app/device on it, and waits for the owner to allow it.
/// Opens on first run and whenever the token is refused. The code stays until it is allowed, turned down or expires.
/// </summary>
internal sealed class SignInWindow : Window
{
    private readonly AppController _controller;
    private readonly DeviceSignIn _signIn;
    private readonly string _deviceName = Environment.MachineName;
    private readonly StackPanel _body = Ui.Stack(14);
    private CancellationTokenSource _waiting = new();

    public SignInWindow(AppController controller)
    {
        _controller = controller;
        _signIn = new DeviceSignIn(controller.Api, new WindowsCredentialStore(), SystemClock.Instance);
        Ui.Setup(this, UploaderCopy.AppName, 460);
        Content = Ui.Column(14, Ui.Heading(UploaderCopy.SignIn.Title), Ui.Muted(UploaderCopy.SignIn.Intro), _body);
        Opened += (_, _) => _ = StartAsync();
        Closed += (_, _) => _waiting.Cancel();
    }

    private async Task StartAsync()
    {
        _waiting.Cancel();
        _waiting = new CancellationTokenSource();
        CancellationToken cancellationToken = _waiting.Token;
        Show(Ui.Muted(UploaderCopy.SignIn.Starting));

        SignInResult started = await _signIn.StartAsync(_deviceName, cancellationToken);
        if (started.Pending is not { } pending)
        {
            ShowOutcome(started.Outcome);
            return;
        }

        _controller.Open(pending.VerificationUriComplete);
        Show(
            new Border { Child = Ui.Text(pending.UserCode, "code") }.WithClass("codebox"),
            Ui.Muted(UploaderCopy.SignIn.Waiting(AppLinks.DevicePageName)),
            Ui.Muted(UploaderCopy.SignIn.BrowserOpened),
            Ui.Buttons(
                Ui.Button(UploaderCopy.SignIn.CopyCode, () => _ = Clipboard?.SetTextAsync(pending.UserCode)),
                Ui.Button(UploaderCopy.SignIn.OpenAgain, () => _controller.Open(pending.VerificationUriComplete),
                    primary: true)));

        SignInOutcome outcome;
        try
        {
            outcome = await _signIn.WaitForApprovalAsync(pending, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (outcome == SignInOutcome.SignedIn)
        {
            _controller.SignedIn(_deviceName);
            return;
        }

        ShowOutcome(outcome);
    }

    private void ShowOutcome(SignInOutcome outcome)
    {
        switch (outcome)
        {
            case SignInOutcome.Expired:
                Show(Ui.Text(UploaderCopy.SignIn.Expired),
                    Ui.Buttons(Ui.Button(UploaderCopy.SignIn.GetNewCode, () => _ = StartAsync(), primary: true)));
                break;
            case SignInOutcome.Denied:
                Show(Ui.Text(UploaderCopy.SignIn.Denied),
                    Ui.Buttons(Ui.Button(UploaderCopy.SignIn.TryAgain, () => _ = StartAsync(), primary: true)));
                break;
            case SignInOutcome.UpdateRequired:
                Show(Ui.Text(UploaderCopy.Notifications.UpdateDetail),
                    Ui.Buttons(Ui.Button(UploaderCopy.Notifications.OpenStore, () => _controller.Open(AppLinks.Store),
                        primary: true)));
                break;
            default:
                Show(Ui.Text(UploaderCopy.SignIn.Unreachable),
                    Ui.Buttons(Ui.Button(UploaderCopy.SignIn.TryAgain, () => _ = StartAsync(), primary: true)));
                break;
        }
    }

    private void Show(params Control[] controls)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _body.Children.Clear();
            _body.Children.AddRange(controls);
        });
    }
}
