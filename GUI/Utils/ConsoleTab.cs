using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using FastColoredTextBoxNS;
using GUI.Controls;

#nullable disable

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

        private struct LogLine
        {
            public DateTime Time;
            public string Component;
            public string Message;
            public TextStyle Style;
        }

        private static readonly TextStyle TextStyleTime = new(Brushes.DarkGray, null, FontStyle.Regular);
        private static readonly TextStyle TextStyleError = new(Brushes.Orange, Brushes.DarkRed, FontStyle.Regular);
        private static readonly TextStyle TextStyleWarn = new(Brushes.Orange, null, FontStyle.Regular);
        private static readonly TextStyle TextStyleDebug = new(Brushes.LightGreen, null, FontStyle.Regular);

        private CodeTextBox control;
        private MyLogger loggerOut;
        private MyLogger loggerError;
        private readonly Queue<LogLine> LogQueue = new(16);

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

            var style = category switch
            {
                Log.Category.DEBUG => TextStyleDebug,
                Log.Category.WARN => TextStyleWarn,
                Log.Category.ERROR => TextStyleError,
                _ => null,
            };

            var queueEmpty = LogQueue.Count == 0;

            // If we happen to spam console text somewhere, appending every line to console bogs down performance (yay winforms)
            // so accumulate it into a buffer and append it all at once when visibility changes
            LogQueue.Enqueue(new LogLine
            {
                Time = DateTime.Now,
                Component = component,
                Message = message,
                Style = style
            });

            if (queueEmpty && control.Visible)
            {
                control.BeginInvoke(DrainQueue);
            }
        }

        private void DrainQueue()
        {
            if (LogQueue.Count == 0)
            {
                return;
            }

            control.BeginUpdate();

            // Fast path when the queue is too big, because calling AppendText is slow due to some subpar code in the text control resetting timers
            if (LogQueue.Count > 1000)
            {
                var sb = new StringBuilder();

                while (LogQueue.TryDequeue(out var line))
                {
                    sb.Append(CultureInfo.InvariantCulture, $"[{line.Time:HH:mm:ss.fff}] [{line.Component}] ");
                    sb.Append(string.Concat(line.Message, Environment.NewLine));
                }

                control.AppendText(sb.ToString());
                control.EndUpdate();
                ScrollToBottom();
                return;
            }

            while (LogQueue.TryDequeue(out var line))
            {
                var lastLine = control.Lines.Count;

                control.AppendText($"[{line.Time:HH:mm:ss.fff}] [{line.Component}] ", TextStyleTime);
                control.AppendText(string.Concat(line.Message, Environment.NewLine), line.Style);

                // Add fold for multi line strings
                // TODO: For multiline strings, indent them with the time/component size
                var newLines = line.Message.AsSpan().Count('\n');

                if (newLines > 0)
                {
                    var marker = lastLine.ToString(CultureInfo.InvariantCulture);
                    control[lastLine - 1].FoldingStartMarker = marker;
                    control[lastLine - 1 + newLines].FoldingEndMarker = marker;
                }
            }

            control.EndUpdate();
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            var selection = control.Selection;

            if (selection.Start != selection.End)
            {
                return;
            }

            control.GoEnd();
        }

        public TabPage CreateTab()
        {
            var bgColor = Color.FromArgb(37, 37, 37);
            control = new CodeTextBox(null, CodeTextBox.HighlightLanguage.None)
            {
                BackColor = bgColor,
                ForeColor = Color.FromArgb(240, 240, 240),
                Paddings = new Padding(0, 10, 0, 10),
            };
            control.VisibleChanged += VisibleChanged;

            control.AppendText($"- Welcome to Source 2 Viewer v{Application.ProductVersion}{Environment.NewLine}", TextStyleDebug);
            control.AppendText($"- If you are experiencing an issue, try using latest dev build from https://valveresourceformat.github.io/{Environment.NewLine}{Environment.NewLine}");

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

        public void InitializeFont()
        {
            control.Font = CodeTextBox.MonospaceFont;
        }

        private void VisibleChanged(object sender, EventArgs e)
        {
            DrainQueue();
        }

        internal void ClearBuffer()
        {
            LogQueue.Clear();
            control.Clear();
        }
    }
}
