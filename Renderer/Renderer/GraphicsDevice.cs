using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

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
    /// <summary>Gets the debug name this device was created with.</summary>
    public string Name { get; }

    private GraphicsDevice(string name)
    {
        Name = name;
    }

    internal static GraphicsDevice Current => GraphicsContext.Current.Device;

    /// <summary>
    /// Creates a device. Called once per set of GPU objects that can be used with each other.
    /// </summary>
    /// <param name="name">Debug name identifying what this device serves.</param>
    /// <returns>The new device, with no contexts yet.</returns>
    public static GraphicsDevice Create(string name)
    {
        return new GraphicsDevice(name);
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

    /// <summary>Creates a buffer object.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The buffer handle.</returns>
    public static int CreateBuffer(string name) => Current.CreateBufferCore(name);

    /// <summary>Creates a texture object of the given type, without storage.</summary>
    /// <param name="type">Texture type, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The texture handle.</returns>
    public static int CreateTexture(TextureType type, string name) => Current.CreateTextureCore(type, name);

    /// <summary>Creates a texture view over a subrange of another texture's storage.</summary>
    /// <param name="texture">Texture whose storage the view shares.</param>
    /// <param name="type">Texture type of the view.</param>
    /// <param name="format">Format the storage is reinterpreted as.</param>
    /// <param name="minLevel">First mip level visible through the view.</param>
    /// <param name="numLevels">Number of mip levels visible through the view.</param>
    /// <param name="minLayer">First array layer visible through the view.</param>
    /// <param name="numLayers">Number of array layers visible through the view.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The view's texture handle.</returns>
    public static int CreateTextureView(int texture, TextureType type, ImageFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
        => Current.CreateTextureViewCore(texture, type, format, minLevel, numLevels, minLayer, numLayers, name);

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

    /// <summary>Creates a query object measuring the given quantity.</summary>
    /// <param name="type">What the query measures, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The query handle.</returns>
    public static int CreateQuery(QueryType type, string name) => Current.CreateQueryCore(type, name);

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

    private int CreateTextureCore(TextureType type, string name)
    {
        GL.CreateTextures(type.ToGLTextureTarget(), 1, out int handle);
        Label(ObjectLabelIdentifier.Texture, handle, name);
        return handle;
    }

    private int CreateTextureViewCore(int texture, TextureType type, ImageFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
    {
        // A view needs a name without a target yet, which only the non-DSA path hands out.
        var handle = GL.GenTexture();
        GL.TextureView(handle, type.ToGLTextureTarget(), texture, (PixelInternalFormat)format.ToGLSizedInternalFormat(), minLevel, numLevels, minLayer, numLayers);
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

    private int CreateQueryCore(QueryType type, string name)
    {
        GL.CreateQueries(type.ToGLQueryTarget(), 1, out int handle);
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

#pragma warning restore CA1822

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
