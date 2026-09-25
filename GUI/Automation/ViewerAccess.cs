#if DEBUG
using System.Threading;
using System.Windows.Forms;
using SkiaSharp;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

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

        /// <summary>Whether the viewer offers the Reload shaders button.</summary>
        internal bool OffersShaderReload => ShowReloadShadersButton;

        /// <summary>
        /// Makes the next frame draw and present even when nothing it shows has changed. Viewers
        /// that present every frame they are asked for need nothing here.
        /// </summary>
        internal virtual void ForceNextFrame()
        {
        }

        /// <summary>Blocks until every requested texture mip is in, so a capture is not of low mips.</summary>
        internal void FinishTextureStreaming(CancellationToken cancellationToken)
        {
            using var lockedGl = MakeCurrent();

            RendererContext.TextureStreaming.FinishAllStreaming(cancellationToken);
        }

        /// <summary>
        /// Holds the render thread off between frames, so scene state it mutates, such as particle
        /// simulations, can be read consistently. Do not take this on the UI thread: a frame can wait
        /// on the UI thread, which would then deadlock.
        /// </summary>
        internal Lock.Scope HoldFrame() => glLock.EnterScope();

        /// <summary>Reaches the viewers' own capture path, which is otherwise protected.</summary>
        internal SKBitmap? CaptureBitmap() => ReadPixelsToBitmap();
    }

    partial class GLTextureViewer
    {
        /// <summary>This viewer otherwise skips drawing and presenting while nothing it shows has changed.</summary>
        internal override void ForceNextFrame() => InvalidateRender();
    }

    partial class GLSceneViewer
    {
        /// <summary>The picking framebuffer.</summary>
        internal PickingTexture? PickingTexture => Picker;

        partial void ApplyAutomationTimestep(ref float timestep)
        {
            var controlled = Automation.AutomationClock.Advance(ref timestep);

            // Moving dither alone makes every pixel of two otherwise identical frames differ.
            Renderer.Postprocess.AnimateDither = !controlled;
        }

        /// <summary>
        /// Stops the viewer acting on picks until disposed, so a pick only reports what is under the
        /// pixel instead of selecting it the way a click would.
        /// </summary>
        internal IDisposable IgnorePicks()
        {
            Picker?.OnPicked -= OnPicked;

            return new PickScope(this);
        }

        private sealed class PickScope(GLSceneViewer viewer) : IDisposable
        {
            public void Dispose() => viewer.Picker?.OnPicked += viewer.OnPicked;
        }

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
        internal List<(string Name, bool Enabled)> GetWorldLayers() => GetChecked(worldLayersComboBox);

        /// <summary>Physics group names with their current checked state.</summary>
        internal List<(string Name, bool Enabled)> GetPhysicsGroups() => GetChecked(physicsGroupsComboBox);

        // Goes through the checkbox so the UI and the 3D sky scene follow the same path as a click.
        internal bool TrySetWorldLayer(string name, bool enabled) => TrySetChecked(worldLayersComboBox, name, enabled);

        internal bool TrySetPhysicsGroup(string name, bool enabled) => TrySetChecked(physicsGroupsComboBox, name, enabled);

        /// <summary>
        /// Selects the entity's node and frames it, as the entity list does, turning on its layer and
        /// physics group when they are off. An entity with nothing drawn is framed at
        /// <paramref name="worldOrigin"/>, which the caller has already placed in the world, so a 3D sky
        /// entity is looked at where it renders rather than at its origin in the sky map.
        /// </summary>
        /// <returns>The selected node, or null when the entity has none.</returns>
        internal SceneNode? SelectAndFocusEntity(EntityLump.Entity entity, Vector3 worldOrigin)
        {
            var node = Scene.Find(entity) ?? SkyboxScene?.Find(entity);

            if (node == null)
            {
                SelectedNodeRenderer?.SelectNode(null);
                FocusCameraOnBounds(new AABB(worldOrigin - new Vector3(32f), worldOrigin + new Vector3(32f)));
                return null;
            }

            SelectAndFocusNode(node);
            return node;
        }

        private static List<(string Name, bool Enabled)> GetChecked(CheckedListBox? listBox)
        {
            var items = new List<(string, bool)>();

            if (listBox == null)
            {
                return items;
            }

            for (var i = 0; i < listBox.Items.Count; i++)
            {
                items.Add((listBox.Items[i].ToString() ?? string.Empty, listBox.GetItemChecked(i)));
            }

            return items;
        }

        private static bool TrySetChecked(CheckedListBox? listBox, string name, bool enabled)
        {
            if (listBox == null)
            {
                return false;
            }

            var index = listBox.FindStringExact(name);

            if (index < 0)
            {
                return false;
            }

            listBox.SetItemChecked(index, enabled);

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
        /// expires. Each frame is drawn in full, even by a viewer that would otherwise skip it as
        /// unchanged. Callers must hold a scope from <see cref="BeginAutomationRendering"/>.
        /// </summary>
        public static bool WaitForFrames(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            for (var i = 0; i < count; i++)
            {
                framePresented.Reset();
                RequestFrame();

                while (!framePresented.Wait(NudgeInterval))
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        return false;
                    }

                    RequestFrame();
                }
            }

            return true;
        }

        private static void RequestFrame()
        {
            currentGLControl?.ForceNextFrame();
            Nudge();
        }

        /// <summary>How long a wait on the loop goes before waking it again.</summary>
        public static readonly TimeSpan NudgeInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Wakes the loop again. It decides to park itself before it rechecks whether automation
        /// holds it, so a scope that begins in between is missed and the loop sleeps through it.
        /// </summary>
        public static void Nudge() => renderSignal.Set();

        static partial void KeepRenderingWhileInBackground(ref bool keepRendering)
            => keepRendering = Volatile.Read(ref automationHolders) > 0;

        static partial void OnFramePresented() => framePresented.Set();
    }
}

namespace GUI.Types.Viewers
{
    partial class Resource
    {
        /// <summary>Why the 3D, texture or other special viewer failed, shown in its Viewer Error tab.</summary>
        internal Exception? ViewerException => GLViewerError?.Exception;

        /// <summary>Why reconstructing the source file failed, shown in its Decompile Error tab.</summary>
        internal Exception? DecompileException => DecompileError?.Exception;
    }
}

namespace GUI.Utils
{
    partial class ConsoleTab
    {
        static partial void RecordLine(Log.Category category, string component, string message)
        {
            if (Automation.Automation.IsEnabled)
            {
                Automation.AutomationLog.Add(category, component, message);
            }
        }
    }
}
#endif
