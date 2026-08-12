using SkiaSharp;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// The card shape a graph is measured and drawn with: the fonts each kind of text uses and the
/// paddings between them. Measurement runs here rather than in the host so a graph can be laid out
/// without a window, and so the sizes the layout works from are the ones that get drawn.
/// </summary>
public static class GraphMetrics
{
    /// <summary>Height of a node's title band.</summary>
    public const float HeaderHeight = 26f;

    /// <summary>Corner rounding of a node card.</summary>
    public const float CornerRadius = 5f;

    /// <summary>Vertical pitch of a row carrying text.</summary>
    public const float RowPitch = 22f;

    /// <summary>Vertical pitch of a socket row with no name, which needs room only for the dot.</summary>
    public const float CompactRowPitch = 14f;

    /// <summary>Gap between the header band and the first row.</summary>
    public const float RowStartPad = 6f;

    /// <summary>Gap left below the last row.</summary>
    public const float BottomPad = 8f;

    /// <summary>Horizontal padding inside a card.</summary>
    public const float MarginX = 10f;

    /// <summary>Narrowest a card may be, whatever its content measures.</summary>
    public const float MinWidth = 160f;

    /// <summary>Radius of the dot a socket is drawn as.</summary>
    public const float SocketRadius = 5f;

    /// <summary>Stroke width of a wire.</summary>
    public const float WireWidth = 2.5f;

    /// <summary>Clear space kept between the two names sharing a paired socket row.</summary>
    public const float PairGap = 28f;

    /// <summary>Font of a node's title.</summary>
    public static SKFont TitleFont { get; } = Prepare(SKTypeface
        .FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
        .ToFont(13.5f));

    /// <summary>Font of a node's subtitle.</summary>
    public static SKFont SubtitleFont { get; } = Prepare(SKTypeface.FromFamilyName("Segoe UI").ToFont(10.5f));

    /// <summary>Font of a node's rows.</summary>
    public static SKFont RowFont { get; } = Prepare(SKTypeface.FromFamilyName("Segoe UI").ToFont(12f));

