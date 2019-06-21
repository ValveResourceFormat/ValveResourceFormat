using System;
using System.Threading;
using System.Windows.Forms;

namespace GUI
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        internal static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            //Application.ThreadException += WinFormsException;
            // Force proper culture so exported OBJ files use . instead of ,
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void WinFormsException(object sender, ThreadExceptionEventArgs t)
        {
            ShowError("Windows Forms Exception", t.Exception);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs ex)
        {
            ShowError("Unhandled Error", (Exception)ex.ExceptionObject);
        }

        private static void ShowError(string title, Exception e)
        {
            Console.WriteLine(e);

            MessageBox.Show(e.GetType().ToString() + Environment.NewLine + e.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
