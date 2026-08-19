using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

// a real device wrapper would have local state to track so making them static isnt the right call.
#pragma warning disable CA1822 // Mark members as static

/// <summary>
/// A moch grapics device that creates GPU objects for one graphics context. 
/// </summary>
public sealed class GraphicsDevice
{
    /// <summary>Creates a buffer object.</summary>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The buffer handle.</returns>
    public int CreateBuffer(string name)
    {
        GL.CreateBuffers(1, out int handle);
        Label(ObjectLabelIdentifier.Buffer, handle, name);
        return handle;
    }

    /// <summary>Creates a texture object of the given target, without storage.</summary>
    /// <param name="target">Texture target, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The texture handle.</returns>
    public int CreateTexture(TextureTarget target, string name)
    {
        GL.CreateTextures(target, 1, out int handle);
        Label(ObjectLabelIdentifier.Texture, handle, name);
        return handle;
    }

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
    public int CreateTextureView(int texture, TextureTarget target, SizedInternalFormat format, int minLevel, int numLevels, int minLayer, int numLayers, string name)
    {
        // A view needs a name without a target yet, which only the non-DSA path hands out.
        var handle = GL.GenTexture();
        GL.TextureView(handle, target, texture, (PixelInternalFormat)format, minLevel, numLevels, minLayer, numLayers);
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

    /// <summary>Creates a query object of the given target.</summary>
    /// <param name="target">Query target, which the object is fixed to.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The query handle.</returns>
    public int CreateQuery(QueryTarget target, string name)
    {
        GL.CreateQueries(target, 1, out int handle);
        Label(ObjectLabelIdentifier.Query, handle, name);
        return handle;
    }

    /// <summary>Creates an empty shader object for one stage.</summary>
    /// <param name="type">The shader stage.</param>
    /// <param name="name">Debug label, visible in graphics debuggers.</param>
    /// <returns>The shader object handle.</returns>
    public int CreateShader(ShaderType type, string name)
    {
        var handle = GL.CreateShader(type);
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
