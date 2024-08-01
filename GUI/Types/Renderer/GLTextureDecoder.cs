using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using GUI.Controls;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.Texture;

namespace GUI.Types.Renderer;

class GLTextureDecoder : IHardwareTextureDecoder, IDisposable
{
    private record DecodeRequest(SKBitmap Bitmap, Resource Resource, int Mip, int Depth, CubemapFace Face, ChannelMapping Channels, TextureCodec DecodeFlags) : IDisposable
    {
        public ManualResetEvent DoneEvent { get; } = new(false);
        public bool Success { get; set; }
        public TimeSpan DecodeTime { get; set; }
        public TimeSpan ResponseTime { get; set; }

        public bool Wait(int timeout = Timeout.Infinite) => DoneEvent.WaitOne(timeout);

        public void MarkAsDone(bool successfully)
        {
            Success = successfully;
            if (!disposedValue)
            {
                DoneEvent.Set();
            }
        }

        private bool disposedValue;
        public void Dispose()
        {
            if (disposedValue)
            {
                return;
            }

            disposedValue = true;
            DoneEvent.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private readonly VrfGuiContext guiContext = new();
    private readonly AutoResetEvent queueUpdateEvent = new(false);
    private readonly ConcurrentQueue<DecodeRequest> decodeQueue = new();
    private readonly object threadStartupLock = new();

    private Thread GLThread;
    private bool IsRunning;

#pragma warning disable CA2213 // Disposable fields should be disposed (handled in Dispose_ThreadResources)
    private GLControl GLControl;
    private Framebuffer Framebuffer;
#pragma warning restore CA2213

    public void StartThread()
    {
        lock (threadStartupLock)
        {
            if (GLThread == null)
            {
                IsRunning = true;

                // create a thread context for OpenGL
                GLThread = new Thread(Initialize_NoExcept)
                {
                    IsBackground = true,
                    Name = nameof(GLTextureDecoder),
                    Priority = ThreadPriority.AboveNormal,
                };
                GLThread.Start();
            }
        }
    }

    public bool Decode(SKBitmap bitmap, Resource resource, uint depth, CubemapFace face, uint mipLevel, TextureCodec decodeFlags)
    {
        StartThread();

        if (!IsRunning)
        {
            Log.Warn(nameof(GLTextureDecoder), "Decoder thread is no longer available.");
            return false;
        }

        using var request = new DecodeRequest(bitmap, resource, (int)mipLevel, (int)depth, face, ChannelMapping.RGBA, decodeFlags);

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
        GLControl = new GLControl(new GraphicsMode(new ColorFormat(8, 8, 8, 8)), GLViewerControl.OpenGlVersionMajor, GLViewerControl.OpenGlVersionMinor, GraphicsContextFlags.Offscreen);
        GLControl.MakeCurrent();

        GLViewerControl.CheckOpenGL();

        Framebuffer = Framebuffer.Prepare(4, 4, 0, LDRFormat.Value, null);
        Framebuffer.Initialize();
        Framebuffer.CheckStatus_ThrowIfIncomplete(nameof(GLTextureDecoder));
        Framebuffer.ClearMask = ClearBufferMask.ColorBufferBit;

        while (IsRunning)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope - already disposed by the code that enqueues
            if (!decodeQueue.TryDequeue(out var decodeRequest))
            {
                queueUpdateEvent.WaitOne();
                continue;
            }
#pragma warning restore CA2000

            try
            {
                var successfully = ProcessDecodeRequest(decodeRequest);
                decodeRequest.MarkAsDone(successfully);
            }
            catch
            {
                decodeRequest.MarkAsDone(false);
                throw;
            }
        }
    }

    private bool ProcessDecodeRequest(DecodeRequest request)
    {
        var sw = Stopwatch.StartNew();
        var inputTexture = guiContext.MaterialLoader.LoadTexture(request.Resource, isViewerRequest: true);

        inputTexture.SetFiltering(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);

        /*
        if (request.Channels == ChannelMapping.RGBA && request.DecodeFlags == TextureCodec.None)
        {
            GL.GetTexImage(inputTexture.Target, request.Mip, PixelFormat.Bgra, PixelType.UnsignedByte, request.Bitmap.GetPixels());
            Log.Info(nameof(GLTextureDecoder), "Using GL.GetTexImage");
            request.DecodeTime = sw.Elapsed;
            return true;
        }
        */

        var framebufferFormat = request.Bitmap.ColorType switch
        {
            SKColorType.RgbaF32 => HDRFormat.Value,
            SKColorType.Bgra8888 => LDRFormat.Value,
            _ => null,
        };

        if (framebufferFormat == null)
        {
            Log.Warn(nameof(GLTextureDecoder), $"Unsupported bitmap output type: {request.Bitmap.ColorType}");
            return false;
        }

        if (Framebuffer.ColorFormat != framebufferFormat)
        {
            Framebuffer.ChangeFormat(framebufferFormat, null);
        }

        if (Framebuffer.Width < inputTexture.Width || Framebuffer.Height < inputTexture.Height)
        {
            Framebuffer.Resize(inputTexture.Width, inputTexture.Height);
        }

        GL.Viewport(0, 0, inputTexture.Width, inputTexture.Height);
        Framebuffer.Clear();
        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);

