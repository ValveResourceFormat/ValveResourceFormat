using System;
using System.Collections.Generic;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer;

class PickingTexture : Framebuffer
{
    public class PickingRequest
    {
        public bool ActiveNextFrame;
        public int CursorPositionX;
        public int CursorPositionY;
        public PickingIntent Intent;

        public void NextFrame(int x, int y, PickingIntent intent)
        {
            ActiveNextFrame = true;
            CursorPositionX = x;
            CursorPositionY = y;
            Intent = intent;
        }
    }

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
        public uint Unused1;
        public uint Unused2;
#pragma warning restore CS0649  // Field is never assigned to, and will always have its default value
    }

    public event EventHandler<PickingResponse> OnPicked;
    public readonly PickingRequest Request = new();
    public readonly Shader Shader;
    public Shader DebugShader;

    public bool IsActive => Request.ActiveNextFrame;

    private readonly VrfGuiContext guiContext;

    public PickingTexture(VrfGuiContext vrfGuiContext, /*Framebuffer*/ int depthSource, EventHandler<PickingResponse> onPicked)
    {
        guiContext = vrfGuiContext;
        Shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.picking");
        OnPicked += onPicked;

        ColorFormat = new(PixelInternalFormat.Rgba32ui, PixelFormat.RgbaInteger, PixelType.UnsignedInt);
        DepthFormat = DepthAttachmentFormat.Depth32F;
        Target = TextureTarget.Texture2D;
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);

        // resize is a good place to initialize the framebuffer with proper dimensions
        if (InitialStatus == FramebufferErrorCode.FramebufferUndefined)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            Initialize();

            using (Color.BindingContext())
            {
                Color.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            }

            // share depth with main framebuffer
            //Depth = depthSource.Depth;

            if (InitialStatus != FramebufferErrorCode.FramebufferComplete)
            {
                // dispose?
                throw new InvalidOperationException($"Framebuffer failed to bind with error: {InitialStatus}");
            }
        }
    }


    public void Finish()
    {
        if (Request.ActiveNextFrame)
        {
            Request.ActiveNextFrame = false;
            var pixelInfo = ReadPixelInfo(Request.CursorPositionX, Request.CursorPositionY);
            OnPicked?.Invoke(this, new PickingResponse
            {
                Intent = Request.Intent,
                PixelInfo = pixelInfo,
            });
        }
    }

    public PixelInfo ReadPixelInfo(int width, int height)
    {
        GL.Flush();
        GL.Finish();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, FboHandle);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        height = Height - height; // flip y

        var pixelInfo = new PixelInfo();
        GL.ReadPixels(width, height, 1, 1, ColorFormat.PixelFormat, ColorFormat.PixelType, ref pixelInfo);

        GL.ReadBuffer(ReadBufferMode.None);
        return pixelInfo;
    }

    public IEnumerable<string> GetAvailableRenderModes()
        => Shader.RenderModes;

    public void SetRenderMode(string renderMode)
    {
        if (Shader.RenderModes.Contains(renderMode))
        {
            DebugShader = guiContext.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, byte>
            {
                { "F_DEBUG_PICKER", 1 },
                { "renderMode_" + renderMode, 1 },
            });
            return;
        }

        DebugShader = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            OnPicked = null;
        }

        base.Dispose(disposing);
    }
}
