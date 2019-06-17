using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GUI.Utils
{
    internal class ConsoleTab
    {
        internal class MyLogger : TextWriter
        {
            private TextBox control;

            public MyLogger(TextBox control)
            {
                this.control = control;
            }

            public override Encoding Encoding => null;

            public override void WriteLine(string value)
            {
                if (control.InvokeRequired)
                {
                    control.Invoke(new MethodInvoker(delegate { WriteLine(value); }));
                    return;
                }

                var logLine = $"[{DateTime.Now.ToString("HH:mm:ss.fff")}] {value}{Environment.NewLine}";
                control.AppendText(logLine);
            }
        }

        public TabPage CreateTab()
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

            Console.SetOut(new MyLogger(control));

            return tab;
        }
    }
}
