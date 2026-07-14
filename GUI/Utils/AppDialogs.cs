using System.Threading.Tasks;
using System.Windows.Forms;

#pragma warning disable RS0030 // Banned API: this is where all of the winforms messagebox code lives, it will be gone when we switch UI

namespace GUI.Utils;

public enum MessageIcon
{
    Info,
    Warning,
    Error,
    Question
}

public enum ConfirmButtons
{
    OkCancel,
    YesNo
}

public static class AppDialogs
{
    public static Task ShowMessageAsync(string message, string title, MessageIcon icon = MessageIcon.Info)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, ToWinForms(icon));
        return Task.CompletedTask;
    }

    public static Task<bool> ConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question, ConfirmButtons buttons = ConfirmButtons.OkCancel)
    {
        var winButtons = buttons == ConfirmButtons.YesNo ? MessageBoxButtons.YesNo : MessageBoxButtons.OKCancel;
        var result = MessageBox.Show(message, title, winButtons, ToWinForms(icon));
        return Task.FromResult(result is DialogResult.OK or DialogResult.Yes);
    }

    private static MessageBoxIcon ToWinForms(MessageIcon icon) => icon switch
    {
        MessageIcon.Info => MessageBoxIcon.Information,
        MessageIcon.Warning => MessageBoxIcon.Warning,
        MessageIcon.Error => MessageBoxIcon.Error,
        MessageIcon.Question => MessageBoxIcon.Question,
        _ => MessageBoxIcon.None,
    };
}
