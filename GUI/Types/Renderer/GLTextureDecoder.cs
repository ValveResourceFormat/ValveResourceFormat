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
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer;

class GLTextureDecoder : IDisposable // ITextureDecoder
{
    private readonly VrfGuiContext guiContext;
    private readonly AutoResetEvent queueUpdateEvent = new(false);
    private readonly ConcurrentQueue<DecodeRequest> decodeQueue;
    private readonly Thread GLThread;

    public record DecodeRequest(SKBitmap Bitmap, Texture Texture, int Mip, int Depth, ChannelMapping Channels)
    {
        public bool HemiOctRB { get; init; }

        public ManualResetEvent DoneEvent { get; } = new(false);
        public bool Success { get; set; }
        public float DecodeTime { get; set; }
        public float TotalTime { get; set; }
    };

    private GLControl GLControl;
    private int FrameBuffer;
    private RenderTexture FrameBufferColor;

    public GLTextureDecoder(VrfGuiContext guiContext)
    {
        this.guiContext = guiContext;
        decodeQueue = new();

        // create a thread context for OpenGL
        GLThread = new Thread(Initialize_NoExcept)
        {
            IsBackground = true,
            Name = nameof(GLTextureDecoder),
        };
        GLThread.Start();
    }

    public bool Initialized { get; private set; }
    public bool IsAvailable => Initialized && GLThread.IsAlive;

    public bool Decode(DecodeRequest request)
    {
        var sw = Stopwatch.StartNew();
        decodeQueue.Enqueue(request);
        queueUpdateEvent.Set();

        request.DoneEvent.WaitOne();
        request.TotalTime = sw.ElapsedMilliseconds;

        Log.Debug(nameof(GLTextureDecoder), $"Decode finished in {request.DecodeTime}ms (wait overhead: {request.TotalTime - request.DecodeTime}ms)");

        return true;
    }

    private void Initialize_NoExcept()
    {
        try
        {
            Initialize();
        }
        catch (Exception e)
        {
            Log.Error(nameof(GLTextureDecoder), $"Failed to initialize GL context: {e}");
        }
    }

    private void Initialize()
    {
        GLControl = new GLControl(new GraphicsMode(new ColorFormat(8, 8, 8, 8)), 4, 6, GraphicsContextFlags.Offscreen);
        GLControl.MakeCurrent();

        FrameBuffer = GL.GenFramebuffer();
        // Bind and stay on this framebuffer, the game window frame buffer is limited by screen size
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBuffer);

        FrameBufferColor = new RenderTexture(TextureTarget.Texture2D, 4096, 4096, 1, 1);
        using (FrameBufferColor.BindingContext())
        {
            //FrameBufferColor.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, FrameBufferColor.Width, FrameBufferColor.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, FrameBufferColor.Target, FrameBufferColor.Handle, 0);

            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new InvalidOperationException($"Framebuffer failed to bind with error: {status}");
            }
        }

        // TODO: Remove this
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        GL.DebugMessageCallback((source, type, id, severity, length, message, userParam) =>
        {
            var msg = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(message, length);
            Log.Warn(nameof(GLTextureDecoder), $"GL: {type} {msg}");
        }, IntPtr.Zero);

        GL.Flush();
        Initialized = true;

        while (true)
        {
            if (!decodeQueue.TryDequeue(out var decodeRequest))
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                queueUpdateEvent.WaitOne();
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                continue;
            }

            if (Decode_Thread(decodeRequest))
            {
                decodeRequest.Success = true;
            }

            decodeRequest.DoneEvent.Set();
        }
    }

    private bool Decode_Thread(DecodeRequest request)
    {
        var sw = Stopwatch.StartNew();
        var inputTexture = guiContext.MaterialLoader.LoadTexture(request.Texture);
        if (inputTexture == MaterialLoader.GetErrorTexture())
        {
            Log.Warn(nameof(GLTextureDecoder), $"Failure loading texture.");
            return false;
        }

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
        inputTexture.Dispose();
        GL.UseProgram(0);
        GL.Flush();

        // extract pixels from framebuffer
        var pixels = request.Bitmap.GetPixels(out var length);
        Debug.Assert(length == inputTexture.Width * inputTexture.Height * 4);
        GL.ReadPixels(0, 0, inputTexture.Width, inputTexture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.Finish();

        request.DecodeTime = sw.ElapsedMilliseconds;
        return true;
    }

    public void Dispose()
    {
        queueUpdateEvent.Dispose();
        GLControl?.Dispose();
        FrameBufferColor?.Dispose();
        GL.DeleteFramebuffer(FrameBuffer);
    }
}
