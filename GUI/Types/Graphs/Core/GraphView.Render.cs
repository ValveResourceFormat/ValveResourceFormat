using SkiaSharp;
using ValveResourceFormat.Graphs;
using Svg.Skia;

namespace GUI.Types.Graphs.Core;

partial class GraphView
{
    // Below this zoom only header, body and wires are drawn.
    private const float DetailZoomCutoff = 0.2f;

    private const string MessageIconResource = "GUI.Icons.About.svg";

    static GraphView()
    {
        SKFont[] fonts = [GraphMetrics.TitleFont, GraphMetrics.SubtitleFont, GraphMetrics.RowFont, GraphMetrics.MessageFont, GraphMetrics.WireLabelFont];
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
    private readonly SKPaint arrowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint chainLayerPaint = new() { Color = SKColors.Black.WithAlpha(150) };
    private readonly SKPaint strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint textPaint = new() { IsAntialias = true };
    private readonly SKPaint gridPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint wirePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint wireUnderlayPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPathEffect wireDashEffect = SKPathEffect.CreateDash([10f, 7f], 0f);

    private SKImageFilter? shadowNormal;
    private SKImageFilter? shadowSelected;
    private SKImageFilter? shadowConnected;

    private static readonly SKPaint HitTestPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 10f };

    /// <summary>
    /// One wire's paint geometry: the drawn path, the fattened path hit testing runs against and
    /// the gradient of a wire whose ends differ in hue. Held until anything moves the wire.
    /// </summary>
    private sealed class WirePaths : IDisposable
    {
        public SKPath? Path;
        public SKPath? HitPath;
        public SKRect HitBounds;
        public SKShader? Shader;

        public void Dispose()
        {
            Path?.Dispose();
            HitPath?.Dispose();
            Shader?.Dispose();
        }
    }

    private WirePaths EnsureWirePath(GraphWire wire)
    {
        if (!wirePaths.TryGetValue(wire, out var cached))
        {
            cached = new WirePaths();
            wirePaths[wire] = cached;
        }

        if (cached.Path == null)
        {
            var fromPivot = Geometry.PivotOf(wire.From);
            var toPivot = Geometry.PivotOf(wire.To);

            using var pathBuilder = new SKPathBuilder();
            BuildWirePath(pathBuilder, wire, Geometry.TryRouteOf(wire), new SKPoint(fromPivot.X, fromPivot.Y), new SKPoint(toPivot.X, toPivot.Y));
            cached.Path = pathBuilder.Detach();
        }

        return cached;
    }

    private WirePaths EnsureWireHitPath(GraphWire wire)
    {
        var cached = EnsureWirePath(wire);

        if (cached.HitPath == null)
        {
            cached.HitPath = HitTestPaint.GetFillPath(cached.Path!);
            cached.HitBounds = cached.HitPath.Bounds;
        }

        return cached;
    }

    private void DisposeRenderResources()
    {
        arrowPaint.Dispose();
        chainLayerPaint.Dispose();
        fillPaint.Dispose();
        strokePaint.Dispose();
        textPaint.Dispose();
        gridPaint.Dispose();
        wirePaint.Dispose();
        wireUnderlayPaint.Dispose();
        wireDashEffect.Dispose();
        shadowNormal?.Dispose();
        shadowSelected?.Dispose();
        shadowConnected?.Dispose();
        headerRoundRect.Dispose();
    }

    private void EnsureAllGeometry()
    {
        if (Document.EnsureGeometry())
        {
            ClearWirePaths();
        }
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

        DrawWiresPass(canvas, visibleRect, zoom, RenderTier.Background);
        DrawNodesPass(canvas, visibleRect, zoom, RenderTier.Background);

        // With a selection or an active search, dim everything drawn so far, render the
        // transitive chain faintly above the dim, then the focused set (selection + direct
        // neighbors, wire + endpoints, or search matches) at full strength.
        if (!Selection.IsEmpty || searchHighlight != null)
        {
            fillPaint.Color = Palette.Canvas.WithAlpha(165);
            canvas.DrawRect(visibleRect, fillPaint);

            if (searchHighlight == null && Selection.PrimaryNode != null && Selection.Connected.Count > 1)
            {
                canvas.SaveLayer(chainLayerPaint);
                DrawWiresPass(canvas, visibleRect, zoom, RenderTier.Chain);
                DrawNodesPass(canvas, visibleRect, zoom, RenderTier.Chain);
                canvas.Restore();
            }

            DrawWiresPass(canvas, visibleRect, zoom, RenderTier.Focus);
            DrawNodesPass(canvas, visibleRect, zoom, RenderTier.Focus);
        }
    }

