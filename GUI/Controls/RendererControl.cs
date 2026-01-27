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

    public ThemedFloatNumeric AddNumericField(string name, float startingValue, Action<float> changeCallback)
    {
        // Use FlowLayoutPanel for horizontal layout
        var flowPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 0),
        };

        var label = new Label
        {
            Text = name,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
        };

        var field = new ThemedFloatNumeric
        {
            MinValue = float.MinValue,
            MaxValue = float.MaxValue,
            DecimalMax = 3,
            DragWithinRange = false,
            Value = startingValue,
            Margin = new Padding(0, 0, 0, 0),
            Size = new Size(40, 20),
        };

        field.ValueChanged += (s, e) => changeCallback(field.Value);

        flowPanel.Controls.Add(label);
        flowPanel.Controls.Add(field);
        ControlsPanel.Controls.Add(flowPanel);
        SetControlLocation(flowPanel);
        return field;
    }

    public Slider AddSlider(string name, float min, float max, float startingValue, Action<float> changeCallback)
    {
        var sliderControl = new GLViewerSliderControl();
        sliderControl.Slider.ValueChanged = changeCallback;

        /*
        Vector2 range = new(min, max);
        float Pack(float v) => (v - range.X) / (range.Y - range.X);
        float Unpack(float s) => s * (range.Y - range.X) + range.X;

        var slider = uiControl.AddTrackBar(val =>
        {
            animGraphController.FloatParameters[paramName] = Unpack(val);
        });

        void SetValue(float v) => slider.Slider.Value = Pack(v);
        SetValue(value);
        */

        ControlsPanel.Controls.Add(sliderControl);

        SetControlLocation(sliderControl);

        return sliderControl.Slider;
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

    public Label AddLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
        };
        ControlsPanel.Controls.Add(label);
        SetControlLocation(label);
        return label;
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