        var textureType = GetTextureTypeDefine(inputTexture.Target);
        var shader = guiContext.ShaderLoader.LoadShader("vrf.texture_decode", new Dictionary<string, byte>
        {
            [textureType] = 1,
        });

        GL.UseProgram(shader.Program);

        shader.SetTexture(0, "g_tInputTexture", inputTexture);
        shader.SetUniform2("g_vViewportSize", new System.Numerics.Vector2(inputTexture.Width, inputTexture.Height));
        shader.SetUniform4("g_vInputTextureSize", new System.Numerics.Vector4(
            inputTexture.Width, inputTexture.Height, inputTexture.Depth, inputTexture.NumMipLevels
        ));
        shader.SetUniform1("g_nSelectedMip", request.Mip);
        shader.SetUniform1("g_nSelectedDepth", request.Depth);
        shader.SetUniform1("g_nSelectedCubeFace", (int)request.Face);
        shader.SetUniform1("g_nSelectedChannels", request.Channels.PackedValue);
        shader.SetUniform1("g_nDecodeFlags", (int)request.DecodeFlags);

        // full screen triangle
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        inputTexture.Dispose();
        GL.UseProgram(0);

        var pixels = request.Bitmap.GetPixels(out var outputLength);
        var fbBytesPerPixel = Framebuffer.ColorFormat.PixelType == PixelType.Float ? 16 : 4;
        var fbRegionLength = request.Bitmap.Width * request.Bitmap.Height * fbBytesPerPixel;

        if (fbRegionLength > outputLength)
        {
            Log.Warn(nameof(GLTextureDecoder), $"Bitmap is too small to copy framebuffer contents to.");
            return false;
        }

        // extract pixels from framebuffer
        GL.Flush();
        GL.Finish();
        GL.ReadPixels(0, 0, request.Bitmap.Width, request.Bitmap.Height, Framebuffer.ColorFormat.PixelFormat, Framebuffer.ColorFormat.PixelType, pixels);

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
        guiContext.Dispose();
        Log.Info(nameof(GLTextureDecoder), "Decoder has been disposed.");
    }

    public static (SizedInternalFormat SizedInternalFormat, PixelFormat PixelFormat, PixelType PixelType) GetImageExportFormat(bool hdr) => hdr switch
    {
        false => (SizedInternalFormat.Rgba8, PixelFormat.Bgra, PixelType.UnsignedByte),
        true => (SizedInternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float),
    };

    public Lazy<Framebuffer.AttachmentFormat> LDRFormat { get; } = new(() => GetPreferredFramebufferFormat(hdr: false));
    public Lazy<Framebuffer.AttachmentFormat> HDRFormat { get; } = new(() => GetPreferredFramebufferFormat(hdr: true));

    public static Framebuffer.AttachmentFormat GetPreferredFramebufferFormat(bool hdr)
    {
        var (internalFormat, pixelFormat, pixelType) = GetImageExportFormat(hdr);

        GL.GetInternalformat(ImageTarget.Texture2D, internalFormat, InternalFormatParameter.InternalformatPreferred, 1, out int internalFormatPreferred);
        return new((PixelInternalFormat)internalFormatPreferred, pixelFormat, pixelType);
    }

    public static string GetTextureTypeDefine(TextureTarget target) => target switch
    {
        TextureTarget.Texture2D => "TYPE_TEXTURE2D",
        TextureTarget.Texture3D => "TYPE_TEXTURE3D",
        TextureTarget.Texture2DArray => "TYPE_TEXTURE2DARRAY",
        TextureTarget.TextureCubeMap => "TYPE_TEXTURECUBEMAP",
        TextureTarget.TextureCubeMapArray => "TYPE_TEXTURECUBEMAPARRAY",
        _ => throw new UnexpectedMagicException("Unsupported texture type", (int)target, target.ToString())
    };
}
