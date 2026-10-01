#if DEBUG
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Automation;

internal enum SidebarControlKind
{
    CheckBox,
    Dropdown,
    List,
    Slider,
    Number,
}

/// <summary>A control in a viewer's sidebar that a user can change, with what the sidebar calls it.</summary>
/// <param name="Control">What the user acts on: a <see cref="CheckBox"/>, <see cref="ComboBox"/>, <see cref="CheckedListBox"/>, <see cref="Slider"/> or <see cref="ThemedFloatNumeric"/>.</param>
/// <param name="Label">The text next to it, or null for a slider, which has none.</param>
/// <param name="Group">The title of the group box it is in, or null outside any.</param>
/// <param name="Index">For a slider, its position among the sliders of its group, from 0.</param>
/// <param name="Near">For a slider, the closest label above it in its group, which usually reads out what it is set to.</param>
internal sealed record SidebarControl(SidebarControlKind Kind, Control Control, string? Label, string? Group, int Index, Label? Near)
{
    public string KindName => SidebarControls.KindName(Kind);

    /// <summary>How a sentence starts when it names it, such as "The 'Show Fog' checkbox" or "Slider 1 in 'Animation'".</summary>
    public string Describe() => Kind == SidebarControlKind.Slider
        ? Group == null ? $"Slider {Index}" : $"Slider {Index} in '{Group}'"
        : $"The '{Label}' {SidebarControls.Noun(Kind)}";
}

/// <summary>Finds the controls the viewers put in their sidebar through <see cref="RendererControl"/>.</summary>
internal static class SidebarControls
{
    /// <summary>What list_controls calls a kind of control.</summary>
    public static string KindName(SidebarControlKind kind) => kind switch
    {
        SidebarControlKind.CheckBox => "checkbox",
        SidebarControlKind.Dropdown => "dropdown",
        SidebarControlKind.List => "list",
        SidebarControlKind.Slider => "slider",
        _ => "number",
    };

    /// <summary>What an error message calls a kind of control.</summary>
    public static string Noun(SidebarControlKind kind) => kind switch
    {
        SidebarControlKind.List => "checked list",
        SidebarControlKind.Number => "number input",
        _ => KindName(kind),
    };

    /// <summary>Every control in the sidebar, top to bottom.</summary>
    public static List<SidebarControl> Collect(RendererControl sidebar)
    {
        var walker = new Walker(sidebar);
        walker.Walk(sidebar, null);
        return walker.Controls;
    }

    /// <summary>The text of an item of a dropdown, which for a themed item is not what it converts to.</summary>
    public static string ItemText(ComboBox comboBox, int index)
    {
        var item = comboBox.Items[index];

        return item is ThemedComboBoxItem themed ? themed.Text : comboBox.GetItemText(item) ?? string.Empty;
    }

    public static bool IsHeader(ComboBox comboBox, int index) => comboBox.Items[index] is ThemedComboBoxItem { IsHeader: true };

    /// <summary>The items of a checked list with whether each is checked.</summary>
    public static List<(string Name, bool Checked)> ListItems(CheckedListBox? listBox)
    {
        var items = new List<(string, bool)>();

        if (listBox == null)
        {
            return items;
        }

        for (var i = 0; i < listBox.Items.Count; i++)
        {
            items.Add((listBox.GetItemText(listBox.Items[i]) ?? string.Empty, listBox.GetItemChecked(i)));
        }

        return items;
    }

    /// <summary>Checks or unchecks an item the way a click does, so the viewer runs its own callback. False when there is no such item.</summary>
    public static bool TrySetListItem(CheckedListBox? listBox, string name, bool isChecked)
    {
        var index = listBox?.FindStringExact(name) ?? -1;

        if (index < 0)
        {
            return false;
        }

        listBox!.SetItemChecked(index, isChecked);

        return true;
    }

    /// <summary>Lines of a label joined into one, so a multi line readout fits on one line.</summary>
    public static string LabelText(Label label)
        => string.Join(", ", label.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed class Walker(RendererControl sidebar)
    {
        public readonly List<SidebarControl> Controls = [];

        private readonly Dictionary<string, int> sliderCounts = [];
        private readonly Dictionary<string, Label> lastLabels = [];

        public void Walk(Control parent, string? group)
        {
            foreach (var child in TopToBottom(parent))
            {
                if (child == sidebar.GLControlContainer)
                {
                    continue;
                }

                switch (child)
                {
                    case GLViewerCheckboxControl checkBox:
                        Add(SidebarControlKind.CheckBox, checkBox.CheckBox, checkBox.CheckBox.Text, group);
                        break;

                    case GLViewerSelectionControl selection:
                        Add(SidebarControlKind.Dropdown, selection.ComboBox, Find<Label>(selection)?.Text.TrimEnd(':'), group);
                        break;

                    case GLViewerMultiSelectionControl list:
                        Add(SidebarControlKind.List, list.CheckedListBox, Find<GroupBox>(list)?.Text, group);
                        break;

                    case GLViewerSliderControl slider:
                        var key = group ?? string.Empty;
                        var index = sliderCounts.GetValueOrDefault(key);
                        sliderCounts[key] = index + 1;
                        Controls.Add(new SidebarControl(SidebarControlKind.Slider, slider.Slider, null, group, index, lastLabels.GetValueOrDefault(key)));
                        break;

                    case Label label:
                        lastLabels[group ?? string.Empty] = label;
                        break;

                    default:
                        if (NumberInput(child) is { } input)
                        {
                            Add(SidebarControlKind.Number, input.Number, input.Label.Text, group);
                            break;
                        }

                        Walk(child, child is GroupBox groupBox ? groupBox.Text : group);
                        break;
                }
            }
        }

        private void Add(SidebarControlKind kind, Control control, string? label, string? group)
            => Controls.Add(new SidebarControl(kind, control, label, group, 0, null));

        private static IEnumerable<Control> TopToBottom(Control parent)
        {
            var children = parent.Controls.Cast<Control>();

            if (parent is TableLayoutPanel table)
            {
                return children
                    .OrderBy(child => table.GetPositionFromControl(child).Row)
                    .ThenBy(child => table.GetPositionFromControl(child).Column);
            }

            // Every control is docked to the top and brought to the front as it is added, so each
            // collection holds them bottom to top.
            return children.Reverse();
        }

        /// <summary>A labelled number input, as <see cref="RendererControl.CreateFloatInput"/> builds it: a panel holding a label and the input.</summary>
        private static (ThemedFloatNumeric Number, Label Label)? NumberInput(Control panel)
        {
            ThemedFloatNumeric? number = null;
            Label? label = null;

            foreach (Control child in panel.Controls)
            {
                switch (child)
                {
                    case ThemedFloatNumeric numeric:
                        number = numeric;
                        break;
                    case Label text:
                        label = text;
                        break;
                    default:
                        return null;
                }
            }

            return number != null && label != null ? (number, label) : null;
        }

        private static T? Find<T>(Control parent)
            where T : Control
        {
            foreach (Control child in parent.Controls)
            {
                if (child is T match)
                {
                    return match;
                }

                if (Find<T>(child) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
#endif
