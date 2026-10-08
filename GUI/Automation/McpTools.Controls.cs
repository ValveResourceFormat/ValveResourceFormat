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

    [Tool("List the controls in a tab's sidebar, top to bottom, each with its group and state: checkboxes, dropdowns with their items, checked lists such as world layers and mesh groups, sliders from 0 to 1, number inputs with their range, vectors such as material parameters, buttons, and text such as readouts. Render modes, world layers and physics groups are among them.")]
    private static List<object> ListControls(GLBaseControl viewer, [Description("Include the items of dropdowns and checked lists, which can run to hundreds of animations.")] bool items = false)
        => SidebarControls(viewer).Select(control => DescribeControl(control, items)).ToList();

    [Tool("Change a control in a tab's sidebar as a user would: check a checkbox, check an item of a checked list, select a dropdown item, move a slider, set a number input or vector, or press a button. A choice that moves the camera, such as a map camera, lands it at once.", Redraws = true)]
    private static object SetControl(
        GLBaseControl viewer,
        [Description("Label of the control, from list_controls.")] string name,
        [Description("Group the control is in, needed only when two controls have the same label.")] string? group = null,
        [Description("The item of a checked list to check or uncheck.")] string? item = null,
        [Description("true or false for a checkbox or checked list item, the item text or its index for a dropdown, a number for a slider or number input, an array of numbers for a vector, nothing for a button.")] JsonNode? value = null)
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

            case TableLayoutPanel vector:
                var components = vector.Controls.OfType<ThemedFloatNumeric>().OrderBy(vector.GetColumn).ToList();
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
            TableLayoutPanel vector => ("vector", vector.Controls.OfType<ThemedFloatNumeric>().OrderBy(vector.GetColumn).Select(static number => number.Value).ToArray(), null),
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

    /// <summary>Every control a user can see in the sidebar, top to bottom.</summary>
    private static List<SidebarControl> SidebarControls(GLBaseControl viewer)
    {
        var controls = new List<SidebarControl>();
        var labelledFields = new HashSet<Control>();

        void Walk(Control parent, string? group)
        {
            // Every control is docked to the top and brought to the front as it is added, so each
            // collection holds them bottom to top
            var children = parent is TableLayoutPanel table
                ? parent.Controls.Cast<Control>().OrderBy(child => table.GetRow(child)).ThenBy(child => table.GetColumn(child))
                : parent.Controls.Cast<Control>().Reverse();

            foreach (var child in children)
            {
                if (child is GLControl || labelledFields.Contains(child))
                {
                    continue;
                }

                // A table of parameters has each label in the first column, and what it names beside it,
                // under headings that span both
                if (parent is TableLayoutPanel headings && headings.GetColumnSpan(child) > 1 && child.Controls is [Label heading])
                {
                    group = heading.Text;
                    continue;
                }

                if (parent is TableLayoutPanel parameters && child is Label { Text.Length: > 0 } fieldLabel && parameters.GetColumn(child) == 0
                    && parameters.GetControlFromPosition(1, parameters.GetRow(child)) is { } field && FieldInput(field) is { } input)
                {
                    labelledFields.Add(field);
                    controls.Add(new SidebarControl(input, fieldLabel.Text, group));
                    continue;
                }

                var labelled = child switch
                {
                    GLViewerCheckboxControl checkBox => new SidebarControl(checkBox.CheckBox, checkBox.CheckBox.Text, group),
                    GLViewerSelectionControl selection => new SidebarControl(selection.ComboBox, Find<Label>(selection)?.Text.TrimEnd(':') ?? string.Empty, group),
                    GLViewerMultiSelectionControl list => new SidebarControl(list.CheckedListBox, Find<GroupBox>(list)?.Text ?? string.Empty, group),
                    GLViewerSliderControl slider => new SidebarControl(slider.Slider, slider.Slider.AccessibleName ?? "Slider", group),
                    Panel { Controls.Count: 2 } panel when panel.Controls.OfType<ThemedFloatNumeric>().FirstOrDefault() is { } number
                        && panel.Controls.OfType<Label>().FirstOrDefault() is { } label => new SidebarControl(number, label.Text, group),
                    Button or Label when child.Text.Length > 0 => new SidebarControl(child, child.Text.ReplaceLineEndings(", "), group),
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

    /// <summary>The input of a parameter field: the control itself, its one number input, or a row of them as a vector.</summary>
    private static Control? FieldInput(Control field) => field switch
    {
        CheckBox or ComboBox => field,
        TableLayoutPanel row => row.Controls.OfType<ThemedFloatNumeric>().Count() > 1 ? row
            : row.Controls.OfType<ThemedFloatNumeric>().FirstOrDefault() ?? (Control?)row.Controls.OfType<ThemedIntNumeric>().FirstOrDefault(),
        _ => null,
    };

    private static T? Find<T>(Control parent)
        where T : Control
        => parent.Controls.OfType<T>().FirstOrDefault() ?? parent.Controls.Cast<Control>().Select(Find<T>).FirstOrDefault(static found => found != null);
}
#endif
