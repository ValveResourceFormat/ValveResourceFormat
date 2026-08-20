using System.Diagnostics;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.Renderer;

// The creation methods read no instance state yet, but a device backed by a real API object will,
// so they stay instance methods and callers reach them through Current rather than through a static.
#pragma warning disable CA1822 // Mark members as static

/// <summary>
/// Creates GPU objects for one graphics context.
///
/// A device stands for the context it was created alongside, so it is reached through
/// <see cref="Current"/> rather than passed down: a context is current on at most one thread at a
/// time, and <see cref="Current"/> names the device belonging to the context current on the calling
/// thread. Whoever makes a context current is responsible for pairing that with
/// <see cref="MakeCurrent"/> and <see cref="MakeNoneCurrent"/> so the two never disagree.
/// </summary>
public sealed class GraphicsDevice
{
    [ThreadStatic]
    private static GraphicsDevice? current;

    // 0 when this device is current on no thread. Written with Interlocked from any thread.
    private int currentThread;

    /// <summary>Gets the debug name this device was created with.</summary>
    public string Name { get; }

    private GraphicsDevice(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the device owning the graphics context current on the calling thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no device is current on this thread, which means the caller is creating GPU
    /// objects without a context, or from a thread the context was never handed to.
    /// </exception>
    public static GraphicsDevice Current => current
        ?? throw new InvalidOperationException(
            $"No graphics device is current on thread {Environment.CurrentManagedThreadId}. "
            + $"GPU objects can only be created on a thread that has made its context, and its device, current.");

    /// <summary>Gets whether a device is current on the calling thread.</summary>
    public static bool HasCurrent => current != null;

    /// <summary>
    /// Creates a device for a graphics context. Called once per context, by whoever owns it.
    /// </summary>
    /// <param name="name">Debug name identifying the context this device belongs to.</param>
    /// <returns>The new device, not yet current on any thread.</returns>
    public static GraphicsDevice Create(string name)
    {
        return new GraphicsDevice(name);
    }

    /// <summary>
    /// Makes this device the one <see cref="Current"/> returns on the calling thread. Call it
    /// wherever the matching graphics context is made current, and release it with
    /// <see cref="MakeNoneCurrent"/> wherever the context is released.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the device is still current on another thread, which is a context that was
    /// never released before being picked up elsewhere.
    /// </exception>
    public void MakeCurrent()
    {
        var thread = Environment.CurrentManagedThreadId;
        var holdingThread = Interlocked.CompareExchange(ref currentThread, thread, 0);

        if (holdingThread != 0 && holdingThread != thread)
        {
            throw new InvalidOperationException(
                $"Graphics device '{Name}' is current on thread {holdingThread} and cannot also be made current on thread {thread}. "
                + $"Release it there first.");
        }

        current = this;
    }

    /// <summary>
    /// Releases whichever device is current on the calling thread. Like the context release it
    /// mirrors, this is not nestable: the innermost call releases for good.
    /// </summary>
    public static void MakeNoneCurrent()
    {
        var device = current;

        if (device == null)
        {
            return;
        }

        current = null;
        Interlocked.CompareExchange(ref device.currentThread, 0, Environment.CurrentManagedThreadId);
    }

    /// <summary>Creates a buffer object.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The buffer handle.</returns>
    public int CreateBuffer(string name)
    {
        GL.CreateBuffers(1, out int handle);
        Label(ObjectLabelIdentifier.Buffer, handle, name);
        return handle;
    }

    /// <summary>Creates a texture object of the given type, without storage.</summary>
    /// <param name="type">Texture type, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The texture handle.</returns>
    public int CreateTexture(TextureType type, string name)
    {
        GL.CreateTextures(type.ToGLTextureTarget(), 1, out int handle);
        Label(ObjectLabelIdentifier.Texture, handle, name);
        return handle;
    }

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
    public int CreateTextureView(int texture, TextureType type, ImageFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
    {
        // A view needs a name without a target yet, which only the non-DSA path hands out.
        var handle = GL.GenTexture();
        GL.TextureView(handle, type.ToGLTextureTarget(), texture, (PixelInternalFormat)format.ToGLSizedInternalFormat(), minLevel, numLevels, minLayer, numLayers);
        Label(ObjectLabelIdentifier.Texture, handle, name);
        return handle;
    }

    /// <summary>Creates a sampler object with default state.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The sampler handle.</returns>
    public int CreateSampler(string name)
    {
        GL.CreateSamplers(1, out int handle);
        Label(ObjectLabelIdentifier.Sampler, handle, name);
        return handle;
    }

    /// <summary>Creates a framebuffer object without attachments.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The framebuffer handle.</returns>
    public int CreateFramebuffer(string name)
    {
        GL.CreateFramebuffers(1, out int handle);
        Label(ObjectLabelIdentifier.Framebuffer, handle, name);
        return handle;
    }

    /// <summary>Creates a vertex array object without attributes.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The vertex array handle.</returns>
    public int CreateVertexArray(string name)
    {
        GL.CreateVertexArrays(1, out int handle);
        Label(ObjectLabelIdentifier.VertexArray, handle, name);
        return handle;
    }

    /// <summary>Creates a query object measuring the given quantity.</summary>
    /// <param name="type">What the query measures, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The query handle.</returns>
    public int CreateQuery(QueryType type, string name)
    {
        GL.CreateQueries(type.ToGLQueryTarget(), 1, out int handle);
        Label(ObjectLabelIdentifier.Query, handle, name);
        return handle;
    }

    /// <summary>Creates an empty shader object for one stage.</summary>
    /// <param name="stage">The pipeline stage the shader runs at.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The shader object handle.</returns>
    public int CreateShader(ShaderStage stage, string name)
    {
        var handle = GL.CreateShader(stage.ToGLShaderType());
        Label(ObjectLabelIdentifier.Shader, handle, name);
        return handle;
    }

    /// <summary>Creates an empty program object.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The program handle.</returns>
    public int CreateProgram(string name)
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
