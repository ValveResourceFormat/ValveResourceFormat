using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.TextureDecoders;
using static ValveResourceFormat.ResourceTypes.Texture;

#nullable disable

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
    private readonly Lock threadStartupLock = new();

    private Thread GLThread;

    private GameWindow GLWindowContext;
    private Framebuffer Framebuffer;

    public void StartThread()
    {
        lock (threadStartupLock)
        {
            if (GLThread == null)
            {
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

        if (!GLThread.IsAlive)
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
            CleanupRequests();
            Dispose_ThreadResources();
            Log.Warn(nameof(GLTextureDecoder), "Decoder thread has exited. It is no longer available.");
        }
    }

    private void Initialize()
    {
        Log.Info(nameof(GLTextureDecoder), "Initializing GPU texture decoder...");

        GLWindowContext = new GameWindow(new() { UpdateFrequency = 200 }, new()
        {
            APIVersion = GLViewerControl.OpenGlVersion,
            Flags = GLViewerControl.OpenGlFlags | OpenTK.Windowing.Common.ContextFlags.Offscreen,
            StartVisible = false,
            StartFocused = false,
            ClientSize = new(4, 4),
            DepthBits = null,
            StencilBits = null,
        });

        GLWindowContext.Load += () =>
        {
            GLViewerControl.CheckOpenGL();
            Framebuffer = Framebuffer.Prepare(4, 4, 0, LDRFormat.Value, null);
            Framebuffer.Initialize();
            Framebuffer.CheckStatus_ThrowIfIncomplete(nameof(GLTextureDecoder));
            Framebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            Framebuffer.ClearColor = new OpenTK.Mathematics.Color4(0, 0, 255, 255);
        };

        GLWindowContext.RenderFrame += (e) =>
        {
            queueUpdateEvent.WaitOne();

            if (decodeQueue.TryDequeue(out var decodeRequest))
            {
                try
                {
                    var isDecoded = DecodeTexture(decodeRequest);
                    decodeRequest.MarkAsDone(isDecoded);
                }
                catch
                {
                    decodeRequest.MarkAsDone(false);
                    throw;
                }
            }
        };

        GLWindowContext.Closing += (e) =>
        {
            Framebuffer?.Dispose();
        };

        GLWindowContext.Run();
    }

    private bool DecodeTexture(DecodeRequest request)
    {
        var sw = Stopwatch.StartNew();
        var inputTexture = guiContext.MaterialLoader.LoadTexture(request.Resource, isViewerRequest: true);

        inputTexture.SetFiltering(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);

        /*
        if (request.Channels == ChannelMapping.RGBA && request.DecodeFlags == TextureCodec.None)
        {
            var texturePixels = request.Bitmap.GetPixels(out var texturePixelLength);
            GL.GetTextureImage(inputTexture.Handle, 0, PixelFormat.Bgra, PixelType.UnsignedByte, (int)texturePixelLength, texturePixels);
            request.DecodeTime = sw.Elapsed;
            return true;
        }
        */
        var framebufferFormat = request.Bitmap.ColorType switch
        {
            HdrBitmapColorType => HDRFormat.Value,
            DefaultBitmapColorType => LDRFormat.Value,
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

        // Render the texture at requested mip level size,
        // reading pixels back to the bitmap below will crop it
        var blockWidth = Math.Max(inputTexture.Width >> request.Mip, 1);
        var blockHeight = Math.Max(inputTexture.Height >> request.Mip, 1);

        if (Framebuffer.Width < blockWidth || Framebuffer.Height < blockHeight)
        {
            Framebuffer.Resize(blockWidth, blockHeight);
        }

        GL.Viewport(0, 0, blockWidth, blockHeight);
        Framebuffer.BindAndClear(FramebufferTarget.DrawFramebuffer);
        GL.DepthMask(false);
        GL.Disable(EnableCap.DepthTest);

        var textureType = GetTextureTypeDefine(inputTexture.Target);
        var shader = guiContext.ShaderLoader.LoadShader("vrf.texture_decode", new Dictionary<string, byte>
        {
            [textureType] = 1,
        });

        shader.Use();

        shader.SetTexture(0, "g_tInputTexture", inputTexture);
        shader.SetUniform2("g_vViewportSize", new Vector2(blockWidth, blockHeight));
        shader.SetUniform4("g_vInputTextureSize", new Vector4(
            blockWidth, blockHeight, inputTexture.Depth, inputTexture.NumMipLevels
        ));
        shader.SetUniform1("g_nSelectedMip", request.Mip);
        shader.SetUniform1("g_nSelectedDepth", request.Depth);
        shader.SetUniform1("g_nSelectedCubeFace", (int)request.Face);
        shader.SetUniform1("g_nSelectedChannels", request.Channels.PackedValue);
        shader.SetUniform1("g_nDecodeFlags", (int)request.DecodeFlags);

        // full screen triangle
        GL.BindVertexArray(guiContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        inputTexture.Delete();

        var pixels = request.Bitmap.GetPixels(out var outputLength);
        var fbBytesPerPixel = Framebuffer.ColorFormat.PixelType == PixelType.Float ? 16 : 4;
        var fbRegionLength = request.Bitmap.Width * request.Bitmap.Height * fbBytesPerPixel;

        if (fbRegionLength > outputLength)
        {
            Log.Warn(nameof(GLTextureDecoder), $"Bitmap is too small to copy framebuffer contents to.");
            return false;
        }

        Framebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        GL.ReadnPixels(
            0, 0,
            request.Bitmap.Width, request.Bitmap.Height,
            Framebuffer.ColorFormat.PixelFormat, Framebuffer.ColorFormat.PixelType,
            (int)outputLength, pixels
        );

        request.DecodeTime = sw.Elapsed;
        return true;
    }

    private void Dispose_ThreadResources()
    {
        GLWindowContext?.Dispose();
    }

    private void Exit()
    {
        GLWindowContext.Close();  // signal the thread that it should exit
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
        TextureTarget.Texture2D => "S_TYPE_TEXTURE2D",
        TextureTarget.Texture3D => "S_TYPE_TEXTURE3D",
        TextureTarget.Texture2DArray => "S_TYPE_TEXTURE2DARRAY",
        TextureTarget.TextureCubeMap => "S_TYPE_TEXTURECUBEMAP",
        TextureTarget.TextureCubeMapArray => "S_TYPE_TEXTURECUBEMAPARRAY",
        _ => throw new UnexpectedMagicException("Unsupported texture type", (int)target, target.ToString())
    };
}
