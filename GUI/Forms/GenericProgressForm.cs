using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Forms
{
    partial class GenericProgressForm : ThemedForm, IProgress<string>
    {
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(500);

        private readonly CancellationTokenSource cancellationTokenSource = new();
        private string? pendingText;
        private int pendingBarValue = -1;
        private int pendingBarMax = -1;
        private long startTimestamp;
        private string? baseTitle;
        private int updateQueued;
        private long lastUpdate;
        private long lastTextUpdate;

        public Func<CancellationToken, Task>? OnProcess { get; set; }

        public GenericProgressForm()
        {
            InitializeComponent();
        }

        public void Report(string value) => SetProgress(value);

        /// <summary>
        /// Stores the latest status text. The label is updated at most every 500ms and only the most recent text is shown,
        /// so this is safe to call from any thread at any rate.
        /// </summary>
        public void SetProgress(string text)
        {
            Volatile.Write(ref pendingText, text);
            QueueUpdate(isText: true);
        }

        /// <summary>
        /// Stores the latest progress bar value, applied together with the status text.
        /// </summary>
        public void SetBarValue(int value)
        {
            Volatile.Write(ref pendingBarValue, value);
            QueueUpdate(isText: false);
        }

        /// <summary>
        /// Switches the bar from marquee to a determinate bar with the given maximum.
        /// </summary>
        public void SetBarMax(int count)
        {
            Volatile.Write(ref pendingBarMax, count);
            QueueUpdate(isText: false);
        }

        private void QueueUpdate(bool isText)
        {
            if (!IsHandleCreated || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();

            // Text is throttled separately from bar values, otherwise a bar update posted just before
            // the first text would consume the slot and leave the text pending until the next file
            var last = isText ? Volatile.Read(ref lastTextUpdate) : Volatile.Read(ref lastUpdate);

            if (Stopwatch.GetElapsedTime(last, now) < UpdateInterval)
            {
                return;
            }

            if (isText)
            {
                Volatile.Write(ref lastTextUpdate, now);
            }

            Volatile.Write(ref lastUpdate, now);

            if (Interlocked.Exchange(ref updateQueued, 1) != 0)
            {
                // An apply is already queued and will pick up the pending values
                return;
            }

            try
            {
                BeginInvoke(ApplyPendingUpdate);
            }
            catch (InvalidOperationException)
            {
                // Handle was destroyed between the check and the post
                Volatile.Write(ref updateQueued, 0);
            }
        }

        private void ApplyPendingUpdate()
        {
            Volatile.Write(ref updateQueued, 0);

            if (IsDisposed)
            {
                return;
            }

            var text = Interlocked.Exchange(ref pendingText, null);

            if (text != null)
            {
                extractStatusLabel.Text = text;
            }

            var barMax = Interlocked.Exchange(ref pendingBarMax, -1);

            if (barMax >= 0)
            {
                extractProgressBar.Style = ProgressBarStyle.Blocks;
                extractProgressBar.Maximum = barMax;
            }

            var barValue = Interlocked.Exchange(ref pendingBarValue, -1);

            if (barValue >= 0)
            {
                extractProgressBar.Value = Math.Min(barValue, extractProgressBar.Maximum);
            }

            UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (baseTitle == null)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var value = extractProgressBar.Value;
            var max = extractProgressBar.Maximum;

            if (extractProgressBar.Style != ProgressBarStyle.Blocks || value <= 0 || max <= 0)
            {
                Text = $"{baseTitle} ({FormatTime(elapsed)} elapsed)";
                return;
            }

            var remaining = elapsed * (max - value) / value;
            Text = $"{baseTitle} {value * 100 / max}% ({FormatTime(elapsed)} elapsed, ~{FormatTime(remaining)} left)";
        }

        private static string FormatTime(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss")
                : time.ToString(@"m\:ss");
        }

        protected override void OnShown(EventArgs e)
        {
            baseTitle = Text;
            startTimestamp = Stopwatch.GetTimestamp();

            // Show anything that was set before the handle existed
            ApplyPendingUpdate();

            Task.Run(
                () => OnProcess?.Invoke(cancellationTokenSource.Token) ?? Task.CompletedTask,
                cancellationTokenSource.Token)
                .ContinueWith((t) =>
                {
                    if (!IsHandleCreated)
                    {
                        return;
                    }

                    if (t.Exception != null)
                    {
                        foreach (var exception in t.Exception.Flatten().InnerExceptions)
                        {
                            Program.ShowError(exception);
                        }
                    }

                    if (!t.IsCanceled)
                    {
                        Invoke(Close);
                    }
                });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancellationTokenSource.Cancel();
            base.OnFormClosing(e);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
