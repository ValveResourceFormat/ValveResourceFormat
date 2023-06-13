using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace GUI
{
    static class Program
    {
        public static MainForm MainForm { get; private set; }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        internal static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            Application.ThreadException += ThreadException;

            // Set invariant culture so we have consistent localization (e.g. dots do not get encoded as commas)
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            MainForm = new MainForm();
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

        private static void ShowError(Exception exception)
        {
            Console.Error.WriteLine(exception);

            MessageBox.Show(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}See console for more information.",
                $"Unhandled exception: {exception.GetType()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}
