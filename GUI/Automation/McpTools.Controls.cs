#if DEBUG
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;

namespace GUI.Automation;

/// <summary>
/// Tools that read and change the controls in a viewer's sidebar. A change goes through the control
/// itself, so the sidebar shows it and the viewer runs the same callback as a click.
/// </summary>
internal sealed partial class McpTools
{
    /// <param name="Label">What the sidebar calls it, which is how a tool names it.</param>
    /// <param name="Group">The title of the group box it is in, or null outside any.</param>
    private sealed record SidebarControl(Control Control, string Label, string? Group);

    [Tool("List the controls in a tab's sidebar and in the toolbar above its viewport, top to bottom, each with its group and state: checkboxes, toggle buttons, dropdowns with their items, checked lists such as world layers and mesh groups, sliders from 0 to 1, number inputs with their range, vectors such as material and animation graph parameters, buttons, and text such as readouts. Render modes, world layers and physics groups are among them.")]
    private static List<object> ListControls(GLBaseControl viewer, [Description("Include the items of dropdowns and checked lists, which can run to hundreds of animations.")] bool items = false)
        => SidebarControls(viewer).Select(control => DescribeControl(control, items)).ToList();

    [Tool("Change a control in a tab's sidebar or toolbar as a user would: check a checkbox, check an item of a checked list, select a dropdown item, move a slider, set a number input or vector, turn a toggle button on or off, or press a button. A choice that moves the camera, such as a map camera, lands it at once.", Redraws = true)]
    private static object SetControl(
        GLBaseControl viewer,
        [Description("Label of the control, from list_controls.")] string name,
        [Description("Group the control is in, needed only when two controls have the same label.")] string? group = null,
        [Description("The item of a checked list to check or uncheck.")] string? item = null,
        [Description("true or false for a checkbox, toggle button or checked list item, the item text or its index for a dropdown, a number for a slider or number input, an array of numbers for a vector, nothing for a button.")] JsonNode? value = null)
    {
        var matches = SidebarControls(viewer).Where(control => control.Label.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (group == null || string.Equals(control.Group, group, StringComparison.OrdinalIgnoreCase))).ToList();

        var control = matches switch
        {
            [var only] => only,
            [] => throw new ToolException($"No control named '{name}'{(group == null ? null : $" in group '{group}'")}. list_controls lists them."),
            _ => throw new ToolException($"'{name}' is in more than one group: {string.Join(", ", matches.Select(static match => match.Group ?? "none"))}. Pass 'group'."),
        };

        if (!control.Control.Visible || !control.Control.Enabled)
        {
            throw new ToolException($"'{name}' is {(control.Control.Visible ? "disabled" : "hidden")}, so a user could not change it.");
        }

        T Value<T>(string expected)
        {
            try
            {
                return (value ?? throw new JsonException()).Deserialize<T>(Json) ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw new ArgumentException($"'value' must be {expected} for '{name}'.");
            }
        }

        var camera = (viewer as GLSceneViewer)?.Input.Camera;
        var (location, angles) = (camera?.Location, camera?.GetQAngle());

        switch (control.Control)
        {
            case CheckBox checkBox:
                checkBox.Checked = Value<bool>("true or false");
                break;

            case CheckedListBox list:
                var index = list.FindStringExact(item ?? throw new ArgumentException($"Pass the 'item' of '{name}' to change."));
                list.SetItemChecked(index >= 0 ? index : throw new ToolException($"'{name}' has no item '{item}'."), Value<bool>("true or false"));
                break;

            case ComboBox comboBox when value?.GetValueKind() == JsonValueKind.Number:
                var selected = Value<int>("an index");
                comboBox.SelectedIndex = selected >= 0 && selected < comboBox.Items.Count && !IsHeader(comboBox, selected)
                    ? selected
                    : throw new ToolException($"'{name}' has no item at index {selected}.");
                break;

            case ComboBox comboBox:
                var text = Value<string>("an item or its index");
                var found = Enumerable.Range(0, comboBox.Items.Count).FirstOrDefault(i => !IsHeader(comboBox, i) && ItemText(comboBox, i).Equals(text, StringComparison.OrdinalIgnoreCase), -1);
                comboBox.SelectedIndex = found >= 0 ? found : throw new ToolException($"'{name}' has no item '{text}'.");
                break;

            case Slider slider:
                slider.Value = Math.Clamp(Value<float>("a number from 0 to 1"), 0f, 1f);
                slider.ValueChanged?.Invoke(slider.Value);
                break;

            case ThemedFloatNumeric number:
                number.Value = Math.Clamp(Value<float>("a number"), number.MinValue, number.MaxValue);
                break;

            case ThemedIntNumeric number:
                number.Value = Math.Clamp(Value<int>("an integer"), number.MinValue, number.MaxValue);
                break;

            case PropertyRow vector:
                var components = vector.Editors.OfType<ThemedFloatNumeric>().ToList();
                var values = Value<float[]>($"an array of {components.Count} numbers");

                if (values.Length != components.Count)
                {
                    throw new ArgumentException($"'value' must be an array of {components.Count} numbers for '{name}'.");
                }

                for (var i = 0; i < values.Length; i++)
                {
                    components[i].Value = values[i];
                }

                break;

            case ThemedToggleButton toggle:
                if (toggle.Checked != Value<bool>("true or false"))
                {
                    toggle.PerformClick();
                }

                break;

            case Button button:
                button.PerformClick();
                break;

            default:
                throw new ToolException($"'{name}' is text, which cannot be changed.");
        }

        if (viewer is GLSceneViewer scene && (camera!.Location != location || camera.GetQAngle() != angles))
        {
            scene.Input.EndTransition();
        }

        // A checked list item is reported through the list, so the list comes back with its items
        return DescribeControl(control, items: control.Control is CheckedListBox);
    }

