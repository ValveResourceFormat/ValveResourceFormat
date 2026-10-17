using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

partial class RendererControl : UserControl
{
#pragma warning disable CA2213 // Disposable fields should be disposed
    private Control? currentControlsTarget;
    private FlowLayoutPanel? toolbarItems;
    private FlowLayoutPanel? toolbarCenteredItems;
    private FlowLayoutPanel? toolbarRightItems;
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
    /// Builds a row with the name in the left half and the editor beside it, so stacked rows line up.
    /// Trailing controls sit at the right end of the editor column.
    /// </summary>
    public static Control CreatePropertyRow(string name, Control? editor, params Control[] trailing)
        => new PropertyRow(name, editor != null ? [editor] : [], trailing);

    /// <summary>
    /// Builds a row led by a checkbox that carries the name across the full width, with trailing controls
    /// lined up at the right like those of <see cref="CreatePropertyRow"/>.
    /// </summary>
    public static Control CreateCheckBoxRow(CheckBox checkBox, params Control[] trailing)
    {
        checkBox.AutoEllipsis = true;
        return new PropertyRow(null, [checkBox], trailing);
    }

    private const int PropertyRowHeight = 24;

    /// <summary>A compact button sized to line up with the other buttons in a property row column.</summary>
    public static ThemedButton CreateRowButton(string text, Action? onClick = null)
    {
        var button = new ThemedButton
        {
            Text = text,
            AutoSize = false,
        };

        button.Size = new Size(button.AdjustForDPI(46), button.AdjustForDPI(PropertyRowHeight - 4));

        if (onClick != null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    /// <summary>
    /// Runs an action when a button is pressed and again for as long as it is held, at the delay and rate
    /// keys repeat with.
    /// </summary>
    public static void RepeatWhileHeld(ThemedButton button, Action action)
    {
#pragma warning disable CA2000 // Disposed with the button
        var timer = new Timer();
#pragma warning restore CA2000
        button.Disposed += (_, _) => timer.Dispose();

        var pressed = true;

        // Each repeat shows as its own press: the button lets go for half the interval, then presses again
        timer.Tick += (_, _) =>
        {
            // Keyboard speed runs from 2.5 to 30 repeats a second
            timer.Interval = (int)(500f / float.Lerp(2.5f, 30f, SystemInformation.KeyboardSpeed / 31f));

            pressed = !pressed;
            button.ShowPressed(pressed);

            if (pressed)
            {
                action();
            }
        };

        button.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            action();
            pressed = true;
            timer.Interval = (SystemInformation.KeyboardDelay + 1) * 250;
            timer.Start();
        };

        button.MouseUp += (_, _) => timer.Stop();
        button.MouseCaptureChanged += (_, _) => timer.Stop();
    }

    /// <summary>A checkbox for an editor column, optionally with a caption.</summary>
    public static CheckBox CreateRowCheckBox(bool isChecked, Action<bool> changeCallback, string text = "")
    {
        var checkBox = new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
        };

        checkBox.CheckedChanged += (_, _) => changeCallback(checkBox.Checked);
        return checkBox;
    }

    /// <summary>A free-range float field for an editor column.</summary>
    public static ThemedFloatNumeric CreateFloatField(float value, int decimals, Action<float> changeCallback)
    {
        var field = new ThemedFloatNumeric
        {
            MinValue = float.MinValue,
            MaxValue = float.MaxValue,
            DecimalMax = decimals,
            DragWithinRange = false,
            Value = value,
        };

        field.ValueChanged += (_, _) => changeCallback(field.Value);
        return field;
    }

    /// <summary>A slider for an editor column.</summary>
    public static Slider CreateRowSlider()
    {
        var slider = new Slider
        {
            SliderHeight = 4,
            KnobSize = 12,
        };

        slider.Height = slider.AdjustForDPI(18);
        return slider;
    }

