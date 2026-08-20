using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Creates the GPU objects that one or more <see cref="GraphicsContext"/> record against.
///
/// Creation goes through the static methods, which resolve the device owning the context current
/// on the calling thread. That is what lets an object be created without every caller holding a
/// device. The instance behind those statics is what a context belongs to, and where per device
/// state lives once there is any.
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
    /// <param name="surface">The window side of the context, made current along with it.</param>
    /// <param name="name">Debug name identifying the context.</param>
    /// <returns>The new context, not yet current on any thread.</returns>
    public GraphicsContext CreateContext(IGraphicsSurface surface, string name)
    {
        return new GraphicsContext(this, surface, name);
    }

    /// <summary>
    /// Creates a context for a surface whose currency the caller owns, such as a window made
    /// current once and never released.
    /// </summary>
    /// <param name="name">Debug name identifying the context.</param>
    /// <returns>The new context, not yet current on any thread.</returns>
    public GraphicsContext CreateContext(string name)
    {
        return new GraphicsContext(this, surface: null, name);
    }

    /// <summary>Creates a buffer object.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The buffer handle.</returns>
    public static int CreateBuffer(string name) => Current.CreateBufferCore(name);

    /// <summary>Creates a texture object of the given target, without storage.</summary>
    /// <param name="target">Texture target, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The texture handle.</returns>
    public static int CreateTexture(TextureTarget target, string name) => Current.CreateTextureCore(target, name);

    /// <summary>Creates a texture view over a subrange of another texture's storage.</summary>
    /// <param name="texture">Texture whose storage the view shares.</param>
    /// <param name="target">Texture target of the view.</param>
    /// <param name="format">Format the storage is reinterpreted as.</param>
    /// <param name="minLevel">First mip level visible through the view.</param>
    /// <param name="numLevels">Number of mip levels visible through the view.</param>
    /// <param name="minLayer">First array layer visible through the view.</param>
    /// <param name="numLayers">Number of array layers visible through the view.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The view's texture handle.</returns>
    public static int CreateTextureView(int texture, TextureTarget target, ImageFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
        => Current.CreateTextureViewCore(texture, target, format, minLevel, numLevels, minLayer, numLayers, name);

    /// <summary>Creates a sampler object with default state.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The sampler handle.</returns>
    public static int CreateSampler(string name) => Current.CreateSamplerCore(name);

    /// <summary>Creates a framebuffer object without attachments.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The framebuffer handle.</returns>
    public static int CreateFramebuffer(string name) => Current.CreateFramebufferCore(name);

    /// <summary>Creates a vertex array object without attributes.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The vertex array handle.</returns>
    public static int CreateVertexArray(string name) => Current.CreateVertexArrayCore(name);

    /// <summary>Creates a query object of the given target.</summary>
    /// <param name="target">Query target, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The query handle.</returns>
    public static int CreateQuery(QueryTarget target, string name) => Current.CreateQueryCore(target, name);

    /// <summary>Creates an empty shader object for one stage.</summary>
    /// <param name="stage">The pipeline stage the shader runs at.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The shader object handle.</returns>
    public static int CreateShader(ShaderProgramType stage, string name) => Current.CreateShaderCore(stage, name);

    /// <summary>Creates an empty program object.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The program handle.</returns>
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
