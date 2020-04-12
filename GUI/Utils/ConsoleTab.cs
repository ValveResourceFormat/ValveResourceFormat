using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GUI.Utils
{
    internal class ConsoleTab
    {
        private class MyLogger : TextWriter
        {
            private readonly TextBox control;

            public MyLogger(TextBox control)
            {
                this.control = control;
            }

            public override Encoding Encoding => null;

            public override void WriteLine(string value)
            {
                if (control.IsDisposed)
                {
                    return;
                }

                if (control.InvokeRequired)
                {
                    control.Invoke(new MethodInvoker(delegate { WriteLine(value); }));
                    return;
                }

                var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {value}{Environment.NewLine}";
                control.AppendText(logLine);
            }
        }

        public static TabPage CreateTab()
        {
            var control = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.Black,
                ForeColor = Color.WhiteSmoke,
            };

            var tab = new TabPage("Console");
            tab.Controls.Add(control);

            var logger = new MyLogger(control);
            Console.SetOut(logger);
            Console.SetError(logger);

            return tab;
        }
    }
}
