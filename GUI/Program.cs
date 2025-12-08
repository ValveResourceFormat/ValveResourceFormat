using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;

namespace GUI
{
    static class Program
    {
#nullable disable
        public static MainForm MainForm { get; private set; }
#nullable enable

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        internal static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            Application.ThreadException += ThreadException;

            // Set invariant culture so we have consistent localization (e.g. dots do not get encoded as commas)
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            MainForm = new MainForm(args);

            Application.Run(MainForm);
        }

        private static void ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ShowError(e.Exception);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs ex)
        {
            ShowError((Exception)ex.ExceptionObject);
        }

        public static void ShowError(Exception exception)
        {
            Log.Error(nameof(Program), exception.ToString());

            if (exception is GUI.Types.Renderer.ShaderLoader.ShaderCompilerException)
            {
                MessageBox.Show(exception.Message, "Failed to compile shader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var output = new StringBuilder(512);
            AppendExceptionWithVersion(output, exception);
            var outputText = output.ToString();

            var copyButton = new TaskDialogButton("Copy to clipboard");

            var firstLocation = string.Empty;

            try
            {
                var stackTrace = new StackTrace(exception, true);
                if (stackTrace.FrameCount > 0)
                {
                    var frame = stackTrace.GetFrame(0);
                    if (frame != null)
                    {
                        var method = frame.GetMethod();
                        var fileName = frame.GetFileName();
                        var lineNumber = frame.GetFileLineNumber();

                        if (method != null)
                        {
                            firstLocation = $"{Environment.NewLine}{method.DeclaringType?.FullName}.{method.Name}";

                            if (!string.IsNullOrEmpty(fileName) && lineNumber > 0)
                            {
                                firstLocation += $" in {fileName}:{lineNumber}";
                            }
                        }
                    }
                }
            }
            catch
            {
                //
            }

            var page = new TaskDialogPage
            {
                Caption = $"Unhandled exception: {exception.GetType()}",
                Text = $"{exception.Message}{firstLocation}{Environment.NewLine}{Environment.NewLine}Use copy button when sharing this error, and also mention your exact steps. Details also available in console.",
                Icon = TaskDialogIcon.Error,
                Buttons = { copyButton, TaskDialogButton.Close },
                DefaultButton = TaskDialogButton.Close,
                AllowCancel = false,
                AllowMinimize = false,
                Expander = new TaskDialogExpander
                {
                    Position = TaskDialogExpanderPosition.AfterFootnote,
                    Text = outputText,
                },
                Footnote = new TaskDialogFootnote
                {
                    Text = $"S2V {Application.ProductVersion[..16].Replace('+', ' ')}{Environment.NewLine}Try using latest dev build to see if the issue persists.",
                    Icon = TaskDialogIcon.Information
                }
            };

            var result = TaskDialog.ShowDialog(page);

            if (result == copyButton)
            {
                if (MainForm != null && MainForm.InvokeRequired)
                {
                    MainForm.BeginInvoke(() => Clipboard.SetText(outputText));
                }
                else
                {
                    Clipboard.SetText(outputText);
                }
            }
        }

        public static void AppendExceptionWithVersion(StringBuilder output, Exception exception)
        {
            var version = Application.ProductVersion;

            output.AppendLine("```");
            output.AppendLine(exception.ToString());
            output.AppendLine("```");

            output.Append("*S2V ");
            output.Append(version.Replace('+', ' '));
            output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

            if (GLEnvironment.GpuRendererAndDriver != null)
            {
                output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
            }

            output.AppendLine("*");
        }
    }
}
