using System;
using System.Collections.Generic;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;

namespace GUI.Types.Renderer;

class GLTextureDecoder : IDisposable // ITextureDecoder
{
    private readonly GameWindow GLWindow;
    private readonly GraphicsContext GLContext;
    public VrfGuiContext GuiContext { get; }

    public GLTextureDecoder(VrfGuiContext guiContext)
    {
        GuiContext = guiContext;

        GLWindow = new GameWindow(1, 1);
        GLContext = new GraphicsContext(new GraphicsMode(new ColorFormat(8), 0, 0, 0, 0, 1), GLWindow.WindowInfo);
        //GLContext.MakeCurrent(GLWindow.WindowInfo);
    }

    public void Decode(SKBitmap bitmap, Resource textureResource, int depth, int mip, ChannelMapping channels)
    {
        GLContext.MakeCurrent(GLWindow.WindowInfo);
        RenderTexture inputTexture = GuiContext.MaterialLoader.LoadTexture(textureResource);
        inputTexture.Bind();
        inputTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        GL.Viewport(0, 0, inputTexture.Width, inputTexture.Height);
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);

        // TYPE_TEXTURE2D
        var textureType = "TYPE_" + inputTexture.Target.ToString().ToUpperInvariant();

        // TODO: get HemiOctIsoRoughness_RG_B from somewhere

        var shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_decode", new Dictionary<string, byte>
        {
            [textureType] = 1
        });

        GL.UseProgram(shader.Program);

        shader.SetTexture(0, "g_tInputTexture", inputTexture);
        shader.SetUniform4("g_vInputTextureSize", new System.Numerics.Vector4(
            inputTexture.Width, inputTexture.Height, inputTexture.Depth, inputTexture.NumMipLevels
        ));
        shader.SetUniform1("g_nSelectedMip", Math.Clamp(mip, 0, inputTexture.NumMipLevels - 1));
        shader.SetUniform1("g_nSelectedDepth", Math.Clamp(depth, 0, inputTexture.Depth - 1));
        shader.SetUniform1("g_nChannelMapping", channels.PackedValue);

        // full screen triangle
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        inputTexture.Unbind();
        GL.UseProgram(0);

        // extract pixels from framebuffer
        GL.ReadPixels(0, 0, inputTexture.Width, inputTexture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.GetPixels(out _));

        GLContext.MakeCurrent(null);
    }

    public void Dispose()
    {
        GLContext.Dispose();
        GLWindow.Dispose();
    }
}
