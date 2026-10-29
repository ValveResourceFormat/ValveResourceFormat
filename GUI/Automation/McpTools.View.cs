#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using SkiaSharp;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace GUI.Automation;

/// <summary>Tools that look at and move around what a tab renders.</summary>
internal sealed partial class McpTools
{
    [Tool("Describe a tab without selecting it: its viewer, file and the tools that work on it. For a 3D tab, it also gives the camera, each scene's node count and spawn group, and the map with its entity count.")]
    private async Task<object> GetInfo(TabPage tab, CancellationToken cancellationToken = default)
    {
        var (description, viewer) = await OnUi(() => (DescribeTab(tab), GLBaseControl.FindHostedIn(tab)), cancellationToken).ConfigureAwait(false);
        var toolNames = Tools.Values.Where(tool => tool.Target == typeof(TabPage) || tool.Target?.IsInstanceOfType(viewer) == true).Select(static tool => tool.Name).ToList();

        if (viewer is not GLSceneViewer scene)
        {
            return new { Tab = description, Tools = toolNames };
        }

        return await WithGl<object>(scene, () => new
        {
            Tab = description,
            Tools = toolNames,
            Camera = DescribeCamera(scene),
            Scenes = scene.Renderer.Scenes.Select(s => new
            {
                Nodes = s.AllNodes.Count(),
                SpawnGroup = SpawnGroupOf(scene, s)?.MapName,
                Sky = Flag(s.WorldGroup != null),
            }).ToList(),
            Map = (scene as GLWorldViewer)?.LoadedWorld?.MapName,
            Entities = MapEntities(scene)?.Entities.Count,
        }, cancellationToken).ConfigureAwait(false);
    }

    [Tool("Capture what a tab renders: a 3D view as the window shows it without the overlay text, or a whole texture, image or graph. Returns a downscaled JPEG. 'path' also writes the full resolution PNG.")]
    private static async Task<object> Screenshot(
        GLBaseControl viewer,
        [Description("Absolute path of a .png file for the full resolution capture.")] string? path = null,
        [Description("Extra frames to render first, so auto exposure can settle.")][Range(0, 300)] int settleFrames = 2,
        [Description("On a model, mesh or material tab, draw only the object on a transparent background, as image export does. The inline JPEG shows transparency as black.")] bool transparent = false,
        [Description("Crop to [x, y, width, height] in render pixels from the top left.")] int[]? region = null,
        [Description("Factor that shrinks the capture after any 'region', for both the PNG and the inline JPEG.")][Range(0.01d, 1d)] float scale = 1f,
        [Description("On a 3D tab, skip post processing as CS2 does with r_csgo_postprocess_enable 0: the output is sRGB(saturate(linear color * exposure)), with the exposure that get_render_stats reports and without exposure bias, bloom, tonemapping, color correction, gamma or dither. Depth of field still applies when it is on. The window shows the normal look again afterwards.")] bool raw = false,
        CancellationToken cancellationToken = default)
    {
        RequireAbsolute(path, nameof(path));

        var postprocess = (viewer as GLSceneViewer)?.Renderer.Postprocess;

        if (raw && postprocess == null)
        {
            throw new ArgumentException("'raw' needs a 3D tab, which has post processing.");
        }

        // The captured frames load every texture first, as streaming would given enough time
        var streaming = viewer.RendererContext.TextureStreaming;
        var streamingMode = streaming.Mode;
        streaming.Mode = TextureStreamingMode.Immediate;
        SKBitmap? frame;

        try
        {
            postprocess?.RawOutput = raw;
            await RenderFrames(viewer, 1 + settleFrames, cancellationToken, readsPixels: true).ConfigureAwait(false);

            // A single node viewer's own capture is its transparent image export
            frame = viewer is GLSingleNodeViewer && !transparent ? viewer.ReadWindowPixels() : viewer.CaptureFrame();
        }
        finally
        {
            streaming.Mode = streamingMode;
            postprocess?.RawOutput = false;
        }

        // The window would otherwise keep showing the raw frame while it is in the background
        if (raw)
        {
            await RenderLoopThread.DrawFrames(viewer, 1, FrameTimeout, cancellationToken).ConfigureAwait(false);
        }

        using var captured = frame ?? throw new ToolException("The viewer has nothing to capture yet.");
        using var cropped = region == null ? null : Crop(captured, region);
        using var scaled = scale < 1f ? Shrink(cropped ?? captured, scale) : null;
        var bitmap = scaled ?? cropped ?? captured;

        if (path != null)
        {
            await File.WriteAllBytesAsync(path, TextureExtract.ToPngImage(bitmap), cancellationToken).ConfigureAwait(false);
        }

        // A full resolution frame is megabytes of base64, so the inline copy is smaller and lossy
        const int InlineWidth = 1600;
        using var inline = bitmap.Width > InlineWidth ? Shrink(bitmap, (float)InlineWidth / bitmap.Width) : null;
        using var jpeg = (inline ?? bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);

        return new ImageReply(new { bitmap.Width, bitmap.Height, Path = path }, jpeg.ToArray());
    }

