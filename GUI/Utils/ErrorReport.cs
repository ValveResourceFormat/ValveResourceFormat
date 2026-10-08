using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GUI.Forms;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Utils;

namespace GUI.Utils;

/// <summary>
/// Shared wording and dialog for showing an error to the user, with advice that depends on the known update state.
/// </summary>
static class ErrorReport
{
    private const string ReportUrl = "https://s2v.app/ValveResourceFormat/guides/reporting-issues";

    private const string NewerFormatHeading = "This file may use a format that this version of S2V does not support";
    private const string GenericHeading = "An error occurred";

    /// <summary>
    /// Logs the exception and shows an error dialog, or reports it to the agent when automation drives the viewer.
    /// Can be called from any thread, and before the main window exists.
    /// </summary>
    /// <param name="exception">The exception to report.</param>
    /// <param name="context">What was being done when the exception happened, such as a file path.</param>
    public static void ShowError(Exception exception, string? context = null)
    {
        Log.Error(nameof(ErrorReport), context == null ? exception.ToString() : $"Failed on \"{context}\": {exception}");

        if (Automation.Automation.TryReportUnhandledException(exception))
        {
            return;
        }

        var report = new StringBuilder(512);
        AppendExceptionWithVersion(report, exception, context);

        var text = context == null ? exception.Message : $"{exception.Message}{Environment.NewLine}{context}";

        ShowDialog(GetHeading(exception), text, report.ToString());
    }

    /// <summary>
    /// Starts the once per session update check, and refreshes the bottom panel badge from the recorded update state.
    /// Safe to call from any thread.
    /// </summary>
    public static void ShowUpdateAfterError()
    {
        UpdateChecker.CheckAfterError();
        RefreshUpdateBadge();
    }

    /// <summary>
    /// Refreshes the bottom panel badge from the recorded update state. Safe to call from any thread.
    /// </summary>
    public static void RefreshUpdateBadge() => InvokeOnMainForm(() => Program.MainForm.ShowUpdateAfterError());

    /// <summary>
    /// Writes the exception in a code block, followed by the version line the issue bot reads.
    /// </summary>
    public static void AppendExceptionWithVersion(StringBuilder output, Exception exception, string? context = null)
    {
        output.AppendLine("```");

        if (context != null)
        {
            output.Append("Context: ");
            output.AppendLine(context);
            output.AppendLine();
        }

        output.AppendLine(exception.ToString());

        if (exception.Data[AnimationGraph.ExceptionDataKey] is string graphState)
        {
            output.AppendLine();
            output.AppendLine(AnimationGraph.ExceptionDataKey + ":");
            output.Append(graphState);
        }

        output.AppendLine();
        output.Append("S2V ");
        output.Append(Program.ProductVersion.Replace('+', ' '));
        output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

        if (GLEnvironment.GpuRendererAndDriver != null)
        {
            output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
        }

        output.AppendLine();
        output.AppendLine("```");
    }

    public static string GetHeading(Exception exception)
    {
        // These are mostly thrown for versions and types the parsers do not know yet, which a game update can introduce
        var mayBeNewerFormat = exception is UnexpectedMagicException or NotImplementedException or NotSupportedException;

        return mayBeNewerFormat ? NewerFormatHeading : GenericHeading;
    }

    /// <summary>
    /// A sentence about what to do next, based on the recorded update state. Empty for local builds.
    /// </summary>
    public static string GetAdvice() => GetAdvice(UpdateChecker.GetUpdateStatus());

