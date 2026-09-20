#if DEBUG
using System.Globalization;
using System.Threading;
using SkiaSharp;
using ValveResourceFormat.Renderer;

// Everything the automation server needs to reach into the viewers lives here rather than in the
// viewers themselves, so that the feature stays in one place and leaves nothing behind in a build
// that does not include it.

namespace GUI.Types.GLViewers
{
    partial class GLBaseControl
    {
        private (int Width, int Height)? viewportOverride;

        /// <summary>The size frames are currently rendered at.</summary>
        internal (int Width, int Height) ViewportSize =>
            GLDefaultFramebuffer is null ? (0, 0) : (GLDefaultFramebuffer.Width, GLDefaultFramebuffer.Height);

        partial void ApplyViewportOverride(ref int width, ref int height)
        {
            if (viewportOverride is { } size)
            {
                (width, height) = size;
            }
        }

        // Rendering smaller than the window leaves the rest of it showing the last frame drawn at
        // the window's own size, alternating as the buffers swap. A clear is bounded by the scissor
        // box rather than the viewport, and nothing has opened a scissor scope this early.
        partial void ClearWindowOutsideViewport()
        {
            if (viewportOverride != null
                && GLNativeWindow is { } window
                && GLDefaultFramebuffer is { } windowFramebuffer
                && (windowFramebuffer.Width != window.Size.X || windowFramebuffer.Height != window.Size.Y))
            {
                windowFramebuffer.BindAndClear();
            }
        }

        /// <summary>
        /// Renders at an exact size regardless of the window, so two runs can be compared pixel for
        /// pixel. Capped to the window, because the frame is read back out of the window's own
        /// framebuffer and a larger request would read past the pixels that exist.
        /// </summary>
        internal void SetViewportSize(int width, int height)
        {
            using var lockedGl = MakeCurrent();

            if (GLNativeWindow != null)
            {
                width = Math.Min(width, GLNativeWindow.Size.X);
                height = Math.Min(height, GLNativeWindow.Size.Y);
            }

            viewportOverride = (width, height);

            ShouldResize = false;
            OnResize(width, height);
        }

        /// <summary>Goes back to following the window, from the next frame.</summary>
        internal void RestoreViewportSize()
        {
            viewportOverride = null;
            ShouldResize = true;
        }

        /// <summary>Puts this control back on the render loop, which minimizing the window takes it off.</summary>
        internal void EnsureAttachedToRenderLoop() => AttachToRenderLoop();

        /// <summary>Blocks until every requested texture mip is in, so a capture is not of low mips.</summary>
        internal void FinishTextureStreaming(CancellationToken cancellationToken)
        {
            using var lockedGl = MakeCurrent();

            RendererContext.TextureStreaming.FinishAllStreaming(cancellationToken);
        }

        /// <summary>Reaches the viewers' own capture path, which is otherwise protected.</summary>
        internal SKBitmap? CaptureBitmap() => ReadPixelsToBitmap();
    }

    partial class GLSceneViewer
    {
        /// <summary>The picking framebuffer.</summary>
        internal PickingTexture? PickingTexture => Picker;

        /// <summary>Selectable render mode names, headers excluded.</summary>
        internal List<string> AvailableRenderModes
        {
            get
            {
                var names = new List<string>(renderModes.Count);

                foreach (var mode in renderModes)
                {
                    if (!mode.IsHeader)
                    {
                        names.Add(mode.Name);
                    }
                }

                return names;
            }
        }

        /// <summary>The render mode the dropdown currently shows.</summary>
        internal string? CurrentRenderMode
        {
            get
            {
                var index = renderModeComboBox?.SelectedIndex ?? -1;

                return index >= 0 && index < renderModes.Count ? renderModes[index].Name : null;
            }
        }

