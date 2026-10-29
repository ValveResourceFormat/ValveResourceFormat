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
/// itself, so the sidebar shows it and the viewer runs the same callback as for a click.
/// </summary>
internal sealed partial class McpTools
{
    /// <param name="Label">The name the sidebar shows, which the tools use to find the control.</param>
    /// <param name="Group">The title of the group box or section it is in, or null outside any.</param>
    private sealed record SidebarControl(Control Control, string Label, string? Group);

    /// <summary>One change of <see cref="SetControls"/>.</summary>
    private sealed record ControlChange(string Name, string? Group = null, string? Item = null, JsonNode? Value = null);

    [Tool("List the controls in a tab's sidebar and toolbar, top to bottom, each with its kind, group and state. Kinds are checkbox, toggle, dropdown, list (a checked list such as world layers or mesh groups), slider (0 to 1), and number (with its range). The others are vector (such as a material or animation graph parameter), button and text. The render mode dropdown is in the Toolbar group.")]
    private static List<object> ListControls(GLBaseControl viewer, [Description("Include the items of dropdowns and lists. An animation list can have hundreds.")] bool items = false)
        => SidebarControls(viewer).Select(control => DescribeControl(control, items)).ToList();

    [Tool("Change sidebar or toolbar controls in order, as a user would. Stops at the first change that fails and keeps the changes before it. A choice that moves the camera, such as a map camera, moves it at once. Returns each changed control once in 'controls'. "
        + "On a map, the Exposure slider sets a manual exposure of 10 times its value, even when the value is the current one, until the tab is reloaded. "
        + "After a change to it, 'exposure' gives the mode, auto or manual, and the exposure value.", Redraws = true)]
    private static object SetControls(
        GLBaseControl viewer,
        [Description("'name' is the label from list_controls. Give 'group' only when two controls have the same label. 'item' is the list item to check. 'value' is true or false for a checkbox, toggle or list item, and the item text or index for a dropdown. It is a number for a slider or number, and a number array for a vector. Omit 'value' to press a button.")] List<ControlChange> changes)
    {
        var changed = new List<SidebarControl>();

        for (var i = 0; i < changes.Count; i++)
        {
            SidebarControl control;

            try
            {
                control = ApplyControl(viewer, changes[i]);
            }
            catch (Exception e) when (e is ToolException or ArgumentException)
            {
                throw new ToolException($"Change {i + 1} of {changes.Count} failed. The changes before it stay applied. {e.Message}");
            }

            if (!changed.Exists(other => other.Control == control.Control))
            {
                changed.Add(control);
            }
        }

        object? exposure = null;

        if (viewer is GLSceneViewer scene && changed.Exists(static control => control.Label == "Exposure"))
        {
            exposure = DescribeExposure(scene);
        }

        return new
        {
            // A checked list item is reported through the list, so the list comes back with its items
            Controls = changed.Select(static control => DescribeControl(control, items: control.Control is CheckedListBox)).ToList(),
            Exposure = exposure,
        };
    }

    private static SidebarControl ApplyControl(GLBaseControl viewer, ControlChange change)
    {
        var (name, group, item, value) = change;

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Every change needs a control 'name'.");
        }

        var controls = SidebarControls(viewer);
        var matches = controls.Where(control => control.Label.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (group == null || string.Equals(control.Group, group, StringComparison.OrdinalIgnoreCase))).ToList();

        if (matches.Count == 0)
        {
            var inGroup = group == null ? null : $" in group '{group}'";
            var changeable = controls.Where(static control => control.Control is not Label).Select(static control => control.Label).Distinct();
            throw new ToolException($"No control named '{name}'{inGroup}. This tab has: {string.Join(", ", changeable)}.");
        }

        // A hidden control with the same name does not count
        if (matches.Count > 1)
        {
            var visible = matches.FindAll(static match => match.Control.Visible);

            if (visible.Count == 1)
            {
                matches = visible;
            }
        }

        if (matches.Count > 1)
        {
            throw new ToolException($"'{name}' is in more than one group: {string.Join(", ", matches.Select(static match => match.Group ?? "none"))}. Pass 'group'.");
        }