    /// <summary>
    /// A slider over a range and a number field to go beside it, for a property row. Typing a value outside
    /// the range widens the slider to include it.
    /// </summary>
    public static (Control Slider, Control Field) CreateRangedFloatEditor(float value, float min, float max, bool wholeNumbers, Action<float> changeCallback)
    {
        var slider = CreateRowSlider();

        var field = new ThemedFloatNumeric
        {
            MinValue = float.MinValue,
            MaxValue = float.MaxValue,
            DecimalMax = wholeNumbers ? 0 : 2,
            DragWithinRange = false,
            Value = value,
        };

        field.Width = field.AdjustForDPI(44);

        void MoveKnob(float newValue)
        {
            min = MathF.Min(min, newValue);
            max = MathF.Max(max, newValue);
            slider.Value = MathUtils.Remap(newValue, min, max);
        }

        MoveKnob(value);

        var fromSlider = false;

        slider.ValueChanged = fraction =>
        {
            var newValue = MathUtils.RemapRange(fraction, 0f, 1f, min, max);
            newValue = wholeNumbers ? MathF.Round(newValue) : newValue;

            fromSlider = true;
            field.Value = newValue;
            fromSlider = false;

            changeCallback(newValue);
        };

        field.ValueChanged += (_, _) =>
        {
            if (fromSlider)
            {
                return;
            }

            MoveKnob(field.Value);
            changeCallback(field.Value);
        };

        return (slider, field);
    }

    /// <summary>A dropdown list for an editor column.</summary>
    public static ThemedComboBox CreateRowComboBox(IEnumerable<string> items, string? selected, Action<string> changeCallback)
    {
        var comboBox = new ThemedComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            TrimStart = true,
        };

        comboBox.Items.AddRange([.. items]);

