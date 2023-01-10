using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using System.Numerics;
using GUI.Utils;

namespace GUI.Types.Renderer;

internal struct PixelInfo
{
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    public uint Id;
    public uint Unused;
    public uint Unused2;
#pragma warning restore CS0649  // Field is never assigned to, and will always have its default value
}

internal class PickingTexture : IDisposable
{
    private int width = 4;
    private int height = 4;

    public Shader shader;
    private VrfGuiContext ctx;
    private int fboHandle;
    private int colorHandle;
    private int depthHandle;

    public PickingTexture(VrfGuiContext vrfGuiContext)
    {
        ctx = vrfGuiContext;
        SetDebug(false);
        Setup();
    }

    public void SetDebug(bool debug)
    {
        if (debug)
        {
            shader = ctx.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, bool>(){
                { "F_DEBUG_PICKER", true }
            });
            return;
        }

        shader = ctx.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, bool>());
    }

    public void Setup()
    {
        if (width == 0 || height == 0)
        {
            return;
        }

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
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public static void Finish()
    {
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
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

    public void Pick(int x, int y, EventHandler<uint> callback)
    {
        var id = ReadIdFromPixel(x, y);
        callback.Invoke(this, id);
    }

    public uint ReadIdFromPixel(int width, int height)
    {
        GL.Flush();
        GL.Finish();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fboHandle);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        var pixelInfo = new PixelInfo();
        GL.ReadPixels(width, this.height - height, 1, 1, PixelFormat.RgbaInteger, PixelType.UnsignedInt, ref pixelInfo);

        GL.ReadBuffer(ReadBufferMode.None);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);

        return pixelInfo.Id;
    }

    public void Dispose()
    {
        ctx.Dispose();
        GL.DeleteTexture(colorHandle);
        GL.DeleteTexture(depthHandle);
        GL.DeleteFramebuffer(fboHandle);
    }
}
