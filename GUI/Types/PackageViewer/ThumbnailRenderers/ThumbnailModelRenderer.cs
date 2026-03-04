using System.Diagnostics;
using System.Drawing;
using System.Threading;
using GUI.Utils;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailModelRenderer : IThumbnailRenderer
{
    public ResourceType RendererType => ResourceType.Model;

    private Renderer? SceneRenderer;
    private Framebuffer? framebuffer;
    private TextRenderer? textRenderer;
    private RendererContext? RendererContext;
    private NativeWindow? NativeWindow;
    private bool disposed;

    public ThumbnailSizes Size { get; private set; } = ThumbnailSizes.Big;

    public bool Loaded { get; private set; }

    public void Load(VrfGuiContext context)
    {
        Loaded = true;

        var nativeWindowSettings = new NativeWindowSettings()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            Vsync = VSyncMode.Adaptive,
            ClientSize = new((int)Size, (int)Size),
            WindowBorder = WindowBorder.Hidden,
            WindowState = WindowState.Normal,
            Title = "S2V Render Test",
            Flags = ContextFlags.ForwardCompatible,
            Profile = ContextProfile.Core,
            StartVisible = false,
        };

        NativeWindow = new NativeWindow(nativeWindowSettings);
        RendererContext = new RendererContext(context, VrfGuiContext.Logger)
        {
            FieldOfView = 75,
            MaxTextureSize = 1024,
        };

        NativeWindow.MakeCurrent();

        GLEnvironment.Initialize(RendererContext.Logger);
        GLEnvironment.SetDefaultRenderState();

        SceneRenderer = new Renderer(RendererContext);

        SceneRenderer.Camera.Pitch = float.DegreesToRadians(-20);
        SceneRenderer.Camera.Yaw = float.DegreesToRadians(225);

        RendererContext.Logger.LogInformation("Loading scene...");

        // Create TextRenderer (needed for Scene.Update)
        textRenderer = new TextRenderer(RendererContext, SceneRenderer.Camera);
        textRenderer.Load();

        SceneRenderer.Postprocess.Load(4);

        // Create framebuffer for rendering
        framebuffer = Framebuffer.Prepare("MainFramebuffer", 4, 4, 4,
            new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            Framebuffer.DepthAttachmentFormat.Depth32FStencil8);
        framebuffer.Initialize();

        SceneRenderer.Initialize();
        SceneRenderer.MainFramebuffer = framebuffer;

        SceneRenderer.LoadRendererResources();

        // Initialize scene (creates lighting buffers, octrees, etc.)
        SceneRenderer.Scene.Initialize();
    }

    public Bitmap? ReadPixelsToBitmap()
    {
        var currentSize = (int)Size;

        NativeWindow?.MakeCurrent();
        using var bitmap = new SkiaSharp.SKBitmap(currentSize, currentSize, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
        var pixels = bitmap.GetPixels(out var length);


        Framebuffer.GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        GL.ReadPixels(0, 0, currentSize, currentSize, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        // Flip y
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Scale(1, -1, 0, bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, new SkiaSharp.SKPoint());

        return bitmap.ToBitmap();
    }

    public void SetModel(Model model)
    {
        NativeWindow?.MakeCurrent();

        Debug.Assert(SceneRenderer != null);

        SceneRenderer.Scene.Clear();

        var modelSceneNode = new ModelSceneNode(SceneRenderer.Scene, model);
        SceneRenderer.Scene.Add(modelSceneNode, true);

        var bbox = modelSceneNode.BoundingBox;

        var center = bbox.Center;
        // add some padding
        var size = bbox.Size * 1.5f;
        var biggestSide = Math.Max(size.X, Math.Max(size.Y, size.Z));

        SceneRenderer.Camera.RecalculateDirectionVectors();
        SceneRenderer.Camera.FrameObject(bbox.Center, size.X, size.Z, size.Y);
    }

    public Bitmap? Render(PackageEntry entry, VrfGuiContext context, CancellationToken cancellationToken)
    {
        using var stream = GameFileLoader.GetPackageEntryStream(context.CurrentPackage!, entry);

        if (stream == null)
        {
            return null;
        }

        using var resource = new Resource { FileName = entry.GetFullPath() };
        resource.Read(stream);

        var model = (Model)resource.DataBlock!;
        SetModel(model);

        var size = (int)Size;

        NativeWindow?.Size = new OpenTK.Mathematics.Vector2i(size, size);
        GL.Viewport(0, 0, size, size);
        SceneRenderer?.Camera.SetViewportSize(size, size);
        framebuffer?.Resize(size, size);

        NativeWindow?.MakeCurrent();

        var updateContext = new Scene.UpdateContext
        {
            Camera = SceneRenderer!.Camera,
            TextRenderer = textRenderer!,
            Timestep = 0,
        };

        SceneRenderer.Update(updateContext);

        GL.ClearColor(Color.Green);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Debug.Assert(SceneRenderer != null, "SceneRenderer is not loaded.");
        Debug.Assert(framebuffer is not null, "Framebuffer is not created.");
        Debug.Assert(textRenderer != null, "TextRenderer is not created.");

        SceneRenderer.Render(framebuffer);
        framebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        Framebuffer.GLDefaultFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);
        SceneRenderer.PostprocessRender(framebuffer, Framebuffer.GLDefaultFramebuffer, flipY: false);

        textRenderer.Render(SceneRenderer.Camera);

        // no need for this since we just want the pixels into bitmap
        //NativeWindow.Context.SwapBuffers();

        GL.Flush();

        return ReadPixelsToBitmap();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            RendererContext?.Dispose();
            SceneRenderer?.Dispose();
            NativeWindow?.Dispose();
        }

        Loaded = false;
        disposed = true;
    }
};