    private enum RenderTier
    {
        Background,
        Chain,
        Focus,
    }

    private RenderTier WireTier(GraphWire wire)
    {
        if (searchHighlight != null)
        {
            return MatchesSearchHighlight(wire.From.Owner) && MatchesSearchHighlight(wire.To.Owner)
                ? RenderTier.Focus
                : RenderTier.Background;
        }

        if (wire == Selection.Wire ||
            (Selection.PrimaryNode != null && (wire.From.Owner == Selection.PrimaryNode || wire.To.Owner == Selection.PrimaryNode)))
        {
            return RenderTier.Focus;
        }

        if (Selection.PrimaryNode != null && Selection.Connected.Contains(wire.From.Owner) && Selection.Connected.Contains(wire.To.Owner))
        {
            return RenderTier.Chain;
        }

        return RenderTier.Background;
    }

    private RenderTier NodeTier(GraphNode node)
    {
        if (searchHighlight != null)
        {
            return MatchesSearchHighlight(node) ? RenderTier.Focus : RenderTier.Background;
        }

        if (node == Selection.PrimaryNode ||
            (Selection.PrimaryNode != null && Selection.Direct.Contains(node)) ||
            (Selection.Wire != null && (Selection.Wire.From.Owner == node || Selection.Wire.To.Owner == node)))
        {
            return RenderTier.Focus;
        }

        if (Selection.PrimaryNode != null && Selection.Connected.Contains(node))
        {
            return RenderTier.Chain;
        }

        return RenderTier.Background;
    }

    private void DrawWiresPass(SKCanvas canvas, SKRect visibleRect, float zoom, RenderTier tier)
    {
        foreach (var wire in wires)
        {
            if (wire.From.Owner.Hidden || wire.To.Owner.Hidden || WireTier(wire) != tier)
            {
                continue;
            }

            var from = Geometry.PivotOf(wire.From);
            var to = Geometry.PivotOf(wire.To);

            var wireBounds = new SKRect(
                Math.Min(from.X, to.X) - 260f,
                Math.Min(from.Y, to.Y) - 10f,
                Math.Max(from.X, to.X) + 260f,
                Math.Max(from.Y, to.Y) + 10f);

            if (Geometry.TryRouteOf(wire) is { IsRouted: true } routedRoute)
            {
                foreach (var waypoint in routedRoute.Waypoints)
                {
                    wireBounds.Left = Math.Min(wireBounds.Left, waypoint.X - 40f);
                    wireBounds.Top = Math.Min(wireBounds.Top, waypoint.Y - 40f);
                    wireBounds.Right = Math.Max(wireBounds.Right, waypoint.X + 40f);
                    wireBounds.Bottom = Math.Max(wireBounds.Bottom, waypoint.Y + 40f);
                }
            }

            if (!visibleRect.IntersectsWith(wireBounds))
            {
                continue;
            }

            DrawWire(canvas, wire, zoom);
        }
    }

    private void DrawNodesPass(SKCanvas canvas, SKRect visibleRect, float zoom, RenderTier tier)
    {
        // A search owns the emphasis while it runs: matches read as the focused set on their own,
        // without the selection colouring a second, competing set of cards on top of them.
        var searching = searchHighlight != null;

        foreach (var node in nodes)
        {
            if (node.Hidden || NodeTier(node) != tier)
            {
                continue;
            }

            var size = Geometry.SizeOf(node);
            var nodeRect = new SKRect(node.Position.X - 14f, node.Position.Y - 14f, node.Position.X + size.X + 14f, node.Position.Y + size.Y + 14f);

            if (!visibleRect.IntersectsWith(nodeRect))
            {
                continue;
            }

            var isPrimarySelected = !searching && node == Selection.PrimaryNode;
            SKColor? connectedColor = null;

            if (!searching && !isPrimarySelected && tier == RenderTier.Focus)
            {
                if (Selection.PrimaryNode != null)
                {
                    // Directional highlight: upstream neighbors in the input color,
                    // downstream in the output color, both directions in accent.
                    var isIn = Selection.DirectIn.Contains(node);
                    var isOut = Selection.DirectOut.Contains(node);
                    connectedColor = isIn == isOut ? Palette.SelectionSoft
                        : isIn ? Palette.Signal(GraphHue.Cyan)
                        : Palette.Signal(GraphHue.Orange);
                }
                else
                {
                    connectedColor = Palette.SelectionSoft;
                }
            }

            var isHovered = !isPrimarySelected && connectedColor == null && node == hoveredNode;

            DrawNode(canvas, node, isPrimarySelected, connectedColor, isHovered, zoom);
        }
    }

