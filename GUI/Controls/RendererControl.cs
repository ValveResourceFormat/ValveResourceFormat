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

            splitContainer.SizeChanged += PreviewControls_HandleResize;
            PreviewControls_HandleResize(splitContainer, EventArgs.Empty);
        }
    }

    private void PreviewControls_HandleResize(object? sender, EventArgs e)
    {
        // Respect an explicit HideSidebar() (e.g. node graph previews), which fixes the splitter and collapses it.
        if (splitContainer.IsSplitterFixed)
        {
            return;
        }

        // Matches the non-preview sidebar width (design SplitterDistance / controlsPanel width).
        var controlsWidth = this.AdjustForDPI(220);
        var available = splitContainer.Width - splitContainer.SplitterWidth;

        // Collapse the controls when the viewer (Panel1, left) would be narrower than the controls (Panel2, right).
        if (available - controlsWidth < controlsWidth)
        {
            splitContainer.Panel2Collapsed = true;
            return;
        }

        splitContainer.Panel2Collapsed = false;
        splitContainer.SplitterDistance = available - controlsWidth;
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

    /// <summary>
    /// Shows the previewed file's icon and name as the first item in the controls panel. Used in preview mode,
    /// where there is no tab header to display the file name. Long names are ellipsized.
    /// </summary>
    public void AddPreviewFileName(string fileName, int imageIndex)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = this.AdjustForDPI(32),
            Padding = new Padding(0, 0, splitContainer.SplitterWidth, 0),
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, this.AdjustForDPI(2), 0, this.AdjustForDPI(2)),
        };

        var nameLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = fileName,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, this.AdjustForDPI(4), 0),
        };

        var iconLabel = new Label
        {
            Dock = DockStyle.Left,
            Width = this.AdjustForDPI(28),
            ImageList = AppIcons.ImageList,
            ImageAlign = ContentAlignment.MiddleCenter,
        };

        if (imageIndex >= 0 && imageIndex < AppIcons.ImageList.Images.Count)
        {
            iconLabel.ImageIndex = imageIndex;
        }

        // Accent underline along the bottom, matching the selected tab's underline.
        var underline = new UnstyledPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.AdjustForDPI(2),
            BackColor = Themer.CurrentThemeColors.Accent,
        };

        content.Controls.Add(nameLabel);
        content.Controls.Add(iconLabel);

        header.Controls.Add(content);
        header.Controls.Add(underline);

        // Pin the header above the scrollable controls panel (in its non-scrolling parent)
        var host = controlsPanel.Parent ?? controlsPanel;
        host.Controls.Add(header);
        header.SendToBack();
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

    /// <summary>
    /// Adds a checkbox (for persistent toggles) alongside a momentary "Signal" button (for one-shot
    /// triggers). The checkbox writes its state via <paramref name="changeCallback"/>; the button invokes
    /// <paramref name="signalCallback"/>, which pulses the value true for a single graph update.
    /// </summary>
    public CheckBox AddCheckBoxWithSignal(string name, bool defaultChecked, Action<bool> changeCallback, Action signalCallback)
    {
        var flowPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 0),
        };

        var checkbox = CreateCheckBox(name, defaultChecked, changeCallback);

        var signalButton = new ThemedButton
        {
            Text = "Signal",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 0),
        };
        signalButton.Click += (_, __) => signalCallback();

        flowPanel.Controls.Add(checkbox);
        flowPanel.Controls.Add(signalButton);
        ControlsPanel.Controls.Add(flowPanel);
        SetControlLocation(flowPanel);

        return checkbox.CheckBox;
    }

    /// <summary>
    /// Adds an editor for a target (transform) parameter: a position (X/Y/Z) and a rotation
    /// (Yaw/Pitch/Roll, in degrees). The callback receives the six components whenever any field changes.
    /// </summary>
    public void AddTargetParameter(string name, Action<float[]> changeCallback)
    {
        var label = new Label
        {
            Text = name,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 4, 0, 0),
        };
        ControlsPanel.Controls.Add(label);
        SetControlLocation(label);

        var flowPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 0),
        };

        string[] fieldLabels = ["X", "Y", "Z", "Yaw", "Pitch", "Roll"];
        var fields = new ThemedFloatNumeric[6];

        for (var i = 0; i < fields.Length; i++)
        {
            flowPanel.Controls.Add(new Label
            {
                Text = fieldLabels[i],
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(i == 0 ? 0 : 6, 4, 2, 0),
            });

            var field = new ThemedFloatNumeric
            {
                MinValue = float.MinValue,
                MaxValue = float.MaxValue,
                DecimalMax = 3,
                DragWithinRange = false,
                Value = 0f,
                Margin = new Padding(0, 0, 0, 0),
                Size = new Size(48, 20),
            };

            field.ValueChanged += (_, __) => changeCallback([.. fields.Select(f => f.Value)]);
            fields[i] = field;
            flowPanel.Controls.Add(field);
        }

        ControlsPanel.Controls.Add(flowPanel);
        SetControlLocation(flowPanel);
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

    public GLViewerSliderControl AddTrackBar(Action<float> changeCallback, float defaultValue = 0f)
    {
        var trackBar = new GLViewerSliderControl();
        trackBar.Slider.Value = defaultValue;
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
