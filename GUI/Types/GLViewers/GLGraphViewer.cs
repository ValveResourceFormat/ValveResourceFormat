using System.Diagnostics;
using System.IO;
using System.Linq;
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

        /// <summary>The built graph, for headless tooling that never shows the viewer.</summary>
        internal GraphView Graph => View;
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

        /// <summary>Frontends that build state machines expose a checkbox to draw them as a statechart.</summary>
        protected virtual bool HasStateMachineToggle => false;

        /// <summary>Rebuilds the graph with state machines drawn as a statechart (true) or flattened (false).</summary>
        protected virtual void SetDrawStateMachines(bool draw)
        {
        }

        private Label? statsLabel;
        private ThemedTextBox? searchBox;
        private Panel? legendPanel;
        private GraphNode? lastSearchResult;
        private CheckedListBox? subtitleFilter;

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
                    BorderStyle = BorderStyle.None,
                    Dock = DockStyle.Fill,
                };
                searchBox.KeyDown += OnSearchKeyDown;
                searchBox.TextChanged += (_, _) => View.SetSearchHighlight(searchBox.Text);

                // The themed textbox melts into the sidebar; a one-pixel outline panel plus
                // vertical breathing room keeps the search field visible below the dropdown.
                var searchOutline = new Panel
                {
                    Padding = new Padding(1),
                    BackColor = Themer.CurrentThemeColors.Border,
                    Dock = DockStyle.Fill,
                };
                searchOutline.Controls.Add(searchBox);

                var searchSection = new Panel
                {
                    Padding = new Padding(0, 8, 0, 8),
                    Height = UiControl.AdjustForDPI(40),
                };
                searchSection.Controls.Add(searchOutline);
                UiControl.AddControl(searchSection);

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
                placementCombo.Items.AddRange(new object[] { "Layered (Sugiyama)", "Organic (MDS)" });
                placementCombo.SelectedIndex = (int)View.Placement;
                suppressPlacementChange = false;

                var suppressWireChange = true;
                var wireCombo = UiControl.AddSelection("Wires", (_, index) =>
                {
                    if (suppressWireChange || index < 0)
                    {
                        return;
                    }

                    View.StraightWires = index == 1;
                    View.MarkVisualDirty();
                    InvalidateRender();
                });
                wireCombo.Items.AddRange(new object[] { "Curved", "Straight" });
                wireCombo.SelectedIndex = View.StraightWires ? 1 : 0;
                suppressWireChange = false;

                var complexitySection = AddComplexityReduction();

                if (HasStateMachineToggle)
                {
                    UiControl.AddCheckBox("Draw state machines", false, SetDrawStateMachines);
                }

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

                    subtitleFilter = filterList;

                    // Content-sized section; the sidebar as a whole scrolls instead.
                    if (filterList.Parent?.Parent is Control filterControl)
                    {
                        filterControl.Height = filterList.ItemHeight * filterList.Items.Count + UiControl.AdjustForDPI(34);
                    }
                }

                AddLegendPanel();

                // SendToBack docks a child at the top, so calling it in reverse leaves the sidebar
                // reading Layout, Wires, Reduce, then the search box. A combo is nested inside its
                // own selection control, so the sidebar child that owns it is what has to move.
                searchSection.SendToBack();
                complexitySection?.SendToBack();
                SidebarSectionOf(wireCombo, searchSection.Parent)?.SendToBack();
                SidebarSectionOf(placementCombo, searchSection.Parent)?.SendToBack();

                // Added last so it docks at the very bottom of the sidebar, below the legend.
                var resetSection = new Panel
                {
                    Padding = new Padding(4, 8, 4, 8),
                    Height = UiControl.AdjustForDPI(48),
                };

                var resetButton = new ThemedButton
                {
                    Text = "Reset view",
                    Dock = DockStyle.Fill,
                };
                resetButton.Click += (_, _) => ResetGraph();

                resetSection.Controls.Add(resetButton);
                UiControl.AddControl(resetSection);

#if DEBUG
                AddLayoutDump();
