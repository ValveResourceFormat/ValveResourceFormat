using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Forms
{
    /// <summary>
    /// Progress dialog for the custom VMDL extractor. Unlike <see cref="GenericProgressForm"/>, this
    /// keeps a scrolling, resizable log of every line the extractor reports, since a single model
    /// export is chatty (dozens of per-file "extracting X" lines) and the user wants to see them go
    /// by rather than a single status line that jumps around.
    /// </summary>
    public partial class CustomVmdlExtractProgressForm : ThemedForm, IProgress<string>
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 100 };
        private readonly Lock pendingLinesLock = new();
        private readonly StringBuilder pendingLines = new();
        private long startTimestamp;
        private string? baseTitle;
        private bool completed;
        private Task? workCompletion;

        public Func<CancellationToken, Task>? OnProcess { get; set; }

        /// <summary>
        /// When set, the dialog stays open after the work completes and shows a completed state instead of closing itself.
        /// </summary>
        public bool StayOpenOnCompletion { get; set; }

        /// <summary>
        /// Time elapsed since the dialog was shown.
        /// </summary>
        internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(startTimestamp);

        /// <summary>
        /// Completes once the work has actually stopped, which on cancellation is after the dialog has already closed.
        /// Await this before disposing anything the work reads from. Never faults, failures are reported to the user.
        /// </summary>
        internal Task WorkCompletion => workCompletion ?? Task.CompletedTask;

        public CustomVmdlExtractProgressForm()
        {
            InitializeComponent();

            updateTimer.Tick += (_, _) => FlushPendingLines();
        }

        public void Report(string value) => AppendLine(value);

        /// <summary>
        /// Queues a line to be appended to the log. Safe to call from any thread at any rate: lines
        /// are batched and flushed to the text box together on a timer, so a chatty extraction does
        /// not turn into one UI thread hop per line.
        /// </summary>
        public void AppendLine(string text)
        {
            lock (pendingLinesLock)
            {
                pendingLines.AppendLine(text);
            }
        }

        private void FlushPendingLines()
        {
            if (IsDisposed)
            {
                return;
            }

            string? toAppend = null;

            lock (pendingLinesLock)
            {
                if (pendingLines.Length > 0)
                {
                    toAppend = pendingLines.ToString();
                    pendingLines.Clear();
                }
            }

            if (toAppend != null)
            {
                logTextBox.AppendText(toAppend);
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

            Text = completed
                ? $"{baseTitle} (completed in {GenericProgressForm.FormatTime(elapsed)})"
                : $"{baseTitle} ({GenericProgressForm.FormatTime(elapsed)} elapsed)";
        }

        protected override void OnShown(EventArgs e)
        {
            baseTitle = Text;
            startTimestamp = Stopwatch.GetTimestamp();

            FlushPendingLines();
            updateTimer.Start();

            workCompletion = Task.Run(
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
                            if (exception is not OperationCanceledException)
                            {
                                Program.ShowError(exception);
                            }
                        }
                    }

                    try
                    {
                        Invoke(OnWorkFinished);
                    }
                    catch (InvalidOperationException)
                    {
                        // The dialog was destroyed before the worker stopped, nothing left to update
                    }
                });
        }

        private void OnWorkFinished()
        {
            // Cancelling already closed the dialog, there is no completed state to show for work that did not finish
            if (cancellationTokenSource.IsCancellationRequested || !StayOpenOnCompletion)
            {
                Close();
                return;
            }

            ShowCompleted();
        }

        private void ShowCompleted()
        {
            updateTimer.Stop();
            completed = true;

            // Flush the last reported lines and render the completed title
            FlushPendingLines();

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
