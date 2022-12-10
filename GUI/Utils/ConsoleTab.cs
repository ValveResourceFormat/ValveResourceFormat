using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GUI.Utils
{
    internal class ConsoleTab : IDisposable
    {
        private MyLogger logger;

        private class MyLogger : TextWriter
        {
            public StringBuilder ConsoleTextBuffer = new();
            private readonly TextBox control;

            public MyLogger(TextBox control)
            {
                this.control = control;

                control.VisibleChanged += VisibleChanged;
            }

            public override Encoding Encoding => null;

            public override void WriteLine(string value)
            {
                if (control.IsDisposed)
                {
                    return;
                }

                var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {value}{Environment.NewLine}";

                if (!control.Visible)
                {
                    // If we happen to spam console text somewhere, appending every line to console bogs down performance (yay winforms)
                    // so accumulate it into a buffer and append it all at once when visibility changes
                    ConsoleTextBuffer.Append(logLine);
                    return;
                }

                if (control.InvokeRequired)
                {
                    control.Invoke(new MethodInvoker(delegate { WriteLine(value); }));
                    return;
                }

                control.AppendText(logLine);
            }

            private void VisibleChanged(object sender, EventArgs e)
            {
                if (ConsoleTextBuffer.Length > 0)
                {
                    var str = ConsoleTextBuffer.ToString();
                    ConsoleTextBuffer.Clear();
                    control.AppendText(str);
                }
            }
        }

        public void Dispose()
        {
            if (logger != null)
            {
                logger.Dispose();
                logger = null;
            }
        }

        public TabPage CreateTab()
        {
            var bgColor = Color.FromArgb(37, 37, 37);
            var control = new MonospaceTextBox
            {
                BackColor = bgColor,
                ForeColor = Color.FromArgb(240, 240, 240),
            };

            var tab = new TabPage("Console")
            {
                BackColor = bgColor,
            };
            tab.Controls.Add(control);

            logger = new MyLogger(control);
            Console.SetOut(logger);
            Console.SetError(logger);

            return tab;
        }
    }
}
