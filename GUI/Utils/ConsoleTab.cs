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

        private static readonly TextStyle TextStyleError = new(Brushes.Orange, null, FontStyle.Regular);
        private static readonly TextStyle TextStyleTime = new(Brushes.DarkGray, null, FontStyle.Regular);

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

        public void WriteLine(string value, TextStyle style)
        {
            if (control.IsDisposed)
            {
                return;
            }

            control.BeginUpdate();

            var lastLine = control.Lines.Count;

            control.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] ", TextStyleTime);
            control.AppendText(value, style);
            control.AppendText(Environment.NewLine);

            // Add fold for multi line strings
            var index = -1;
            var newLines = 0;

            while (-1 != (index = value.IndexOf('\n', index + 1)))
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

            using var textStyleGreen = new TextStyle(Brushes.LightGreen, null, FontStyle.Regular);
            control.AppendText($"- Welcome to Source 2 Viewer v{Application.ProductVersion}{Environment.NewLine}", textStyleGreen);
            control.AppendText($"- If you are experiencing an issue, try using latest unstable build from https://valveresourceformat.github.io/{Environment.NewLine}{Environment.NewLine}");

            var tab = new TabPage("Console")
            {
                BackColor = bgColor,
            };
            tab.Controls.Add(control);

            loggerOut = new MyLogger((message) => WriteLine(message, null));
            Console.SetOut(loggerOut);

            loggerError = new MyLogger((message) => WriteLine(message, TextStyleError));
            Console.SetError(loggerError);

            Console.Error.WriteLine("test error\nlol");

            return tab;
        }

        private void VisibleChanged(object sender, EventArgs e)
        {
            var control = (CodeTextBox)sender;
            control.GoEnd();
        }
    }
}
