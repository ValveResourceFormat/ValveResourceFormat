using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer;

public ref struct GLDebugGroup
{
    public GLDebugGroup(string name)
    {
#if DEBUG
        GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, name.Length, name);
#endif
    }

#pragma warning disable CA1822 // Mark members as static
    public readonly void Dispose()
#pragma warning restore CA1822
    {
#if DEBUG
        GL.PopDebugGroup();
#endif
    }
}