    private static object DescribeControl(SidebarControl control, bool items)
    {
        var (kind, value, list) = control.Control switch
        {
            CheckBox checkBox => ("checkbox", (object?)checkBox.Checked, (object?)null),
            CheckedListBox listBox => ("list", null, Enumerable.Range(0, listBox.Items.Count).ToDictionary(i => listBox.GetItemText(listBox.Items[i]) ?? string.Empty, listBox.GetItemChecked)),
            ComboBox comboBox => ("dropdown", comboBox.SelectedIndex >= 0 ? ItemText(comboBox, comboBox.SelectedIndex) : null,
                Enumerable.Range(0, comboBox.Items.Count).Select(i => IsHeader(comboBox, i) ? $"-- {ItemText(comboBox, i)} --" : ItemText(comboBox, i)).ToList()),
            Slider slider => ("slider", slider.Value, null),
            ThemedFloatNumeric number => ("number", new { number.Value, Min = number.MinValue, Max = number.MaxValue }, null),
            ThemedIntNumeric number => ("number", new { number.Value, Min = number.MinValue, Max = number.MaxValue }, null),
            PropertyRow vector => ("vector", vector.Editors.OfType<ThemedFloatNumeric>().Select(static number => number.Value).ToArray(), null),
            ThemedToggleButton toggle => ("toggle", toggle.Checked, null),
            Button => ("button", null, null),
            _ => ("text", null, null),
        };

        return new
        {
            Kind = kind,
            control.Label,
            control.Group,
            Value = value,
            Items = items ? list : null,
            Disabled = Flag(!control.Control.Enabled),
            Hidden = Flag(!control.Control.Visible),
        };
    }

    private static string ItemText(ComboBox comboBox, int index)
        => comboBox.Items[index] is ThemedComboBoxItem themed ? themed.Text : comboBox.GetItemText(comboBox.Items[index]) ?? string.Empty;

    private static bool IsHeader(ComboBox comboBox, int index) => comboBox.Items[index] is ThemedComboBoxItem { IsHeader: true };

    /// <summary>Every control a user can see in the sidebar and the toolbar, top to bottom.</summary>
    private static List<SidebarControl> SidebarControls(GLBaseControl viewer)
    {
        var controls = new List<SidebarControl>();

        void Walk(Control parent, string? group)
        {
            // Docked controls are brought to the front as they are added, so those collections hold them
            // bottom to top
            var children = parent switch
            {
                TableLayoutPanel table => parent.Controls.Cast<Control>().OrderBy(table.GetRow).ThenBy(table.GetColumn),
                FlowLayoutPanel => parent.Controls.Cast<Control>(),
                _ => parent.Controls.Cast<Control>().Reverse(),
            };

            string? previousRowName = null;

            foreach (var child in children)
            {
                if (child is GLControl || IsCovered(parent, child))
                {
                    continue;
                }

                if (SectionHeading(child) is { } heading)
                {
                    group = heading;
                    continue;
                }

                if (child is PropertyRow row)
                {
                    previousRowName = AddRowControls(controls, row, previousRowName, group);
                    continue;
                }

                var labelled = child switch
                {
                    GLViewerCheckboxControl checkBox => new SidebarControl(checkBox.CheckBox, checkBox.CheckBox.Text, group),
                    GLViewerSelectionControl selection => new SidebarControl(selection.ComboBox, Find<Label>(selection)?.Text.TrimEnd(':') ?? string.Empty, group),
                    GLViewerMultiSelectionControl list => new SidebarControl(list.CheckedListBox, Find<GroupBox>(list)?.Text ?? string.Empty, group),
                    GLViewerSliderControl slider => new SidebarControl(slider.Slider, slider.Slider.AccessibleName ?? "Slider", group),
                    ThemedFlatComboBox { DefaultItemText: { } placeholder } toolbarSelection => new SidebarControl(toolbarSelection, placeholder, group),
                    Panel { Controls.Count: 2 } panel when panel.Controls.OfType<ThemedFloatNumeric>().FirstOrDefault() is { } number
                        && panel.Controls.OfType<Label>().FirstOrDefault() is { } label => new SidebarControl(number, label.Text, group),
                    Button when Caption(child) is { Length: > 0 } caption => new SidebarControl(child, caption, group),
                    Label when child.Text.Length > 0 => new SidebarControl(child, child.Text.ReplaceLineEndings(", "), group),
                    _ => null,
                };

                if (labelled != null)
                {
                    controls.Add(labelled);
                }
                else
                {
                    Walk(child, child is GroupBox groupBox ? groupBox.Text : group);
                }
            }
        }

        if (viewer.UiControl != null)
        {
            Walk(viewer.UiControl, null);
        }

        return controls;
    }

