using SkiaSharp;
using Svg.Skia;

namespace GUI.Types.Graphs.Core;

partial class GraphView
{
    private const float HeaderHeight = 26f;
    private const float CornerRadius = 5f;
    private const float RowPitch = 22f;
    private const float CompactRowPitch = 14f;
    private const float RowStartPad = 6f;
    private const float BottomPad = 8f;
    private const float MarginX = 10f;
    private const float MinWidth = 160f;
    private const float SocketRadius = 5f;
    private const float WireWidth = 2.5f;
    private const float PairGap = 28f;

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

    private SKPath BuildWireHitPath(GraphWire wire)
    {
        var fromPivot = Geometry.PivotOf(wire.From);
        var toPivot = Geometry.PivotOf(wire.To);
        var from = new SKPoint(fromPivot.X, fromPivot.Y);
        var to = new SKPoint(toPivot.X, toPivot.Y);

        using var pathBuilder = new SKPathBuilder();
        BuildWirePath(pathBuilder, wire, Geometry.TryRouteOf(wire), from, to);
        using var path = pathBuilder.Detach();
        return HitTestPaint.GetFillPath(path);
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

    private bool EnsureGeometry(GraphNode node)
    {
        var geometry = Geometry.NodeOf(node);

        if (geometry.ComputedVersion == node.ContentVersion)
        {
            return false;
        }

        BuildLayoutRows(node, geometry);
        var layoutRows = geometry.LayoutRows;

        var width = MarginX * 2f + TitleFont.MeasureText(node.Title);

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            width += 14f + SubtitleFont.MeasureText(node.Subtitle);
        }

        width = Math.Max(MinWidth, width);

        foreach (var row in layoutRows)
        {
            var rowWidth = row switch
            {
                TextRow text => MarginX * 2f + (text.IsMessage ? 19f : 0f) + (text.Text.Length == 0 ? 0f : RowFont.MeasureText(text.Text)),
                SocketRow socket => MarginX * 2f + (socket.Socket.Name.Length == 0 ? 0f : RowFont.MeasureText(socket.Socket.Name)),
                // Both names share the line, so the row must fit them side by side.
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

        // Room for the header entity icon; the title starts after it.
        width += node.IconKey != null ? HeaderHeight : 0f;

        geometry.RowCenters = new float[layoutRows.Count];
        var rowTop = HeaderHeight + RowStartPad;

        for (var i = 0; i < layoutRows.Count; i++)
        {
            var row = layoutRows[i];
            var pitch = PitchOf(row);
            var centerOffsetY = rowTop + pitch * 0.5f;
            rowTop += pitch;
            geometry.RowCenters[i] = centerOffsetY;

            if (row is SocketRow socketRow)
            {
                Geometry.SetPivotOffset(socketRow.Socket, new Vector2(socketRow.Socket.IsInput ? 0f : width, centerOffsetY));
            }
            else if (row is PairedSocketRow pairedRow)
            {
                if (pairedRow.Input != null)
                {
                    Geometry.SetPivotOffset(pairedRow.Input, new Vector2(0f, centerOffsetY));
                }

                if (pairedRow.Output != null)
                {
                    Geometry.SetPivotOffset(pairedRow.Output, new Vector2(width, centerOffsetY));
                }
            }
        }

        geometry.Size = new Vector2(width, height);
        geometry.ComputedVersion = node.ContentVersion;
        return true;
    }

    // Socket rows without any name carry only the dot; they stack tighter than text rows.
    private static float PitchOf(GraphRow row) => row switch
    {
        SocketRow socket when socket.Socket.Name.Length == 0 => CompactRowPitch,
        PairedSocketRow paired when paired.Input is not { Name.Length: > 0 } &&
                                    paired.Output is not { Name.Length: > 0 } => CompactRowPitch,
        _ => RowPitch,
    };

    // Consecutive socket rows collapse into shared input|output lines; anything else keeps
    // its own row. Frontend-declared paired rows pass through untouched.
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

            if (Geometry.TryRouteOf(wire)?.Waypoints is { Count: > 0 } routedPoints)
            {
                foreach (var waypoint in routedPoints)
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

            var isPrimarySelected = node == Selection.PrimaryNode;
            SKColor? connectedColor = null;

            if (!isPrimarySelected && tier == RenderTier.Focus)
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

            var isHovered = !isPrimarySelected && connectedColor == null && node == lastHovered;

            DrawNode(canvas, node, isPrimarySelected, connectedColor, isHovered, zoom);
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

    // A routed wire draws as one continuous curve through its waypoints rather than as straight
    // runs joined by fillets, so a wire detouring around a card still reads as the same kind of
    // line as every other wire. Catmull-Rom tangents converted to cubic segments, with the end
    // points duplicated so the curve starts and finishes exactly on the sockets.
    private static void BuildSmoothRoute(SKPathBuilder path, SKPoint from, List<Vector2> waypoints, SKPoint to)
    {
        var points = new List<Vector2>(waypoints.Count + 2) { new(from.X, from.Y) };
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
        // Exact routed curve from the layout, drawn verbatim; conics carry the rounded
        // segment corners precisely.
        if (route?.CurvePath is { Count: >= 2 } commands)
        {
            foreach (var command in commands)
            {
                switch (command.Verb)
                {
                    case GraphCurveVerb.MoveTo:
                        path.MoveTo(new SKPoint(command.End.X, command.End.Y));
                        break;

                    case GraphCurveVerb.LineTo:
                        path.LineTo(new SKPoint(command.End.X, command.End.Y));
                        break;

                    case GraphCurveVerb.CubicTo:
                        path.CubicTo(
                            new SKPoint(command.A.X, command.A.Y),
                            new SKPoint(command.B.X, command.B.Y),
                            new SKPoint(command.End.X, command.End.Y));
                        break;

                    case GraphCurveVerb.ConicTo:
                        path.ConicTo(
                            new SKPoint(command.A.X, command.A.Y),
                            new SKPoint(command.End.X, command.End.Y),
                            command.Weight);
                        break;
                }
            }

            return;
        }

        if (route?.Waypoints is not { Count: > 0 } waypoints)
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

            foreach (var waypoint in waypoints)
            {
                path.LineTo(new SKPoint(waypoint.X, waypoint.Y));
            }

            path.LineTo(to);
            return;
        }

        BuildSmoothRoute(path, from, waypoints, to);
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

        var route = Geometry.TryRouteOf(wire);

        // A self-wire without a route would draw straight through its own card; give it
        // the deterministic synthetic loop instead.
        if (wire.From.Owner == wire.To.Owner && (route == null || (route.Waypoints == null && route.CurvePath == null)))
        {
            GraphLayout.SynthesizeSelfLoop(wire, Geometry);
            route = Geometry.TryRouteOf(wire);
        }

        using var pathBuilder = new SKPathBuilder();
        BuildWirePath(pathBuilder, wire, route, from, to);
        using var path = pathBuilder.Detach();

        var width = WireWidth * Math.Max(1f, 1f / zoom);

        if (wire == lastHovered || wire == Selection.Wire)
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
            wirePaint.Shader = SKShader.CreateLinearGradient(
                from, to,
                [Palette.Signal(wire.From.Hue), Palette.Signal(wire.To.Hue)],
                null, SKShaderTileMode.Clamp);
        }

        canvas.DrawPath(path, wirePaint);

        wirePaint.Shader?.Dispose();
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

        var textWidth = WireLabelFont.MeasureText(label);
        var metrics = WireLabelFont.Metrics;
        var textHeight = metrics.Descent - metrics.Ascent;

        var rect = new SKRect(midX - textWidth / 2f - 4f, midY - textHeight / 2f - 2f, midX + textWidth / 2f + 4f, midY + textHeight / 2f + 2f);
        fillPaint.Color = Palette.Canvas.WithAlpha(215);
        canvas.DrawRoundRect(rect, 4f, 4f, fillPaint);

        textPaint.Color = Palette.TextDim;
        canvas.DrawText(label, midX - textWidth / 2f, midY - (metrics.Ascent + metrics.Descent) / 2f, SKTextAlign.Left, WireLabelFont, textPaint);
    }

    private static readonly SKPoint[] HeaderCornerRadii =
    [
        new(CornerRadius, CornerRadius),
        new(CornerRadius, CornerRadius),
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
        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, fillPaint);
        fillPaint.ImageFilter = null;

        headerRoundRect.SetRectRadii(new SKRect(x, y, x + size.X, y + HeaderHeight), HeaderCornerRadii);
        fillPaint.Color = Palette.Category(node.EffectiveCategory);
        canvas.DrawRoundRect(headerRoundRect, fillPaint);

        // Hammer entity icon at the left end of the header; the title shifts right of it.
        var titleOffset = 0f;

        if (node.IconKey != null)
        {
            titleOffset = HeaderHeight;

            if (zoom >= DetailZoomCutoff && IconResolver?.Invoke(node.IconKey) is { } icon)
            {
                var iconRect = new SKRect(x + 4f, y + 2f, x + 26f, y + HeaderHeight - 2f);
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

        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, strokePaint);

        var titleMetrics = TitleFont.Metrics;
        var titleBaseline = y + HeaderHeight / 2f - (titleMetrics.Ascent + titleMetrics.Descent) / 2f;

        textPaint.Color = Palette.HeaderText;
        canvas.DrawText(node.Title, x + titleOffset + MarginX, titleBaseline, SKTextAlign.Left, TitleFont, textPaint);

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            var subtitleWidth = SubtitleFont.MeasureText(node.Subtitle);
            textPaint.Color = Palette.HeaderTextDim;
            canvas.DrawText(node.Subtitle, rect.Right - MarginX - subtitleWidth, titleBaseline, SKTextAlign.Left, SubtitleFont, textPaint);
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
        var textX = x + MarginX;
        DrawIcon(canvas, GetIcon($"GUI.Icons.AssetTypes.{row.Icon}.svg"), textX, rowCenterY);
        textX += 19f;

        var metrics = RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;

        textPaint.Color = Palette.Signal(row.Hue);
        canvas.DrawText(row.Text, textX, baseline, SKTextAlign.Left, RowFont, textPaint);
    }

    private void DrawAnnotationRow(SKCanvas canvas, AnnotationRow row, float x, float rowCenterY)
    {
        var color = Palette.Signal(row.Hue);
        var markerX = x + MarginX + 4f;

        using var markerBuilder = new SKPathBuilder();
        markerBuilder.MoveTo(new SKPoint(markerX, rowCenterY - 4f));
        markerBuilder.LineTo(new SKPoint(markerX + 4f, rowCenterY));
        markerBuilder.LineTo(new SKPoint(markerX, rowCenterY + 4f));
        markerBuilder.LineTo(new SKPoint(markerX - 4f, rowCenterY));
        markerBuilder.Close();
        using var markerPath = markerBuilder.Detach();
        arrowPaint.Color = color;
        canvas.DrawPath(markerPath, arrowPaint);

        var metrics = RowFont.Metrics;
        var baseline = rowCenterY - (metrics.Ascent + metrics.Descent) / 2f;
        textPaint.Color = color;
        canvas.DrawText(row.Text, x + MarginX + 14f, baseline, SKTextAlign.Left, RowFont, textPaint);
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
            canvas.DrawText(socket.Name, nodeRect.Left + MarginX, baseline, SKTextAlign.Left, RowFont, textPaint);
        }
        else
        {
            var textWidth = RowFont.MeasureText(socket.Name);
            canvas.DrawText(socket.Name, nodeRect.Right - MarginX - textWidth, baseline, SKTextAlign.Left, RowFont, textPaint);
        }
    }
}
