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
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer;

class GLTextureDecoderForLibrary : IHardwareTextureDecoder
{
    private readonly GLTextureDecoder hardwareDecoder;

    public GLTextureDecoderForLibrary()
    {
        hardwareDecoder = new GLTextureDecoder(new VrfGuiContext(null, null));
    }

    public void Decode(SKBitmap bitmap, Texture texture)
    {
        hardwareDecoder.Decode(new GLTextureDecoder.DecodeRequest(bitmap, texture, 0, 0, ChannelMapping.RGBA)
        {
            HemiOctRB = false,
        });
    }
}

class GLTextureDecoder : IDisposable // ITextureDecoder
{
    private readonly VrfGuiContext guiContext;
    private readonly AutoResetEvent queueUpdateEvent = new(false);
    private readonly ConcurrentQueue<DecodeRequest> decodeQueue;
    private readonly Thread GLThread;

#pragma warning disable CA2213 // Disposable fields should be disposed (handled in Dispose_ThreadResources)
    private GLControl GLControl;
    private Framebuffer Framebuffer;
    private DecodeRequest activeRequest;
#pragma warning restore CA2213 // Disposable fields should be disposed (handled in Dispose_ThreadResources)

    public GLTextureDecoder(VrfGuiContext guiContext)
    {
        this.guiContext = guiContext;
        decodeQueue = new();

        // create a thread context for OpenGL
        GLThread = new Thread(Initialize_NoExcept)
        {
            IsBackground = true,
            Name = nameof(GLTextureDecoder),
            Priority = ThreadPriority.AboveNormal,
        };

        IsRunning = true;
        GLThread.Start();
    }

    public record DecodeRequest(SKBitmap Bitmap, Texture Texture, int Mip, int Depth, ChannelMapping Channels) : IDisposable
    {
        public bool HemiOctRB { get; init; }
        public bool YCoCg { get; init; }

        public ManualResetEvent DoneEvent { get; } = new(false);
        public bool Success { get; set; }
        public TimeSpan DecodeTime { get; set; }
        public TimeSpan ResponseTime { get; set; }

        public bool Wait(int timeout = Timeout.Infinite) => DoneEvent.WaitOne(timeout);

        public void MarkAsDone(bool successfully)
        {
            Success = successfully;
            DoneEvent.Set();
        }

        public void Dispose()
        {
            DoneEvent.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public bool IsRunning { get; private set; }

    public bool Decode(DecodeRequest request)
    {
        if (!IsRunning)
        {
            Log.Warn(nameof(GLTextureDecoder), "Decoder thread is no longer available.");
            return false;
        }

        var sw = Stopwatch.StartNew();
        decodeQueue.Enqueue(request);
        queueUpdateEvent.Set();

        request.Wait();
        request.ResponseTime = sw.Elapsed - request.DecodeTime;

        var status = request.Success ? "succeeded" : "failed";
        Log.Debug(nameof(GLTextureDecoder), $"Decode {status} in {request.DecodeTime.Milliseconds}ms (response time: {request.ResponseTime.Milliseconds}ms)");

        return request.Success;
    }

    private void Initialize_NoExcept()
    {
        try
        {
            Initialize();
        }
        catch (Exception e)
        {
            Log.Error(nameof(GLTextureDecoder), $"GL context failure: {e}");
        }
        finally
        {
            IsRunning = false;
            CleanupRequests();
            Dispose_ThreadResources();
            Log.Warn(nameof(GLTextureDecoder), "Decoder thread has exited. It is no longer available.");
        }
    }

    private void Initialize()
    {
        GLControl = new GLControl(new GraphicsMode(new ColorFormat(8, 8, 8, 8)), 4, 6, GraphicsContextFlags.Offscreen);
        GLControl.MakeCurrent();

        Framebuffer = Framebuffer.Prepare(8192, 8192, 0, new(PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte), null);
        Framebuffer.Initialize();
        Framebuffer.CheckStatus_ThrowIfIncomplete(nameof(GLTextureDecoder));

        // TODO: Remove this
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        GL.DebugMessageCallback((source, type, id, severity, length, message, userParam) =>
        {
            var msg = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(message, length);
            Log.Warn(nameof(GLTextureDecoder), $"GL: {type} {msg}");
        }, IntPtr.Zero);

        GL.Flush();

        while (IsRunning)
        {
            if (!decodeQueue.TryDequeue(out var decodeRequest))
            {
                queueUpdateEvent.WaitOne();
                continue;
            }

            activeRequest = decodeRequest;
            var successfully = Decode_Thread(activeRequest);
            activeRequest.MarkAsDone(successfully);
        }
    }

    private bool Decode_Thread(DecodeRequest request)
    {
        var sw = Stopwatch.StartNew();
        var inputTexture = guiContext.MaterialLoader.LoadTexture(request.Texture);
        /*
        if (inputTexture == MaterialLoader.GetErrorTexture())
        {
            Log.Warn(nameof(GLTextureDecoder), $"Failure loading texture (unsupported format?).");
            return false;
        }*/

        inputTexture.Bind();
        inputTexture.SetFiltering(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);

        if (request.Channels == ChannelMapping.RGBA
            && request.HemiOctRB == false && request.YCoCg == false)
        {
            GL.GetTexImage(inputTexture.Target, request.Mip, PixelFormat.Rgba, PixelType.UnsignedByte, request.Bitmap.GetPixels());
            Log.Info(nameof(GLTextureDecoder), "Using GL.GetTexImage");
            request.DecodeTime = sw.Elapsed;
            return true;
        }

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
            ["YCoCg_Conversion"] = request.YCoCg ? (byte)1 : (byte)0,
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

        request.DecodeTime = sw.Elapsed;
        return true;
    }

    private void Dispose_ThreadResources()
    {
        Framebuffer?.Dispose();
        GLControl?.Dispose();
    }

    private void Exit()
    {
        IsRunning = false;  // signal the thread that it should exit
        queueUpdateEvent.Set(); // wake the thread up
        GLThread.Join(); // wait for the thread to exit
    }

    private void CleanupRequests()
    {
        if (activeRequest != null)
        {
            activeRequest.MarkAsDone(successfully: false);
            activeRequest.Dispose();
            activeRequest = null;
        }

        foreach (var queuedRequest in decodeQueue)
        {
            queuedRequest.MarkAsDone(successfully: false);
            queuedRequest.Dispose();
        }

        decodeQueue.Clear();
    }

    public void Dispose()
    {
        Exit();
        queueUpdateEvent.Dispose();
        Log.Info(nameof(GLTextureDecoder), "Decoder has been disposed.");
    }
}
