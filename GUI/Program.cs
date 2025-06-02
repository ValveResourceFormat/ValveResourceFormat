using System.Globalization;
using System.Threading;
using System.Windows.Forms;
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

            MessageBox.Show(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}See console for more information.{Environment.NewLine}{Environment.NewLine}Try using latest dev build to see if the issue persists.{Environment.NewLine}Source 2 Viewer Version: {Application.ProductVersion[..16]}",
                $"Unhandled exception: {exception.GetType()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}
