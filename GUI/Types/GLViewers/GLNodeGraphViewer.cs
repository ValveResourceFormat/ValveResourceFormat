using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;

#nullable disable

namespace GUI.Types.GLViewers
{
    class GLNodeGraphViewer : GLTextureViewer
    {
        protected readonly NodeGraphControl.NodeGraphControl nodeGraph;
        private SKRect graphBounds;
        private bool needsFit = true;

        // Skia/OpenGL context
        private GRGlInterface glInterface;
        private GRContext grContext;
        private GRBackendRenderTarget renderTarget;
        private SKSurface surface;
        private SKSizeI lastSize;

        public GLNodeGraphViewer(VrfGuiContext guiContext, NodeGraphControl.NodeGraphControl graph)
            : base(guiContext, (SKBitmap)null)
        {
            nodeGraph = graph;
            nodeGraph.GraphChanged += OnGraphChanged;
            graphBounds = nodeGraph.GetGraphBounds();
        }

        private void OnGraphChanged(object sender, System.EventArgs e)
        {
            InvalidateRender();
        }

        protected override void OnGLLoad()
        {
            if (MainFramebuffer != GLDefaultFramebuffer)
            {
                MainFramebuffer.Delete();
                MainFramebuffer = GLDefaultFramebuffer;
            }

            var bgColor = nodeGraph.CanvasBackgroundColor;
            MainFramebuffer.ClearColor = new OpenTK.Mathematics.Color4(
                bgColor.Red / 255f,
                bgColor.Green / 255f,
                bgColor.Blue / 255f,
                bgColor.Alpha / 255f
            );
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;

            // Set texture size to graph bounds for zoom calculations
            graphBounds = nodeGraph.GetGraphBounds();
            OriginalWidth = (int)graphBounds.Width;
            OriginalHeight = (int)graphBounds.Height;
        }

        protected override void OnFirstPaint()
        {
            // Initial fit is handled in Draw()
        }

        protected override void Draw(Framebuffer fbo, bool captureFullSizeImage = false)
        {
            var bgColor = nodeGraph.CanvasBackgroundColor;
            GL.ClearColor(bgColor.Red / 255f, bgColor.Green / 255f, bgColor.Blue / 255f, bgColor.Alpha / 255f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            if (grContext == null)
            {
                glInterface = GRGlInterface.Create();
                grContext = GRContext.CreateGl(glInterface);
            }

            var newSize = new SKSizeI(fbo.Width, fbo.Height);

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
                FitToViewport();
                needsFit = false;
            }

            var (scale, position) = GetCurrentPositionAndScale();
            graphBounds = nodeGraph.GetGraphBounds();
            OriginalWidth = (int)graphBounds.Width;
            OriginalHeight = (int)graphBounds.Height;

            canvas.Clear(nodeGraph.CanvasBackgroundColor);
            canvas.Save();

            // Apply pan/zoom transform
            canvas.Translate(-position.X, -position.Y);
            canvas.Scale(scale, scale);
            canvas.Translate(-graphBounds.Left, -graphBounds.Top);

            // Render the node graph
            nodeGraph.RenderToCanvas(canvas, graphBounds.Location, new SKPoint(graphBounds.Right, graphBounds.Bottom));

            canvas.Restore();
            canvas.Flush();
            grContext.Flush();
        }

        private void FitToViewport()
        {
            if (GLControl == null || graphBounds.IsEmpty)
            {
                return;
            }

            // Calculate zoom to fit graph in viewport with padding
            var scaleX = (GLControl.Width * 0.9f) / graphBounds.Width;
            var scaleY = (GLControl.Height * 0.9f) / graphBounds.Height;
            TextureScale = Math.Min(scaleX, scaleY);
            TextureScale = Math.Max(0.1f, Math.Min(TextureScale, 10f));

            // Center the view
            Position = new System.Numerics.Vector2(
                -(GLControl.Width - graphBounds.Width * TextureScale) / 2f,
                -(GLControl.Height - graphBounds.Height * TextureScale) / 2f
            );

            TextureScaleChangeTime = 10f; // Skip animation
        }

        protected override void OnMouseDown(object sender, MouseEventArgs e)
        {
            // Middle mouse = pan (let GLTextureViewer handle it)
            if (e.Button == MouseButtons.Middle)
            {
                base.OnMouseDown(sender, e);
                return;
            }

            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);

            // Left click: check if clicking on a node/wire or empty space
            if (e.Button == MouseButtons.Left && Control.ModifierKeys == Keys.None)
            {
                var element = nodeGraph.FindElementAt(graphPoint);

                // If clicking empty space, pan instead
                if (element == null)
                {
                    base.OnMouseDown(sender, e);
                    return;
                }
            }

            // Forward to node graph for node interaction
            nodeGraph.HandleMouseDown(graphPoint, e.Button, Control.ModifierKeys);
        }

        protected override void OnMouseMove(object sender, MouseEventArgs e)
        {
            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);

            // Check if we're panning the view (ClickPosition is set by GLTextureViewer)
            if (ClickPosition.HasValue)
            {
                base.OnMouseMove(sender, e);
                return;
            }

            // Forward to node graph for node dragging
            nodeGraph.HandleMouseMove(graphPoint);
        }

        protected override void OnMouseUp(object sender, MouseEventArgs e)
        {
            // Always call base first to clear ClickPosition for panning
            base.OnMouseUp(sender, e);

            // Forward to node graph for node interaction
            var screenPoint = new SKPoint(e.Location.X, e.Location.Y);
            var graphPoint = ScreenToGraph(screenPoint);

            nodeGraph.HandleMouseUp(graphPoint, e.Button);
        }

        private SKPoint ScreenToGraph(SKPoint screenPoint)
        {
            // Convert screen to canvas coordinates (accounting for pan/zoom)
            var canvasX = (screenPoint.X + Position.X) / TextureScale;
            var canvasY = (screenPoint.Y + Position.Y) / TextureScale;

            // Convert canvas to graph coordinates (accounting for graph bounds offset)
            var graphX = canvasX + graphBounds.Left;
            var graphY = canvasY + graphBounds.Top;

            return new SKPoint(graphX, graphY);
        }

        public override void Dispose()
        {
            nodeGraph.GraphChanged -= OnGraphChanged;
            nodeGraph?.Dispose();

            surface?.Dispose();
            renderTarget?.Dispose();
            grContext?.Dispose();
            glInterface?.Dispose();

            base.Dispose();
        }
    }
}
