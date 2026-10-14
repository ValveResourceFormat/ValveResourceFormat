using OpenTK.Windowing.Desktop;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

/// <summary>
/// Presents a GLFW window's OpenGL context to the renderer as the surface a
/// <see cref="GraphicsContext"/> drives, so opening the context makes the window current too.
/// </summary>
sealed class GLFWSurface(IGLFWGraphicsContext context) : IGraphicsSurface
{
    public void Begin() => context.MakeCurrent();

    public void End() => context.MakeNoneCurrent();
}
