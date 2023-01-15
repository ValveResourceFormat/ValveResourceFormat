using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using GUI.Utils;

namespace GUI.Types.Renderer;

internal class PickingTexture : IDisposable
{
    internal class PickingRequest
    {
        public bool ActiveNextFrame;
        public int CursorPositionX;
        public int CursorPositionY;
        public int Clicks;

        public void NextFrame(int x, int y, int clicks)
        {
            ActiveNextFrame = true;
            CursorPositionX = x;
            CursorPositionY = y;
            Clicks = clicks;
        }
    }

    internal struct PickingResponse
    {
        public int Clicks;
        public PixelInfo PixelInfo;
    }

    internal struct PixelInfo
    {
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        public uint ObjectId;
        public uint MeshId;
        public uint Unused2;
#pragma warning restore CS0649  // Field is never assigned to, and will always have its default value
    }

    public event EventHandler<PickingResponse> OnPicked;
    public readonly PickingRequest Request = new();

    public readonly Shader shader;
    public readonly Shader debugShader;

    public bool IsActive => Request.ActiveNextFrame;
    public bool Debug { get; set; }

    private int width = 4;
    private int height = 4;
    private int fboHandle;
    private int colorHandle;
    private int depthHandle;

    public PickingTexture(VrfGuiContext vrfGuiContext, EventHandler<PickingResponse> onPicked)
    {
        shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, bool>());
        debugShader = vrfGuiContext.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, bool>() { { "F_DEBUG_PICKER", true } });
        OnPicked += onPicked;
        Setup();
    }

    public void Setup()
    {
        fboHandle = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);

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
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Render()
    {
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fboHandle);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public void Finish()
    {
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);

        if (Request.ActiveNextFrame)
        {
            Request.ActiveNextFrame = false;
            var pixelInfo = ReadPixelInfo(Request.CursorPositionX, Request.CursorPositionY);
            OnPicked?.Invoke(this, new PickingResponse
            {
                Clicks = Request.Clicks,
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

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fboHandle);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        var pixelInfo = new PixelInfo();
        GL.ReadPixels(width, this.height - height, 1, 1, PixelFormat.RgbaInteger, PixelType.UnsignedInt, ref pixelInfo);

        GL.ReadBuffer(ReadBufferMode.None);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);

        return pixelInfo;
    }

    public void Dispose()
    {
        OnPicked = null;
        GL.DeleteTexture(colorHandle);
        GL.DeleteTexture(depthHandle);
        GL.DeleteFramebuffer(fboHandle);
    }
}
