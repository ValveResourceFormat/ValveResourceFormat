using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class GenericProgressForm : Form
    {
        private CancellationTokenSource cancellationTokenSource;
        public event EventHandler OnProcess;

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

        protected override void OnShown(EventArgs e)
        {
            Task.Run(
                () => OnProcess?.Invoke(this, new EventArgs()),
                cancellationTokenSource.Token)
                .ContinueWith((t) =>
                {
                    if (t.Exception != null)
                    {
                        Console.Error.WriteLine(t.Exception);
                        Console.Error.WriteLine("Search existing issues or create a new one here: https://github.com/SteamDatabase/ValveResourceFormat/issues");
                        SetProgress($"An exception occured, view console tab for more information. ({t.Exception.Message})");

                        // TODO: Throwing doesn't actually display the exception ui
                        throw t.Exception;
                    }

                    if (!t.IsCanceled)
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
