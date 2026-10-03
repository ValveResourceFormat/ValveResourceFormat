using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// A fixed height sidebar row laid out by hand: an optional name column, the editors, and trailing controls
/// at the right edge. Placing children directly keeps long parameter lists fast to show, where nested table
/// layouts take a full layout pass per row.
/// </summary>
internal sealed class PropertyRow : Panel
{
    private const int RowHeight = 26;
    private const int Gap = 4;

#pragma warning disable CA2213 // Added to Controls, which disposes it
    private readonly Label? nameLabel;
#pragma warning restore CA2213
    private readonly Control[] editors;
    private readonly Control[] trailing;

    /// <param name="name">The name shown in the left half of the row, or <see langword="null"/> to give the editors the full width.</param>
    /// <param name="editors">Controls sharing the space between the name and the trailing controls equally.</param>
    /// <param name="trailing">Controls kept at their own width at the right edge, in order.</param>
    public PropertyRow(string? name, Control[] editors, Control[] trailing)
    {
        this.editors = editors;
        this.trailing = trailing;

        SuspendLayout();

        Height = this.AdjustForDPI(RowHeight);
        Margin = Padding.Empty;

        if (name != null)
        {
            nameLabel = new Label
            {
                Text = name,
                AutoEllipsis = true,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            };

            Controls.Add(nameLabel);
        }

        foreach (var control in editors)
        {
            // The row decides the width, so a long caption ellipsizes instead of running under the trailing controls
            if (control is CheckBox checkBox)
            {
                checkBox.AutoSize = false;
            }

            control.Margin = Padding.Empty;
            Controls.Add(control);
        }

        foreach (var control in trailing)
        {
            control.Margin = Padding.Empty;
            Controls.Add(control);
        }

        ResumeLayout(false);
    }

    // The height is fixed and the width comes from docking, so measuring the children is never needed
    public override System.Drawing.Size GetPreferredSize(System.Drawing.Size proposedSize) => new(Width, Height);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Positions are computed here in full, so the default anchoring layout must not run as well
        var area = DisplayRectangle;
        var gap = this.AdjustForDPI(Gap);
        var right = area.Right;

        for (var i = trailing.Length - 1; i >= 0; i--)
        {
            var control = trailing[i];
            right -= control.Width;
            control.SetBounds(right, CenterTop(area, control.Height), control.Width, control.Height);
            right -= gap;
        }

        var left = area.Left;

        if (nameLabel != null)
        {
            var nameWidth = area.Width / 2;
            nameLabel.SetBounds(left, area.Top, nameWidth - gap, area.Height);
            left += nameWidth;
        }

        if (editors.Length == 0)
        {
            return;
        }

        // Right now sits a gap short of the first trailing control, or at the edge without any
        var available = Math.Max(0, right - left - gap * (editors.Length - 1));

        for (var i = 0; i < editors.Length; i++)
        {
            var control = editors[i];
            var width = i == editors.Length - 1 ? available - (available / editors.Length) * i : available / editors.Length;
            var height = control is CheckBox ? control.PreferredSize.Height : control.Height;
            control.SetBounds(left, CenterTop(area, height), width, height);
            left += width + gap;
        }
    }

    private static int CenterTop(System.Drawing.Rectangle area, int height) => area.Top + (area.Height - height) / 2;
}
