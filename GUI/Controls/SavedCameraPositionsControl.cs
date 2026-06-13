using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    partial class SavedCameraPositionsControl : UserControl
    {
        public class RestoreCameraRequestEvent : EventArgs
        {
            public required string Camera { get; init; }
        }

        public event EventHandler? SaveCameraRequest;
        public event EventHandler<RestoreCameraRequestEvent>? RestoreCameraRequest;
        public event EventHandler<bool>? GetOrSetPositionFromClipboardRequest;

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            BackColor = Parent?.BackColor ?? Themer.CurrentThemeColors.AppMiddle;
        }

        public SavedCameraPositionsControl()
        {
            InitializeComponent();

            Settings.RefreshCamerasOnSave += RefreshSavedPositions;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveCameraRequest?.Invoke(this, EventArgs.Empty);
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            var camera = cmbPositions.SelectedItem?.ToString();

            if (camera is null)
            {
                return;
            }

            var ev = new RestoreCameraRequestEvent
            {
                Camera = camera,
            };

            RestoreCameraRequest?.Invoke(this, ev);
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            var camera = cmbPositions.SelectedItem?.ToString();

            if (camera is null)
            {
                return;
            }

            Settings.Config.SavedCameras.Remove(camera);
            Settings.InvokeRefreshCamerasOnSave();
        }

        private void RefreshSavedPositions(object? sender, EventArgs e) => RefreshSavedPositions();

        public void RefreshSavedPositions()
        {
            var previousCamera = cmbPositions.SelectedText;

            cmbPositions.BeginUpdate();
            cmbPositions.Items.Clear();

            if (Settings.Config.SavedCameras.Count == 0)
            {
                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                cmbPositions.Enabled = false;
                cmbPositions.Items.Add("(no saved cameras)");
                cmbPositions.SelectedIndex = 0;
            }
            else
            {
                btnRestore.Enabled = true;
                btnDelete.Enabled = true;
                cmbPositions.Enabled = true;
                var selectedIndex = 0;

                foreach (var kvp in Settings.Config.SavedCameras)
                {
                    cmbPositions.Items.Add(kvp.Key);

                    if (kvp.Key == previousCamera)
                    {
                        selectedIndex = cmbPositions.Items.Count - 1;
                    }
                }

                cmbPositions.SelectedIndex = selectedIndex;
            }

            cmbPositions.EndUpdate();
        }

        private void BtnSetPos_Click(object sender, EventArgs e)
        {
            GetOrSetPositionFromClipboardRequest?.Invoke(sender, true);
        }

        private void BtnGetPos_Click(object sender, EventArgs e)
        {
            GetOrSetPositionFromClipboardRequest?.Invoke(sender, false);
        }
    }
}
