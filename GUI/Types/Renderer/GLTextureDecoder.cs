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
    private readonly AutoResetEvent queueUpdateEvent = new(false);
    private readonly ConcurrentQueue<DecodeRequest> decodeQueue;
    private readonly Thread GLThread;


    public record DecodeRequest(SKBitmap Bitmap, Resource TextureResource, int Mip, int Depth, ChannelMapping Channels)
    {
        public bool HemiOctRB { get; init; }

        public ManualResetEvent DoneEvent { get; } = new ManualResetEvent(false);
        public float DecodeTime { get; set; }
        public float TotalTime { get; set; }
    };

    private GameWindow GLWindow;
    private GraphicsContext GLContext;
    private int FrameBuffer;
    private RenderTexture FrameBufferColor;

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

    public void Decode(DecodeRequest request)
    {
        var sw = Stopwatch.StartNew();
        decodeQueue.Enqueue(request);
        queueUpdateEvent.Set();

        request.DoneEvent.WaitOne();
        request.TotalTime = sw.ElapsedMilliseconds;

        Log.Debug(nameof(GLTextureDecoder), $"Decoded in {request.DecodeTime}ms, total (including comm overhead) {request.TotalTime}ms");
    }

    private void Initialize()
    {
        GLWindow = new GameWindow(1, 1);
        GLContext = new GraphicsContext(new GraphicsMode(new ColorFormat(8, 8, 8, 8)), GLWindow.WindowInfo, 4, 6, GraphicsContextFlags.Offscreen);
        GLContext.MakeCurrent(GLWindow.WindowInfo);

        FrameBuffer = GL.GenFramebuffer();
        // Bind and stay on this framebuffer, the game window frame buffer is limited by screen size
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBuffer);

        FrameBufferColor = new RenderTexture(TextureTarget.Texture2D, 4096, 4096, 1, 1);
        using (FrameBufferColor.BindingContext())
        {
            //FrameBufferColor.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, FrameBufferColor.Width, FrameBufferColor.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, FrameBufferColor.Target, FrameBufferColor.Handle, 0);
        }

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer failed to bind with error: {status}");
        }

        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        GL.DebugMessageCallback((source, type, id, severity, length, message, userParam) =>
        {
            var msg = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(message, length);
            Log.Warn(nameof(GLTextureDecoder), $"GL: {type} {message}");
        }, IntPtr.Zero);

        while (true)
        {
            if (!decodeQueue.TryDequeue(out var decodeRequest))
            {
                queueUpdateEvent.WaitOne();
                continue;
            }

            Decode_Thread(decodeRequest);
            decodeRequest.Bitmap.NotifyPixelsChanged();
            decodeRequest.DoneEvent.Set();
        }
    }

    private void Decode_Thread(DecodeRequest request)
    {
        //GLContext.MakeCurrent(GLWindow.WindowInfo);
        var sw = Stopwatch.StartNew();
        RenderTexture inputTexture = guiContext.MaterialLoader.LoadTexture(request.TextureResource);
        inputTexture.Bind();
        inputTexture.SetFiltering(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);

        GL.Viewport(0, 0, inputTexture.Width, inputTexture.Height);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        //GL.ClearColor(0, 0, 0, 1);
        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);

        // TYPE_TEXTURE2D
        var textureType = "TYPE_" + inputTexture.Target.ToString().ToUpperInvariant();

        var shader = guiContext.ShaderLoader.LoadShader("vrf.texture_decode", new Dictionary<string, byte>
        {
            [textureType] = 1,
            ["HemiOctIsoRoughness_RG_B"] = request.HemiOctRB ? (byte)1 : (byte)0,
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
        GL.Flush();

        // extract pixels from framebuffer
        var pixels = request.Bitmap.GetPixels(out var length);
        Debug.Assert(length == inputTexture.Width * inputTexture.Height * 4);
        GL.ReadPixels(0, 0, inputTexture.Width, inputTexture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.Finish();

        request.DecodeTime = sw.ElapsedMilliseconds;
    }

    public void Dispose()
    {
        queueUpdateEvent.Dispose();
        GLContext?.Dispose();
        GLWindow?.Dispose();
        FrameBufferColor?.Dispose();
        GL.DeleteFramebuffer(FrameBuffer);
    }
}