#endif

                // Saving is the least used control here, so it goes under everything else.
                SaveSection?.BringToFront();
            }

            if (GLControl != null)
            {
                GLControl.MouseDoubleClick += OnMouseDoubleClick;
                GLControl.LostFocus += OnGlControlLostFocus;
            }
        }

        /// <summary>Walks up from a control to the sidebar child that owns it, so it can be reordered.</summary>
        private static Control? SidebarSectionOf(Control control, Control? sidebar)
        {
            Control? section = control;

            while (section != null && section.Parent != sidebar)
            {
                section = section.Parent;
            }

            return section;
        }

        /// <summary>
        /// Sidebar command that re-runs the crossing repair with no time limit. The automatic
        /// pass is capped so opening a graph stays quick, which means a large graph keeps
        /// crossings the full pass would have removed; this is how the user asks for those.
        /// </summary>
        private Panel? AddComplexityReduction()
        {
            if (UiControl == null)
            {
                return null;
            }

            var section = new Panel
            {
                Padding = new Padding(4, 10, 4, 6),
                Height = UiControl.AdjustForDPI(94),
            };

            var caption = new Label
            {
                Text = "Wires are untangled for up to four seconds when the graph opens.",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = UiControl.AdjustForDPI(42),
            };

            // Docked to the bottom with its own height rather than filling: a Fill button competes
            // with the caption for the same space and ends up drawn over the last line of it.
            var button = new ThemedButton
            {
                Text = "Reduce Visual Graph Complexity",
                Dock = DockStyle.Bottom,
                Height = UiControl.AdjustForDPI(30),
            };

            button.Click += (_, _) =>
            {
                button.Enabled = false;
                var previous = button.Text;
                button.Text = "Working...";

                try
                {
                    Application.DoEvents();
                    View.ReduceVisualComplexity();
                    InvalidateRender();
                }
                finally
                {
                    button.Text = previous;
                    button.Enabled = true;
                }
            };

            section.Controls.Add(button);
            section.Controls.Add(caption);
            UiControl.AddControl(section);
            return section;
        }

#if DEBUG
        /// <summary>
        /// Sidebar command that writes the graph exactly as it is currently arranged, cards and
        /// wire dock points alike, to a text file in the user's Downloads folder. Dragging cards
        /// by hand and dumping the result gives an arrangement to compare the layout against.
        /// </summary>
        private void AddLayoutDump()
        {
            if (UiControl == null)
            {
                return;
            }

            var section = new Panel
            {
                Padding = new Padding(4, 0, 4, 8),
                Height = UiControl.AdjustForDPI(40),
            };

            var button = new ThemedButton
            {
                Text = "Log layout to file",
                Dock = DockStyle.Fill,
            };

            button.Click += (_, _) =>
            {
                var name = Path.GetFileNameWithoutExtension(VrfGuiContext.FileName);

                if (string.IsNullOrEmpty(name))
                {
                    name = "graph";
                }

                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(folder);

                // Each click writes a new file so an arrangement can be dumped, changed, and dumped again.
                var path = Path.Combine(folder, $"graph-layout-{name}.txt");

                for (var i = 2; File.Exists(path); i++)
                {
                    path = Path.Combine(folder, $"graph-layout-{name}-{i}.txt");
                }

                File.WriteAllText(path, View.DescribeLayout(VrfGuiContext.FileName));
                Log.Info(nameof(GLGraphViewer), $"Wrote graph layout to {path}");

                button.Text = Path.GetFileName(path);
            };

            section.Controls.Add(button);
            UiControl.AddControl(section);
        }
