using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FastColoredTextBoxNS;
using GUI.Controls;

namespace GUI.Utils
{
    internal class ConsoleTab : IDisposable
    {
        private class MyLogger : TextWriter
        {
            private readonly Action<string> action;

            public MyLogger(Action<string> action)
            {
                this.action = action;
            }

            public override Encoding Encoding => null;

            public override void WriteLine(string value) => action(value);
        }

        private static readonly TextStyle TextStyleTime = new(Brushes.DarkGray, null, FontStyle.Regular);
        private static readonly TextStyle TextStyleError = new(Brushes.Orange, null, FontStyle.Regular);
        private static readonly TextStyle TextStyleDebug = new(Brushes.LightGreen, null, FontStyle.Regular);

        private CodeTextBox control;
        private MyLogger loggerOut;
        private MyLogger loggerError;

        public void Dispose()
        {
            if (control != null)
            {
                control.Dispose();
                control = null;
            }

            if (loggerOut != null)
            {
                loggerOut.Dispose();
                loggerOut = null;
            }

            if (loggerError != null)
            {
                loggerError.Dispose();
                loggerError = null;
            }
        }

        public void WriteLine(Log.Category category, string component, string message)
        {
            if (control.IsDisposed)
            {
                return;
            }

            control.BeginUpdate();

            var lastLine = control.Lines.Count;
            var style = category switch
            {
                Log.Category.DEBUG => TextStyleDebug,
                Log.Category.WARN => TextStyleError,
                Log.Category.ERROR => TextStyleError,
                _ => null,
            };

            control.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] [{component}] ", TextStyleTime);
            control.AppendText(string.Concat(message, Environment.NewLine), style);

            // Add fold for multi line strings
            // TODO: For multiline strings, indent them with the time/component size
            var index = -1;
            var newLines = 0;

            while (-1 != (index = message.IndexOf('\n', index + 1)))
            {
                newLines++;
            }

            if (newLines > 0)
            {
                var marker = lastLine.ToString(CultureInfo.InvariantCulture);
                control[lastLine - 1].FoldingStartMarker = marker;
                control[lastLine - 1 + newLines].FoldingEndMarker = marker;
            }

            control.EndUpdate();
        }

        public TabPage CreateTab()
        {
            var bgColor = Color.FromArgb(37, 37, 37);
            control = new CodeTextBox
            {
                BackColor = bgColor,
                ForeColor = Color.FromArgb(240, 240, 240),
                Paddings = new Padding(0, 10, 0, 10),
            };
            control.VisibleChanged += VisibleChanged;

            control.AppendText($"- Welcome to Source 2 Viewer v{Application.ProductVersion}{Environment.NewLine}", TextStyleDebug);
            control.AppendText($"- If you are experiencing an issue, try using latest unstable build from https://valveresourceformat.github.io/{Environment.NewLine}{Environment.NewLine}");

            const string CONSOLE = "Console";

            var tab = new TabPage(CONSOLE)
            {
                BackColor = bgColor,
            };
            tab.Controls.Add(control);

            loggerOut = new MyLogger((message) => WriteLine(Log.Category.INFO, CONSOLE, message));
            Console.SetOut(loggerOut);

            loggerError = new MyLogger((message) => WriteLine(Log.Category.ERROR, CONSOLE, message));
            Console.SetError(loggerError);

            return tab;
        }

        private void VisibleChanged(object sender, EventArgs e)
        {
            var control = (CodeTextBox)sender;
            control.GoEnd();
        }
    }
}
