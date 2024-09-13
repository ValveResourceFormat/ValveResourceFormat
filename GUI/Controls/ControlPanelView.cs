using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GUI.Controls
{
    class ControlPanelView : UserControl
    {
        protected virtual Panel ControlsPanel { get; }

        public ControlPanelView()
        {
            Dock = DockStyle.Fill;
        }

        public void AddControl(Control control)
        {
            ControlsPanel.Controls.Add(control);
            SetControlLocation(control);
        }

        public BetterCheckBox AddCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
        {
            var checkbox = new GLViewerCheckboxControl(name, defaultChecked);
            checkbox.BetterCheckBox.CheckedChanged += (_, __) =>
            {
                changeCallback(checkbox.BetterCheckBox.Checked);
            };

            ControlsPanel.Controls.Add(checkbox);

            SetControlLocation(checkbox);

            return checkbox.BetterCheckBox;
        }

        public ComboBox AddSelection(string name, Action<string, int> changeCallback)
        {
            var selectionControl = new GLViewerSelectionControl(name);

            ControlsPanel.Controls.Add(selectionControl);

            SetControlLocation(selectionControl);

            selectionControl.ComboBox.SelectedIndexChanged += (_, __) =>
            {
                selectionControl.Refresh();
                changeCallback(selectionControl.ComboBox.SelectedItem as string, selectionControl.ComboBox.SelectedIndex);
            };

            return selectionControl.ComboBox;
        }

        public BetterCheckedListBox AddMultiSelection(string name, Action<BetterCheckedListBox> initializeCallback, Action<IEnumerable<string>> changeCallback)
        {
            var selectionControl = new GLViewerMultiSelectionControl(name);

            initializeCallback?.Invoke(selectionControl.BetterCheckedListBox);

            ControlsPanel.Controls.Add(selectionControl);

            SetControlLocation(selectionControl);

            selectionControl.BetterCheckedListBox.ItemCheck += (_, __) =>
            {
                // ItemCheck is called before CheckedItems is updated
                BeginInvoke((MethodInvoker)(() =>
                {
                    selectionControl.Refresh();
                    changeCallback(selectionControl.BetterCheckedListBox.CheckedItems.OfType<string>());
                }));
            };

            selectionControl.BorderStyle = BorderStyle.FixedSingle;

            return selectionControl.BetterCheckedListBox;
        }

        public GLViewerTrackBarControl AddTrackBar(Action<int> changeCallback)
        {
            var trackBar = new GLViewerTrackBarControl();
            trackBar.TrackBar.Scroll += (_, __) =>
            {
                changeCallback(trackBar.TrackBar.Value);
            };

            ControlsPanel.Controls.Add(trackBar);

            SetControlLocation(trackBar);

            return trackBar;
        }

        public void AddDivider()
        {
            var panel = new Panel
            {
                AutoSize = true,
                Padding = new Padding(0, 10, 0, 10),
            };

            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SystemColors.ActiveBorder,
            };

            panel.Controls.Add(label);
            ControlsPanel.Controls.Add(panel);
            SetControlLocation(panel);
        }

        private static void SetControlLocation(Control control)
        {
            control.Dock = DockStyle.Top;
            control.BringToFront();
        }
    }
}
