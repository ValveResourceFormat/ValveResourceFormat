using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

partial class RendererControl : UserControl
{
#pragma warning disable CA2213 // Disposable fields should be disposed
    private Control? currentControlsTarget;
#pragma warning restore CA2213 // Disposable fields should be disposed
    private Control ControlsPanel => currentControlsTarget ?? controlsPanel;
    public Control GLControlContainer => glControlContainer;
    private readonly Dictionary<string, Panel> namedGroups = [];

    public RendererControl(bool isPreview = false)
    {
        InitializeComponent();
        currentControlsTarget = controlsPanel;

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

    public static GLViewerCheckboxControl CreateCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
    {
        var checkbox = new GLViewerCheckboxControl(name, defaultChecked);
        checkbox.CheckBox.CheckedChanged += (_, __) =>
        {
            changeCallback(checkbox.CheckBox.Checked);
        };

        return checkbox;
    }

    public CheckBox AddCheckBox(string name, bool defaultChecked, Action<bool> changeCallback)
    {
        var checkbox = CreateCheckBox(name, defaultChecked, changeCallback);
        AddControl(checkbox);

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

    public CheckedListBox AddMultiSelection(string name, Action<CheckedListBox>? initializeCallback, Action<IEnumerable<string>> changeCallback)
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

    public GLViewerSliderControl AddTrackBar(Action<float> changeCallback)
    {
        var trackBar = new GLViewerSliderControl();
        trackBar.Slider.ValueChanged = changeCallback;

        ControlsPanel.Controls.Add(trackBar);

        SetControlLocation(trackBar);

        return trackBar;
    }

    public static Panel CreateFloatInput(string name, Action<float> onValChanged, float startValue = 0, float minValue = 0, float maxValue = 1000)
    {
        var panel = new Panel();

        var label = new Label
        {
            Text = name,
            Dock = DockStyle.Fill,
        };

        var numeric = new ThemedFloatNumeric
        {
            MinValue = minValue,
            MaxValue = maxValue,
            DragWithinRange = true,
            DragDistance = 600,
            Value = startValue,
            Dock = DockStyle.Right,
            Padding = new Padding(0, 0, 4, 0),
        };

        numeric.Width = numeric.AdjustForDPI(50);

        numeric.ValueChanged += (obj, e) =>
        {
            onValChanged(((ThemedFloatNumeric)obj!).Value);
        };

        panel.Controls.Add(label);
        panel.Controls.Add(numeric);
        panel.Height = panel.AdjustForDPI(22);

        return panel;
    }

    public ControlGroup BeginGroup(string title)
    {
        if (!namedGroups.TryGetValue(title, out var content))
        {
            var groupPanel = new Panel { AutoSize = true, Padding = new(0, 2, 0, 2) };
            var groupBox = new ThemedGroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new(4, 8, 4, 4),
            };
            content = new Panel { Dock = DockStyle.Top, AutoSize = true };

            groupBox.Controls.Add(content);
            groupPanel.Controls.Add(groupBox);
            controlsPanel.Controls.Add(groupPanel);
            SetControlLocation(groupPanel);

            namedGroups[title] = content;
        }

        currentControlsTarget = content;
        return new ControlGroup(this);
    }

    public ref struct ControlGroup(RendererControl? owner)
    {
        public void Dispose()
        {
            owner?.currentControlsTarget = null;
            owner = null;
        }
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
