using System.IO;
using System.Windows.Forms;

#pragma warning disable RS0030 // Banned API: this is where all of the winforms code lives, it will be gone when we switch UI

namespace GUI.Utils;

// File and folder picker wrapper
public static class AppFileDialogs
{
    public enum RememberIn
    {
        None,
        OpenDirectory,
        SaveDirectory,
    }

    // updateRemembered: set to false when the caller validates the picked path first and
    // wants to remember the directory only when the pick is actually accepted.
    public static string? PickFolder(string? title, RememberIn remember = RememberIn.None, bool updateRemembered = true)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title ?? string.Empty,
            UseDescriptionForTitle = title != null,
            SelectedPath = GetRememberedDirectory(remember),
            AddToRecent = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        if (updateRemembered)
        {
            SetRememberedDirectory(remember, dialog.SelectedPath);
        }

        return dialog.SelectedPath;
    }

    public static string? OpenFile(string? title, string? filter, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
    {
        var files = OpenFilesCore(title, filter, multiselect: false, remember, updateRemembered);
        return files is { Length: > 0 } ? files[0] : null;
    }

    public static string[]? OpenFiles(string? title, string? filter, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
    {
        return OpenFilesCore(title, filter, multiselect: true, remember, updateRemembered);
    }

    private static string[]? OpenFilesCore(string? title, string? filter, bool multiselect, RememberIn remember, bool updateRemembered)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter ?? string.Empty,
            InitialDirectory = GetRememberedDirectory(remember),
            Multiselect = multiselect,
            AddToRecent = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK || dialog.FileNames.Length < 1)
        {
            return null;
        }

        if (updateRemembered && Path.GetDirectoryName(dialog.FileNames[0]) is { Length: > 0 } directory)
        {
            SetRememberedDirectory(remember, directory);
        }

        return dialog.FileNames;
    }

    public static string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, RememberIn remember = RememberIn.SaveDirectory)
    {
        return SaveFile(title, defaultFileName, defaultExtension, filter, out _, remember);
    }

    // selectedFilterIndex is 1 based, 0 means used canceled
    public static string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, out int selectedFilterIndex, RememberIn remember = RememberIn.SaveDirectory)
    {
        using var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            DefaultExt = defaultExtension,
            Filter = filter,
            InitialDirectory = GetRememberedDirectory(remember),
            AddToRecent = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            selectedFilterIndex = 0;
            return null;
        }

        selectedFilterIndex = dialog.FilterIndex;

        if (Path.GetDirectoryName(dialog.FileName) is { Length: > 0 } directory)
        {
            SetRememberedDirectory(remember, directory);
        }

        return dialog.FileName;
    }

    private static string GetRememberedDirectory(RememberIn remember) => remember switch
    {
        RememberIn.OpenDirectory => Settings.Config.OpenDirectory,
        RememberIn.SaveDirectory => Settings.Config.SaveDirectory,
        _ => string.Empty,
    };

    private static void SetRememberedDirectory(RememberIn remember, string path)
    {
        switch (remember)
        {
            case RememberIn.OpenDirectory:
                Settings.Config.OpenDirectory = path;
                break;
            case RememberIn.SaveDirectory:
                Settings.Config.SaveDirectory = path;
                break;
            case RememberIn.None:
                break;
        }
    }
}