    // Multi-level dot grid: each level 5x the previous, dots keep near-constant screen size
    // and fade in as their level's screen spacing grows.
    private static readonly float[] GridSteps = [20f, 100f, 500f, 2500f];

    // One buffer per level, sized exactly because DrawPoints takes a whole array, and kept until
    // that level's dot count changes.
    private readonly SKPoint[]?[] gridPointBuffers = new SKPoint[GridSteps.Length][];

    private void DrawGrid(SKCanvas canvas, SKRect visibleRect, float zoom)
    {
        for (var level = 0; level < GridSteps.Length; level++)
        {
            var step = GridSteps[level];
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

            var needed = countX * countY;

            if (countX <= 0 || countY <= 0 || needed > 40000)
            {
                continue;
            }

            var buffer = gridPointBuffers[level];

            if (buffer == null || buffer.Length != needed)
            {
                buffer = new SKPoint[needed];
                gridPointBuffers[level] = buffer;
            }

            var i = 0;

            for (var ix = 0; ix < countX; ix++)
            {
                for (var iy = 0; iy < countY; iy++)
                {
                    buffer[i++] = new SKPoint(startX + ix * step, startY + iy * step);
                }
            }

            gridPaint.Color = Palette.GridDot.WithAlpha((byte)(fade * 170f));
            gridPaint.StrokeWidth = 2f * dotRadiusPx / zoom;
            canvas.DrawPoints(SKPointMode.Points, buffer, gridPaint);
        }
    }

    // Bezier with horizontal handles; handle length grows with horizontal distance and is damped
    // when the endpoints are nearly level so straight runs stay straight. The fan reach pushes
    // wires sharing one socket apart so they do not draw on top of each other.
    private static void BuildWirePath(SKPathBuilder path, SKPoint from, SKPoint to)
    {
        var offset = GraphWireGeometry.HandleOffset(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));

