using System.Drawing;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

/// <summary>
/// A fixed height list of named events, painted directly so that replacing its lines costs one repaint and
/// no layout. Lines that are no longer current are dimmed.
/// </summary>
internal sealed class EventListControl : Control
{
    /// <param name="Name">The event name, on the left.</param>
    /// <param name="Kind">What kind of event it is, on the right.</param>
    /// <param name="Current">Whether the event is happening now, as opposed to having just ended.</param>
    public readonly record struct Line(string Name, string Kind, bool Current);

    private const int LineHeight = 17;
    private const int Gap = 6;

    private readonly int visibleLines;
    private Line[] lines = [];

    public string EmptyText { get; init; } = string.Empty;

    public EventListControl(int visibleLines)
    {
        this.visibleLines = visibleLines;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = this.AdjustForDPI(LineHeight) * visibleLines;
        Margin = Padding.Empty;
    }

    public void SetLines(Line[] newLines)
    {
        lines = newLines;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Width, Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        var lineHeight = this.AdjustForDPI(LineHeight);
        var dimmed = Themer.CurrentThemeColors.ContrastSoft;
        const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        if (lines.Length == 0)
        {
            TextRenderer.DrawText(e.Graphics, EmptyText, Font, new Rectangle(0, 0, Width, lineHeight), dimmed, Flags);
            return;
        }

        // The last line counts the ones that do not fit
        var shown = lines.Length > visibleLines ? visibleLines - 1 : lines.Length;

        for (var i = 0; i < shown; i++)
        {
            var line = lines[i];
            var top = i * lineHeight;
            var kindWidth = TextRenderer.MeasureText(e.Graphics, line.Kind, Font, new Size(int.MaxValue, lineHeight), Flags & ~TextFormatFlags.EndEllipsis).Width;
            var nameWidth = Math.Max(0, Width - kindWidth - this.AdjustForDPI(Gap));

            TextRenderer.DrawText(e.Graphics, line.Name, Font, new Rectangle(0, top, nameWidth, lineHeight), line.Current ? ForeColor : dimmed, Flags);
            TextRenderer.DrawText(e.Graphics, line.Kind, Font, new Rectangle(Width - kindWidth, top, kindWidth, lineHeight), dimmed, Flags);
        }

        if (shown < lines.Length)
        {
            TextRenderer.DrawText(e.Graphics, $"+{lines.Length - shown} more", Font, new Rectangle(0, shown * lineHeight, Width, lineHeight), dimmed, Flags);
        }
    }
}