    /// <summary>Font of a row carrying a message rather than data.</summary>
    public static SKFont MessageFont { get; } = Prepare(SKTypeface
        .FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic))
        .ToFont(12f));

    /// <summary>Font of the label drawn at a wire's midpoint.</summary>
    public static SKFont WireLabelFont { get; } = Prepare(SKTypeface.FromFamilyName("Segoe UI").ToFont(10.5f));

    private static SKFont Prepare(SKFont font)
    {
        font.Hinting = SKFontHinting.Normal;
        font.Subpixel = true;
        font.Edging = SKFontEdging.SubpixelAntialias;
        return font;
    }

    /// <summary>
    /// Vertical pitch of one presentation row. Socket rows without any name carry only the dot, so
    /// they stack tighter than text rows.
    /// </summary>
    public static float PitchOf(GraphRow row) => row switch
    {
        SocketRow socket when socket.Socket.Name.Length == 0 => CompactRowPitch,
        PairedSocketRow paired when paired.Input is not { Name.Length: > 0 }
                                 && paired.Output is not { Name.Length: > 0 } => CompactRowPitch,
        _ => RowPitch,
    };

    /// <summary>
    /// Measures one node into <paramref name="geometry"/> if its content changed since the last
    /// measurement, filling in the presentation rows, the card size, the row baselines and the
    /// socket pivots.
    /// </summary>
    /// <param name="node">The node to measure.</param>
    /// <param name="geometry">The geometry store the result is written into.</param>
    /// <returns>Whether anything was recomputed.</returns>
    public static bool Measure(GraphNode node, GraphGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(geometry);

        var nodeGeometry = geometry.NodeOf(node);

        if (nodeGeometry.ComputedVersion == node.ContentVersion)
        {
            return false;
        }

        BuildLayoutRows(node, nodeGeometry);
        var layoutRows = nodeGeometry.LayoutRows;

        var width = MarginX * 2f + TitleFont.MeasureText(node.Title);

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            width += 14f + SubtitleFont.MeasureText(node.Subtitle);
        }

        width = Math.Max(MinWidth, width);

        foreach (var row in layoutRows)
        {
            // Both names of a paired row share the line, so it must fit them side by side.
            var rowWidth = row switch
            {
                TextRow text => MarginX * 2f + (text.IsMessage ? 19f : 0f) + (text.Text.Length == 0 ? 0f : RowFont.MeasureText(text.Text)),
                SocketRow socket => MarginX * 2f + (socket.Socket.Name.Length == 0 ? 0f : RowFont.MeasureText(socket.Socket.Name)),
                PairedSocketRow paired => MarginX * 2f + PairGap
                    + (paired.Input is { Name.Length: > 0 } input ? RowFont.MeasureText(input.Name) : 0f)
                    + (paired.Output is { Name.Length: > 0 } output ? RowFont.MeasureText(output.Name) : 0f),
                ResourceRow resource => MarginX * 2f + 19f + RowFont.MeasureText(resource.Text),
                AnnotationRow annotation => MarginX * 2f + 14f + RowFont.MeasureText(annotation.Text),
                _ => 0f,
            };

            width = Math.Max(width, rowWidth);
        }

        var rowsHeight = 0f;

        foreach (var row in layoutRows)
        {
            rowsHeight += PitchOf(row);
        }

        var height = layoutRows.Count > 0
            ? HeaderHeight + RowStartPad + rowsHeight + BottomPad
            : HeaderHeight + 14f;

        width += node.IconKey != null ? HeaderHeight : 0f;

        nodeGeometry.RowCenters = new float[layoutRows.Count];
        var rowTop = HeaderHeight + RowStartPad;

        for (var i = 0; i < layoutRows.Count; i++)
        {
            var row = layoutRows[i];
            var pitch = PitchOf(row);
            var centerOffsetY = rowTop + pitch * 0.5f;
            rowTop += pitch;
            nodeGeometry.RowCenters[i] = centerOffsetY;

            if (row is SocketRow socketRow)
            {
                geometry.SetPivotOffset(socketRow.Socket, new Vector2(socketRow.Socket.IsInput ? 0f : width, centerOffsetY));
            }
            else if (row is PairedSocketRow pairedRow)
            {
                if (pairedRow.Input != null)
                {
                    geometry.SetPivotOffset(pairedRow.Input, new Vector2(0f, centerOffsetY));
                }

                if (pairedRow.Output != null)
                {
                    geometry.SetPivotOffset(pairedRow.Output, new Vector2(width, centerOffsetY));
                }
            }
        }

        nodeGeometry.Size = new Vector2(width, height);
        nodeGeometry.ComputedVersion = node.ContentVersion;
        return true;
    }

    /// <summary>
    /// Rebuilds a node's presentation rows: consecutive socket rows collapse into shared
    /// input-output lines, and everything else keeps a row of its own.
    /// </summary>
    private static void BuildLayoutRows(GraphNode node, NodeGeometry geometry)
    {
        var layoutRows = geometry.LayoutRows;
        layoutRows.Clear();

        var runInputs = new List<GraphSocket>();
        var runOutputs = new List<GraphSocket>();

        void FlushRun()
        {
            var count = Math.Max(runInputs.Count, runOutputs.Count);

            for (var i = 0; i < count; i++)
            {
                var input = i < runInputs.Count ? runInputs[i] : null;
                var output = i < runOutputs.Count ? runOutputs[i] : null;

                if (input != null && output != null)
                {
                    layoutRows.Add(new PairedSocketRow(input, output));
                }
                else
                {
                    layoutRows.Add(new SocketRow((input ?? output)!));
                }
            }

            runInputs.Clear();
            runOutputs.Clear();
        }

        foreach (var row in node.Rows)
        {
            if (row is SocketRow socketRow)
            {
                (socketRow.Socket.IsInput ? runInputs : runOutputs).Add(socketRow.Socket);
            }
            else
            {
                FlushRun();
                layoutRows.Add(row);
            }
        }

        FlushRun();
    }
}
