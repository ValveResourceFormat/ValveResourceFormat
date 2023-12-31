using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
    private readonly VrfGuiContext guiContext;
    private readonly ConcurrentQueue<DecodeRequest> decodeQueue;
    private readonly Thread GLThread;


    public record DecodeRequest(SKBitmap Bitmap, Resource TextureResource, int Mip, int Depth, ChannelMapping Channels)
    {
        public bool Done { get; set; }
    };

    private GameWindow GLWindow;
    private GraphicsContext GLContext;

    public GLTextureDecoder(VrfGuiContext guiContext)
    {
        this.guiContext = guiContext;
        decodeQueue = new();

        // create a thread context for OpenGL
        GLThread = new Thread(Initialize)
        {
            IsBackground = true,
            Name = "OpenGL Thread",
            Priority = ThreadPriority.AboveNormal,

        };
        GLThread.Start();
    }

    private void Initialize()
    {
        GLWindow = new GameWindow(4096, 4096);
        GLContext = new GraphicsContext(new GraphicsMode(new ColorFormat(8, 8, 8, 8)), GLWindow.WindowInfo, 4, 6, GraphicsContextFlags.Offscreen);
        GLContext.MakeCurrent(GLWindow.WindowInfo);

        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        GL.DebugMessageCallback((source, type, id, severity, length, message, userParam) =>
        {
            Log.Warn(nameof(GLTextureDecoder), $"GL: {type} {message}");
        }, IntPtr.Zero);

        while (true)
        {
            if (!decodeQueue.TryDequeue(out var decodeRequest))
            {
                Thread.Sleep(100);
                continue;
            }


            Decode(decodeRequest);
            decodeRequest.Bitmap.NotifyPixelsChanged();
        }
    }

    public void Decode(SKBitmap bitmap, Resource textureResource, int mip, int depth, ChannelMapping channels)
    {
        var request = new DecodeRequest(bitmap, textureResource, mip, depth, channels);
        decodeQueue.Enqueue(request);

        while (!request.Done)
        {
            Thread.Sleep(100);
        }
    }

    private void Decode(DecodeRequest request)
    {
        //GLContext.MakeCurrent(GLWindow.WindowInfo);
        RenderTexture inputTexture = guiContext.MaterialLoader.LoadTexture(request.TextureResource);
        inputTexture.Bind();
        inputTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        // TODO: this is limited by window size (needs separate fbo)
        GL.Viewport(0, 0, inputTexture.Width, inputTexture.Height);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        //GL.ClearColor(0, 0, 0, 1);
        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);

        // TYPE_TEXTURE2D
        var textureType = "TYPE_" + inputTexture.Target.ToString().ToUpperInvariant();

        // TODO: get HemiOctIsoRoughness_RG_B from somewhere

        var shader = guiContext.ShaderLoader.LoadShader("vrf.texture_decode", new Dictionary<string, byte>
        {
            [textureType] = 1
        });

        GL.UseProgram(shader.Program);

        shader.SetTexture(0, "g_tInputTexture", inputTexture);
        shader.SetUniform4("g_vInputTextureSize", new System.Numerics.Vector4(
            inputTexture.Width, inputTexture.Height, inputTexture.Depth, inputTexture.NumMipLevels
        ));
        shader.SetUniform1("g_nSelectedMip", Math.Clamp(request.Mip, 0, inputTexture.NumMipLevels - 1));
        shader.SetUniform1("g_nSelectedDepth", Math.Clamp(request.Depth, 0, inputTexture.Depth - 1));
        shader.SetUniform1("g_nChannelMapping", request.Channels.PackedValue);

        // full screen triangle
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        inputTexture.Unbind();
        GL.UseProgram(0);

        // extract pixels from framebuffer
        var pixels = request.Bitmap.GetPixels(out var length);
        Debug.Assert(length == inputTexture.Width * inputTexture.Height * 4);
        GL.ReadPixels(0, 0, inputTexture.Width, inputTexture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        request.Done = true;
    }

    public void Dispose()
    {
        GLContext.Dispose();
        GLWindow.Dispose();
    }
}