        var control = matches[0];

        if (!control.Control.Visible || !control.Control.Enabled)
        {
            var state = control.Control.Visible ? "disabled" : "hidden";
            throw new ToolException($"'{name}' is {state}, so a user cannot change it.");
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

        var scene = viewer as GLSceneViewer;
        var location = scene?.Input.Camera.Location;
        var angles = scene?.Input.Camera.GetQAngle();

        switch (control.Control)
        {
            case CheckBox checkBox:
                checkBox.Checked = Value<bool>("true or false");
                break;

            case CheckedListBox list:
                if (item == null)
                {
                    throw new ArgumentException($"Pass the 'item' of '{name}' to change.");
                }

                var index = list.FindStringExact(item);

                if (index < 0)
                {
                    throw new ToolException($"'{name}' has no item '{item}'.");
                }

                list.SetItemChecked(index, Value<bool>("true or false"));
                break;

            case ComboBox comboBox when value?.GetValueKind() == JsonValueKind.Number:
                var selected = Value<int>("an index");

                if (selected < 0 || selected >= comboBox.Items.Count || IsHeader(comboBox, selected))
                {
                    throw new ToolException($"'{name}' has no item at index {selected}.");
                }

                comboBox.SelectedIndex = selected;
                break;

            case ComboBox comboBox:
                var text = Value<string>("an item or its index");
                var found = FindItem(comboBox, text);

                if (found < 0)
                {
                    throw new ToolException($"'{name}' has no item '{text}'.");
                }

                comboBox.SelectedIndex = found;
                break;

            case Slider slider:
                // The knob clamps to 0 to 1, and reports the change as a drag does
                slider.Value = Value<float>("a number from 0 to 1");
                slider.ValueChanged?.Invoke(slider.Value);
                break;

            case ThemedFloatNumeric number:
                number.Value = Math.Clamp(Value<float>("a number"), number.MinValue, number.MaxValue);
                break;

            case ThemedIntNumeric number:
                number.Value = Math.Clamp(Value<int>("an integer"), number.MinValue, number.MaxValue);
                break;

            case PropertyRow vector:
                var components = vector.Editors.Count;
                var values = Value<float[]>($"an array of {components} numbers");

                if (values.Length != components)
                {
                    throw new ArgumentException($"'value' must be an array of {components} numbers for '{name}'.");
                }

                for (var i = 0; i < values.Length; i++)
                {
                    ((ThemedFloatNumeric)vector.Editors[i]).Value = values[i];
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
                throw new ToolException($"'{name}' is read-only text.");
        }

        if (scene != null && (scene.Input.Camera.Location != location || scene.Input.Camera.GetQAngle() != angles))
        {
            scene.Input.SaveCameraForTransition(transitionDuration: 0f);
        }

        return control;
    }

    private static object DescribeControl(SidebarControl control, bool items)
    {
        var input = control.Control;

        return new
        {
            Kind = KindOf(input),
            control.Label,
            control.Group,
            Value = ValueOf(input),
            Items = items ? ListItems(input) : null,
            Disabled = Flag(!input.Enabled),
            Hidden = Flag(!input.Visible),
        };
    }

    private static string KindOf(Control input) => input switch
    {
        CheckBox => "checkbox",
        CheckedListBox => "list",
        ComboBox => "dropdown",
        Slider => "slider",
        ThemedFloatNumeric or ThemedIntNumeric => "number",
        PropertyRow => "vector",
        ThemedToggleButton => "toggle",
        Button => "button",
        _ => "text",
    };

    private static object? ValueOf(Control input) => input switch
    {
        CheckBox checkBox => checkBox.Checked,
        ComboBox comboBox when comboBox.SelectedIndex >= 0 => ItemText(comboBox, comboBox.Items[comboBox.SelectedIndex]),
        Slider slider => slider.Value,
        ThemedFloatNumeric number => new { number.Value, Min = Bound(number.MinValue), Max = Bound(number.MaxValue) },
        ThemedIntNumeric number => new { number.Value, Min = number.MinValue, Max = number.MaxValue },
        PropertyRow vector => vector.Editors.Cast<ThemedFloatNumeric>().Select(static number => number.Value).ToArray(),
        ThemedToggleButton toggle => toggle.Checked,
        _ => null,
    };

    /// <summary>The items of a dropdown or checked list, or null for any other control.</summary>
    private static object? ListItems(Control input) => input switch
    {
        CheckedListBox list => ListItems(list),
        ComboBox comboBox => ListItems(comboBox),
        _ => null,
    };

    /// <summary>Each item of a checked list, and whether it is checked.</summary>
    private static Dictionary<string, bool> ListItems(CheckedListBox list)
    {
        var items = new Dictionary<string, bool>(list.Items.Count);

        for (var i = 0; i < list.Items.Count; i++)
        {
            items[ItemText(list, list.Items[i])] = list.GetItemChecked(i);
        }

        return items;
    }

    /// <summary>Each item of a dropdown, with each header between dashes.</summary>
    private static List<string> ListItems(ComboBox comboBox)
    {
        var items = new List<string>(comboBox.Items.Count);

        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            var text = ItemText(comboBox, comboBox.Items[i]);

            if (IsHeader(comboBox, i))
            {
                text = $"-- {text} --";
            }

            items.Add(text);
        }

        return items;
    }