#endif

        /// <summary>
        /// Clears the search, selection and subtitle filter, restores every isolated node
        /// and island, then refits the view to the whole graph.
        /// </summary>
        private void ResetGraph()
        {
            lastSearchResult = null;

            if (searchBox != null)
            {
                searchBox.Text = string.Empty;
            }

            View.SetSearchHighlight(null);
            View.ClearSelection();

            if (subtitleFilter != null)
            {
                for (var i = 0; i < subtitleFilter.Items.Count; i++)
                {
                    subtitleFilter.SetItemChecked(i, true);
                }
            }

            ShowAllIslands();
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

                foreach (var (label, hue, kind) in View.Legend)
                {
                    // Palette slots resolve at paint time so the legend follows the theme.
                    var skColor = kind == GraphLegendKind.Category ? View.Palette.Category(hue) : View.Palette.Signal(hue);
                    using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue));

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

                    e.Graphics.DrawString(label, legendPanel!.Font, text, 24, y);
                    y += 18;
                }
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

            var name = node.ExternalResourceName;

            // A subgraph an uncompiled animation graph points at is itself an uncompiled loose
            // file that never ships compiled, so it resolves without the _c suffix and next to
            // the graph on disk rather than through the compiled-resource path.
            if (name.EndsWith(".vsubgrph", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".vanmgrph", StringComparison.OrdinalIgnoreCase))
            {
                OpenLooseGraphReference(name);
                return;
            }

            var foundFile = VrfGuiContext.FindFileWithContext(name + ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context != null)
            {
                Debug.Assert(foundFile.PackageEntry != null);
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            }
        }

        // Resolves an uncompiled graph reference against the loaded content and then, when the
        // graph was opened straight from a folder, next to it and up its parent directories.
        // Opens the referenced file in a new tab if found, otherwise does nothing.
        private void OpenLooseGraphReference(string referencePath)
        {
            var found = VrfGuiContext.FindFileWithContext(referencePath);
            if (found.Context != null && found.PackageEntry != null)
            {
                Program.MainForm.OpenFile(found.Context, found.PackageEntry);
                return;
            }

            var openPath = VrfGuiContext.FileName;
            if (string.IsNullOrEmpty(openPath))
            {
                return;
            }

            var relative = referencePath.Replace('/', Path.DirectorySeparatorChar);
            var baseName = Path.GetFileName(relative);
            var directory = Path.GetDirectoryName(Path.GetFullPath(openPath));

            while (!string.IsNullOrEmpty(directory))
            {
                foreach (var candidate in new[] { Path.Combine(directory, relative), Path.Combine(directory, baseName) })
                {
                    if (File.Exists(candidate) && !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(openPath), StringComparison.OrdinalIgnoreCase))
                    {
                        Program.MainForm.OpenFile(candidate);
                        return;
                    }
                }

                directory = Path.GetDirectoryName(directory);
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

                var center = node.Position + View.Geometry.SizeOf(node) / 2f;
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

        private void AddShowEverything(ThemedContextMenuStrip menu)
        {
            var showAllItem = new ToolStripMenuItem("Show everything");
            showAllItem.Click += (_, _) => ShowAllIslands();
            menu.Items.Add(showAllItem);
        }

        /// <summary>
        /// Adds a node filtering action twice: keeping the surviving nodes where they are,
        /// and a re-layout variant that lays out just the survivors.
        /// </summary>
        private void AddFilterAction(ThemedContextMenuStrip menu, string label, Action filter)
        {
            var keepItem = new ToolStripMenuItem(label);
            keepItem.Click += (_, _) =>
            {
                filter();
                RefitToGraph();
            };
            menu.Items.Add(keepItem);

            var relayoutItem = new ToolStripMenuItem($"{label} (re-layout)");
            relayoutItem.Click += (_, _) =>
            {
                filter();
                View.LayoutNodesPacked();
                pendingFullRelayout = true;
                RefitToGraph();
            };
            menu.Items.Add(relayoutItem);
        }

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

            // Selecting the node dims the rest of the graph, so it is unambiguous which
            // node the menu actions will apply to.
            if (node != null)
            {
                View.SelectNode(node);
            }

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

                AddShowEverything(contextMenu);
                contextMenu.Items.Add(new ToolStripSeparator());

                AddFilterAction(contextMenu, "Isolate chain", () => View.IsolateChainOf(node));
                AddFilterAction(contextMenu, "Isolate upstream", () => View.IsolateUpstreamOf(node));
                AddFilterAction(contextMenu, "Isolate downstream", () => View.IsolateDownstreamOf(node));
            }
            else
            {
                AddShowEverything(contextMenu);
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
            subtitleFilter?.Dispose();
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
