#if DEBUG
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;

namespace GUI.Automation;

/// <summary>
/// Tools that read and change the controls in a viewer's sidebar. Each change goes through the
/// control itself, so the sidebar shows what was asked for and the viewer runs the same callback as
/// a click, and a control a user could not change is refused.
/// </summary>
internal sealed partial class McpTools
{
    private void RegisterControlTools()
    {
        Add("list_controls", "List the controls in a tab's sidebar, top to bottom, with the group each is in and its current state: checkboxes (such as Show Fog), dropdowns (such as Animation or Mip level) with their items and the selected one, checked lists (such as Mesh Group or World Layers) with each item's state, sliders, and number inputs. A slider has no label, so it is named by its group and its 'index' among the sliders of that group, and 'near' is the closest label above it, which usually reads out its value. A disabled or hidden control cannot be changed until whatever disables or hides it is.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["items"] = Prop("boolean", "Include the items of dropdowns and checked lists. Defaults to true; false gives only their count, for a model with hundreds of animations."),
            }),
            ListControls, AnyViewer);

        Add("set_checkbox", "Check or uncheck a checkbox in a tab's sidebar, such as Show Fog or PVS Culling, or one item of a checked list, such as a mesh group or world layer, by passing 'item'. Runs the same change a click does.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Checkbox label, or the title of the checked list when 'item' is given, from list_controls."),
                ["group"] = Prop("string", "Group the control is in, from list_controls. Needed only when two controls have the same label."),
                ["item"] = Prop("string", "Item of the checked list named by 'name'."),
                ["checked"] = Prop("boolean", "Whether it should be checked."),
            }, "name", "checked"),
            SetCheckBox, AnyViewer);

        Add("set_dropdown", "Select an item of a dropdown in a tab's sidebar, such as an animation, level of detail, material group, map camera or texture mip, which runs the same change picking it does. A choice that moves the camera, such as a map camera, lands it at once instead of flying it there.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Dropdown label from list_controls."),
                ["group"] = Prop("string", "Group the dropdown is in, from list_controls. Needed only when two dropdowns have the same label."),
                ["item"] = Prop("string", "Item text to select."),
                ["index"] = Prop("integer", "Position of the item to select, from 0, instead of 'item'. Needed when the same text appears more than once, such as animations of the same name in different folders."),
            }, "name"),
            SetDropdown, AnyViewer);

        Add("set_value", "Move a slider or set a number input in a tab's sidebar, as dragging or typing does. A number input is named by its label and clamped to its range. A slider is named by 'group' and 'index' and set from 0 to 1, the knob position; what that means is up to the slider, and its 'near' label from list_controls reads out the result. Dragging the animation frame slider leaves playback as it was, so uncheck Autoplay or pause first to hold a frame.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Number input label from list_controls. Omit for a slider."),
                ["group"] = Prop("string", "Group the control is in, from list_controls. For a slider, omit it only for a slider outside any group."),
                ["index"] = Prop("integer", "Slider index within its group, from list_controls. Defaults to 0."),
                ["value"] = Prop("number", "New value: from 0 to 1 for a slider, or in the input's range for a number."),
            }, "value"),
            SetValue, AnyViewer);
    }

    private Task<McpToolResult> ListControls(JsonObject args, CancellationToken cancellationToken)
    {
        var items = GetBool(args, "items") ?? true;

        return WithViewer<GLBaseControl>(args, viewer =>
        {
            var controls = new JsonArray();

            foreach (var control in SidebarOf(viewer))
            {
                controls.Add(DescribeControl(control, items));
            }

            return McpToolResult.Json(new JsonObject
            {
                ["controls"] = controls,
            });
        }, cancellationToken);
    }

    private static List<SidebarControl> SidebarOf(GLBaseControl viewer)
        => viewer.Sidebar is { } sidebar ? SidebarControls.Collect(sidebar) : [];

    private static JsonObject DescribeControl(SidebarControl control, bool items)
    {
        var entry = new JsonObject
        {
            ["kind"] = control.KindName,
        };

        if (control.Label != null)
        {
            entry["label"] = control.Label;
        }

        if (control.Group != null)
        {
            entry["group"] = control.Group;
        }

        switch (control.Control)
        {
            case CheckBox checkBox:
                entry["checked"] = checkBox.Checked;
                break;

            case ComboBox comboBox:
                if (comboBox.SelectedIndex >= 0)
                {
                    entry["selected"] = SidebarControls.ItemText(comboBox, comboBox.SelectedIndex);
                    entry["selected_index"] = comboBox.SelectedIndex;
                }

                if (!items)
                {
                    entry["count"] = comboBox.Items.Count;
                    break;
                }

                var texts = new JsonArray();
                var headers = new JsonArray();

                for (var i = 0; i < comboBox.Items.Count; i++)
                {
                    texts.Add(SidebarControls.ItemText(comboBox, i));

                    if (SidebarControls.IsHeader(comboBox, i))
                    {
                        headers.Add(i);
                    }
                }

                entry["items"] = texts;

                if (headers.Count > 0)
                {
                    entry["headers"] = headers;
                }

                break;

            case CheckedListBox listBox:
                if (items)
                {
                    entry["items"] = CheckedMap(SidebarControls.ListItems(listBox));
                }
                else
                {
                    entry["count"] = listBox.Items.Count;
                }

                break;

            case Slider slider:
                entry["index"] = control.Index;
                entry["value"] = Math.Round(slider.Value, 3);

                if (NearText(control) is { } near)
                {
                    entry["near"] = near;
                }

                break;

            case ThemedFloatNumeric number:
                entry["value"] = Math.Round(number.Value, 4);
                entry["min"] = number.MinValue;
                entry["max"] = number.MaxValue;
                break;
        }

        if (!control.Control.Enabled)
        {
            entry["disabled"] = true;
        }

        if (!control.Control.Visible)
        {
            entry["hidden"] = true;
        }

        return entry;
    }

    /// <summary>
    /// The control of this kind with this label, and in this group when one is given. An error when
    /// there is none, when the label is in more than one group, or when a user could not change it.
    /// </summary>
    private static (SidebarControl? Control, McpToolResult? Error) FindControl(GLBaseControl viewer, SidebarControlKind kind, string name, string? group)
    {
        var all = SidebarOf(viewer);
        var matches = all.FindAll(control => control.Kind == kind
            && string.Equals(control.Label, name, StringComparison.OrdinalIgnoreCase)
            && (group == null || string.Equals(control.Group, group, StringComparison.OrdinalIgnoreCase)));

        var noun = SidebarControls.Noun(kind);

        if (matches.Count == 0)
        {
            var inGroup = group == null ? string.Empty : $" in group '{group}'";
            var hint = kind == SidebarControlKind.CheckBox && all.Exists(control => control.Kind == SidebarControlKind.List && string.Equals(control.Label, name, StringComparison.OrdinalIgnoreCase))
                ? $" '{name}' is a checked list, so pass the 'item' to change."
                : " Call list_controls for the names.";

            return (null, McpToolResult.Error($"No {noun} named '{name}'{inGroup}.{hint}"));
        }

        if (matches.Count > 1)
        {
            var groups = matches.Select(control => control.Group == null ? "no group" : $"'{control.Group}'").Distinct();

            return (null, McpToolResult.Error(group == null
                ? $"There are {matches.Count} {noun}s named '{name}', in {string.Join(" and ", groups)}. Pass 'group' to pick one."
                : $"There are {matches.Count} {noun}s named '{name}' in group '{group}', which cannot be told apart."));
        }

        return Changeable(matches[0]);
    }

    /// <summary>Refuses a control a user could not change, because it is disabled or hidden.</summary>
    private static (SidebarControl? Control, McpToolResult? Error) Changeable(SidebarControl control)
    {
        if (!control.Control.Visible)
        {
            return (null, McpToolResult.Error($"{control.Describe()} is hidden, so a user could not change it."));
        }

        if (!control.Control.Enabled)
        {
            return (null, McpToolResult.Error($"{control.Describe()} is disabled, so a user could not change it."));
        }

        return (control, null);
    }

    private static JsonObject Named(string kind, SidebarControl control)
    {
        var result = new JsonObject
        {
            [kind] = control.Label,
        };

        if (control.Group != null)
        {
            result["group"] = control.Group;
        }

        return result;
    }

    private Task<McpToolResult> SetCheckBox(JsonObject args, CancellationToken cancellationToken)
    {
        var name = GetString(args, "name");
        var group = GetString(args, "group");
        var item = GetString(args, "item");
        var isChecked = GetBool(args, "checked");

        if (name == null || isChecked == null)
        {
            return Task.FromResult(MissingArgument("name and checked", args));
        }

        return WithViewer<GLBaseControl>(args, viewer =>
        {
            if (item == null)
            {
                var (control, error) = FindControl(viewer, SidebarControlKind.CheckBox, name, group);

                if (error != null)
                {
                    return error;
                }

                var checkBox = (CheckBox)control!.Control;
                checkBox.Checked = isChecked.Value;

                var result = Named("checkbox", control);
                result["checked"] = checkBox.Checked;
                return McpToolResult.Json(result);
            }

            var (list, listError) = FindControl(viewer, SidebarControlKind.List, name, group);

            if (listError != null)
            {
                return listError;
            }

            var listBox = (CheckedListBox)list!.Control;
            var index = listBox.FindStringExact(item);

            if (index < 0)
            {
                return McpToolResult.Error($"The '{list.Label}' checked list has no item '{item}'. Call list_controls for its items.");
            }

            listBox.SetItemChecked(index, isChecked.Value);

            var listResult = Named("list", list);
            listResult["item"] = listBox.GetItemText(listBox.Items[index]);
            listResult["checked"] = listBox.GetItemChecked(index);
            return McpToolResult.Json(listResult);
        }, cancellationToken);
    }

    private Task<McpToolResult> SetDropdown(JsonObject args, CancellationToken cancellationToken)
    {
        var name = GetString(args, "name");
        var group = GetString(args, "group");
        var item = GetString(args, "item");
        var index = GetInt(args, "index");

        if (name == null)
        {
            return Task.FromResult(MissingArgument("name", args));
        }

        if ((item == null) == (index == null))
        {
            return Task.FromResult(McpToolResult.Error("Pass either 'item' or 'index'."));
        }

        return WithViewer<GLBaseControl>(args, viewer =>
        {
            var (control, error) = FindControl(viewer, SidebarControlKind.Dropdown, name, group);

            if (error != null)
            {
                return error;
            }

            var comboBox = (ComboBox)control!.Control;
            var target = index ?? -1;

            if (index != null)
            {
                if (index < 0 || index >= comboBox.Items.Count)
                {
                    return McpToolResult.Error($"The '{control.Label}' dropdown has {comboBox.Items.Count} items, so 'index' runs from 0 to {comboBox.Items.Count - 1}.");
                }
            }
            else
            {
                var found = new List<int>();

                for (var i = 0; i < comboBox.Items.Count; i++)
                {
                    if (!SidebarControls.IsHeader(comboBox, i) && string.Equals(SidebarControls.ItemText(comboBox, i), item, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(i);
                    }
                }

                if (found.Count == 0)
                {
                    return McpToolResult.Error($"The '{control.Label}' dropdown has no item '{item}'. Call list_controls for its items.");
                }

                if (found.Count > 1)
                {
                    var places = found.Select(i => HeaderAbove(comboBox, i) is { } header ? $"{i} under '{header}'" : $"{i}");

                    return McpToolResult.Error($"'{item}' appears {found.Count} times in the '{control.Label}' dropdown, at {string.Join(", ", places)}. Pass 'index' to pick one.");
                }

                target = found[0];
            }

            if (SidebarControls.IsHeader(comboBox, target))
            {
                return McpToolResult.Error($"Item {target} '{SidebarControls.ItemText(comboBox, target)}' of the '{control.Label}' dropdown is a header, which cannot be selected.");
            }

            var camera = (viewer as GLSceneViewer)?.Input.Camera;
            var location = camera?.Location;
            var angles = camera?.GetQAngle();
            var unchanged = comboBox.SelectedIndex == target;

            comboBox.SelectedIndex = target;

            var result = Named("dropdown", control);

            if (comboBox.SelectedIndex >= 0)
            {
                result["selected"] = SidebarControls.ItemText(comboBox, comboBox.SelectedIndex);
                result["selected_index"] = comboBox.SelectedIndex;
            }

            if (unchanged)
            {
                result["note"] = "It was already selected, so nothing ran.";
            }

            if (viewer is GLSceneViewer scene && (scene.Input.Camera.Location != location || scene.Input.Camera.GetQAngle() != angles))
            {
                // A map camera starts a fly-in transition towards it.
                scene.Input.SetCameraImmediate(scene.Input.Camera.Location, scene.Input.Camera.GetQAngle());
                result["camera"] = DescribeCamera(scene.Input.Camera);
            }

            return McpToolResult.Json(result);
        }, cancellationToken);
    }

    /// <summary>The header an item is listed under, or null when there is none above it.</summary>
    private static string? HeaderAbove(ComboBox comboBox, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (SidebarControls.IsHeader(comboBox, i))
            {
                return SidebarControls.ItemText(comboBox, i);
            }
        }

        return null;
    }

    private async Task<McpToolResult> SetValue(JsonObject args, CancellationToken cancellationToken)
    {
        var name = GetString(args, "name");
        var group = GetString(args, "group");
        var index = GetInt(args, "index") ?? 0;
        var value = GetFloat(args, "value");

        if (value == null)
        {
            return MissingArgument("value", args);
        }

        if (!float.IsFinite(value.Value))
        {
            return McpToolResult.Error("'value' must be a finite number.");
        }

        if (name == null && value is < 0f or > 1f)
        {
            return McpToolResult.Error("A slider is set from 0 to 1, the knob position.");
        }

        SidebarControl? changed = null;

        var result = await WithViewer<GLBaseControl>(args, viewer =>
        {
            var (control, error) = name == null
                ? FindSlider(viewer, group, index)
                : FindControl(viewer, SidebarControlKind.Number, name, group);

            if (error != null)
            {
                return error;
            }

            changed = control;

            if (control!.Control is Slider slider)
            {
                slider.Value = value.Value;
                slider.ValueChanged?.Invoke(slider.Value);
            }
            else
            {
                var number = (ThemedFloatNumeric)control.Control;
                number.Value = Math.Clamp(value.Value, number.MinValue, number.MaxValue);
            }

            return McpToolResult.Json(new JsonObject());
        }, cancellationToken).ConfigureAwait(false);

        if (changed == null)
        {
            return result;
        }

        // A slider's readout, such as the animation frame, is only updated by the next frame.
        await Redraw(cancellationToken).ConfigureAwait(false);

        return await OnUi(() =>
        {
            var control = changed;

            if (control.Control is Slider slider)
            {
                var sliderResult = new JsonObject
                {
                    ["slider"] = control.Index,
                };

                if (control.Group != null)
                {
                    sliderResult["group"] = control.Group;
                }

                sliderResult["value"] = Math.Round(slider.Value, 3);

                if (NearText(control) is { } near)
                {
                    sliderResult["near"] = near;
                }

                return McpToolResult.Json(sliderResult);
            }

            var number = (ThemedFloatNumeric)control.Control;
            var numberResult = Named("number", control);
            numberResult["value"] = Math.Round(number.Value, 4);

            if (MathF.Abs(number.Value - value.Value) > 1e-4f)
            {
                numberResult["note"] = $"Clamped to the input's range, {number.MinValue} to {number.MaxValue}.";
            }

            return McpToolResult.Json(numberResult);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? NearText(SidebarControl control)
        => control.Near is { } label && SidebarControls.LabelText(label) is { Length: > 0 } text ? text : null;

    /// <summary>The slider at <paramref name="index"/> among the sliders of a group, or of those outside any group.</summary>
    private static (SidebarControl? Control, McpToolResult? Error) FindSlider(GLBaseControl viewer, string? group, int index)
    {
        var sliders = SidebarOf(viewer).FindAll(control => control.Kind == SidebarControlKind.Slider);
        var inGroup = sliders.FindAll(control => group == null ? control.Group == null : string.Equals(control.Group, group, StringComparison.OrdinalIgnoreCase));

        if (inGroup.Count == 0)
        {
            if (sliders.Count == 0)
            {
                return (null, McpToolResult.Error("This sidebar has no sliders."));
            }

            var where = sliders
                .GroupBy(control => control.Group)
                .Select(found => found.Key == null ? $"{found.Count()} outside any group" : $"{found.Count()} in '{found.Key}'");

            return (null, McpToolResult.Error(group == null
                ? $"No slider is outside a group. The sliders are: {string.Join(", ", where)}. Pass 'group'."
                : $"No slider is in group '{group}'. The sliders are: {string.Join(", ", where)}."));
        }

        if (index < 0 || index >= inGroup.Count)
        {
            var place = group == null ? "outside any group" : $"in '{inGroup[0].Group}'";

            return (null, McpToolResult.Error($"There are {inGroup.Count} sliders {place}, so 'index' runs from 0 to {inGroup.Count - 1}."));
        }

        return Changeable(inGroup[index]);
    }
}
#endif
