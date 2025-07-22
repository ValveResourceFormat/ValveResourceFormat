using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace GUI.Types.Renderer;

#nullable disable

class PickingTexture : Framebuffer
{
    internal enum PickingIntent
    {
        Select,
        Open,
        Details,
    }

    internal struct PickingResponse
    {
        public PickingIntent Intent;
        public PixelInfo PixelInfo;
    }

    internal struct PixelInfo
    {
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        public uint ObjectId;
        public uint MeshId;
        public uint IsSkybox;
        public uint Unused2;
#pragma warning restore CS0649  // Field is never assigned to, and will always have its default value
    }

    public event EventHandler<PickingResponse> OnPicked;
    public Shader Shader { get; }
    public Shader DebugShader { get; private set; }
    public bool ActiveNextFrame { get; private set; }

    private int CursorPositionX;
    private int CursorPositionY;
    private PickingIntent Intent;
    private PickingResponse? Response;

    private readonly VrfGuiContext guiContext;

    // could share depth buffer with main framebuffer, but msaa doesn't match
    // private readonly Framebuffer depthSource;

    public PickingTexture(VrfGuiContext vrfGuiContext, EventHandler<PickingResponse> onPicked) : base(nameof(PickingTexture))
    {
        guiContext = vrfGuiContext;
        Shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.picking");
        OnPicked += onPicked;

        ColorFormat = new(PixelInternalFormat.Rgba32ui, PixelFormat.RgbaInteger, PixelType.UnsignedInt);
        DepthFormat = DepthAttachmentFormat.Depth32F;
        Target = TextureTarget.Texture2D;
        ClearColor = Color4.Black;

        Width = 4;
        Height = 4;
        Initialize();
    }

    public void RequestNextFrame(int x, int y, PickingIntent intent)
    {
        ActiveNextFrame = true;
        CursorPositionX = x;
        CursorPositionY = y;
        Intent = intent;
    }

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

        GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(width, height, 1, 1, ColorFormat.PixelFormat, ColorFormat.PixelType, ref pixelInfo);
        GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.None);

        return pixelInfo;
    }

    public void SetRenderMode(string renderMode)
    {
        if (Shader.RenderModes.Contains(renderMode))
        {
            DebugShader = guiContext.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, byte>
            {
                { "F_DEBUG_PICKER", 1 },
            });
            return;
        }

        DebugShader = null;
    }
}
