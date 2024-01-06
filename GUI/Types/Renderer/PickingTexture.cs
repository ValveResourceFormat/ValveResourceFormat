using System;
using System.Collections.Generic;
using GUI.Utils;
using OpenTK.Graphics.OpenGL4;

namespace GUI.Types.Renderer;

class PickingTexture : IDisposable
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

    private int width = 4;
    private int height = 4;
    public int Framebuffer { get; private set; } = -1;
    private int colorHandle;
    private int depthHandle;
    private readonly VrfGuiContext guiContext;

    public PickingTexture(VrfGuiContext vrfGuiContext, EventHandler<PickingResponse> onPicked)
    {
        guiContext = vrfGuiContext;
        Shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.picking");
        OnPicked += onPicked;
        Setup();
    }

    public void Setup()
    {
        Framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);

        colorHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, colorHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32ui, width, height, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colorHandle, 0);

        depthHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, depthHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthHandle, 0);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer failed to bind with error: {status}");
        }

        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, guiContext.DefaultFrameBuffer);
    }

    public void Finish()
    {
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, guiContext.DefaultFrameBuffer);

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

    public void Resize(int width, int height)
    {
        this.width = width;
        this.height = height;

        GL.BindTexture(TextureTarget.Texture2D, colorHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32ui, width, height, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, IntPtr.Zero);

        GL.BindTexture(TextureTarget.Texture2D, depthHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent, width, height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
    }

    public PixelInfo ReadPixelInfo(int width, int height)
    {
        GL.Flush();
        GL.Finish();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Framebuffer);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        var pixelInfo = new PixelInfo();
        GL.ReadPixels(width, this.height - height, 1, 1, PixelFormat.RgbaInteger, PixelType.UnsignedInt, ref pixelInfo);

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

    public void Dispose()
    {
        OnPicked = null;
        GL.DeleteTexture(colorHandle);
        GL.DeleteTexture(depthHandle);
        GL.DeleteFramebuffer(Framebuffer);
    }
}