    [Tool("Compare two image files pixel by pixel, such as screenshots saved with 'path'. Returns how many pixels differ by more than 'threshold' in any channel. It also returns the largest and mean difference, and the box around the differing pixels. "
        + "'regions' also measures named boxes in both images, such as the sky of a game frame and of a viewer frame.", Concurrent = true)]
    private static object CompareScreenshots(
        [Description("Absolute path of the first image.")] string a,
        [Description("Absolute path of the second image, which must have the same size unless 'resize' is true.")] string b,
        [Description("Largest difference in a channel, from 0 to 255, that still counts as equal.")][Range(0, 255)] int threshold = 0,
        [Description("Absolute path of a .png file that shows the differing pixels in red over a dimmed copy of the first image.")] string? diff = null,
        [Description("Named boxes [x, y, width, height] in pixels of 'a' from the top left, such as {\"sky\": [0, 0, 200, 100]}. Each returns the mean sRGB color (0 to 255) and mean linear color (0 to 1) of both images, and the ratio b/a of each linear channel and of the linear luminance.")] Dictionary<string, int[]>? regions = null,
        [Description("Scale 'b' to the size of 'a' before comparing, for images of different sizes, such as a game capture and a viewer capture.")] bool resize = false,
        [Description("Absolute path of a .png file with 'a', 'b' and the diff image side by side.")] string? sideBySide = null)
    {
        RequireAbsolute(diff, nameof(diff));
        RequireAbsolute(sideBySide, nameof(sideBySide));

        using var first = LoadImage(a, nameof(a));
        using var loadedSecond = LoadImage(b, nameof(b));
        var sameSize = first.Width == loadedSecond.Width && first.Height == loadedSecond.Height;

        if (!sameSize && !resize)
        {
            throw new ToolException($"The images are different sizes: {first.Width}x{first.Height} and {loadedSecond.Width}x{loadedSecond.Height}. Pass 'resize' true to scale 'b' to the size of 'a'.");
        }

        using var resized = sameSize ? null : Resize(loadedSecond, first.Width, first.Height);
        var second = resized ?? loadedSecond;

        var (width, height) = (first.Width, first.Height);
        var measured = regions?.ToDictionary(static region => region.Key, region => MeasureRegion(first, second, region.Key, region.Value));
        var firstPixels = first.GetPixelSpan();
        var secondPixels = second.GetPixelSpan();
        using var marked = diff != null || sideBySide != null ? new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul)) : null;
        var markedPixels = marked != null ? marked.GetPixelSpan() : default;

        var differing = 0;
        var maxDelta = 0;
        var deltaSum = 0L;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var delta = 0;

                for (var channel = 0; channel < 4; channel++)
                {
                    delta = Math.Max(delta, Math.Abs(firstPixels[offset + channel] - secondPixels[offset + channel]));
                }

                deltaSum += delta;
                maxDelta = Math.Max(maxDelta, delta);

                var differs = delta > threshold;

                if (differs)
                {
                    differing++;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }

                if (marked != null)
                {
                    // Elsewhere the first image at a quarter of its brightness, so the red stands out
                    var gray = (byte)((firstPixels[offset] + firstPixels[offset + 1] + firstPixels[offset + 2]) / 12);
                    var color = differs ? SKColors.Red : new SKColor(gray, gray, gray);

                    markedPixels[offset] = color.Red;
                    markedPixels[offset + 1] = color.Green;
                    markedPixels[offset + 2] = color.Blue;
                    markedPixels[offset + 3] = 255;
                }
            }
        }

        if (diff != null)
        {
            File.WriteAllBytes(diff, TextureExtract.ToPngImage(marked!));
        }

        if (sideBySide != null)
        {
            WriteSideBySide(sideBySide, first, second, marked!);
        }

        var pixels = (double)width * height;

        return new
        {
            Width = width,
            Height = height,
            ResizedFrom = sameSize ? null : new[] { loadedSecond.Width, loadedSecond.Height },
            DifferingPixels = differing,
            DifferingPercent = Math.Round(differing * 100 / pixels, 3),
            MaxDelta = maxDelta,
            MeanDelta = Math.Round(deltaSum / pixels, 3),
            DifferingRegion = differing > 0 ? new[] { minX, minY, maxX - minX + 1, maxY - minY + 1 } : null,
            Regions = measured,
            Diff = diff,
            SideBySide = sideBySide,
        };
    }

    /// <summary>Writes three images of the same size next to each other as a PNG file.</summary>
    private static void WriteSideBySide(string path, SKBitmap left, SKBitmap middle, SKBitmap right)
    {
        var (width, height) = (left.Width, left.Height);
        using var combined = new SKBitmap(new SKImageInfo(width * 3, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(combined))
        {
            canvas.DrawBitmap(left, new SKPoint(0, 0), SKSamplingOptions.Default);
            canvas.DrawBitmap(middle, new SKPoint(width, 0), SKSamplingOptions.Default);
            canvas.DrawBitmap(right, new SKPoint(width * 2, 0), SKSamplingOptions.Default);
        }

        File.WriteAllBytes(path, TextureExtract.ToPngImage(combined));
    }

    /// <summary>The linear value of each 8 bit sRGB value.</summary>
    private static readonly float[] SrgbToLinear = [.. Enumerable.Range(0, 256).Select(static value => ColorSpace.SrgbGammaToLinear(new Vector3(value / 255f)).X)];

    /// <summary>The mean colors of a box in two images of the same size, and how the second compares to the first.</summary>
    private static object MeasureRegion(SKBitmap first, SKBitmap second, string name, int[] region)
    {
        var bounds = RegionBounds(region, first.Width, first.Height, $"regions.{name}");
        var (firstSrgb, firstLinear) = MeanColor(first, bounds);
        var (secondSrgb, secondLinear) = MeanColor(second, bounds);

        var ratio = new double?[3];

        for (var channel = 0; channel < 3; channel++)
        {
            ratio[channel] = Ratio(secondLinear[channel], firstLinear[channel]);
        }

        return new
        {
            A = new { Srgb = Rounded(firstSrgb, 2), Linear = Rounded(firstLinear, 5) },
            B = new { Srgb = Rounded(secondSrgb, 2), Linear = Rounded(secondLinear, 5) },
            Ratio = ratio,
            LuminanceRatio = Ratio(Luminance(secondLinear), Luminance(firstLinear)),
        };
    }

    /// <summary>The mean 8 bit sRGB color of a box, and the mean of its pixels converted to linear, as [r, g, b].</summary>
    private static (double[] Srgb, double[] Linear) MeanColor(SKBitmap bitmap, SKRectI bounds)
    {
        var pixels = bitmap.GetPixelSpan();
        var srgb = new double[3];
        var linear = new double[3];

        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                var offset = (y * bitmap.Width + x) * 4;

                for (var channel = 0; channel < 3; channel++)
                {
                    var value = pixels[offset + channel];
                    srgb[channel] += value;
                    linear[channel] += SrgbToLinear[value];
                }
            }
        }

        var count = (double)bounds.Width * bounds.Height;

        for (var channel = 0; channel < 3; channel++)
        {
            srgb[channel] /= count;
            linear[channel] /= count;
        }

        return (srgb, linear);
    }

    private static double Luminance(double[] linear)
        => MathUtils.LuminanceWeights.X * linear[0] + MathUtils.LuminanceWeights.Y * linear[1] + MathUtils.LuminanceWeights.Z * linear[2];

    /// <summary>The ratio of two values, or null when the divisor is zero.</summary>
    private static double? Ratio(double value, double divisor) => divisor > 0 ? Math.Round(value / divisor, 4) : null;

    private static double[] Rounded(double[] values, int digits) => [.. values.Select(value => Math.Round(value, digits))];

    /// <summary>Decodes an image file into 8 bit RGBA, so two of them compare channel by channel.</summary>
    private static SKBitmap LoadImage(string path, string argument)
    {
        RequireAbsolute(path, argument);

        using var decoded = SKBitmap.Decode(path) ?? throw new ToolException($"'{path}' is missing or not an image.");

        return decoded.Copy(SKColorType.Rgba8888) ?? throw new ToolException($"'{path}' could not be converted to RGBA.");
    }

    private static void RequireAbsolute(string? path, string argument)
    {
        if (path != null && !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{argument}' must be absolute, got '{path}'.");
        }
    }

    /// <summary>The box of an [x, y, width, height] argument, which must lie inside an image of the given size.</summary>
    private static SKRectI RegionBounds(int[] region, int width, int height, string argument)
    {
        if (region.Length != 4 || region[2] <= 0 || region[3] <= 0)
        {
            throw new ArgumentException($"'{argument}' must be [x, y, width, height] with a width and height more than zero.");
        }

        var bounds = SKRectI.Create(region[0], region[1], region[2], region[3]);

        if (!SKRectI.Create(width, height).Contains(bounds))
        {
            throw new ArgumentException($"'{argument}' [{string.Join(", ", region)}] is not inside the {width}x{height} image.");
        }

        return bounds;
    }

    private static SKBitmap Crop(SKBitmap bitmap, int[] region)
    {
        var bounds = RegionBounds(region, bitmap.Width, bitmap.Height, nameof(region));
        var subset = new SKBitmap();

        if (!bitmap.ExtractSubset(subset, bounds))
        {
            subset.Dispose();
            throw new ToolException("The capture could not be cropped.");
        }

        return subset;
    }

    private static SKBitmap Shrink(SKBitmap bitmap, float scale)
    {
        var width = Math.Max(1, (int)MathF.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(bitmap.Height * scale));

        return Resize(bitmap, width, height);
    }

    private static SKBitmap Resize(SKBitmap bitmap, int width, int height)
        => bitmap.Resize(bitmap.Info.WithSize(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));

    /// <summary>Cameras saved by name for the camera tool, with exact values instead of the rounded numbers in its replies.</summary>
    private readonly Dictionary<string, (Vector3 Location, Vector3 Angles, float FieldOfView)> SavedCameras = new(StringComparer.OrdinalIgnoreCase);

    [Tool("Move the camera of a 3D tab at once, without a fly-in, or save it by name. A saved camera restores exactly, in this tab or another. With no arguments, it only reads the camera. Returns the position, angles, field of view, forward direction, and the pixel size of the render area that pick reads.", Redraws = true)]
    private object Camera(
        GLSceneViewer viewer,
        [Description("World position to move to.")] Vector3? position = null,
        [Description("[pitch, yaw, roll] degrees to face, pitch positive downwards. Pitch stays within 90 degrees either way. Roll is always 0.")] Vector3? angles = null,
        [Description("World point to face, instead of 'angles'.")] Vector3? lookAt = null,
        [Description("Field of view in degrees.")][Range(1d, 170d)] float? fov = null,
        [Description("Name of a camera from 'save' to move to first, with its field of view. The other arguments then change it.")] string? restore = null,
        [Description("Name to save the camera under after the move. It replaces a camera saved with that name. Saved cameras stay until the viewer quits.")] string? save = null)
    {
        if (angles != null && lookAt != null)
        {
            throw new ArgumentException("Pass 'angles' or 'look_at', not both.");
        }

        var camera = viewer.Input.Camera;

        if (restore != null)
        {
            if (!SavedCameras.TryGetValue(restore, out var saved))
            {
                var names = SavedCameras.Count == 0 ? "No cameras are saved" : $"The saved cameras are {string.Join(", ", SavedCameras.Keys)}";
                throw new ToolException($"No camera is saved as '{restore}'. {names}.");
            }

            viewer.Input.SaveCameraForTransition(transitionDuration: 0f);
            camera.SetLocation(saved.Location);
            camera.SetFromQAngle(saved.Angles);
            viewer.SetFieldOfView(saved.FieldOfView);
        }

        var moves = position != null || angles != null || lookAt != null;

        if (moves)
        {
            viewer.Input.SaveCameraForTransition(transitionDuration: 0f);
        }

        if (position is { } location)
        {
            camera.SetLocation(location);
        }

        if (lookAt is { } target)
        {
            camera.LookAt(target);
        }
        else if (angles is { } facing)
        {
            camera.SetFromQAngle(new Vector3(MathUtils.Wrap(facing.X, -180f, 180f), facing.Y, 0f));
            camera.ClampRotation();
        }

        if (fov is { } fieldOfView)
        {
            viewer.SetFieldOfView(fieldOfView);
        }

        if (save != null)
        {
            SavedCameras[save] = (camera.Location, camera.GetQAngle(), camera.FieldOfView);
        }

        var described = DescribeCamera(viewer);

        // Reading or saving the camera leaves the view as it was
        if (restore == null && !moves && fov == null)
        {
            return new UnchangedReply(described);
        }

        return described;
    }

    private static object DescribeCamera(GLSceneViewer viewer)
    {
        var camera = viewer.Input.Camera;
        var angles = camera.GetQAngle();
        var picker = viewer.Picker;
        int[]? viewport = null;

        if (picker != null)
        {
            viewport = [picker.Texture.Width, picker.Texture.Height];
        }

        return new
        {
            Position = camera.Location,
            Angles = new Vector3(MathUtils.Wrap(angles.X, -180f, 180f), MathUtils.Wrap(angles.Y, -180f, 180f), MathUtils.Wrap(angles.Z, -180f, 180f)),
            Fov = camera.FieldOfView,
            Forward = EntityTransformHelper.EulerAnglesToForwardDirection(angles),
            Viewport = viewport,
        };
    }

    [Tool("Render a tab at an exact pixel size, capped to the window, so you can compare screenshots from two builds. Pass no size to follow the window again.", Redraws = true)]
    private static object SetViewport(GLBaseControl viewer, [Description("Render width.")][Range(1, int.MaxValue)] int? width = null, [Description("Render height.")][Range(1, int.MaxValue)] int? height = null)
    {
        var control = viewer.GLControl ?? throw new ToolException("The viewer has no render area yet.");

        if ((width == null) != (height == null))
        {
            throw new ArgumentException("Pass both 'width' and 'height', or neither to follow the window.");
        }

        if (width != null && height != null)
        {
            // A minimized window has no area to cap the size to
            RestoreMinimized();

            var area = control.Parent!.ClientSize;
            control.Dock = DockStyle.None;
            control.Bounds = new Rectangle(0, 0, Math.Clamp(width.Value, 1, area.Width), Math.Clamp(height.Value, 1, area.Height));
        }
        else
        {
            control.Dock = DockStyle.Fill;
        }

        return new { control.Width, control.Height };
    }

    [Tool("Recompile the shaders of every open tab from the source tree and redraw, without a viewer restart. A saved shader file already reloads the shown tab, and tabs opened later compile the saved sources. If that reload fails, the viewer opens a dialog. If this tool fails to compile or link a shader, it returns the error, and the viewer keeps drawing with the previous programs of the shaders that failed.", Redraws = true)]
    private static Task<object> ReloadShaders(
        [Description("Only reload the shaders built from this file, such as complex.frag.slang. A file without a stage in its name is an include, so it reloads every shader.")] string? name = null,
        CancellationToken cancellationToken = default)
    {
        if (name != null && ShaderParser.ShaderSourceDirectory is { } sources
            && !Directory.EnumerateFiles(sources, Path.GetFileName(name), SearchOption.AllDirectories).Any())
        {
            throw new ArgumentException($"No shader source file '{name}' in {sources}.");
        }

        // The reload rebuilds the render mode list, so it holds the UI thread for as long as it takes
        return OnUi<object>(() =>
        {
            var failures = new List<string>();

            foreach (TabPage page in Program.MainForm.Tabs.TabPages)
            {
                var viewer = GLBaseControl.FindHostedIn(page);

                if (viewer == null)
                {
                    continue;
                }

                try
                {
                    viewer.ShaderHotReload.ReloadShaders(name);
                }
                catch (ShaderLoader.ShaderCompilerException e)
                {
                    failures.Add($"{page.Text}: {e.Message}");
                }
            }

            if (failures.Count > 0)
            {
                throw new ToolException(string.Join("\n\n", failures));
            }

            return new { Reloaded = name ?? "all" };
        }, cancellationToken, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Whether the scene's own exposure or the Exposure slider applies, and the linear exposure scalar in
    /// use. A manual value is the slider's, which applies from the next frame on, and an auto value is
    /// the last drawn frame's.
    /// </summary>
    private static object DescribeExposure(GLSceneViewer viewer)
    {
        var postprocess = viewer.Renderer.Postprocess;
        var manual = postprocess.CustomExposure != -1;

        return new
        {
            Mode = manual ? "manual" : "auto",
            Value = Math.Round(manual ? postprocess.CustomExposure : postprocess.TonemapScalar, 4),
        };
    }

    [Tool("Render new frames of a 3D tab and return the numbers behind its performance overlays. Exposure is 'auto' while the scene sets it, or 'manual' with the Exposure slider, and its value is the linear scalar the frame is multiplied by before tonemapping. Counters are the last frame's draw counts and renderer metrics, without zeros. Timings are the CPU and GPU milliseconds of each timed render pass, averaged over the frames, in frame order with depth for nesting. GPU readings arrive a few frames late and not every frame. So each row counts its GPU readings, and a single frame may have none.")]
    private static async Task<object> GetRenderStats(
        GLSceneViewer viewer,
        [Description("Frames to average the timings over.")][Range(1, 600)] int frames = 1,
        CancellationToken cancellationToken = default)
    {
        var timings = viewer.Renderer.PerfStats.Timings;
        viewer.CaptureStats = true;

        try
        {
            // The first frames create the GPU queries, whose readings come back frames later
            await RenderFrames(viewer, 4, cancellationToken).ConfigureAwait(false);
            await WithGl(viewer, timings.StartAveraging, cancellationToken).ConfigureAwait(false);
            await RenderFrames(viewer, frames, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Averaging left running would go on summing every frame the timings overlay times
            await RestoreWithGl(viewer, () => timings.StopAveraging()).ConfigureAwait(false);
            throw;
        }
        finally
        {
            viewer.CaptureStats = false;
        }

        // Not cancellable, because averaging has to stop either way
        var rows = await WithGl(viewer, timings.StopAveraging, CancellationToken.None).ConfigureAwait(false);
        var averaged = timings.AveragedFrames;

        return new
        {
            Exposure = DescribeExposure(viewer),
            Counters = viewer.Renderer.PerfStats.Snapshot()
                .Where(static stat => stat.Value != 0 && double.IsFinite(stat.Value))
                .ToDictionary(static stat => JsonNamingPolicy.SnakeCaseLower.ConvertName(stat.Key), static stat => Math.Round(stat.Value, 3)),
            Frames = averaged,
            Timings = rows.Select(row => new
            {
                row.Name,
                Depth = NonZero(row.Depth),
                CpuMs = Math.Round(row.CpuMs, 3),
                GpuMs = row.GpuMs.HasValue ? Math.Round(row.GpuMs.Value, 3) : (double?)null,
                row.GpuSamples,
                CpuSamples = row.CpuSamples != averaged ? row.CpuSamples : (int?)null,
            }).ToList(),
        };
    }

    [Tool("Move the camera of a 3D tab at once to frame an entity, a node or a box, from a point with a clear view. With none of them, it frames the whole scene. 'select' also selects the entity or node and enables its layer and physics group, as a double click in the entity list does.", Redraws = true)]
    private async Task<object> Frame(
        GLSceneViewer viewer,
        [Description("Entity id from find_entities or pick.")] int? entity = null,
        [Description("Node id from pick or get_entity.")] int? node = null,
        [Description("One corner of a world box to frame, with 'max'.")] Vector3? min = null,
        [Description("Opposite corner of the box.")] Vector3? max = null,
        [Description("Select the entity or node.")] bool select = false,
        CancellationToken cancellationToken = default)
    {
        var hasBox = min != null || max != null;
        var given = 0;

        if (entity != null)
        {
            given++;
        }

        if (node != null)
        {
            given++;
        }

        if (hasBox)
        {
            given++;
        }

        if (given > 1)
        {
            throw new ArgumentException("Pass one of 'entity', 'node' or 'min' with 'max'.");
        }

        if ((min == null) != (max == null))
        {
            throw new ArgumentException("Pass both 'min' and 'max'.");
        }

        var (entities, data, target, bounds) = await WithGl(viewer, () =>
        {
            var mapEntities = MapEntities(viewer);
            var entityData = EntityData(mapEntities, entity);
            SceneNode? framed = null;
            AABB box;

            if (entityData != null)
            {
                framed = viewer.Renderer.FindNode(entityData);
                box = framed == null ? ((GLWorldViewer)viewer).EntityOriginBounds(entityData) : GLSceneViewer.SelectionBounds(framed);
            }
            else if (node != null)
            {
                framed = NodeById(viewer, node.Value);
                box = GLSceneViewer.SelectionBounds(framed);
            }
            else if (min != null && max != null)
            {
                box = new AABB(Vector3.Min(min.Value, max.Value), Vector3.Max(min.Value, max.Value));
            }
            else
            {
                box = SceneBounds(viewer) ?? throw new ToolException("Nothing in the scene has bounds to frame.");
            }

            return (mapEntities, entityData, framed, box);
        }, cancellationToken).ConfigureAwait(false);

        var selected = select ? target : null;
        var selectsInMap = false;

        // Select as the entity list of the map does, which enables the layer and physics group through their sidebar lists
        if (selected != null && viewer is GLWorldViewer world)
        {
            selectsInMap = true;

            await OnUi(() =>
            {
                if (data != null)
                {
                    world.SelectAndFocusEntity(data);
                }
                else
                {
                    world.SelectAndFocusNode(selected);
                }

                // Lands the camera at once instead of flying it in
                world.Input.SaveCameraForTransition(transitionDuration: 0f);
            }, cancellationToken).ConfigureAwait(false);
        }

        return await WithGl<object>(viewer, () =>
        {
            // The map's entity list has already focused the camera on what it selected
            if (!selectsInMap)
            {
                if (selected != null)
                {
                    viewer.Selection.Select(selected);
                }

                viewer.FocusCameraOnBounds(bounds);
                viewer.Input.SaveCameraForTransition(transitionDuration: 0f);
            }

            return new
            {
                Camera = DescribeCamera(viewer),
                bounds.Center,
                bounds.Size,
                Selected = selected == null ? null : DescribeNode(viewer, entities, selected),
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The box around every drawn node with sensible bounds, outside the 3D sky, or null for none.</summary>
    private static AABB? SceneBounds(GLSceneViewer viewer)
    {
        AABB? bounds = null;

        foreach (var scene in viewer.Renderer.Scenes)
        {
            if (scene.WorldGroup != null)
            {
                continue;
            }

            foreach (var node in scene.AllNodes)
            {
                var size = node.BoundingBox.Size.MaxComponent();

                if (!node.LayerEnabled || !node.Visible || size <= 0f || size >= 1e6f)
                {
                    continue;
                }

                var nodeBounds = node.BoundingBox.Transform(scene.ToViewerWorld);
                bounds = bounds == null ? nodeBounds : bounds.Value.Union(nodeBounds);
            }
        }

        return bounds;
    }
}
#endif
