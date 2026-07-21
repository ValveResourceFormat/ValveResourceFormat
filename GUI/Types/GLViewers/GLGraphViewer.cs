using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Graphs;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// OpenGL host for <see cref="GraphView"/>: provides a GL-backed Skia surface, pan/zoom,
    /// raster copy/save and double click to open node resource references.
    /// </summary>
    class GLGraphViewer : GLTextureViewer
    {
        protected readonly GraphView View;
        private SKRect graphBounds;
        private bool needsFit = true;
        private ThemedContextMenuStrip? contextMenu;

        // Skia/OpenGL context
        private GRGlInterface? glInterface;
        private GRContext? grContext;
        private GRBackendRenderTarget? renderTarget;
        private SKSurface? surface;
        private SKSizeI lastSize;
        private int lastRenderHash;
        private int numRendersLastHash;

        public GLGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, GraphView view)
            : base(vrfGuiContext, rendererContext, (SKBitmap?)null)
        {
            View = view;
            View.GraphChanged += OnGraphChanged;
            graphBounds = View.GetGraphBounds();
        }

        private void OnGraphChanged(object? sender, System.EventArgs e)
        {
            InvalidateRender();
        }

        protected override bool ShowResetZoomButton => false;

        private Label? statsLabel;
        private ThemedTextBox? searchBox;
        private Panel? legendPanel;
        private GraphNode? lastSearchResult;
        private ListBox? xrefList;
        private readonly List<GraphNode> xrefTargets = [];
        private ThemedTextBox? inspectorBox;
        private ListBox? hubsList;
        private readonly List<GraphNode> hubTargets = [];
        private readonly HashSet<GraphHue> legendFilter = [];

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            base.AddUiControls();

            {
                statsLabel = new Label
                {
                    AutoSize = false,
                    AutoEllipsis = true,
                    Padding = new Padding(3, 8, 3, 0),
                };
                UiControl.AddControl(statsLabel);
                RefreshStatsLabel();

                searchBox = new ThemedTextBox
                {
                    PlaceholderText = "Name search (Enter = next)",
                };
                searchBox.KeyDown += OnSearchKeyDown;
                searchBox.TextChanged += (_, _) => View.SetSearchHighlight(searchBox.Text);
                UiControl.AddControl(searchBox);

                var suppressPlacementChange = true;
                var placementCombo = UiControl.AddSelection("Layout", (_, index) =>
                {
                    if (suppressPlacementChange || index < 0)
                    {
                        return;
                    }

                    View.Placement = (GraphPlacement)index;
                    View.LayoutNodesPacked();
                    RefitToGraph();
                });
                placementCombo.Items.AddRange(new object[] { "Organic (MDS)", "Layered (Sugiyama)" });
                placementCombo.SelectedIndex = (int)View.Placement;
                suppressPlacementChange = false;

                var subtitles = View.GetDistinctSubtitles();

                if (subtitles.Count > 1)
                {
                    var filterList = UiControl.AddMultiSelection("Filter", listBox =>
                    {
                        foreach (var subtitle in subtitles)
                        {
                            listBox.Items.Add(subtitle, true);
                        }
                    }, visible =>
                    {
                        View.SetSubtitleFilter(visible);
                        RefitToGraph();
                    });

                    // Content-sized section; the sidebar as a whole scrolls instead.
                    if (filterList.Parent?.Parent is Control filterControl)
                    {
                        filterControl.Height = filterList.ItemHeight * filterList.Items.Count + UiControl.AdjustForDPI(34);
                    }
                }

                AddXrefPanel();
                AddInspectorPanel();
                AddHubsPanel();
                AddEntryPointsPanel();
                AddLegendPanel();

                View.SelectionChanged += OnViewSelectionChanged;

                // Top of the sidebar, above the base viewer buttons: search second, the
                // layout dropdown at the very top (SendToBack order is bottom-up). The combo
                // is nested inside its selection control; reorder that sidebar child.
                searchBox.SendToBack();

                Control placementSection = placementCombo;

                while (placementSection.Parent != null && placementSection.Parent != searchBox.Parent)
                {
                    placementSection = placementSection.Parent;
                }

                placementSection.SendToBack();
            }

            if (GLControl != null)
            {
                GLControl.MouseDoubleClick += OnMouseDoubleClick;
                GLControl.LostFocus += OnGlControlLostFocus;
            }
        }

        private void OnGlControlLostFocus(object? sender, System.EventArgs e)
        {
            View.CancelDrag();
        }

        protected virtual string BuildStatsText(int islandCount)
        {
            var islandSuffix = islandCount == 1 ? "island" : "islands";
            var text = $"{View.NodeCount} nodes\n{View.WireCount} connections\n{islandCount} {islandSuffix}";
            var (selfLoops, orphans) = View.GetGraphHealthCounts();

            if (selfLoops > 0)
            {
                text += $"\n{selfLoops} self-loop{(selfLoops == 1 ? "" : "s")}";
            }

            if (orphans > 0)
            {
                text += $"\n{orphans} unconnected node{(orphans == 1 ? "" : "s")}";
            }

            return text;
        }

        /// <summary>Recomputes the stats label text and height.</summary>
        protected void RefreshStatsLabel()
        {
            if (statsLabel == null || UiControl == null)
            {
                return;
            }

            var statsText = BuildStatsText(View.GetComponents().Count);
            var statsLines = statsText.AsSpan().Count('\n') + 1;
            statsLabel.Height = UiControl.AdjustForDPI(12 + 16 * statsLines);
            statsLabel.Text = statsText;
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || searchBox == null)
            {
                return;
            }

            e.SuppressKeyPress = true;

            var match = View.FindNextNode(searchBox.Text, lastSearchResult);
            lastSearchResult = match;

            if (match == null)
            {
                return;
            }

            if (match.Hidden)
            {
                FocusIslandOf(match);
            }

            FocusNode(match);
        }

        private void OnViewSelectionChanged(object? sender, System.EventArgs e)
        {
            RefreshXrefPanel();
            RefreshInspectorPanel();
        }

        // Incoming and outgoing wires of the selected node as clickable jump rows.
        private void AddXrefPanel()
        {
            Debug.Assert(UiControl != null);

            xrefList = new ListBox
            {
                IntegralHeight = false,
                HorizontalScrollbar = true,
                Visible = false,
            };
            xrefList.Click += (_, _) =>
            {
                if (xrefList.SelectedIndex >= 0 && xrefList.SelectedIndex < xrefTargets.Count)
                {
                    FocusNode(xrefTargets[xrefList.SelectedIndex]);
                }
            };
            UiControl.AddControl(xrefList);
        }

        private void RefreshXrefPanel()
        {
            if (xrefList == null || UiControl == null)
            {
                return;
            }

            xrefList.BeginUpdate();
            xrefList.Items.Clear();
            xrefTargets.Clear();

            void AddRow(string text, GraphNode target)
            {
                xrefList.Items.Add(text);
                xrefTargets.Add(target);
            }

            static string LabelSuffix(GraphWire wire) => wire.Label != null ? $"  ({wire.Label})" : string.Empty;

            if (View.Selection.PrimaryNode is { } node)
            {
                foreach (var socket in node.Inputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        AddRow($"◀ {wire.From.Owner.Title}.{wire.From.Name}{LabelSuffix(wire)}", wire.From.Owner);
                    }
                }

                foreach (var socket in node.Outputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        AddRow($"▶ {wire.To.Owner.Title}.{wire.To.Name}{LabelSuffix(wire)}", wire.To.Owner);
                    }
                }
            }
            else if (View.Selection.Wire is { } wire)
            {
                AddRow($"◀ {wire.From.Owner.Title}.{wire.From.Name}", wire.From.Owner);
                AddRow($"▶ {wire.To.Owner.Title}.{wire.To.Name}", wire.To.Owner);
            }

            xrefList.EndUpdate();
            xrefList.Visible = xrefTargets.Count > 0;
            xrefList.Height = Math.Min(
                xrefList.ItemHeight * Math.Max(1, xrefList.Items.Count) + 6,
                UiControl.AdjustForDPI(240));
        }

        // Raw backing data of the selected node, including the fields the node rows omit.
        private void AddInspectorPanel()
        {
            Debug.Assert(UiControl != null);

            inspectorBox = new ThemedTextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Visible = false,
                Height = UiControl.AdjustForDPI(220),
            };
            UiControl.AddControl(inspectorBox);
        }

        private void RefreshInspectorPanel()
        {
            if (inspectorBox == null)
            {
                return;
            }

            var text = View.Selection.PrimaryNode is { } node ? DescribeNodeData(node) : null;
            inspectorBox.Text = text ?? string.Empty;
            inspectorBox.Visible = text != null;
        }

        /// <summary>Raw backing-data dump of a node for the inspector panel and clipboard; null hides both.</summary>
        protected virtual string? DescribeNodeData(GraphNode node)
            => node is KVGraphNode { Data: not null } kvNode ? kvNode.DumpData() : null;

        // Most connected nodes; degree centrality points at the hub worth reading first.
        private void AddHubsPanel()
        {
            Debug.Assert(UiControl != null);

            var hubs = View.GetTopConnectedNodes(10);

            if (hubs.Count < 2)
            {
                return;
            }

            hubsList = new ListBox
            {
                IntegralHeight = false,
                HorizontalScrollbar = true,
            };

            foreach (var (node, degree) in hubs)
            {
                hubsList.Items.Add($"{degree} × {node.Title}");
                hubTargets.Add(node);
            }

            hubsList.Click += (_, _) =>
            {
                if (hubsList.SelectedIndex >= 0 && hubsList.SelectedIndex < hubTargets.Count)
                {
                    FocusNode(hubTargets[hubsList.SelectedIndex]);
                }
            };
            hubsList.Height = hubsList.ItemHeight * hubsList.Items.Count + 6;
            UiControl.AddControl(hubsList);
        }

        /// <summary>Nodes execution originates from: wired outputs but nothing incoming.</summary>
        protected virtual List<GraphNode> GetEntryPoints()
        {
            var roots = new List<(GraphNode Node, int OutDegree)>();

            foreach (var node in View.Nodes)
            {
                var hasIncoming = false;

                foreach (var socket in node.Inputs)
                {
                    hasIncoming |= socket.Wires.Count > 0;
                }

                if (hasIncoming)
                {
                    continue;
                }

                var outDegree = 0;

                foreach (var socket in node.Outputs)
                {
                    outDegree += socket.Wires.Count;
                }

                if (outDegree > 0)
                {
                    roots.Add((node, outDegree));
                }
            }

            roots.Sort(static (a, b) => b.OutDegree.CompareTo(a.OutDegree));
            return [.. roots.Select(static r => r.Node).Take(20)];
        }

        private ListBox? entryPointsList;
        private readonly List<GraphNode> entryPointTargets = [];

        // Where execution starts: map-spawn logic, inflow cells, unreferenced roots.
        private void AddEntryPointsPanel()
        {
            Debug.Assert(UiControl != null);

            var entryPoints = GetEntryPoints();

            if (entryPoints.Count == 0)
            {
                return;
            }

            entryPointsList = new ListBox
            {
                IntegralHeight = false,
                HorizontalScrollbar = true,
            };

            foreach (var node in entryPoints)
            {
                entryPointsList.Items.Add(node.Subtitle != null ? $"{node.Title}  ({node.Subtitle})" : node.Title);
                entryPointTargets.Add(node);
            }

            entryPointsList.Click += (_, _) =>
            {
                if (entryPointsList.SelectedIndex >= 0 && entryPointsList.SelectedIndex < entryPointTargets.Count)
                {
                    FocusNode(entryPointTargets[entryPointsList.SelectedIndex]);
                }
            };
            entryPointsList.Height = entryPointsList.ItemHeight * entryPointsList.Items.Count + 6;
            UiControl.AddControl(entryPointsList);
        }

        private static string BuildNodeText(GraphNode node)
        {
            var sb = new StringBuilder();
            sb.AppendLine(node.Subtitle != null ? $"{node.Title} ({node.Subtitle})" : node.Title);

            foreach (var row in node.Rows)
            {
                switch (row)
                {
                    case TextRow { Text.Length: > 0 } text:
                        sb.AppendLine(text.Text);
                        break;
                    case SocketRow socket:
                        sb.AppendLine($"{(socket.Socket.IsInput ? "in" : "out")} {socket.Socket.Name}");
                        break;
                    case PairedSocketRow paired:
                        if (paired.Input is { Name.Length: > 0 })
                        {
                            sb.AppendLine($"in {paired.Input.Name}");
                        }

                        if (paired.Output is { Name.Length: > 0 })
                        {
                            sb.AppendLine($"out {paired.Output.Name}");
                        }

                        break;
                    case ResourceRow resource:
                        sb.AppendLine(resource.Text);
                        break;
                    case AnnotationRow annotation:
                        sb.AppendLine(annotation.Text);
                        break;
                }
            }

            return sb.ToString();
        }

        private void AddLegendPanel()
        {
            Debug.Assert(UiControl != null);

            if (View.Legend.Count == 0)
            {
                return;
            }

            legendPanel = new Panel
            {
                Height = UiControl.AdjustForDPI(10 + View.Legend.Count * 18),
            };
            legendPanel.Paint += (_, e) =>
            {
                var y = 6;
                using var text = new System.Drawing.SolidBrush(Themer.CurrentThemeColors.Contrast);
                using var dimmedText = new System.Drawing.SolidBrush(Themer.CurrentThemeColors.ContrastSoft);

                foreach (var (label, hue, kind) in View.Legend)
                {
                    // Palette slots resolve at paint time so the legend follows the theme.
                    var skColor = kind == GraphLegendKind.Category ? View.Palette.Category(hue) : View.Palette.Signal(hue);
                    using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue));

                    // Category rows act as a click filter; rows filtered out paint dimmed.
                    var dimmed = legendFilter.Count > 0 && kind == GraphLegendKind.Category && !legendFilter.Contains(hue);

                    if (kind is GraphLegendKind.Wire or GraphLegendKind.DashedWire)
                    {
                        using var pen = new System.Drawing.Pen(brush.Color, 3f);

                        if (kind == GraphLegendKind.DashedWire)
                        {
                            pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        }

                        e.Graphics.DrawLine(pen, 4, y + 8, 20, y + 8);
                    }
                    else if (kind == GraphLegendKind.Marker)
                    {
                        e.Graphics.FillPolygon(brush, [new(12, y + 3), new(17, y + 8), new(12, y + 13), new(7, y + 8)]);
                    }
                    else
                    {
                        e.Graphics.FillRectangle(brush, 6, y + 2, 12, 12);
                    }

                    e.Graphics.DrawString(label, legendPanel!.Font, dimmed ? dimmedText : text, 24, y);
                    y += 18;
                }
            };

            // Clicking a category row toggles it in an OR-filter that dims everything else.
            legendPanel.MouseClick += (_, e) =>
            {
                var index = (e.Y - 6) / 18;

                if (index < 0 || index >= View.Legend.Count)
                {
                    return;
                }

                var entry = View.Legend[index];

                if (entry.Kind != GraphLegendKind.Category)
                {
                    return;
                }

                if (!legendFilter.Add(entry.Hue))
                {
                    legendFilter.Remove(entry.Hue);
                }

                View.SetCategoryHighlight(legendFilter);
                legendPanel!.Invalidate();
            };

            UiControl.AddControl(legendPanel);
        }

        /// <summary>Refits the view to the current graph bounds on the next frame.</summary>
        public void RefitToGraph()
        {
            needsFit = true;
            InvalidateRender();
        }

        private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);
            var element = View.FindElementAt(graphPoint);

            if (element is GraphNode { ExternalResourceName: not null } node)
            {
                OpenExternalResource(node);
            }
        }

        private void OpenExternalResource(GraphNode node)
        {
            Debug.Assert(node.ExternalResourceName != null);

            var foundFile = VrfGuiContext.FindFileWithContext(node.ExternalResourceName + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context != null)
            {
                Debug.Assert(foundFile.PackageEntry != null);
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            }
        }

        /// <summary>Selects <paramref name="node"/> and centers the view on it.</summary>
        public void FocusNode(GraphNode node)
        {
            View.SelectNode(node);

            graphBounds = View.GetGraphBounds();
            OriginalWidth = (int)graphBounds.Width;
            OriginalHeight = (int)graphBounds.Height;
            needsFit = false;

            if (GLControl != null)
            {
                if (TextureScale < 0.4f)
                {
                    TextureScale = 1f;
                }

                var center = node.Position + node.Size / 2f;
                Position = new Vector2(
                    (center.X - graphBounds.Left) * TextureScale - GLControl.Width / 2f,
                    (center.Y - graphBounds.Top) * TextureScale - GLControl.Height / 2f);
                TextureScaleChangeTime = 10f; // Skip animation
            }

            InvalidateRender();
        }

        /// <summary>Lets subclasses prepend their own items to the node right-click menu.</summary>
        protected virtual void AddNodeContextMenuItems(ThemedContextMenuStrip menu, GraphNode node)
        {
        }

        /// <summary>Whether the graph is made of more than one connected component.</summary>
        protected virtual bool HasMultipleIslands => View.HasMultipleIslands();

        /// <summary>Hides every island except the one containing <paramref name="node"/>.</summary>
        protected virtual void FocusIslandOf(GraphNode node)
        {
            View.FocusIslandOf(node);
            RefitToGraph();
        }

        private bool pendingFullRelayout;

        protected virtual void ShowAllIslands()
        {
            View.ShowAllNodes();

            // A chain that was isolated with relayout moved; re-lay everything so the
            // restored nodes cannot overlap it.
            if (pendingFullRelayout)
            {
                pendingFullRelayout = false;
                View.LayoutNodesPacked();
            }

            RefitToGraph();
        }

        private void ShowContextMenu(System.Drawing.Point location)
        {
            Debug.Assert(GLControl != null);

            var graphPoint = ScreenToGraph(new SKPoint(location.X, location.Y));
            var node = View.FindElementAt(graphPoint) switch
            {
                GraphNode n => n,
                GraphSocket socket => socket.Owner,
                GraphWire wire => wire.From.Owner,
                _ => null,
            };

            contextMenu ??= new ThemedContextMenuStrip();

            while (contextMenu.Items.Count > 0)
            {
                var item = contextMenu.Items[0];
                contextMenu.Items.RemoveAt(0);
                item.Dispose();
            }

            if (node != null)
            {
                AddNodeContextMenuItems(contextMenu, node);

                if (node.ExternalResourceName != null)
                {
                    var openItem = new ToolStripMenuItem($"Open {node.ExternalResourceName}");
                    openItem.Click += (_, _) => OpenExternalResource(node);
                    contextMenu.Items.Add(openItem);
                }

                if (HasMultipleIslands)
                {
                    var focusItem = new ToolStripMenuItem("Focus on this island");
                    focusItem.Click += (_, _) => FocusIslandOf(node);
                    contextMenu.Items.Add(focusItem);
                }

                var isolateItem = new ToolStripMenuItem("Isolate this chain");
                isolateItem.Click += (_, _) =>
                {
                    View.IsolateChainOf(node);
                    RefitToGraph();
                };
                contextMenu.Items.Add(isolateItem);

                // Laying out just the isolated chain gives it room the full-island layout could not.
                var isolateRelayoutItem = new ToolStripMenuItem("Isolate this chain (relayout)");
                isolateRelayoutItem.Click += (_, _) =>
                {
                    View.IsolateChainOf(node);
                    View.LayoutNodesPacked();
                    pendingFullRelayout = true;
                    RefitToGraph();
                };
                contextMenu.Items.Add(isolateRelayoutItem);

                var upstreamItem = new ToolStripMenuItem("Trace what triggers this");
                upstreamItem.Click += (_, _) =>
                {
                    View.IsolateUpstreamOf(node);
                    RefitToGraph();
                };
                contextMenu.Items.Add(upstreamItem);

                var downstreamItem = new ToolStripMenuItem("Trace what this triggers");
                downstreamItem.Click += (_, _) =>
                {
                    View.IsolateDownstreamOf(node);
                    RefitToGraph();
                };
                contextMenu.Items.Add(downstreamItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                var copyTextItem = new ToolStripMenuItem("Copy node text");
                copyTextItem.Click += (_, _) => AppClipboard.SetText(BuildNodeText(node));
                contextMenu.Items.Add(copyTextItem);

                if (DescribeNodeData(node) is { } rawData)
                {
                    var copyRawItem = new ToolStripMenuItem("Copy raw data");
                    copyRawItem.Click += (_, _) => AppClipboard.SetText(rawData);
                    contextMenu.Items.Add(copyRawItem);
                }
            }

            if (View.HasHiddenNodes())
            {
                var showAllItem = new ToolStripMenuItem("Show all islands");
                showAllItem.Click += (_, _) => ShowAllIslands();
                contextMenu.Items.Add(showAllItem);
            }

            if (contextMenu.Items.Count > 0)
            {
                contextMenu.Show(GLControl, location);
            }
        }

        protected override void OnGLLoad()
        {
            if (MainFramebuffer != GLDefaultFramebuffer)
            {
                MainFramebuffer?.Delete();
                MainFramebuffer = GLDefaultFramebuffer;
            }

            Debug.Assert(MainFramebuffer != null);

            var bgColor = View.Palette.Canvas;
            MainFramebuffer.ClearColor = new OpenTK.Mathematics.Color4(
                bgColor.Red / 255f,
                bgColor.Green / 255f,
                bgColor.Blue / 255f,
                bgColor.Alpha / 255f
            );
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;

            // Set texture size to graph bounds for zoom calculations
            graphBounds = View.GetGraphBounds();
            OriginalWidth = (int)graphBounds.Width;
            OriginalHeight = (int)graphBounds.Height;
        }

        protected override void OnFirstPaint()
        {
            // Initial fit is handled in Draw()
        }

        protected override void OnPaint(float frameTime)
        {
            Debug.Assert(MainFramebuffer != null);

            var renderHash = HashCode.Combine(
                GetCurrentPositionAndScale(),
                View.VisualVersion,
                MainFramebuffer.Width,
                MainFramebuffer.Height,
                needsFit);

            if (renderHash != lastRenderHash)
            {
                lastRenderHash = renderHash;
                numRendersLastHash = 0;
            }

            // Once both back buffers hold the current content, swapping them is free.
            const int NumBackBuffers = 2;
            if (numRendersLastHash >= NumBackBuffers)
            {
                return;
            }

            numRendersLastHash++;

            if (grContext == null)
            {
                glInterface = GRGlInterface.Create();
                grContext = GRContext.CreateGl(glInterface);
            }
            else
            {
                // The viewer loop issues its own GL calls (clears, framebuffer binds) between
                // frames; make Skia re-sync its cached GL state or its offscreen mask/layer
                // composites break at some zoom levels.
                grContext.ResetContext();
            }

            var newSize = new SKSizeI(MainFramebuffer.Width, MainFramebuffer.Height);

            if (renderTarget == null || lastSize != newSize || !renderTarget.IsValid)
            {
                lastSize = newSize;

                GL.GetInteger(GetPName.FramebufferBinding, out var framebuffer);
                GL.GetInteger(GetPName.Samples, out var samples);

                var maxSamples = grContext.GetMaxSurfaceSampleCount(SKColorType.Rgba8888);
                if (samples > maxSamples)
                {
                    samples = maxSamples;
                }

                var glInfo = new GRGlFramebufferInfo((uint)framebuffer, SKColorType.Rgba8888.ToGlSizedFormat());

                surface?.Dispose();
                surface = null;
                renderTarget?.Dispose();
                renderTarget = new GRBackendRenderTarget(newSize.Width, newSize.Height, samples, 0, glInfo);
            }

            surface ??= SKSurface.Create(grContext, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);

            var canvas = surface.Canvas;

            if (needsFit)
            {
                graphBounds = View.GetGraphBounds();
                OriginalWidth = (int)graphBounds.Width;
                OriginalHeight = (int)graphBounds.Height;
                FitToViewport();
                needsFit = false;
            }
            else
            {
                // Update graphBounds and compensate Position for any origin shift
                var newGraphBounds = View.GetGraphBounds();

                var deltaLeft = newGraphBounds.Left - graphBounds.Left;
                var deltaTop = newGraphBounds.Top - graphBounds.Top;

                if (deltaLeft != 0 || deltaTop != 0)
                {
                    Position = new Vector2(
                        Position.X - deltaLeft * TextureScale,
                        Position.Y - deltaTop * TextureScale
                    );
                    TextureScaleChangeTime = 10f; // Skip interpolation for instant compensation
                }

                if ((int)newGraphBounds.Width != OriginalWidth || (int)newGraphBounds.Height != OriginalHeight)
                {
                    OriginalWidth = (int)newGraphBounds.Width;
                    OriginalHeight = (int)newGraphBounds.Height;
                }

                graphBounds = newGraphBounds;
            }

            var (scale, position) = GetCurrentPositionAndScale();

            canvas.Save();

            // Apply pan/zoom transform
            canvas.Translate(-position.X, -position.Y);
            canvas.Scale(scale, scale);
            canvas.Translate(-graphBounds.Left, -graphBounds.Top);

            var visibleRect = new SKRect(
                position.X / scale + graphBounds.Left,
                position.Y / scale + graphBounds.Top,
                (position.X + MainFramebuffer.Width) / scale + graphBounds.Left,
                (position.Y + MainFramebuffer.Height) / scale + graphBounds.Top
            );
            visibleRect.Inflate(50f / scale, 50f / scale);

            View.RenderToCanvas(canvas, visibleRect, scale);

            canvas.Restore();
            canvas.Flush();
            grContext.Flush();
        }

        /// <summary>
        /// Zoom-out floor: zooming stops once the graph's limiting dimension shrinks
        /// to 30% of the viewport (never above 1:1 for small graphs).
        /// </summary>
        internal float MinTextureScale()
        {
            if (GLControl == null || graphBounds.IsEmpty)
            {
                return 0.01f;
            }

            var fitScale = Math.Min(GLControl.Width / graphBounds.Width, GLControl.Height / graphBounds.Height);
            return Math.Min(1f, 0.3f * fitScale);
        }

        private void FitToViewport()
        {
            if (GLControl == null || graphBounds.IsEmpty)
            {
                return;
            }

            var scaleX = (GLControl.Width * 0.9f) / graphBounds.Width;
            var scaleY = (GLControl.Height * 0.9f) / graphBounds.Height;
            TextureScale = Math.Min(scaleX, scaleY);
            TextureScale = Math.Max(MinTextureScale(), Math.Min(TextureScale, 2f));

            Position = new Vector2(
                -(GLControl.Width - graphBounds.Width * TextureScale) / 2f,
                -(GLControl.Height - graphBounds.Height * TextureScale) / 2f
            );

            TextureScaleChangeTime = 10f; // Skip animation
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            // A live node drag owns the gesture; chorded buttons must not start panning
            // or reach the view mid-drag.
            if (View.IsMoving && e.Button != MouseButtons.Left)
            {
                return;
            }

            // Middle mouse = pan (let GLTextureViewer handle it)
            if (e.Button == MouseButtons.Middle)
            {
                base.OnMouseDown(sender, e);
                return;
            }

            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);

            View.HandleMouseDown(graphPoint, e.Button, Control.ModifierKeys);

            if (e.Button == MouseButtons.Left && Control.ModifierKeys == Keys.None)
            {
                var element = View.FindElementAt(graphPoint);

                if (element == null)
                {
                    base.OnMouseDown(sender, e);
                }
            }
        }

        protected override void OnMouseMove(object? sender, MouseEventArgs e)
        {
            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);

            if (ClickPosition.HasValue)
            {
                base.OnMouseMove(sender, e);
                return;
            }

            View.HandleMouseMove(graphPoint, Control.ModifierKeys);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            // Always call base first to clear ClickPosition for panning
            base.OnMouseUp(sender, e);

            var wasDragging = View.IsMoving;
            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);

            View.HandleMouseUp(graphPoint, e.Button);

            if (e.Button == MouseButtons.Right && !wasDragging)
            {
                ShowContextMenu(e.Location);
            }
        }

        protected SKPoint ScreenToGraph(SKPoint screenPoint)
        {
            // Hit-test with the same animated transform the frame is drawn with.
            var (scale, position) = GetCurrentPositionAndScale();

            var canvasX = (screenPoint.X + position.X) / scale;
            var canvasY = (screenPoint.Y + position.Y) / scale;

            return new SKPoint(canvasX + graphBounds.Left, canvasY + graphBounds.Top);
        }

        // The graph is drawn through Skia, not the texture/shader pipeline the base capture
        // path assumes. Captures the current viewport at the current zoom for Ctrl+C / saving,
        // cropped to the graph bounds so a zoomed-out capture has no empty margins.
        protected override SKBitmap ReadPixelsToBitmap()
        {
            Debug.Assert(MainFramebuffer != null);

            var (scale, position) = GetCurrentPositionAndScale();

            var viewportRect = new SKRect(
                position.X / scale + graphBounds.Left,
                position.Y / scale + graphBounds.Top,
                (position.X + MainFramebuffer.Width) / scale + graphBounds.Left,
                (position.Y + MainFramebuffer.Height) / scale + graphBounds.Top);

            var captureRect = new SKRect(
                Math.Max(viewportRect.Left, graphBounds.Left),
                Math.Max(viewportRect.Top, graphBounds.Top),
                Math.Min(viewportRect.Right, graphBounds.Right),
                Math.Min(viewportRect.Bottom, graphBounds.Bottom));

            if (captureRect.Width <= 0 || captureRect.Height <= 0)
            {
                captureRect = viewportRect;
            }

            var width = Math.Max(1, (int)(captureRect.Width * scale));
            var height = Math.Max(1, (int)(captureRect.Height * scale));

            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

            try
            {
                using var canvas = new SKCanvas(bitmap);
                canvas.Scale(scale, scale);
                canvas.Translate(-captureRect.Left, -captureRect.Top);

                View.RenderToCanvas(canvas, captureRect, scale);

                var bitmapToReturn = bitmap;
                bitmap = null;
                return bitmapToReturn;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private bool disposed;

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (GLControl != null)
            {
                GLControl.MouseDoubleClick -= OnMouseDoubleClick;
                GLControl.LostFocus -= OnGlControlLostFocus;
            }

            contextMenu?.Dispose();
            statsLabel?.Dispose();
            searchBox?.Dispose();
            legendPanel?.Dispose();
            xrefList?.Dispose();
            inspectorBox?.Dispose();
            hubsList?.Dispose();
            entryPointsList?.Dispose();
            View.SelectionChanged -= OnViewSelectionChanged;
            View.GraphChanged -= OnGraphChanged;

            // Stops the render loop first; afterwards the GL context may no longer be
            // current, so abandon it to make Skia skip GL calls during disposal.
            base.Dispose();

            grContext?.AbandonContext();
            surface?.Dispose();
            renderTarget?.Dispose();
            grContext?.Dispose();
            glInterface?.Dispose();

            View.Dispose();
        }
    }
}
