using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Creates the GPU objects that one or more <see cref="GraphicsContext"/> record against.
/// </summary>
public sealed class GraphicsDevice
{
    internal static GraphicsDevice Current => GraphicsContext.Current.Device;

    /// <summary>
    /// Creates a device. Called once per set of GPU objects that can be used with each other.
    /// </summary>
    /// <returns>The new device, with no contexts yet.</returns>
    public static GraphicsDevice Create()
    {
        return new GraphicsDevice();
    }

    /// <summary>
    /// Creates a context that records against this device's objects.
    /// </summary>
    /// <param name="surface">The window side of the context, opened along with it.</param>
    /// <returns>The new context, not yet current on any thread.</returns>
    public GraphicsContext CreateContext(IGraphicsSurface surface)
    {
        return new GraphicsContext(this, surface);
    }

    /// <summary>
    /// Creates a context for a surface whose currency the caller owns, such as a window made
    /// current once and never released.
    /// </summary>
    /// <returns>The new context, not yet current on any thread.</returns>
    public GraphicsContext CreateContext()
    {
        return new GraphicsContext(this, surface: null);
    }

    /// <summary>Creates a buffer object.</summary>
    public static int CreateBuffer(string name) => Current.CreateBufferCore(name);

    /// <summary>Creates a texture object of the given target, without storage.</summary>
    public static int CreateTexture(TextureTarget target, string name) => Current.CreateTextureCore(target, name);

    /// <summary>Creates a texture view over a subrange of another texture's storage.</summary>
    public static int CreateTextureView(int texture, TextureTarget target, ImageFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
        => Current.CreateTextureViewCore(texture, target, format, minLevel, numLevels, minLayer, numLayers, name);

    /// <summary>Creates a sampler object with default state.</summary>
    public static int CreateSampler(string name) => Current.CreateSamplerCore(name);

    /// <summary>Creates a framebuffer object without attachments.</summary>
    public static int CreateFramebuffer(string name) => Current.CreateFramebufferCore(name);

    /// <summary>Creates a vertex array object without attributes.</summary>
    public static int CreateVertexArray(string name) => Current.CreateVertexArrayCore(name);

    /// <summary>Creates a query object of the given target.</summary>
    public static int CreateQuery(QueryTarget target, string name) => Current.CreateQueryCore(target, name);

    /// <summary>Creates an empty shader object for one stage.</summary>
    public static int CreateShader(ShaderProgramType stage, string name) => Current.CreateShaderCore(stage, name);

    /// <summary>Creates an empty program object.</summary>
    public static int CreateProgram(string name) => Current.CreateProgramCore(name);

    // The implementations read no instance state yet, but a device holding a real API object will.
#pragma warning disable CA1822 // Mark members as static

    private int CreateBufferCore(string name)
    {
        GL.CreateBuffers(1, out int handle);
        Label(ObjectLabelIdentifier.Buffer, handle, name);
        return handle;
    }

    private int CreateTextureCore(TextureTarget target, string name)
    {
        GL.CreateTextures(target, 1, out int handle);
        Label(ObjectLabelIdentifier.Texture, handle, name);
        return handle;
    }

    private int CreateTextureViewCore(int texture, TextureTarget target, ImageFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
    {
        // A view needs a name without a target yet, which only the non-DSA path hands out.
        var handle = GL.GenTexture();
        GL.TextureView(handle, target, texture, (PixelInternalFormat)format.ToGLSizedInternalFormat(), minLevel, numLevels, minLayer, numLayers);
        Label(ObjectLabelIdentifier.Texture, handle, name);
        return handle;
    }

    private int CreateSamplerCore(string name)
    {
        GL.CreateSamplers(1, out int handle);
        Label(ObjectLabelIdentifier.Sampler, handle, name);
        return handle;
    }

    private int CreateFramebufferCore(string name)
    {
        GL.CreateFramebuffers(1, out int handle);
        Label(ObjectLabelIdentifier.Framebuffer, handle, name);
        return handle;
    }

    private int CreateVertexArrayCore(string name)
    {
        GL.CreateVertexArrays(1, out int handle);
        Label(ObjectLabelIdentifier.VertexArray, handle, name);
        return handle;
    }

    private int CreateQueryCore(QueryTarget target, string name)
    {
        GL.CreateQueries(target, 1, out int handle);
        Label(ObjectLabelIdentifier.Query, handle, name);
        return handle;
    }

    private int CreateShaderCore(ShaderProgramType stage, string name)
    {
        var handle = GL.CreateShader(stage.ToGLShaderType());
        Label(ObjectLabelIdentifier.Shader, handle, name);
        return handle;
    }

    private int CreateProgramCore(string name)
    {
        var handle = GL.CreateProgram();
        Label(ObjectLabelIdentifier.Program, handle, name);
        return handle;
    }
    [Conditional("DEBUG")]
    private static void Label(ObjectLabelIdentifier identifier, int handle, string name)
    {
#if DEBUG
        if (name.Length == 0)
        {
            return;
        }

        var maxLength = GLEnvironment.MaxLabelLength;
        var length = maxLength > 0 ? Math.Min(maxLength, name.Length) : name.Length;

        GL.ObjectLabel(identifier, handle, length, name);
#endif
    }
}
