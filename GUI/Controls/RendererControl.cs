using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

partial class RendererControl : UserControl
{
    private Control ControlsPanel => controlsPanel;
    public Control GLControlContainer => glControlContainer;

    public RendererControl(bool isPreview = false)
    {
        InitializeComponent();

        if (isPreview)
        {
            splitContainer.SuspendLayout();
            splitContainer.Panel1.Controls.Clear();
            splitContainer.Panel2.Controls.Clear();
            splitContainer.Panel1.Controls.Add(glControlContainer);
            splitContainer.Panel2.Controls.Add(controlsPanel);
            splitContainer.FixedPanel = FixedPanel.Panel2;
            splitContainer.ResumeLayout();
        }
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        Themer.ThemeControl(this);
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

            if (selectionControl.ComboBox.SelectedItem is string selectedItem)
            {
                changeCallback(selectedItem, selectionControl.ComboBox.SelectedIndex);
            }
            else if (selectionControl.ComboBox.SelectedItem is ThemedComboBoxItem selectedThemedItem)
            {
                changeCallback(selectedThemedItem.Text, selectionControl.ComboBox.SelectedIndex);
            }
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
            if (selectionControl.CheckedListBox.Items[e.Index] is string changedItem)
            {
                var checkedItems = selectionControl.CheckedListBox.CheckedItems.OfType<string>().ToHashSet();

                if (e.NewValue == CheckState.Checked)
                {
                    checkedItems.Add(changedItem);
                }
                else if (e.NewValue == CheckState.Unchecked)
                {
                    checkedItems.Remove(changedItem);
                }

                changeCallback(checkedItems);
            }
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

    public void SetMoveSpeed(string text)
    {
        moveSpeed.Text = text;
    }

    public void UseWideSplitter()
    {
        // Do not change the splitter distance if the controls got swapped for preview
        if (splitContainer.FixedPanel == FixedPanel.Panel2)
        {
            return;
        }

        splitContainer.SplitterDistance = 450;
    }

    public void HideSidebar()
    {
        splitContainer.IsSplitterFixed = true;

        if (splitContainer.FixedPanel == FixedPanel.Panel2)
        {
            splitContainer.Panel2Collapsed = true;
        }
        else
        {
            splitContainer.Panel1Collapsed = true;
        }
    }

    private static void SetControlLocation(Control control)
    {
        control.Dock = DockStyle.Top;
        control.BringToFront();
    }
}
