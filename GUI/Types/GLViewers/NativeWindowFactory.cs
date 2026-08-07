using OpenTK.Windowing.Desktop;

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