        // Goes through the dropdown so the UI shows what was asked for instead of diverging from it.
        internal bool TrySetRenderMode(string name)
        {
            if (renderModeComboBox == null)
            {
                return false;
            }

            for (var i = 0; i < renderModes.Count; i++)
            {
                if (!renderModes[i].IsHeader && string.Equals(renderModes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    renderModeComboBox.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Drops the selection, including the debug geometry a selected light probe volume or envmap
        /// draws over the scene in every render mode.
        /// </summary>
        internal void ClearSelection() => SelectedNodeRenderer?.SelectNode(null);

        /// <summary>The value of <see cref="PerfDisplayMode"/> that fills the per frame counters.</summary>
        internal const int PerfDisplayStats = (int)PerfDisplay.Stats;

        // Goes through the dropdown when there is one, so the UI shows what is actually being collected.
        internal int PerfDisplayMode
        {
            get => (int)perfDisplay;
            set
            {
                if (perfDisplayComboBox != null)
                {
                    perfDisplayComboBox.SelectedIndex = value;
                }
                else
                {
                    perfDisplay = (PerfDisplay)value;
                }
            }
        }
    }

    partial class GLWorldViewer
    {
        /// <summary>World layer names with their current checked state.</summary>
        internal List<(string Name, bool Enabled)> GetWorldLayers()
        {
            var layers = new List<(string, bool)>();

            if (worldLayersComboBox == null)
            {
                return layers;
            }

            for (var i = 0; i < worldLayersComboBox.Items.Count; i++)
            {
                layers.Add((worldLayersComboBox.Items[i].ToString() ?? string.Empty, worldLayersComboBox.GetItemChecked(i)));
            }

            return layers;
        }

        // Goes through the checkbox so the UI and the 3D sky scene follow the same path as a click.
        internal bool TrySetWorldLayer(string name, bool enabled)
        {
            if (worldLayersComboBox == null)
            {
                return false;
            }

            var index = worldLayersComboBox.FindStringExact(name);

            if (index < 0)
            {
                return false;
            }

            worldLayersComboBox.SetItemChecked(index, enabled);

            return true;
        }
    }

    partial class RenderLoopThread
    {
        // Automation needs frames while the app sits in the background, which the idle pause would
        // otherwise stop. Scoped rather than a global switch so the app still parks itself when no
        // tool is waiting on a frame.
        private static int automationHolders;
        private static readonly ManualResetEventSlim framePresented = new(initialState: false);

        /// <summary>
        /// Keeps the loop rendering regardless of window activation until the returned scope is
        /// disposed. Only the active GL control renders, so select the tab first.
        /// </summary>
        public static IDisposable BeginAutomationRendering()
        {
            Interlocked.Increment(ref automationHolders);
            renderSignal.Set();

            return new AutomationScope();
        }

        private sealed class AutomationScope : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref automationHolders);
        }

        /// <summary>
        /// Blocks until <paramref name="count"/> more frames have been presented, or the timeout
        /// expires. Callers must hold a scope from <see cref="BeginAutomationRendering"/>.
        /// </summary>
        public static bool WaitForFrames(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            for (var i = 0; i < count; i++)
            {
                framePresented.Reset();

                var remaining = deadline - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero || !framePresented.Wait(remaining))
                {
                    return false;
                }
            }

            return true;
        }

        static partial void KeepRenderingWhileInBackground(ref bool keepRendering)
            => keepRendering = Volatile.Read(ref automationHolders) > 0;

        static partial void OnFramePresented() => framePresented.Set();
    }
}

namespace GUI.Utils
{
    partial class ConsoleTab
    {
        /// <summary>
        /// Console contents, oldest first, capped to the newest <paramref name="limit"/> lines.
        /// Reads the queue as well as the text box, because the queue is only drained while the tab
        /// is on screen. Does not consume it, so the tab still fills in later. Call on the UI thread.
        /// </summary>
        internal List<string> GetLines(int limit)
        {
            var lines = new List<string>();

            if (control is { IsDisposed: false })
            {
                foreach (var line in control.Text.Split('\n'))
                {
                    lines.Add(line.TrimEnd('\r'));
                }

                if (lines.Count > 0 && lines[^1].Length == 0)
                {
                    lines.RemoveAt(lines.Count - 1);
                }
            }

            foreach (var line in LogQueue)
            {
                lines.Add(FormatPrefix(line) + line.Message);
            }

            if (lines.Count > limit)
            {
                lines.RemoveRange(0, lines.Count - limit);
            }

            return lines;
        }
    }

    partial class Log
    {
        /// <summary>Recent console lines. Call on the UI thread.</summary>
        public static List<string> GetLines(int limit) => console?.GetLines(limit) ?? [];
    }
}
#endif
