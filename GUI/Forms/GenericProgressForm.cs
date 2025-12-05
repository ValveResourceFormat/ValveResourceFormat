using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#nullable disable

namespace GUI.Forms
{
    partial class GenericProgressForm : ThemedForm
    {
        private CancellationTokenSource cancellationTokenSource;
        public event EventHandler<CancellationToken> OnProcess;

        public GenericProgressForm()
        {
            InitializeComponent();

            cancellationTokenSource = new CancellationTokenSource();
        }

        public void SetProgress(string text)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            Invoke((Action)(() =>
            {
                extractStatusLabel.Text = text;
            }));
        }

        public void SetBarValue(int value)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            extractProgressBar.Value = value;
        }

        public void SetBarMax(int count)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            extractProgressBar.Style = ProgressBarStyle.Blocks;
            extractProgressBar.Maximum = count;
        }

        protected override void OnShown(EventArgs e)
        {
            Task.Run(
                () => OnProcess?.Invoke(this, cancellationTokenSource.Token),
                cancellationTokenSource.Token)
                .ContinueWith((t) =>
                {
                    if (extractProgressBar.Style != ProgressBarStyle.Blocks && IsHandleCreated)
                    {
                        Invoke(() =>
                        {
                            extractProgressBar.Style = ProgressBarStyle.Blocks;
                            extractProgressBar.Value = extractProgressBar.Maximum;
                        });
                    }

                    if (t.Exception != null)
                    {
                        var exceptions = t.Exception.Flatten().InnerExceptions;

                        SetProgress($"An exception occurred, view console tab for more information. ({(exceptions.Count > 0 ? exceptions[0].Message : t.Exception.InnerException.Message)})");

                        foreach (var exception in exceptions)
                        {
                            Program.ShowError(exception);
                        }
                    }

                    if (!t.IsCanceled && IsHandleCreated)
                    {
                        Invoke((Action)Close);
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
