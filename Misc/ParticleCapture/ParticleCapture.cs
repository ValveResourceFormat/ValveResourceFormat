using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using Vector3 = System.Numerics.Vector3;

// Renders a particle system at fixed simulation times into PNG files and prints the state of every
// system in its tree, so effects can be compared between builds without the GUI.
//
// Usage: ParticleCapture <pak01_dir.vpk> <particles/x.vpcf> [--times 0.5,1,2] [--size 512] [--out dir]
//        [--fps 60] [--camera x,y,z] [--target x,y,z] [--cp index=x,y,z]...
internal static class ParticleCapture
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ParticleCapture <vpk> <particle path> [--times 0.5,1,2] [--size 512] [--out dir] [--fps 60] [--camera x,y,z] [--target x,y,z] [--cp i=x,y,z]");
            return 1;
        }

        var vpkPath = args[0];
        var particlePath = args[1];
        float[] times = [0.5f, 1f, 2f];
        var size = 512;
        var outDir = "capture";
        var fps = 60f;
        Vector3? camera = null;
        Vector3? target = null;
        var controlPoints = new List<(int Index, Vector3 Position)>();

        for (var i = 2; i < args.Length - 1; i += 2)
        {
            var value = args[i + 1];

            switch (args[i])
            {
                case "--times": times = [.. value.Split(',').Select(ParseFloat)]; break;
                case "--size": size = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "--out": outDir = value; break;
                case "--fps": fps = ParseFloat(value); break;
                case "--camera": camera = ParseVector(value); break;
                case "--target": target = ParseVector(value); break;
                case "--cp":
                    var parts = value.Split('=');
                    controlPoints.Add((int.Parse(parts[0], CultureInfo.InvariantCulture), ParseVector(parts[1])));
                    break;
                default: throw new ArgumentException($"Unknown option {args[i]}");
            }
        }

        Directory.CreateDirectory(outDir);

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddSimpleConsole(options => options.SingleLine = true));
        var logger = loggerFactory.CreateLogger("ParticleCapture");

        using var window = new NativeWindow(new()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            Flags = ContextFlags.ForwardCompatible | ContextFlags.Offscreen,
            Profile = ContextProfile.Core,
            StartVisible = false,
            ClientSize = new(size, size),
            Title = "Particle Capture",
        });

        window.MakeCurrent();

        using var package = new Package();
        package.Read(vpkPath);
        using var fileLoader = new GameFileLoader(package, vpkPath);
        using var rendererContext = new RendererContext(fileLoader, logger);

        rendererContext.Device.CreateContext().Begin();
        GLEnvironment.Initialize(logger);
        GLEnvironment.SetDefaultRenderState();
        rendererContext.TextureStreaming.Mode = TextureStreamingMode.Immediate;

        var renderer = new Renderer(rendererContext);
        var textRenderer = new TextRenderer(rendererContext, renderer.Camera);
        textRenderer.Load();

        renderer.Postprocess.Load(4);

        var framebuffer = Framebuffer.Prepare("MainFramebuffer", size, size, 4, ImageFormat.RGBA16161616F, ImageFormat.D32);
        framebuffer.Initialize();
        renderer.Initialize();
        renderer.MainFramebuffer = framebuffer;
        renderer.LoadRendererResources();

        LoadLighting(renderer);

        var resource = fileLoader.LoadFileCompiled(particlePath)
            ?? throw new FileNotFoundException($"Could not load {particlePath}");

        var node = new ParticleSceneNode(renderer.Scene, (ParticleSystem)resource.DataBlock!, null, preview: true)
        {
            // A capture is a single pass through the effect, not the looping preview
            Loop = false,
        };

        foreach (var (index, position) in controlPoints)
        {
            node.SetControlPoint(index, System.Numerics.Matrix4x4.CreateTranslation(position));
        }

        renderer.Scene.Add(node, true);
        renderer.Scene.Initialize();
        renderer.Camera.SetViewportSize(size, size);
        GL.Viewport(0, 0, size, size);

        var updateContext = new Scene.UpdateContext
        {
            Camera = renderer.Camera,
            TextRenderer = textRenderer,
            Timestep = 1f / fps,
        };

        var elapsed = 0f;
        var name = Path.GetFileNameWithoutExtension(particlePath);

        foreach (var time in times.Order())
        {
            while (elapsed + (0.5f / fps) < time)
            {
                renderer.Update(updateContext);
                elapsed += 1f / fps;
            }

            if (camera.HasValue)
            {
                renderer.Camera.SetLocation(camera.Value);
                renderer.Camera.LookAt(target ?? Vector3.Zero);
            }
            else
            {
                var bounds = node.BoundingBox;
                var extent = bounds.Size * 1.3f;
                renderer.Camera.SetFromQAngle(new Vector3(15f, 200f, 0f));
                renderer.Camera.FrameObject(bounds.Center, extent.X, extent.Z, extent.Y);
            }

            // A second update with no time passing refreshes buffers against the framed camera
            renderer.Update(updateContext with { Timestep = 0f });

            GL.ClearColor(0.12f, 0.12f, 0.14f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            renderer.Render(framebuffer);
            framebuffer.Bind(FramebufferTarget.ReadFramebuffer);
            Framebuffer.GLDefaultFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);
            renderer.PostprocessRender(framebuffer, Framebuffer.GLDefaultFramebuffer, flipY: false);
            GL.Finish();

            var file = Path.Combine(outDir, string.Create(CultureInfo.InvariantCulture, $"{name}_{time:0.00}s.png"));
            SavePixels(file, size);

            var bright = CountLitPixels(file);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"t={elapsed:0.00}s lit_pixels={bright} -> {file}"));
            PrintTree(node.ParticleSimulation, 1);
        }

        return 0;
    }

    private static void LoadLighting(Renderer renderer)
    {
        var cubemapPath = Path.Combine(AppContext.BaseDirectory, "industrial_sunset_puresky.vtex_c");

        if (!File.Exists(cubemapPath))
        {
            return;
        }

        using var cubemap = new Resource { FileName = "vrf_default_cubemap.vtex_c" };
        cubemap.Read(cubemapPath);
        Renderer.LoadDefaultLighting(renderer.Scene, cubemap);

        renderer.Scene.PostProcessInfo.AddPostProcessVolume(new ScenePostProcessVolume(renderer.Scene)
        {
            HasBloom = true,
            IsMaster = true,
            BloomSettings = new BloomSettings
            {
                BlendMode = BloomBlendType.BLOOM_BLEND_SCREEN,
                BloomStartValue = 1,
                ScreenBloomStrength = 0.584f,
                BloomThreshold = 1.972f,
                BloomThresholdWidth = 2.364f,
            },
        });
    }

    private static void PrintTree(ParticleSystemSimulation simulation, int depth)
    {
        Console.WriteLine($"{new string(' ', depth * 2)}{Path.GetFileName(simulation.Name)}: {simulation.Particles.Count} live");

        foreach (var child in simulation.Children)
        {
            PrintTree(child, depth + 1);
        }
    }

    private static void SavePixels(string file, int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Framebuffer.GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        GL.ReadPixels(0, 0, size, size, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());

        using var flipped = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Scale(1, -1, 0, size / 2f);
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        using var stream = File.Create(file);
        flipped.Encode(stream, SKEncodedImageFormat.Png, 100);
    }

    // Pixels noticeably brighter than the clear colour, a rough "did anything draw" measure
    private static int CountLitPixels(string file)
    {
        using var bitmap = SKBitmap.Decode(file);
        var lit = 0;

        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Red + pixel.Green + pixel.Blue > 3 * 60)
            {
                lit++;
            }
        }

        return lit;
    }

    private static float ParseFloat(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    private static Vector3 ParseVector(string value)
    {
        var parts = value.Split(',').Select(ParseFloat).ToArray();
        return new Vector3(parts[0], parts[1], parts[2]);
    }
}
