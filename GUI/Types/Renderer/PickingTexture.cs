using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using System.Numerics;
using GUI.Utils;

namespace GUI.Types.Renderer;

internal class PickingTexture
{
    private int width = 4;
    private int height = 4;

    public readonly Shader shader;
    private int fboHandle;
    private int colorHandle;
    private int depthHandle;

    public AABB BoundingBox => throw new NotImplementedException();

    public PickingTexture(VrfGuiContext vrfGuiContext)
    {
        shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.picking", new Dictionary<string, bool>());
        Setup();
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
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public static void Finish()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public int ReadPixel(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboHandle);
        int pixel = default;

        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.ReadPixels(width, this.height - height, 1, 1, PixelFormat.RgbaInteger, PixelType.UnsignedInt, ref pixel);
        GL.ReadBuffer(ReadBufferMode.None);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return pixel;
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

    public void Update(float deltaTime)
    {
        throw new NotImplementedException();
    }
}
