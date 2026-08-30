using OpenTK.Graphics.OpenGL;

namespace ShaderCompilerBench;

/// <summary>
/// Draws with a freshly linked program, because a link that reports success is not proof the driver
/// has finished. NVIDIA specializes a program again against the render state it is first drawn
/// with, so a path that looks cheap at link time can simply have moved the cost to the first frame.
/// The second draw is timed as well, since it is the floor the first one should be compared to.
/// </summary>
internal static class DrawProbe
{
    private static int framebuffer;
    private static int colorTarget;
    private static int depthTarget;
    private static int vertexArray;

    private const int Size = 64;

    private static void EnsureTarget()
    {
        if (framebuffer != 0)
        {
            return;
        }

        colorTarget = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, colorTarget);
        GL.TexStorage2D(TextureTarget2d.Texture2D, 1, SizedInternalFormat.Rgba16f, Size, Size);

        depthTarget = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthTarget);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent32f, Size, Size);

        framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colorTarget, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthTarget);

        vertexArray = GL.GenVertexArray();
        GL.Viewport(0, 0, Size, Size);
    }

    /// <summary>
    /// Times the first draw with this program and then a second one. Any specialization the driver
    /// held back lands in the first number and not the second.
    /// </summary>
    public static void Run(Timings timings, int program)
    {
        EnsureTarget();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.BindVertexArray(vertexArray);
        GL.UseProgram(program);

        // Errors from unbound uniform blocks and textures are expected and are not what is being
        // measured, so the queue is drained here rather than reported.
        while (GL.GetError() != ErrorCode.NoError)
        {
        }

        timings.Measure("first draw + glFinish", () =>
        {
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            GL.Finish();
        });

        timings.Measure("second draw + glFinish", () =>
        {
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            GL.Finish();
        });

        GL.UseProgram(0);
    }
}
