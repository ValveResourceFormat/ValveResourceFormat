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