    /// <summary>
    /// Shows an error dialog with buttons to check for updates in the About window, report the issue, and copy the report.
    /// Can be called from any thread, and before the main window exists.
    /// </summary>
    /// <param name="heading">The bold line above the text.</param>
    /// <param name="text">The body text, which is followed by advice based on the update state.</param>
    /// <param name="report">The text in the expander, which the copy and report buttons place on the clipboard.</param>
    public static void ShowDialog(string heading, string text, string report)
    {
        ShowUpdateAfterError();

        var status = UpdateChecker.GetUpdateStatus();
        var advice = GetAdvice(status);
        var body = new StringBuilder(text);

        if (advice.Length > 0)
        {
            body.AppendLine();
            body.AppendLine();
            body.Append(advice);
        }

        var mainForm = Program.MainForm;
        var hasMainForm = mainForm is { IsHandleCreated: true, IsDisposed: false };
        var statusSuggestsUpdate = status is UpdateChecker.UpdateStatus.Unknown or UpdateChecker.UpdateStatus.NewerStable or UpdateChecker.UpdateStatus.NewerDev;

        var checkForUpdatesButton = new TaskDialogButton("Check for updates");
        var reportButton = new TaskDialogButton("Report");
        var copyButton = new TaskDialogButton("Copy to clipboard");

        var page = new TaskDialogPage
        {
            Caption = "Source 2 Viewer",
            Heading = heading,
            Text = body.ToString(),
            Icon = TaskDialogIcon.Error,
            DefaultButton = TaskDialogButton.Close,
            AllowCancel = false,
            AllowMinimize = false,
            Expander = new TaskDialogExpander
            {
                Position = TaskDialogExpanderPosition.AfterFootnote,
                Text = report,
            },
            Footnote = new TaskDialogFootnote
            {
                Text = $"S2V {Program.DisplayVersion}",
                Icon = TaskDialogIcon.Information,
            },
        };

        if (hasMainForm && statusSuggestsUpdate)
        {
            page.Buttons.Add(checkForUpdatesButton);
        }
        else if (status == UpdateChecker.UpdateStatus.UpToDate)
        {
            page.Buttons.Add(reportButton);
        }

        page.Buttons.Add(copyButton);
        page.Buttons.Add(TaskDialogButton.Close);

        TaskDialogButton result;

        // Owning it from another thread would tie that thread's input to the UI thread, which may be the one that is stuck
        if (hasMainForm && !mainForm.InvokeRequired)
        {
            result = TaskDialog.ShowDialog(Form.ActiveForm ?? mainForm, page);
        }
        else
        {
            result = TaskDialog.ShowDialog(page);
        }

        if (result == checkForUpdatesButton)
        {
            InvokeOnMainForm(() => Program.MainForm.ShowAbout());
        }
        else if (result == reportButton)
        {
            CopyToClipboard(report);
            AboutForm.OpenUrl(ReportUrl);
        }
        else if (result == copyButton)
        {
            CopyToClipboard(report);
        }
    }

    private static string GetAdvice(UpdateChecker.UpdateStatus status)
    {
        var state = Settings.Config.Update;
        string newerBuild;

        switch (status)
        {
            case UpdateChecker.UpdateStatus.NewerDev:
                newerBuild = string.Create(CultureInfo.InvariantCulture, $"dev build {state.LatestDevBuild}");
                break;
            case UpdateChecker.UpdateStatus.NewerStable:
                newerBuild = $"version {state.LatestStableVersion}";
                break;
            case UpdateChecker.UpdateStatus.UpToDate:
                return "You are on the latest build. Report this error with your exact steps.";
            case UpdateChecker.UpdateStatus.Unknown:
                return "Check for updates in the About window and try the latest dev build before reporting this error.";
            default:
                return string.Empty;
        }

        var where = "from the About window";

        if (status == UpdateChecker.UpdateStatus.NewerDev && state.Channel == Settings.UpdateChannel.Stable)
        {
            where = "by switching to the Dev update channel in the About window";
        }

        return $"A newer build ({newerBuild}) is available {where}. Try it before reporting this error.";
    }

    private static void CopyToClipboard(string text)
    {
        // The clipboard needs an STA thread, which background threads are not
        if (Program.MainForm != null && Program.MainForm.InvokeRequired)
        {
            InvokeOnMainForm(() => AppClipboard.SetText(text));
        }
        else
        {
            AppClipboard.SetText(text);
        }
    }

    /// <summary>
    /// Posts the action to the main window's message queue. Does nothing when there is no main window.
    /// </summary>
    private static void InvokeOnMainForm(Action action)
    {
        var mainForm = Program.MainForm;

        if (mainForm is not { IsHandleCreated: true, IsDisposed: false })
        {
            return;
        }

        try
        {
            mainForm.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is being destroyed
        }
    }
}
