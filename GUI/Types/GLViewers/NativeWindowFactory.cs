using System.Threading.Tasks;
using GUI.Utils;
using Microsoft.Win32;
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
    private const string GpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private static bool GpuPreferenceRequested;
    private static readonly System.Threading.Lock GlfwLock = new();

    public static NativeWindow Create(NativeWindowSettings settings)
    {
        using var _ = GlfwLock.EnterScope();

        // Windows picks the adapter once per process at the first pixel format call.
        // The preference is only needed while the first window is created, so a portable exe leaves no entry behind.
        var wroteGpuPreference = false;

        if (!GpuPreferenceRequested)
        {
            GpuPreferenceRequested = true;
            wroteGpuPreference = RequestHighPerformanceGpu();
        }

        try
        {
            return new NativeWindow(settings);
        }
        finally
        {
            if (wroteGpuPreference)
            {
                RemoveGpuPreference();
            }
        }
    }

    // Skipped when the user already set a preference
    private static bool RequestHighPerformanceGpu()
    {
        var exePath = Environment.ProcessPath;

        if (exePath == null)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GpuPreferencesKey);

            if (key.GetValue(exePath) != null)
            {
                return false;
            }

            key.SetValue(exePath, "GpuPreference=2;");
            return true;
        }
        catch (Exception e)
        {
            Log.Debug(nameof(NativeWindowFactory), $"Failed to request the high performance GPU: {e.Message}");
            return false;
        }
    }

    private static void RemoveGpuPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPreferencesKey, writable: true);
            key?.DeleteValue(Environment.ProcessPath!, throwOnMissingValue: false);
        }
        catch (Exception e)
        {
            Log.Debug(nameof(NativeWindowFactory), $"Failed to remove the GPU preference: {e.Message}");
        }
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
                    StartFocused = false,
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
