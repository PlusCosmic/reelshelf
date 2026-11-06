using Avalonia.Controls;
using Reelshelf.Uploader.Core.Copy;

namespace Reelshelf.Uploader.Views;

/// <summary>A question with a confirm and a cancel button, such as Quit's while uploads are running.</summary>
internal sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string question, string confirm, string cancel, Action onConfirm)
    {
        Ui.Setup(this, UploaderCopy.AppName, 380);
        Content = Ui.Column(16,
            Ui.Text(question),
            Ui.Buttons(
                Ui.Button(cancel, Close),
                Ui.Button(confirm, () =>
                {
                    Close();
                    onConfirm();
                }, primary: true)));
    }
}
