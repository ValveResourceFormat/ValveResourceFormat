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

public enum SaveChangesChoice
{
    Save,
    DontSave,
    Cancel
}

public enum ReplaceFilesChoice
{
    Replace,
    Skip,
    Cancel
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

    public static bool Confirm(string message, string title, MessageIcon icon = MessageIcon.Question)
    {
        return MessageBox.Show(message, title, MessageBoxButtons.OKCancel, ToWinForms(icon)) == DialogResult.OK;
    }

    public static SaveChangesChoice AskToSaveChanges(string message, string title)
    {
        var result = MessageBox.Show(message, title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        return result switch
        {
            DialogResult.Yes => SaveChangesChoice.Save,
            DialogResult.No => SaveChangesChoice.DontSave,
            _ => SaveChangesChoice.Cancel,
        };
    }

    public static ReplaceFilesChoice AskToReplaceFiles(string heading, string message, string title)
    {
        var replaceButton = new TaskDialogButton("Replace");
        var skipButton = new TaskDialogButton("Skip");
        var page = new TaskDialogPage
        {
            Caption = title,
            Heading = heading,
            Text = message,
            Icon = TaskDialogIcon.Warning,
            Buttons = { replaceButton, skipButton, TaskDialogButton.Cancel },
            DefaultButton = replaceButton,
        };

        var result = TaskDialog.ShowDialog(Program.MainForm, page);

        if (result == replaceButton)
        {
            return ReplaceFilesChoice.Replace;
        }

        return result == skipButton ? ReplaceFilesChoice.Skip : ReplaceFilesChoice.Cancel;
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