    /// <summary>A bound of a number input, or null for the end of the float range, which bounds nothing.</summary>
    private static float? Bound(float value) => value > float.MinValue && value < float.MaxValue ? value : null;

    private static string ItemText(ListControl list, object? item) => list.GetItemText(item) ?? string.Empty;

    private static bool IsHeader(ComboBox comboBox, int index) => comboBox.Items[index] is ThemedComboBoxItem { IsHeader: true };

    /// <summary>The index of the selectable item with this text, ignoring case, or -1.</summary>
    private static int FindItem(ComboBox comboBox, string text)
    {
        var first = comboBox.FindStringExact(text);
        var index = first;

        // The search starts after the given index and wraps around, so a return to the first match
        // means every match is a header
        while (index >= 0 && IsHeader(comboBox, index))
        {
            index = comboBox.FindStringExact(text, index);

            if (index == first)
            {
                return -1;
            }
        }

        return index;
    }

    /// <summary>Every control a user can see in the sidebar, top to bottom.</summary>
    private static List<SidebarControl> SidebarControls(GLBaseControl viewer)
    {
        var controls = new List<SidebarControl>();

        void Walk(Control parent, string? group)
        {
            // Every control is docked to the top and brought to the front as it is added, so each
            // collection holds them bottom to top
            var children = parent is TableLayoutPanel table
                ? parent.Controls.Cast<Control>().OrderBy(child => table.GetRow(child)).ThenBy(child => table.GetColumn(child))
                : parent.Controls.Cast<Control>().Reverse();

            // A row without a name takes the name of the row above it, as the fields of a vector do
            var rowName = string.Empty;

            foreach (var child in children)
            {
                if (child is GLControl || IsOutOfSight(parent, child))
                {
                    continue;
                }

                var sectionTitle = SectionTitle(child);

                if (sectionTitle != null)
                {
                    group = sectionTitle;
                    continue;
                }

                if (child is PropertyRow row)
                {
                    var ownName = RowName(row);
                    rowName = ownName ?? rowName;
                    AddRowInputs(controls, row, ownName, rowName, group);
                    continue;
                }

                // Inputs are named where they are created
                if (!string.IsNullOrEmpty(child.AccessibleName))
                {
                    controls.Add(new SidebarControl(child, child.AccessibleName, group));
                    continue;
                }

                // Other buttons and labels use their text as the name, but a label that only repeats the name of an input beside it is skipped
                if (child is Button or Label && child.Text.Length > 0)
                {
                    if (child is Button || !HasInputNamed(parent, child.Text))
                    {
                        controls.Add(new SidebarControl(child, child.Text.ReplaceLineEndings(", "), group));
                    }

                    continue;
                }

                var childGroup = group;

                // A group box titles the controls in it, unless it frames a single input of that name, as a checked list's does
                if (child is GroupBox groupBox && !HasInputNamed(groupBox, groupBox.Text))
                {
                    childGroup = groupBox.Text;
                }
                else if (child.AccessibleRole == AccessibleRole.ToolBar)
                {
                    childGroup = "Toolbar";
                }

                Walk(child, childGroup);
            }
        }

        if (viewer.UiControl != null)
        {
            Walk(viewer.UiControl, null);
        }

        return controls;
    }

