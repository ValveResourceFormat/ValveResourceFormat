using System;
using System.Globalization;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;

namespace GUI.Controls
{
    public partial class GLViewerControl : UserControl
    {
        public GLControl GLControl => glControl;
        public Panel ViewerControls => viewerControls;

        public GLViewerControl()
        {
            InitializeComponent();

            Dock = DockStyle.Fill;
        }

        public void SetFps(double fps)
        {
            labelFpsNumber.Text = Math.Round(fps).ToString(CultureInfo.InvariantCulture);
        }
    }
}
