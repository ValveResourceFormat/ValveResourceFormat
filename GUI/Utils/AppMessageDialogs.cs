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

public static class AppMessageDialogs
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

    /// <summary>
    /// Asks the user to pick one of <paramref name="choices"/>, each shown as a button next to a Cancel button.
    /// </summary>
    /// <returns>The index of the picked choice, or -1 when cancelled.</returns>
    public static int Choose(string message, string title, IReadOnlyList<string> choices, MessageIcon icon = MessageIcon.Question, IWin32Window? owner = null)
    {
        var page = new TaskDialogPage
        {
            Caption = title,
            Text = message,
            Icon = ToTaskDialog(icon),
            AllowCancel = true,
        };

        foreach (var choice in choices)
        {
            page.Buttons.Add(new TaskDialogButton(choice));
        }

        page.Buttons.Add(TaskDialogButton.Cancel);

        var result = owner != null ? TaskDialog.ShowDialog(owner, page) : TaskDialog.ShowDialog(page);
        var index = page.Buttons.IndexOf(result);

        return index < choices.Count ? index : -1;
    }

    private static TaskDialogIcon ToTaskDialog(MessageIcon icon) => icon switch
    {
        MessageIcon.Info => TaskDialogIcon.Information,
        MessageIcon.Warning => TaskDialogIcon.Warning,
        MessageIcon.Error => TaskDialogIcon.Error,
        _ => TaskDialogIcon.None,
    };

    private static MessageBoxIcon ToWinForms(MessageIcon icon) => icon switch
    {
        MessageIcon.Info => MessageBoxIcon.Information,
        MessageIcon.Warning => MessageBoxIcon.Warning,
        MessageIcon.Error => MessageBoxIcon.Error,
        MessageIcon.Question => MessageBoxIcon.Question,
        _ => MessageBoxIcon.None,
    };
}