        // The list opens as wide as its longest option, the box itself shows the trimmed end
        comboBox.DropDown += (_, _) =>
        {
            var widest = comboBox.Items.OfType<string>()
                .Select(item => TextRenderer.MeasureText(item, comboBox.Font).Width)
                .DefaultIfEmpty(0)
                .Max();

            comboBox.DropDownWidth = Math.Max(comboBox.Width, widest + SystemInformation.VerticalScrollBarWidth + comboBox.AdjustForDPI(8));
        };
        comboBox.SelectedItem = selected;
        comboBox.SelectedIndexChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string item)
            {
                changeCallback(item);
            }
        };

        return comboBox;
    }

    /// <summary>
    /// Builds a full-width row of equally wide X/Y/Z fields. The callback receives the vector whenever any field changes.
    /// </summary>
    public static (Control Row, ThemedFloatNumeric[] Fields) CreateVectorRow(Vector3 startingValue, Action<Vector3> changeCallback)
    {
        var fields = new ThemedFloatNumeric[3];

        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = CreateFloatField(startingValue[i], 2, _ => changeCallback(new Vector3(fields[0].Value, fields[1].Value, fields[2].Value)));
        }

        return (new PropertyRow(null, fields, []), fields);
    }

    /// <summary>A dimmed caption that starts a run of related rows.</summary>
    public static Label CreateSectionHeader(string text)
    {
        var label = new Label
        {
            Text = text.ToUpperInvariant(),
            AutoSize = true,
            ForeColor = Themer.CurrentThemeColors.ContrastSoft,
            Margin = Padding.Empty,
            AccessibleRole = AccessibleRole.Grouping,
        };

        // Derived from the sidebar font once parented, since an unparented label only knows the system default
        label.ParentChanged += (_, _) =>
        {
            if (label.Parent is { } parent)
            {
                label.Font = new Font(parent.Font.FontFamily, parent.Font.Size * 0.85f, FontStyle.Bold);
            }
        };
        label.Padding = new Padding(0, label.AdjustForDPI(8), 0, label.AdjustForDPI(2));
        return label;
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
        => AddMultiSelectionControl(name, initializeCallback, changeCallback).CheckedListBox;

    public GLViewerMultiSelectionControl AddMultiSelectionControl(string name, Action<CheckedListBox>? initializeCallback, Action<IEnumerable<string>> changeCallback)
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

        return selectionControl;
    }

    public GLViewerSliderControl AddTrackBar(string name, Action<float> changeCallback, float defaultValue = 0f)
    {
        var trackBar = new GLViewerSliderControl();
        trackBar.Slider.AccessibleName = name;
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

    /// <summary>Adds a button to the toolbar above the viewport, either on its left or in its middle.</summary>
    public ThemedButton AddToolbarButton(string text, Action onClick, bool centered = false)
    {
        var button = new ThemedButton();
        AddToolbarItem(button, text, centered);
        button.Click += (_, _) => onClick();

        return button;
    }

    /// <summary>Adds a button showing an icon instead of a label to the toolbar above the viewport.</summary>
    public ThemedButton AddToolbarButton(Image icon, string accessibleName, Action onClick, bool centered = false)
    {
        var button = AddToolbarButton(string.Empty, onClick, centered);
        button.Image = icon;
        button.AccessibleName = accessibleName;

        // The icon is drawn to fit the height, so the button only has to be a little wider than tall
        button.AutoSize = false;
        button.Size = new Size(this.AdjustForDPI(ToolbarItemHeight * 3 / 2), this.AdjustForDPI(ToolbarItemHeight));

        return button;
    }

    /// <summary>Adds a button that stays pressed while on to the toolbar above the viewport.</summary>
    public ThemedToggleButton AddToolbarToggle(string text, bool initialChecked, Action<bool> onToggled)
    {
        var button = new ThemedToggleButton { Checked = initialChecked };
        AddToolbarItem(button, text);
        button.Toggled += onToggled;

        return button;
    }

    /// <summary>Adds a dropdown on the right of the toolbar above the viewport.</summary>
    /// <param name="changeCallback">Called with the selected item's text and index.</param>
    /// <param name="defaultItemText">Shown in place of the first item while it is selected.</param>
    public ComboBox AddToolbarSelection(Action<string, int> changeCallback, string? defaultItemText = null)
    {
        var comboBox = new ThemedFlatComboBox
        {
            Width = this.AdjustForDPI(180),
            ItemHeight = this.AdjustForDPI(20),
            Margin = new Padding(this.AdjustForDPI(2), 0, this.AdjustForDPI(2), 0),
            DefaultItemText = defaultItemText,
        };

        comboBox.SelectedIndexChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is string selectedItem)
            {
                changeCallback(selectedItem, comboBox.SelectedIndex);
            }
            else if (comboBox.SelectedItem is ThemedComboBoxItem selectedThemedItem)
            {
                changeCallback(selectedThemedItem.Text, comboBox.SelectedIndex);
            }
        };

        EnsureToolbar();
        toolbarRightItems.Controls.Add(comboBox);

        return comboBox;
    }

    // Every item is this tall, so the ones with a label line up with the ones with an icon
    private const int ToolbarItemHeight = 26;

    private void AddToolbarItem(ThemedButton button, string text, bool centered = false)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(0, this.AdjustForDPI(ToolbarItemHeight));
        button.Padding = new Padding(this.AdjustForDPI(6), 0, this.AdjustForDPI(6), 0);
        button.Margin = new Padding(this.AdjustForDPI(2), 0, this.AdjustForDPI(2), 0);

        EnsureToolbar();
        (centered ? toolbarCenteredItems : toolbarItems).Controls.Add(button);
    }

    // Created on first use, so viewers without toolbar items keep the whole height for the viewport
    [MemberNotNull(nameof(toolbarItems), nameof(toolbarCenteredItems), nameof(toolbarRightItems))]
    private void EnsureToolbar()
    {
        if (toolbarItems != null && toolbarCenteredItems != null && toolbarRightItems != null)
        {
            return;
        }

        var padding = this.AdjustForDPI(2);

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = this.AdjustForDPI(ToolbarItemHeight) + padding * 2,
            Padding = new Padding(padding),
            Font = controlsPanel.Font,
        };

        var items = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };

        var centeredItems = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };

        var rightItems = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };

        toolbar.Controls.Add(centeredItems);
        toolbar.Controls.Add(items);
        toolbar.Controls.Add(rightItems);

        void CenterItems(object? sender, EventArgs e)
        {
            // In the middle of the toolbar, unless the items on either side reach that far, the left ones winning
            var middle = (toolbar.ClientSize.Width - centeredItems.Width) / 2;
            var left = Math.Max(items.Right, Math.Min(middle, rightItems.Left - centeredItems.Width));
            centeredItems.Location = new Point(left, padding);
        }

        toolbar.SizeChanged += CenterItems;
        items.SizeChanged += CenterItems;
        centeredItems.SizeChanged += CenterItems;
        rightItems.SizeChanged += CenterItems;

        toolbarItems = items;
        toolbarCenteredItems = centeredItems;
        toolbarRightItems = rightItems;

        // In whichever side of the splitter the viewport is, which preview mode swaps
        var host = glControlContainer.Parent ?? splitContainer.Panel2;
        host.Controls.Add(toolbar);

        // Docking goes from the back, so the viewport fills what is left under the toolbar
        glControlContainer.BringToFront();
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
