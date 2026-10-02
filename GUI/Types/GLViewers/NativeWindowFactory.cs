using System.Threading.Tasks;
using GUI.Utils;
using OpenTK.Windowing.Desktop;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.GLViewers;

/// <summary>
/// Serializes every GLFW window created or destroyed in the process. GLFW and the lazy
/// WGL initialization inside its first window creation are not thread-safe.
/// </summary>
static class NativeWindowFactory
{
    private static readonly System.Threading.Lock GlfwLock = new();

    public static NativeWindow Create(NativeWindowSettings settings)
    {
        using var _ = GlfwLock.EnterScope();
        return new NativeWindow(settings);
    }

    /// <summary>
    /// Loads a viewer of an embedded model in the background and throws it away, so the first real viewer does
    /// not pay for the driver load of the first context, jitting the renderer, or compiling its shaders.
    /// </summary>
    public static void WarmUpInBackground()
    {
        Task.Run(() =>
        {
            try
            {
                // The viewer makes its window on the UI thread, which should not be the one to load the driver
                Destroy(Create(new NativeWindowSettings
                {
                    APIVersion = ValveResourceFormat.Renderer.GLEnvironment.RequiredVersion,
                    Flags = GLBaseControl.Flags | OpenTK.Windowing.Common.ContextFlags.Offscreen,
                    StartVisible = false,
                    Title = "Source 2 Viewer OpenGL warm-up",
                }));

                using var guiContext = new VrfGuiContext("env_cubemap.vmdl_c", null);
                var viewer = new WarmUpViewer(guiContext, guiContext.CreateRendererContext());

                try
                {
                    viewer.InitializeLoad();
                }
                finally
                {
                    Program.MainForm.Invoke(viewer.Dispose);
                }
            }
            catch (Exception e)
            {
                Log.Debug(nameof(NativeWindowFactory), $"OpenGL warm-up failed: {e.Message}");
            }
        });
    }

    private sealed class WarmUpViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
        : GLSingleNodeViewer(vrfGuiContext, rendererContext)
    {
        protected override void LoadScene()
        {
            base.LoadScene();

            var material = Scene.RendererContext.MaterialLoader.GetMaterial(null, null);
            Scene.Add(MeshSceneNode.CreateMaterialPreviewQuad(Scene, material, new Vector2(32)), false);
        }
    }

    public static void Destroy(NativeWindow? window)
    {
        if (window == null)
        {
            return;
        }

        using var _ = GlfwLock.EnterScope();
        window.Dispose();
    }
}
