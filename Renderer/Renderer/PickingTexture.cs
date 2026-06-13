using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Framebuffer for GPU-based object picking using unique object IDs.
/// </summary>
public class PickingTexture : Framebuffer
{
    /// <summary>
    /// Type of interaction when picking an object.
    /// </summary>
    public enum PickingIntent
    {
        /// <summary>Select the picked object.</summary>
        Select,
        /// <summary>Open the picked object for viewing.</summary>
        Open,
        /// <summary>Show detail information about the picked object.</summary>
        Details,
    }

    /// <summary>
    /// Object picking response containing intent and pixel data.
    /// </summary>
    public readonly struct PickingResponse
    {
        /// <summary>Gets the interaction intent that triggered this pick.</summary>
        public PickingIntent Intent { get; init; }

        /// <summary>Gets the pixel data read back from the picking framebuffer.</summary>
        public PixelInfo PixelInfo { get; init; }
    }

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

        /// <summary>Non-zero when the picked pixel belongs to the skybox.</summary>
        public uint IsSkybox;

        /// <summary>Reserved padding field.</summary>
        public uint Unused2;
#pragma warning restore CS0649  // Field is never assigned to, and will always have its default value
    }

    /// <summary>Raised when a pick response is ready to be consumed.</summary>
    public event EventHandler<PickingResponse> OnPicked;

    /// <summary>Gets the picking shader used during the picking render pass.</summary>
    public Shader Shader { get; }

    /// <summary>Gets the debug shader that visualizes the picking buffer contents on screen.</summary>
    public Shader DebugShader { get; }

    /// <summary>Gets whether the current render mode has activated picking debug visualization.</summary>
    public bool IsDebugActive { get; private set; }

    /// <summary>Gets whether a pick has been requested and will be resolved on the next frame.</summary>
    public bool ActiveNextFrame { get; private set; }

    private int CursorPositionX;
    private int CursorPositionY;
    private PickingIntent Intent;
    private PickingResponse? Response;

    private readonly RendererContext RendererContext;

    // could share depth buffer with main framebuffer, but msaa doesn't match
    // private readonly Framebuffer depthSource;

    /// <summary>Initializes the picking framebuffer, shaders, and subscribes to the pick event.</summary>
    /// <param name="rendererContext">Renderer context for loading shaders.</param>
    /// <param name="onPicked">Handler invoked when a pick result is available.</param>
    public PickingTexture(RendererContext rendererContext, EventHandler<PickingResponse> onPicked) : base(nameof(PickingTexture))
    {
        RendererContext = rendererContext;
        Shader = rendererContext.ShaderLoader.LoadShader("vrf.picking");
        DebugShader = rendererContext.ShaderLoader.LoadShader("vrf.picking", ("F_DEBUG_PICKER", 1));
        OnPicked += onPicked;

        ColorFormat = new(PixelInternalFormat.Rgba32ui, PixelFormat.RgbaInteger, PixelType.UnsignedInt);
        DepthFormat = DepthAttachmentFormat.Depth32F;
        Target = TextureTarget.Texture2D;
        ClearColor = Color4.Black;

        Width = 4;
        Height = 4;

        Initialize();
    }

    /// <summary>Schedules a pick at the given cursor position to be resolved after the next frame renders.</summary>
    /// <param name="x">Cursor X position in window coordinates.</param>
    /// <param name="y">Cursor Y position in window coordinates.</param>
    /// <param name="intent">The interaction intent for this pick request.</param>
    public void RequestNextFrame(int x, int y, PickingIntent intent)
    {
        ActiveNextFrame = true;
        CursorPositionX = x;
        CursorPositionY = y;
        Intent = intent;
    }

    /// <summary>Reads back the picking pixel if a request was pending and stores the response for the next event trigger.</summary>
    public void Finish()
    {
        if (ActiveNextFrame)
        {
            ActiveNextFrame = false;
            var pixelInfo = ReadPixelInfo(CursorPositionX, CursorPositionY);
            Response = new PickingResponse
            {
                Intent = Intent,
                PixelInfo = pixelInfo,
            };
        }
    }

    /// <summary>Fires <see cref="OnPicked"/> with the stored response if one is available.</summary>
    public void TriggerEventIfAny()
    {
        if (Response is PickingResponse response)
        {
            Response = null;
            OnPicked?.Invoke(this, response);
        }
    }

    private PixelInfo ReadPixelInfo(int width, int height)
    {
        GL.Flush();
        GL.Finish();

        height = Height - height; // flip y
        var pixelInfo = new PixelInfo();

        Debug.Assert(ColorFormat is not null);

        GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(width, height, 1, 1, ColorFormat.PixelFormat, ColorFormat.PixelType, ref pixelInfo);
        GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.None);

        return pixelInfo;
    }

    /// <summary>Updates <see cref="IsDebugActive"/> based on whether the current render mode matches the picking shader's supported modes.</summary>
    /// <param name="renderMode">Name of the active render mode.</param>
    public void SetRenderMode(string renderMode)
    {
        IsDebugActive = Shader.RenderModes.Contains(renderMode);
    }
}
