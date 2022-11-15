using System;
using System.Windows.Forms;

namespace GUI.Controls
{
    public partial class GLViewerTrackBarControl : UserControl
    {
        public TrackBar TrackBar => trackBar;
        public bool IgnoreValueChanged { get; private set; }

        private GLViewerTrackBarControl()
        {
            InitializeComponent();
        }

        public GLViewerTrackBarControl(string name)
            : this()
        {
            IgnoreValueChanged = false;
            trackBarLabel.Text = name;
            trackBar.Value = 0;
        }

        public void UpdateValueSilently(int value)
        {
            IgnoreValueChanged = true;
            trackBar.Value = value;
            IgnoreValueChanged = false;
        }

        private void OnTrackVolumeMouseDown(object sender, MouseEventArgs e)
        {
            double dblValue;

            // Jump to the clicked location
            dblValue = (double)e.X / (double)trackBar.Width * (trackBar.Maximum - trackBar.Minimum);
            trackBar.Value = Convert.ToInt32(dblValue);
        }
    }
}
