using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Utils;

#nullable disable

namespace GUI.Forms
{
    partial class GenericProgressForm : Form
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
                    if (t.Exception != null)
                    {
                        foreach (var exception in t.Exception.Flatten().InnerExceptions)
                        {
                            Log.Error(nameof(GenericProgressForm), exception.ToString());
                        }

                        Log.Error(nameof(GenericProgressForm), "Search existing issues or create a new one here: https://github.com/ValveResourceFormat/ValveResourceFormat/issues");

                        SetProgress($"An exception occured, view console tab for more information. ({t.Exception.InnerException.Message})");

                        // TODO: Throwing doesn't actually display the exception ui
                        throw t.Exception;
                    }

                    if (!t.IsCanceled && IsHandleCreated)
                    {
                        Invoke((Action)Close);
                    }
                });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            cancellationTokenSource.Cancel();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
