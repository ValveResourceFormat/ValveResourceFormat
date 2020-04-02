using System;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls
{
    public partial class SavedCameraPositionsControl : UserControl
    {
        public event EventHandler SaveCameraRequest;
        public event EventHandler<string> RestoreCameraRequest;

        public SavedCameraPositionsControl()
        {
            InitializeComponent();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveCameraRequest?.Invoke(this, new EventArgs());
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            RestoreCameraRequest?.Invoke(this, cmbPositions.SelectedItem.ToString());
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            Settings.Config.SavedCameras.Remove(cmbPositions.SelectedItem.ToString());
            Settings.Save();

            cmbPositions.Items.RemoveAt(cmbPositions.SelectedIndex);

            if (cmbPositions.Items.Count == 0)
            {
                btnRestore.Enabled = false;
                btnDelete.Enabled = false;
                cmbPositions.Enabled = false;
                cmbPositions.Items.Add("(no saved cameras)");
                cmbPositions.SelectedIndex = 0;
            }
            else
            {
                cmbPositions.SelectedIndex = cmbPositions.Items.Count - 1;
            }
        }

        public void RefreshSavedPositions()
        {
            var previousIndex = cmbPositions.SelectedIndex;

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

                foreach (var kvp in Settings.Config.SavedCameras)
                {
                    cmbPositions.Items.Add(kvp.Key);
                }

                if (previousIndex >= 0)
                {
                    cmbPositions.SelectedIndex = previousIndex;
                }
                else
                {
                    cmbPositions.SelectedIndex = 0;
                }
            }
        }

        private void CmbPositions_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbPositions.SelectedIndex >= 0)
            {
                btnRestore.Enabled = true;
                btnDelete.Enabled = true;
            }
        }
    }
}
