using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ValveResourceFormat.Renderer.Editor.Picking;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Framebuffer for GPU-based object picking using unique object IDs. Rendering the scene into it with
/// <see cref="Shader"/> as the replacement shader writes each pixel's node and mesh, which
/// <see cref="Read"/> reads back.
/// </summary>
public class PickingTexture : Framebuffer
{
    /// <summary>
    /// Pixel data read back from the picking framebuffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PixelInfo
    {
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        /// <summary>The scene node object ID under the cursor.</summary>
        public uint ObjectId;

        /// <summary>The mesh ID within the picked object.</summary>
        public uint MeshId;

        /// <summary>Index into <see cref="Renderer.Scenes"/> of the scene holding the picked object.</summary>
        public uint SceneIndex;

        /// <summary>Reserved padding field.</summary>
        public uint Unused2;
#pragma warning restore CS0649  // Field is never assigned to, and will always have its default value
    }

    /// <summary>Gets the picking shader used during the picking render pass.</summary>
    public Shader Shader { get; }

    /// <summary>Gets the debug shader that visualizes the picking buffer contents on screen.</summary>
    public Shader DebugShader { get; }

    /// <summary>Gets whether the current render mode has activated picking debug visualization.</summary>
    public bool IsDebugActive { get; private set; }

    // could share depth buffer with main framebuffer, but msaa doesn't match
    // private readonly Framebuffer depthSource;

    /// <summary>Initializes the picking framebuffer and its shaders.</summary>
    /// <param name="rendererContext">Renderer context for loading shaders.</param>
    public PickingTexture(RendererContext rendererContext) : base(nameof(PickingTexture))
    {
        Shader = rendererContext.ShaderLoader.LoadShader("picking");
        DebugShader = rendererContext.ShaderLoader.LoadShader("picking", ("F_DEBUG_PICKER", 1));

        ColorFormat = ImageFormat.RGBA32323232_UINT;
        DepthFormat = ImageFormat.D32;
        Target = TextureTarget.Texture2D;
        ClearColor = Color4.Black;

        Width = 4;
        Height = 4;

        Initialize();
    }

    /// <summary>
    /// Reads back the pixel and depth at a request's point, waiting for rendering into the framebuffer to finish.
    /// </summary>
    /// <param name="request">The request, whose point is in window coordinates from the top.</param>
    /// <returns>The node, mesh and depth drawn at that pixel.</returns>
    internal PickResult Read(PickRequest request)
    {
        GL.Flush();
        GL.Finish();

        var x = request.X;
        var y = Height - 1 - request.Y; // flip y
        var pixelInfo = new PixelInfo();
        var depth = 0f;

        Debug.Assert(ColorFormat is not null);

        var previousReadFramebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, FboHandle);

        GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(x, y, 1, 1, ColorFormat!.Value.ToGLPixelFormat(), ColorFormat.Value.ToGLPixelType(), ref pixelInfo);
        GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.None);

        GL.ReadPixels(x, y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, ref depth);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousReadFramebuffer);

        return new PickResult(request, pixelInfo, depth);
    }

    /// <summary>Updates <see cref="IsDebugActive"/> based on whether the current render mode matches the picking shader's supported modes.</summary>
    /// <param name="renderMode">Name of the active render mode.</param>
    public void SetRenderMode(string renderMode)
    {
        IsDebugActive = Shader.RenderModes.Contains(renderMode);
    }
}
