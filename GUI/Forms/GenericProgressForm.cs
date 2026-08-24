using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Forms
{
    partial class GenericProgressForm : ThemedForm, IProgress<string>
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 500 };
        private string? pendingText;
        private int pendingBarValue = -1;
        private int pendingBarMax = -1;
        private long startTimestamp;
        private string? baseTitle;
        private bool completed;

        public Func<CancellationToken, Task>? OnProcess { get; set; }

        /// <summary>
        /// When set, the dialog stays open after the work completes and shows a completed state instead of closing itself.
        /// </summary>
        public bool StayOpenOnCompletion { get; set; }

        /// <summary>
        /// Time elapsed since the dialog was shown.
        /// </summary>
        internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(startTimestamp);

        public GenericProgressForm()
        {
            InitializeComponent();

            updateTimer.Tick += (_, _) => ApplyPendingUpdate();
        }

        public void Report(string value) => SetProgress(value);

        /// <summary>
        /// Stores the latest status text. Pending values are applied on a timer and only the most recent text is shown,
        /// so this is safe to call from any thread at any rate.
        /// </summary>
        public void SetProgress(string text)
        {
            Volatile.Write(ref pendingText, text);
        }

        /// <summary>
        /// Stores the latest progress bar value, applied together with the status text.
        /// </summary>
        public void SetBarValue(int value)
        {
            Volatile.Write(ref pendingBarValue, value);
        }

        /// <summary>
        /// Switches the bar from marquee to a determinate bar with the given maximum.
        /// </summary>
        public void SetBarMax(int count)
        {
            Volatile.Write(ref pendingBarMax, count);
        }

        private void ApplyPendingUpdate()
        {
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

            // A single unit of work has no meaningful progression, keep the marquee
            if (barMax > 1)
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

            if (completed)
            {
                Text = $"{baseTitle} (completed in {FormatTime(elapsed)})";
                return;
            }

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

        internal static string FormatTime(TimeSpan time)
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
            updateTimer.Start();

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
                        Invoke(StayOpenOnCompletion ? ShowCompleted : Close);
                    }
                });
        }

        private void ShowCompleted()
        {
            updateTimer.Stop();
            completed = true;

            // Flush the last reported text and render the completed title
            ApplyPendingUpdate();

            extractProgressBar.Style = ProgressBarStyle.Blocks;
            extractProgressBar.Value = extractProgressBar.Maximum;
            cancelButton.Text = "Close";

            // Let Escape close the dialog once the work is done
            CancelButton = cancelButton;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            updateTimer.Stop();
            cancellationTokenSource.Cancel();
            base.OnFormClosing(e);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
