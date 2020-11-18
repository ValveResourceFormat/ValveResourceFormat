using System.Windows.Forms;

namespace GUI.Controls
{
    public partial class GLViewerTrackBarControl : UserControl
    {
        public TrackBar TrackBar => trackBar;

        private GLViewerTrackBarControl()
        {
            InitializeComponent();
        }

        public GLViewerTrackBarControl(string name)
            : this()
        {
            trackBarLabel.Text = name;
            trackBar.Value = 0;
        }
    }
}
