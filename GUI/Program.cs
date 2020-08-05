using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace GUI
{
    internal static class Program
    {
        public static MainForm MainForm { get; private set; }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        internal static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            //Application.ThreadException += WinFormsException;
            // Force proper culture so exported OBJ files use . instead of ,
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm = new MainForm();
            Application.Run(MainForm);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs ex)
        {
            ShowError("Unhandled Error", (Exception)ex.ExceptionObject);
        }

        private static void ShowError(string title, Exception e)
        {
            Console.WriteLine(e);

            MessageBox.Show(e.GetType() + Environment.NewLine + e.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
