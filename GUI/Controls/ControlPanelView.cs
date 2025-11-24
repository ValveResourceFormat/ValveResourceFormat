using System.Drawing;
using System.Linq;
using System.Windows.Forms;

#nullable disable

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

        public CheckBox AddCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
        {
            var checkbox = new GLViewerCheckboxControl(name, defaultChecked);
            checkbox.CheckBox.CheckedChanged += (_, __) =>
            {
                changeCallback(checkbox.CheckBox.Checked);
            };

            ControlsPanel.Controls.Add(checkbox);

            SetControlLocation(checkbox);

            return checkbox.CheckBox;
        }

        public ComboBox AddSelection(string name, Action<string, int> changeCallback, bool horizontal = false, bool fill = false)
        {
            var selectionControl = new GLViewerSelectionControl(name, horizontal, fill);

            ControlsPanel.Controls.Add(selectionControl);

            SetControlLocation(selectionControl);

            selectionControl.ComboBox.SelectedIndexChanged += (_, __) =>
            {
                selectionControl.Refresh();
                changeCallback(selectionControl.ComboBox.SelectedItem as string, selectionControl.ComboBox.SelectedIndex);
            };

            return selectionControl.ComboBox;
        }

        public CheckedListBox AddMultiSelection(string name, Action<CheckedListBox> initializeCallback, Action<IEnumerable<string>> changeCallback)
        {
            var selectionControl = new GLViewerMultiSelectionControl(name);

            initializeCallback?.Invoke(selectionControl.CheckedListBox);

            ControlsPanel.Controls.Add(selectionControl);

            SetControlLocation(selectionControl);

            selectionControl.CheckedListBox.ItemCheck += (_, e) =>
            {
                // Manually calculate the new checked items since ItemCheck is called before CheckedItems is updated
                var checkedItems = selectionControl.CheckedListBox.CheckedItems.OfType<string>().ToHashSet();
                var changedItem = selectionControl.CheckedListBox.Items[e.Index] as string;

                if (e.NewValue == CheckState.Checked)
                {
                    checkedItems.Add(changedItem);
                }
                else if (e.NewValue == CheckState.Unchecked)
                {
                    checkedItems.Remove(changedItem);
                }

                changeCallback(checkedItems);
            };

            return selectionControl.CheckedListBox;
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
