using SkiaSharp;
using Svg.Skia;

namespace GUI.Types.Graphs.Core;

partial class GraphView
{
    private const float HeaderHeight = 26f;
    private const float CornerRadius = 5f;
    private const float RowPitch = 22f;
    private const float RowStartPad = 6f;
    private const float BottomPad = 8f;
    private const float MarginX = 10f;
    private const float MinWidth = 160f;
    private const float SocketRadius = 5f;
    private const float WireWidth = 2.5f;

    // Below this zoom only header, body and wires are drawn.
    private const float DetailZoomCutoff = 0.2f;

    private static readonly SKFont TitleFont = SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)).ToFont(13.5f);
    private static readonly SKFont SubtitleFont = SKTypeface.FromFamilyName("Segoe UI").ToFont(10.5f);
    private static readonly SKFont RowFont = SKTypeface.FromFamilyName("Segoe UI").ToFont(12f);
    private static readonly SKFont MessageFont = SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)).ToFont(12f);
    private static readonly SKFont WireLabelFont = SKTypeface.FromFamilyName("Segoe UI").ToFont(10.5f);

    private const string MessageIconResource = "GUI.Icons.About.svg";

    static GraphView()
    {
        SKFont[] fonts = [TitleFont, SubtitleFont, RowFont, MessageFont, WireLabelFont];
        foreach (var font in fonts)
        {
            font.Hinting = SKFontHinting.Normal;
            font.Subpixel = true;
            font.Edging = SKFontEdging.SubpixelAntialias;
        }
    }

    private static readonly Dictionary<string, SKSvg?> IconCache = [];

    private static SKSvg? GetIcon(string resourcePath)
    {
        lock (IconCache)
        {
            if (!IconCache.TryGetValue(resourcePath, out var svg))
            {
                using var svgResource = typeof(GraphView).Assembly.GetManifestResourceStream(resourcePath);
                if (svgResource != null)
                {
                    svg = new SKSvg();
                    svg.Load(svgResource);
                }

                IconCache[resourcePath] = svg;
            }

            return svg;
        }
    }

    private static SKColor BlendColors(SKColor from, SKColor to, float t)
    {
        static byte Lerp(byte a, byte b, float t) => (byte)(a + (b - a) * t);
        return new SKColor(
            Lerp(from.Red, to.Red, t),
            Lerp(from.Green, to.Green, t),
            Lerp(from.Blue, to.Blue, t),
            from.Alpha);
    }

    private static void DrawIcon(SKCanvas canvas, SKSvg? svg, float x, float rowCenterY)
    {
        if (svg?.Picture is { } picture && picture.CullRect.Width > 0)
        {
            canvas.Save();
            canvas.Translate(x, rowCenterY - 7f);
            var scale = 14f / picture.CullRect.Width;
            canvas.Scale(scale, scale);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }
    }

    private readonly SKPaint fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint textPaint = new() { IsAntialias = true };
    private readonly SKPaint gridPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint wirePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint wireUnderlayPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    private SKImageFilter? shadowNormal;
    private SKImageFilter? shadowSelected;
    private SKImageFilter? shadowConnected;

    private static readonly SKPaint HitTestPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 10f };

    private static SKPath BuildWireHitPath(GraphWire wire)
    {
        var from = new SKPoint(wire.From.Pivot.X, wire.From.Pivot.Y);
        var to = new SKPoint(wire.To.Pivot.X, wire.To.Pivot.Y);

        using var path = new SKPath();
        BuildWirePath(path, from, to);
        return HitTestPaint.GetFillPath(path);
    }

    private void DisposeRenderResources()
    {
        fillPaint.Dispose();
        strokePaint.Dispose();
        textPaint.Dispose();
        gridPaint.Dispose();
        wirePaint.Dispose();
        wireUnderlayPaint.Dispose();
        shadowNormal?.Dispose();
        shadowSelected?.Dispose();
        shadowConnected?.Dispose();
        headerRoundRect.Dispose();
    }

    private void EnsureAllGeometry()
    {
        var anyChanged = false;

        foreach (var node in nodes)
        {
            anyChanged |= EnsureGeometry(node);
        }

        if (anyChanged)
        {
            ClearWireHitPaths();
        }
    }

    private static bool EnsureGeometry(GraphNode node)
    {
        if (!node.GeometryDirty)
        {
            return false;
        }

        var width = MarginX * 2f + TitleFont.MeasureText(node.Title);

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            width += 14f + SubtitleFont.MeasureText(node.Subtitle);
        }

        width = Math.Max(MinWidth, width);

        foreach (var row in node.Rows)
        {
            var rowWidth = row switch
            {
                TextRow text => MarginX * 2f + (text.IsMessage ? 19f : 0f) + (text.Text.Length == 0 ? 0f : RowFont.MeasureText(text.Text)),
                SocketRow socket => MarginX * 2f + (socket.Socket.Name.Length == 0 ? 0f : RowFont.MeasureText(socket.Socket.Name)),
                ResourceRow resource => MarginX * 2f + 19f + RowFont.MeasureText(resource.Text),
                _ => 0f,
            };

            width = Math.Max(width, rowWidth);
        }

        var height = node.Rows.Count > 0
            ? HeaderHeight + RowStartPad + node.Rows.Count * RowPitch + BottomPad
            : HeaderHeight + 14f;

        for (var i = 0; i < node.Rows.Count; i++)
        {
            var row = node.Rows[i];
            row.CenterOffsetY = HeaderHeight + RowStartPad + i * RowPitch + RowPitch * 0.5f;

            if (row is SocketRow socketRow)
            {
                socketRow.Socket.PivotOffset = new Vector2(socketRow.Socket.IsInput ? 0f : width, row.CenterOffsetY);
            }
        }

        node.Size = new Vector2(width, height);
        node.GeometryDirty = false;
        return true;
    }

    public void RenderToCanvas(SKCanvas canvas, SKRect visibleRect, float zoom)
    {
        using var _ = stateLock.EnterScope();

        canvas.Clear(Palette.Canvas);
        DrawGrid(canvas, visibleRect, zoom);

        if (nodes.Count == 0)
        {
            return;
        }

        EnsureAllGeometry();

        foreach (var wire in wires)
        {
            if (wire.From.Owner.Hidden || wire.To.Owner.Hidden)
            {
                continue;
            }

            var from = wire.From.Pivot;
            var to = wire.To.Pivot;

            var wireBounds = new SKRect(
                Math.Min(from.X, to.X) - 260f,
                Math.Min(from.Y, to.Y) - 10f,
                Math.Max(from.X, to.X) + 260f,
                Math.Max(from.Y, to.Y) + 10f);

            if (!visibleRect.IntersectsWith(wireBounds))
            {
                continue;
            }

            DrawWire(canvas, wire, zoom);
        }

        foreach (var node in nodes)
        {
            if (node.Hidden)
            {
                continue;
            }

            var nodeRect = new SKRect(node.Position.X - 14f, node.Position.Y - 14f, node.Position.X + node.Size.X + 14f, node.Position.Y + node.Size.Y + 14f);

            if (!visibleRect.IntersectsWith(nodeRect))
            {
                continue;
            }

            var isPrimarySelected = node == primarySelectedNode;
            var isConnected = !isPrimarySelected && connectedNodes.Contains(node);
            var isHovered = !isPrimarySelected && !isConnected && node == lastHovered;

            DrawNode(canvas, node, isPrimarySelected, isConnected, isHovered, zoom);
        }
    }

    // Multi-level dot grid: each level 5x the previous, dots keep near-constant screen size
    // and fade in as their level's screen spacing grows.
    private static readonly float[] GridSteps = [20f, 100f, 500f, 2500f];

    private SKPoint[] gridPointBuffer = [];

    private void DrawGrid(SKCanvas canvas, SKRect visibleRect, float zoom)
    {
        foreach (var step in GridSteps)
        {
            var spacingPx = step * zoom;

            if (spacingPx < 10f)
            {
                continue;
            }

            var fade = Math.Clamp((spacingPx - 10f) / 20f, 0f, 1f);
            fade *= fade;

            var dotRadiusPx = Math.Clamp(spacingPx / 12f, 1.2f, 2.6f);

            var startX = MathF.Floor(visibleRect.Left / step) * step;
            var startY = MathF.Floor(visibleRect.Top / step) * step;

            var countX = (int)((visibleRect.Right - startX) / step) + 1;
            var countY = (int)((visibleRect.Bottom - startY) / step) + 1;

            if (countX <= 0 || countY <= 0 || countX * countY > 40000)
            {
                continue;
            }

            if (gridPointBuffer.Length != countX * countY)
            {
                gridPointBuffer = new SKPoint[countX * countY];
            }

            var i = 0;

            for (var ix = 0; ix < countX; ix++)
            {
                for (var iy = 0; iy < countY; iy++)
                {
                    gridPointBuffer[i++] = new SKPoint(startX + ix * step, startY + iy * step);
                }
            }

            gridPaint.Color = Palette.GridDot.WithAlpha((byte)(fade * 170f));
            gridPaint.StrokeWidth = 2f * dotRadiusPx / zoom;
            canvas.DrawPoints(SKPointMode.Points, gridPointBuffer, gridPaint);
        }
    }

    // Bezier with horizontal handles; handle length grows with horizontal distance and is damped
    // when the endpoints are nearly level so straight runs stay straight.
    private static float WireHandleOffset(SKPoint from, SKPoint to)
    {
        var dx = to.X - from.X;
        var distX = Math.Abs(dx);
        var distY = Math.Abs(to.Y - from.Y);

        if (dx >= 0f)
        {
            var clampFactor = distX <= 0f ? 1f : Math.Min(1f, 3.5f * distY / distX);
            return 0.4f * distX * clampFactor;
        }

        return Math.Min(0.5f * distX + 40f, 250f);
    }

    private static void BuildWirePath(SKPath path, SKPoint from, SKPoint to)
    {
        var offset = WireHandleOffset(from, to);

        path.MoveTo(from);
        path.CubicTo(from.X + offset, from.Y, to.X - offset, to.Y, to.X, to.Y);
    }

    private void DrawWire(SKCanvas canvas, GraphWire wire, float zoom)
    {
        var fromPivot = wire.From.Pivot;
        var toPivot = wire.To.Pivot;
        var from = new SKPoint(fromPivot.X, fromPivot.Y);
        var to = new SKPoint(toPivot.X, toPivot.Y);

        if (Vector2.Distance(fromPivot, toPivot) < 1f)
        {
            return;
        }

        using var path = new SKPath();
        BuildWirePath(path, from, to);

        var width = WireWidth * Math.Max(1f, 1f / zoom);

        if (wire == lastHovered)
        {
            width *= 1.8f;
        }

        wireUnderlayPaint.Color = Palette.WireUnderlay;
        wireUnderlayPaint.StrokeWidth = width + 1.6f;
        canvas.DrawPath(path, wireUnderlayPaint);

        var touchesSelection = primarySelectedNode != null &&
            (wire.From.Owner == primarySelectedNode || wire.To.Owner == primarySelectedNode);

        wirePaint.StrokeWidth = width;
        wirePaint.PathEffect = wire.Dashed ? SKPathEffect.CreateDash([10f, 7f], 0f) : null;

        if (touchesSelection)
        {
            wirePaint.Shader = null;
            wirePaint.Color = Palette.Selection;
        }
        else if (wire.From.Hue == wire.To.Hue)
        {
            wirePaint.Shader = null;
            wirePaint.Color = Palette.Signal(wire.From.Hue);
        }
        else
        {
            wirePaint.Color = SKColors.White;
            wirePaint.Shader = SKShader.CreateLinearGradient(
                from, to,
                [Palette.Signal(wire.From.Hue), Palette.Signal(wire.To.Hue)],
                null, SKShaderTileMode.Clamp);
        }

        canvas.DrawPath(path, wirePaint);

        wirePaint.Shader?.Dispose();
        wirePaint.Shader = null;
        wirePaint.PathEffect?.Dispose();
        wirePaint.PathEffect = null;

        if (wire.Label != null && zoom >= 0.4f)
        {
            DrawWireLabel(canvas, wire.Label, from, to);
        }
    }

    private void DrawWireLabel(SKCanvas canvas, string label, SKPoint from, SKPoint to)
    {
        var midX = (from.X + to.X) / 2f;
        var midY = (from.Y + to.Y) / 2f;

        var textWidth = WireLabelFont.MeasureText(label);
        var metrics = WireLabelFont.Metrics;
        var textHeight = metrics.Descent - metrics.Ascent;

        var rect = new SKRect(midX - textWidth / 2f - 4f, midY - textHeight / 2f - 2f, midX + textWidth / 2f + 4f, midY + textHeight / 2f + 2f);
        fillPaint.Color = Palette.Canvas.WithAlpha(215);
        canvas.DrawRoundRect(rect, 4f, 4f, fillPaint);

        textPaint.Color = Palette.TextDim;
        canvas.DrawText(label, midX - textWidth / 2f, midY - (metrics.Ascent + metrics.Descent) / 2f, WireLabelFont, textPaint);
    }

    private static readonly SKPoint[] HeaderCornerRadii =
    [
        new(CornerRadius, CornerRadius),
        new(CornerRadius, CornerRadius),
        new(0, 0),
        new(0, 0),
    ];

    private readonly SKRoundRect headerRoundRect = new();

    private void DrawNode(SKCanvas canvas, GraphNode node, bool isPrimarySelected, bool isConnected, bool isHovered, float zoom)
    {
        var x = node.Position.X;
        var y = node.Position.Y;
        var rect = new SKRect(x, y, x + node.Size.X, y + node.Size.Y);

        if (zoom >= DetailZoomCutoff)
        {
            shadowNormal ??= SKImageFilter.CreateDropShadow(0, 2f, 8f, 8f, Palette.Shadow);
            shadowSelected ??= SKImageFilter.CreateDropShadow(0, 0, 12f, 12f, Palette.Selection.WithAlpha(140));
            shadowConnected ??= SKImageFilter.CreateDropShadow(0, 0, 10f, 10f, Palette.Selection.WithAlpha(80));

            fillPaint.ImageFilter = isPrimarySelected ? shadowSelected : isConnected ? shadowConnected : shadowNormal;
        }

        fillPaint.Color = node.BodyTint is { } tint
            ? BlendColors(Palette.NodeBody, Palette.Category(tint), 0.55f)
            : Palette.NodeBody;
        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, fillPaint);
        fillPaint.ImageFilter = null;

        headerRoundRect.SetRectRadii(new SKRect(x, y, x + node.Size.X, y + HeaderHeight), HeaderCornerRadii);
        fillPaint.Color = Palette.Category(node.EffectiveCategory);
        canvas.DrawRoundRect(headerRoundRect, fillPaint);

        if (isPrimarySelected)
        {
            strokePaint.Color = Palette.Selection;
            strokePaint.StrokeWidth = 2f;
        }
        else if (isConnected)
        {
            strokePaint.Color = Palette.SelectionSoft;
            strokePaint.StrokeWidth = 1.6f;
        }
        else if (isHovered)
        {
            strokePaint.Color = Palette.Hover;
            strokePaint.StrokeWidth = 1.4f;
        }
        else
        {
            strokePaint.Color = Palette.NodeOutline;
            strokePaint.StrokeWidth = 1f;
        }

        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, strokePaint);

        var titleMetrics = TitleFont.Metrics;
        var titleBaseline = y + HeaderHeight / 2f - (titleMetrics.Ascent + titleMetrics.Descent) / 2f;

        textPaint.Color = Palette.HeaderText;
        canvas.DrawText(node.Title, x + MarginX, titleBaseline, TitleFont, textPaint);

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            var subtitleWidth = SubtitleFont.MeasureText(node.Subtitle);
            textPaint.Color = Palette.HeaderTextDim;
            canvas.DrawText(node.Subtitle, rect.Right - MarginX - subtitleWidth, titleBaseline, SubtitleFont, textPaint);
        }

        if (zoom < DetailZoomCutoff)
        {
            return;
        }

        foreach (var row in node.Rows)
        {
            var rowCenterY = y + row.CenterOffsetY;

            switch (row)
            {
                case TextRow textRow:
                    DrawTextRow(canvas, textRow, x, rowCenterY);
                    break;

                case SocketRow socketRow:
                    DrawSocketRow(canvas, socketRow.Socket, rect, rowCenterY);
                    break;

                case ResourceRow resourceRow:
                    DrawResourceRow(canvas, resourceRow, x, rowCenterY);
                    break;
            }
        }
    }

    private void DrawResourceRow(SKCanvas canvas, ResourceRow row, float x, float rowCenterY)
    {
        var textX = x + MarginX;
        DrawIcon(canvas, GetIcon($"GUI.Icons.AssetTypes.{row.Icon}.svg"), textX, rowCenterY);
        textX += 19f;

        var metrics = RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;

        textPaint.Color = Palette.Signal(row.Hue);
        canvas.DrawText(row.Text, textX, baseline, RowFont, textPaint);
    }

    private void DrawTextRow(SKCanvas canvas, TextRow row, float x, float rowCenterY)
    {
        if (row.Text.Length == 0 && !row.IsMessage)
        {
            return;
        }

        var textX = x + MarginX;
        var font = row.IsMessage ? MessageFont : RowFont;

        if (row.IsMessage)
        {
            DrawIcon(canvas, GetIcon(MessageIconResource), textX, rowCenterY);
            textX += 19f;
            textPaint.Color = Palette.MessageText;
        }
        else
        {
            textPaint.Color = Palette.Text;
        }

        var metrics = font.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(row.Text, textX, baseline, font, textPaint);
    }

    private void DrawSocketRow(SKCanvas canvas, GraphSocket socket, SKRect nodeRect, float rowCenterY)
    {
        var pivotX = socket.IsInput ? nodeRect.Left : nodeRect.Right;
        var color = Palette.Signal(socket.Hue);

        // Inputs that actually receive multiple wires render as a taller capsule.
        var capsule = socket.IsInput && socket.Wires.Count > 1;

        if (capsule)
        {
            var capsuleRect = new SKRect(pivotX - 4.5f, rowCenterY - 8f, pivotX + 4.5f, rowCenterY + 8f);

            if (socket.IsConnected)
            {
                fillPaint.Color = color;
                canvas.DrawRoundRect(capsuleRect, 4.5f, 4.5f, fillPaint);
            }

            strokePaint.Color = Palette.SocketOutline;
            strokePaint.StrokeWidth = 1f;
            canvas.DrawRoundRect(capsuleRect, 4.5f, 4.5f, strokePaint);
        }
        else if (socket.IsConnected)
        {
            fillPaint.Color = color;
            canvas.DrawCircle(pivotX, rowCenterY, SocketRadius, fillPaint);

            strokePaint.Color = Palette.SocketOutline;
            strokePaint.StrokeWidth = 1f;
            canvas.DrawCircle(pivotX, rowCenterY, SocketRadius, strokePaint);
        }
        else
        {
            strokePaint.Color = color;
            strokePaint.StrokeWidth = 1.8f;
            canvas.DrawCircle(pivotX, rowCenterY, SocketRadius - 0.9f, strokePaint);
        }

        if (socket.Name.Length == 0)
        {
            return;
        }

        var metrics = RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;

        textPaint.Color = Palette.Text;

        if (socket.IsInput)
        {
            canvas.DrawText(socket.Name, nodeRect.Left + MarginX, baseline, RowFont, textPaint);
        }
        else
        {
            var textWidth = RowFont.MeasureText(socket.Name);
            canvas.DrawText(socket.Name, nodeRect.Right - MarginX - textWidth, baseline, RowFont, textPaint);
        }
    }
}