    /// <summary>
    /// Adds the inputs of a property row, named by the row and each captioned one by its caption too.
    /// Returns the name of the row, which a row of vector fields without a name of its own goes by.
    /// </summary>
    private static string? AddRowControls(List<SidebarControl> controls, PropertyRow row, string? previousRowName, string? group)
    {
        var leading = row.RowName == null && row.Editors is [CheckBox { Text.Length: > 0 } checkBox] ? checkBox : null;
        var name = row.RowName ?? leading?.Text;
        var hasField = row.Editors is [Slider] && row.Trailing.OfType<ThemedFloatNumeric>().Any();

        if (row.Editors.OfType<ThemedFloatNumeric>().Count() > 1)
        {
            controls.Add(new SidebarControl(row, name ?? previousRowName ?? string.Empty, group));
            return name ?? previousRowName;
        }

        var added = controls.Count;

        foreach (var control in row.Editors.Concat(row.Trailing))
        {
            var label = control switch
            {
                _ when control == leading => name,
                ButtonBase when Caption(control) is { Length: > 0 } caption => name == null ? caption : $"{name} {caption}",
                CheckBox or ComboBox or ThemedIntNumeric or ThemedFloatNumeric => name,

                // A ranged value is set through its number field, which moves the slider along
                Slider slider when !hasField => name ?? slider.AccessibleName,
                _ => null,
            };

            if (label != null)
            {
                controls.Add(new SidebarControl(control, label, group));
            }
        }

        // A row whose editor cannot be set this way, such as a color, is still listed by its name
        if (controls.Count == added && row.RowName != null && row.Controls.OfType<Label>().FirstOrDefault() is { } nameLabel)
        {
            controls.Add(new SidebarControl(nameLabel, row.RowName, group));
        }

        return name;
    }

    private static string? Caption(Control control) => control.Text.Length > 0 ? control.Text : control.AccessibleName;

    /// <summary>The text of a heading that names the rows below it, or null when the control is not one.</summary>
    private static string? SectionHeading(Control control) => control switch
    {
        Label { AccessibleRole: AccessibleRole.Grouping } heading => heading.Text,
        Panel { Controls: [Label { AccessibleRole: AccessibleRole.Grouping } heading] } => heading.Text,
        _ => null,
    };

    /// <summary>
    /// Whether a control is out of sight although shown: clipped away by its parent, such as the parameters
    /// of the animation graphs while none plays, or behind a sibling placed over it, such as those of the
    /// graphs other than the one playing.
    /// </summary>
    private static bool IsCovered(Control parent, Control child)
    {
        if (!parent.DisplayRectangle.IntersectsWith(child.Bounds))
        {
            return true;
        }

        if (child.Dock != DockStyle.None)
        {
            return false;
        }

        var index = parent.Controls.GetChildIndex(child);

        for (var i = 0; i < index; i++)
        {
            var front = parent.Controls[i];

            if (front.Dock == DockStyle.None && front.Width > 0 && front.Height > 0 && front.Bounds.Contains(child.Location))
            {
                return true;
            }
        }

        return false;
    }

    private static T? Find<T>(Control parent)
        where T : Control
        => parent.Controls.OfType<T>().FirstOrDefault() ?? parent.Controls.Cast<Control>().Select(Find<T>).FirstOrDefault(static found => found != null);
}
#endif