    /// <summary>The text of a section header, which titles the controls below it, or null for any other control.</summary>
    private static string? SectionTitle(Control control) => control switch
    {
        Label { AccessibleRole: AccessibleRole.Grouping } header => header.Text,
        Panel { Controls: [Label { AccessibleRole: AccessibleRole.Grouping } header] } => header.Text,
        _ => null,
    };

    /// <summary>Whether <paramref name="container"/> holds an input named <paramref name="caption"/>, ignoring a trailing colon.</summary>
    private static bool HasInputNamed(Control container, string caption)
    {
        var name = caption.TrimEnd(':');

        foreach (Control child in container.Controls)
        {
            if (child.AccessibleName == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a control placed by hand, not docked, is out of sight. Its parent can clip it, or a
    /// sibling at the same spot in front of it can cover it. Panels swapped within one host do this.
    /// </summary>
    private static bool IsOutOfSight(Control parent, Control child)
    {
        if (child.Dock != DockStyle.None || !child.Visible)
        {
            return false;
        }

        if (!parent.DisplayRectangle.IntersectsWith(child.Bounds))
        {
            return true;
        }

        // The front of the z-order comes first in the collection
        var index = parent.Controls.GetChildIndex(child);

        for (var i = 0; i < index; i++)
        {
            var sibling = parent.Controls[i];

            if (sibling.Visible && sibling.Dock == DockStyle.None && sibling.Location == child.Location)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The name of a property row: its own, or the caption of its leading checkbox, or null for neither.</summary>
    private static string? RowName(PropertyRow row)
    {
        if (row.AccessibleName != null)
        {
            return row.AccessibleName;
        }

        if (row.Editors is [CheckBox { Text.Length: > 0 } leadingCheckBox])
        {
            return leadingCheckBox.Text;
        }

        return null;
    }

    /// <summary>Adds the inputs of a property row with names from the row, and a vector as one control with the row's name.</summary>
    /// <param name="ownName">The name of the row from <see cref="RowName"/>, which names its inputs, or null.</param>
    /// <param name="rowName">The name of a vector. For a row without a name, this is the name of the row above.</param>
    private static void AddRowInputs(List<SidebarControl> controls, PropertyRow row, string? ownName, string rowName, string? group)
    {
        // Only a vector has more than one editor, one for each component
        if (row.Editors.Count > 1)
        {
            controls.Add(new SidebarControl(row, rowName, group));
        }

        // Laid out left to right in the order they were added
        foreach (Control input in row.Controls)
        {
            var name = ownName == null ? input.AccessibleName : RowInputName(row, input, ownName);

            if (!string.IsNullOrEmpty(name))
            {
                controls.Add(new SidebarControl(input, name, group));
            }
        }
    }

    /// <summary>The name of an input in a named row: the row's name, plus the input's caption when it has its own.</summary>
    private static string? RowInputName(PropertyRow row, Control input, string rowName)
    {
        var isEditor = row.Editors.Contains(input);

        // The fields of a vector are one value, which the row stands for
        var isVectorField = isEditor && row.Editors.Count > 1;

        // The label beside the editors shows the name itself
        var isNameLabel = !isEditor && input is Label;

        if (isVectorField || isNameLabel)
        {
            return input.AccessibleName;
        }

        switch (input)
        {
            // It moves with the number beside it, which carries the value
            case Slider:
                return input.AccessibleName;

            // An icon or a color swatch, rather than an action of the row
            case Button { Text.Length: 0 }:
                return input.AccessibleName;

            // The caption tells it apart from the row's other inputs
            case CheckBox or Button when input.Text.Length > 0 && input.Text != rowName:
                return $"{rowName} {input.Text}";

            default:
                return rowName;
        }
    }
}
#endif