        path.MoveTo(from);
        path.CubicTo(new SKPoint(from.X + offset, from.Y), new SKPoint(to.X - offset, to.Y), to);
    }

    /// <summary>How much of the neighbouring span each spline tangent reaches across.</summary>
    private const float RouteTension = 1f / 5f;

    private readonly List<Vector2> smoothRoutePoints = [];

    // A routed wire draws as one continuous curve through its waypoints rather than as straight
    // runs joined by fillets, so a wire detouring around a card still reads as the same kind of
    // line as every other wire. Catmull-Rom tangents converted to cubic segments, with the end
    // points duplicated so the curve starts and finishes exactly on the sockets.
    private void BuildSmoothRoute(SKPathBuilder path, SKPoint from, List<Vector2> waypoints, SKPoint to)
    {
        var points = smoothRoutePoints;
        points.Clear();
        points.Add(new Vector2(from.X, from.Y));
        points.AddRange(waypoints);
        points.Add(new Vector2(to.X, to.Y));

        path.MoveTo(from);

        for (var i = 0; i + 1 < points.Count; i++)
        {
            var p0 = points[Math.Max(i - 1, 0)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(i + 2, points.Count - 1)];

            var c1 = p1 + (p2 - p0) * RouteTension;
            var c2 = p2 - (p3 - p1) * RouteTension;

            path.CubicTo(new SKPoint(c1.X, c1.Y), new SKPoint(c2.X, c2.Y), new SKPoint(p2.X, p2.Y));
        }
    }


    // Straight mode: a short horizontal stub at each end so the wire visibly belongs to its
    // socket, and one plain segment between them.
    private static void BuildStraightWirePath(SKPathBuilder path, SKPoint from, SKPoint to)
    {
        const float stub = GraphWireGeometry.StraightStub;

        path.MoveTo(from);
        path.LineTo(new SKPoint(from.X + stub, from.Y));
        path.LineTo(new SKPoint(to.X - stub, to.Y));
        path.LineTo(to);
    }

    private void BuildWirePath(SKPathBuilder path, GraphWire wire, WireRoute? route, SKPoint from, SKPoint to)
    {
        if (route is not { IsRouted: true })
        {
            if (StraightWires)
            {
                BuildStraightWirePath(path, from, to);
            }
            else
            {
                BuildWirePath(path, from, to);
            }

            return;
        }

        // Straight mode squares off everything; otherwise only a self-loop keeps its right
        // angles, because the boxy orbit is what identifies it as looping back into its own card.
        if (StraightWires || wire.From.Owner == wire.To.Owner)
        {
            path.MoveTo(from);

            foreach (var waypoint in route.Waypoints)
            {
                path.LineTo(new SKPoint(waypoint.X, waypoint.Y));
            }

            path.LineTo(to);
            return;
        }

        BuildSmoothRoute(path, from, route.Waypoints, to);
    }

    private void DrawWire(SKCanvas canvas, GraphWire wire, float zoom)
    {
        var fromPivot = Geometry.PivotOf(wire.From);
        var toPivot = Geometry.PivotOf(wire.To);
        var from = new SKPoint(fromPivot.X, fromPivot.Y);
        var to = new SKPoint(toPivot.X, toPivot.Y);

        if (Vector2.Distance(fromPivot, toPivot) < 1f)
        {
            return;
        }

        // A self-wire without a route would draw straight through its own card; give it
        // the deterministic synthetic loop instead.
        if (wire.From.Owner == wire.To.Owner && Geometry.TryRouteOf(wire) is not { IsRouted: true })
        {
            Document.ReanchorWireWaypoints(wire.From.Owner);

            if (wirePaths.Remove(wire, out var stale))
            {
                stale.Dispose();
            }
        }

        var cached = EnsureWirePath(wire);
        var path = cached.Path!;

        var width = GraphMetrics.WireWidth * Math.Max(1f, 1f / zoom);

        if (wire == hoveredWire || wire == Selection.Wire)
        {
            width *= 1.8f;
        }

        wireUnderlayPaint.Color = Palette.WireUnderlay;
        wireUnderlayPaint.StrokeWidth = width + 1.6f;
        canvas.DrawPath(path, wireUnderlayPaint);

        var touchesSelection = wire == Selection.Wire ||
            (Selection.PrimaryNode != null &&
            (wire.From.Owner == Selection.PrimaryNode || wire.To.Owner == Selection.PrimaryNode));

        wirePaint.StrokeWidth = width;
        wirePaint.PathEffect = wire.Dashed ? wireDashEffect : null;

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
            cached.Shader ??= SKShader.CreateLinearGradient(
                from, to,
                [Palette.Signal(wire.From.Hue), Palette.Signal(wire.To.Hue)],
                null, SKShaderTileMode.Clamp);
            wirePaint.Shader = cached.Shader;
        }

        canvas.DrawPath(path, wirePaint);

        wirePaint.Shader = null;
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

        var textWidth = GraphMetrics.WireLabelFont.MeasureText(label);
        var metrics = GraphMetrics.WireLabelFont.Metrics;
        var textHeight = metrics.Descent - metrics.Ascent;

        var rect = new SKRect(midX - textWidth / 2f - 4f, midY - textHeight / 2f - 2f, midX + textWidth / 2f + 4f, midY + textHeight / 2f + 2f);
        fillPaint.Color = Palette.Canvas.WithAlpha(215);
        canvas.DrawRoundRect(rect, 4f, 4f, fillPaint);

        textPaint.Color = Palette.TextDim;
        canvas.DrawText(label, midX - textWidth / 2f, midY - (metrics.Ascent + metrics.Descent) / 2f, SKTextAlign.Left, GraphMetrics.WireLabelFont, textPaint);
    }

    private static readonly SKPoint[] HeaderCornerRadii =
    [
        new(GraphMetrics.CornerRadius, GraphMetrics.CornerRadius),
        new(GraphMetrics.CornerRadius, GraphMetrics.CornerRadius),
        new(0, 0),
        new(0, 0),
    ];

    private static readonly SKSamplingOptions IconSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly SKRoundRect headerRoundRect = new();

    private void DrawNode(SKCanvas canvas, GraphNode node, bool isPrimarySelected, SKColor? connectedColor, bool isHovered, float zoom)
    {
        var x = node.Position.X;
        var y = node.Position.Y;
        var size = Geometry.SizeOf(node);
        var rect = new SKRect(x, y, x + size.X, y + size.Y);

        if (zoom >= DetailZoomCutoff)
        {
            shadowNormal ??= SKImageFilter.CreateDropShadow(0, 2f, 8f, 8f, Palette.Shadow);
            shadowSelected ??= SKImageFilter.CreateDropShadow(0, 0, 12f, 12f, Palette.Selection.WithAlpha(140));
            shadowConnected ??= SKImageFilter.CreateDropShadow(0, 0, 10f, 10f, Palette.Selection.WithAlpha(80));

            fillPaint.ImageFilter = isPrimarySelected ? shadowSelected : connectedColor != null ? shadowConnected : shadowNormal;
        }

        fillPaint.Color = node.BodyTint is { } tint
            ? BlendColors(Palette.NodeBody, Palette.Category(tint), 0.55f)
            : Palette.NodeBody;
        canvas.DrawRoundRect(rect, GraphMetrics.CornerRadius, GraphMetrics.CornerRadius, fillPaint);
        fillPaint.ImageFilter = null;

        headerRoundRect.SetRectRadii(new SKRect(x, y, x + size.X, y + GraphMetrics.HeaderHeight), HeaderCornerRadii);
        fillPaint.Color = Palette.Category(node.EffectiveCategory);
        canvas.DrawRoundRect(headerRoundRect, fillPaint);

        // Hammer entity icon at the left end of the header; the title shifts right of it.
        var titleOffset = 0f;

        if (node.IconKey != null)
        {
            titleOffset = GraphMetrics.HeaderHeight;

            if (zoom >= DetailZoomCutoff && IconResolver?.Invoke(node.IconKey) is { } icon)
            {
                var iconRect = new SKRect(x + 4f, y + 2f, x + 26f, y + GraphMetrics.HeaderHeight - 2f);
                canvas.DrawImage(icon, iconRect, IconSampling);
            }
        }

        if (isPrimarySelected)
        {
            strokePaint.Color = Palette.Selection;
            strokePaint.StrokeWidth = 2f;
        }
        else if (connectedColor is { } connected)
        {
            strokePaint.Color = connected;
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

        canvas.DrawRoundRect(rect, GraphMetrics.CornerRadius, GraphMetrics.CornerRadius, strokePaint);

        var titleMetrics = GraphMetrics.TitleFont.Metrics;
        var titleBaseline = y + GraphMetrics.HeaderHeight / 2f - (titleMetrics.Ascent + titleMetrics.Descent) / 2f;

        textPaint.Color = Palette.HeaderText;
        canvas.DrawText(node.Title, x + titleOffset + GraphMetrics.MarginX, titleBaseline, SKTextAlign.Left, GraphMetrics.TitleFont, textPaint);

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            var subtitleWidth = GraphMetrics.SubtitleFont.MeasureText(node.Subtitle);
            textPaint.Color = Palette.HeaderTextDim;
            canvas.DrawText(node.Subtitle, rect.Right - GraphMetrics.MarginX - subtitleWidth, titleBaseline, SKTextAlign.Left, GraphMetrics.SubtitleFont, textPaint);
        }

        if (zoom < DetailZoomCutoff)
        {
            return;
        }

        var geometry = Geometry.NodeOf(node);

        for (var rowIndex = 0; rowIndex < geometry.LayoutRows.Count; rowIndex++)
        {
            var row = geometry.LayoutRows[rowIndex];
            var rowCenterY = y + geometry.RowCenters[rowIndex];

            switch (row)
            {
                case TextRow textRow:
                    DrawTextRow(canvas, textRow, x, rowCenterY);
                    break;

                case SocketRow socketRow:
                    DrawSocketRow(canvas, socketRow.Socket, rect, rowCenterY);
                    break;

                case PairedSocketRow pairedRow:
                    if (pairedRow.Input != null)
                    {
                        DrawSocketRow(canvas, pairedRow.Input, rect, rowCenterY);
                    }

                    if (pairedRow.Output != null)
                    {
                        DrawSocketRow(canvas, pairedRow.Output, rect, rowCenterY);
                    }

                    break;

                case ResourceRow resourceRow:
                    DrawResourceRow(canvas, resourceRow, x, rowCenterY);
                    break;

                case AnnotationRow annotationRow:
                    DrawAnnotationRow(canvas, annotationRow, x, rowCenterY);
                    break;
            }
        }
    }

    private void DrawResourceRow(SKCanvas canvas, ResourceRow row, float x, float rowCenterY)
    {
        var textX = x + GraphMetrics.MarginX;
        DrawIcon(canvas, GetIcon($"GUI.Icons.AssetTypes.{row.Icon}.svg"), textX, rowCenterY);
        textX += 19f;

        var metrics = GraphMetrics.RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;

        textPaint.Color = Palette.Signal(row.Hue);
        canvas.DrawText(row.Text, textX, baseline, SKTextAlign.Left, GraphMetrics.RowFont, textPaint);
    }

    private void DrawAnnotationRow(SKCanvas canvas, AnnotationRow row, float x, float rowCenterY)
    {
        var color = Palette.Signal(row.Hue);
        var markerX = x + GraphMetrics.MarginX + 4f;

        using var markerBuilder = new SKPathBuilder();
        markerBuilder.MoveTo(new SKPoint(markerX, rowCenterY - 4f));
        markerBuilder.LineTo(new SKPoint(markerX + 4f, rowCenterY));
        markerBuilder.LineTo(new SKPoint(markerX, rowCenterY + 4f));
        markerBuilder.LineTo(new SKPoint(markerX - 4f, rowCenterY));
        markerBuilder.Close();
        using var markerPath = markerBuilder.Detach();
        arrowPaint.Color = color;
        canvas.DrawPath(markerPath, arrowPaint);

        var metrics = GraphMetrics.RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;
        textPaint.Color = color;
        canvas.DrawText(row.Text, x + GraphMetrics.MarginX + 14f, baseline, SKTextAlign.Left, GraphMetrics.RowFont, textPaint);
    }

    private void DrawTextRow(SKCanvas canvas, TextRow row, float x, float rowCenterY)
    {
        if (row.Text.Length == 0 && !row.IsMessage)
        {
            return;
        }

        var textX = x + GraphMetrics.MarginX;
        var font = row.IsMessage ? GraphMetrics.MessageFont : GraphMetrics.RowFont;

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
        canvas.DrawText(row.Text, textX, baseline, SKTextAlign.Left, font, textPaint);
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
            canvas.DrawCircle(pivotX, rowCenterY, GraphMetrics.SocketRadius, fillPaint);

            strokePaint.Color = Palette.SocketOutline;
            strokePaint.StrokeWidth = 1f;
            canvas.DrawCircle(pivotX, rowCenterY, GraphMetrics.SocketRadius, strokePaint);
        }
        else
        {
            strokePaint.Color = color;
            strokePaint.StrokeWidth = 1.8f;
            canvas.DrawCircle(pivotX, rowCenterY, GraphMetrics.SocketRadius - 0.9f, strokePaint);
        }

        if (socket.Name.Length == 0)
        {
            return;
        }

        var metrics = GraphMetrics.RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;

        textPaint.Color = Palette.Text;

        if (socket.IsInput)
        {
            canvas.DrawText(socket.Name, nodeRect.Left + GraphMetrics.MarginX, baseline, SKTextAlign.Left, GraphMetrics.RowFont, textPaint);
        }
        else
        {
            var textWidth = GraphMetrics.RowFont.MeasureText(socket.Name);
            canvas.DrawText(socket.Name, nodeRect.Right - GraphMetrics.MarginX - textWidth, baseline, SKTextAlign.Left, GraphMetrics.RowFont, textPaint);
        }
    }
}
