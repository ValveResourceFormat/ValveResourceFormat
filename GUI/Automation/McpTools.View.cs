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
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Automation;

/// <summary>Tools that look at and move around what a tab renders.</summary>
internal sealed partial class McpTools
{
    [Tool("Describe a tab without switching to it: its viewer and file, the tools that work on it, and for a 3D tab its camera, each scene with its node count and spawn group, and the map with its entity count.")]
    private async Task<object> GetInfo(TabPage tab, CancellationToken cancellationToken = default)
    {
        var (description, viewer) = await OnUi(() => (DescribeTab(tab), GLBaseControl.FindHostedIn(tab)), cancellationToken).ConfigureAwait(false);
        var toolNames = tools.Values.Where(tool => tool.Target == typeof(TabPage) || tool.Target?.IsInstanceOfType(viewer) == true).Select(static tool => tool.Name).ToList();

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
            Entities = scene is GLWorldViewer { LoadedWorld: not null } world ? Entities(world).Entities.Count : (int?)null,
        }, cancellationToken).ConfigureAwait(false);
    }

    [Tool("Capture what a tab renders: a 3D view as the window shows it, without the overlay text, or a whole texture, image or graph. Returns a downscaled JPEG to look at; 'path' also writes the full resolution PNG.")]
    private static async Task<object> Screenshot(
        GLBaseControl viewer,
        [Description("Absolute path of a .png file to write the full resolution capture to.")] string? path = null,
        [Description("Extra frames to render first, for auto exposure to settle.")][Range(0, 300)] int settleFrames = 2,
        [Description("On a model, mesh or material tab, draw only the object on a transparent background, as its image export does. The inline JPEG shows the transparency as black.")] bool transparent = false,
        CancellationToken cancellationToken = default)
    {
        if (path != null && !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'path' must be absolute, got '{path}'.");
        }

        // Frames still draw while minimized, but the window has no pixels to read back
        if (await OnUi(static () => Program.MainForm.WindowState == FormWindowState.Minimized, cancellationToken).ConfigureAwait(false))
        {
            throw new ToolException("The viewer window is minimized, so there is nothing to capture. Restore it and try again.");
        }

        await RenderFrames(viewer, 1 + settleFrames, cancellationToken).ConfigureAwait(false);

        // A single node viewer's own capture is its transparent image export
        using var bitmap = (viewer is GLSingleNodeViewer && !transparent ? viewer.ReadWindowPixels() : viewer.ReadPixelsToBitmap())
            ?? throw new ToolException("The viewer has nothing to capture yet.");

        if (path != null)
        {
            await File.WriteAllBytesAsync(path, TextureExtract.ToPngImage(bitmap), cancellationToken).ConfigureAwait(false);
        }

        // A full resolution frame is megabytes of base64, so the inline copy is smaller and lossy
        const int InlineWidth = 1600;
        using var inline = bitmap.Width > InlineWidth ? bitmap.Resize(new SKImageInfo(InlineWidth, bitmap.Height * InlineWidth / bitmap.Width), SKSamplingOptions.Default) : null;
        using var jpeg = (inline ?? bitmap).Encode(SKEncodedImageFormat.Jpeg, 85);

        return new ImageReply(new { bitmap.Width, bitmap.Height, Path = path }, jpeg.ToArray());
    }

    [Tool("Move the camera of a 3D tab at once, with no fly-in. Returns its position, angles, field of view, the direction it looks along, and the size of the render area that pick takes pixels in, which get_info also reports without moving it.", Redraws = true)]
    private static object Camera(
        GLSceneViewer viewer,
        [Description("World position to move to.")] Vector3? position = null,
        [Description("[pitch, yaw, roll] degrees to face, pitch positive downwards. Pitch is held within 90 degrees either way, and there is no roll.")] Vector3? angles = null,
        [Description("World point to face, instead of 'angles'.")] Vector3? lookAt = null,
        [Description("Field of view in degrees.")][Range(1d, 170d)] float? fov = null)
    {
        if (angles != null && lookAt != null)
        {
            throw new ArgumentException("Pass 'angles' or 'look_at', not both.");
        }

        if (position == null && angles == null && lookAt == null && fov == null)
        {
            throw new ArgumentException("Pass 'position', 'angles', 'look_at' or 'fov'. get_info reads the camera.");
        }

        var camera = viewer.Input.Camera;

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

        if (position != null || angles != null || lookAt != null)
        {
            viewer.Input.EndTransition();
        }

        if (fov is { } fieldOfView)
        {
            viewer.SetFieldOfView(fieldOfView);
        }

        return DescribeCamera(viewer);
    }

    private static object DescribeCamera(GLSceneViewer viewer)
    {
        var camera = viewer.Input.Camera;
        var angles = camera.GetQAngle();

        return new
        {
            Position = camera.Location,
            Angles = new Vector3(MathUtils.Wrap(angles.X, -180f, 180f), MathUtils.Wrap(angles.Y, -180f, 180f), MathUtils.Wrap(angles.Z, -180f, 180f)),
            Fov = camera.FieldOfView,
            Forward = EntityTransformHelper.EulerAnglesToForwardDirection(angles),
            Viewport = viewer.Picker is { } picker ? new[] { picker.Width, picker.Height } : null,
        };
    }

    [Tool("Render a tab at an exact pixel size, capped to the window, so screenshots from two builds can be compared. Pass no size to follow the window again.", Redraws = true)]
    private static object SetViewport(GLBaseControl viewer, [Description("Render width.")][Range(1, int.MaxValue)] int? width = null, [Description("Render height.")][Range(1, int.MaxValue)] int? height = null)
    {
        var control = viewer.GLControl ?? throw new ToolException("The viewer has no render area yet.");

        if ((width == null) != (height == null))
        {
            throw new ArgumentException("Pass both 'width' and 'height', or neither to follow the window.");
        }

        if (width is { } w && height is { } h)
        {
            var area = control.Parent!.ClientSize;

            if (area.Width == 0 || area.Height == 0)
            {
                throw new ToolException("The viewer window is minimized. Restore it and try again.");
            }

            control.Dock = DockStyle.None;
            control.Bounds = new Rectangle(0, 0, Math.Clamp(w, 1, area.Width), Math.Clamp(h, 1, area.Height));
        }
        else
        {
            control.Dock = DockStyle.Fill;
        }

        return new { control.Width, control.Height };
    }

    [Tool("Recompile shaders from the source tree and redraw, without restarting the viewer. A compile failure comes back as the compiler's error.", Redraws = true)]
    private static Task<object> ReloadShaders(
        GLBaseControl viewer,
        [Description("Only reload the shaders built from this file, such as complex.frag.slang. A file without a stage in its name is included by others, so it reloads every shader.")] string? name = null,
        CancellationToken cancellationToken = default)
    {
        if (name != null && !name.EndsWith(".slang", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'name' must be a shader source file such as complex.frag.slang, got '{name}'.");
        }

        if (name != null && ShaderParser.ShaderSourceDirectory is { } sources
            && !Directory.EnumerateFiles(sources, Path.GetFileName(name), SearchOption.AllDirectories).Any())
        {
            throw new ArgumentException($"There is no shader source file '{name}' in {sources}.");
        }

        // The reload rebuilds the render mode list, so it holds the UI thread for as long as it takes
        return OnUi<object>(() =>
        {
            try
            {
                viewer.ShaderHotReload.ReloadShaders(name);
            }
            catch (ValveResourceFormat.Renderer.Shaders.ShaderLoader.ShaderCompilerException e)
            {
                throw new ToolException(e.Message);
            }

            return new { Reloaded = name?.Count(static c => c == '.') > 1 ? name : "all" };
        }, cancellationToken, TimeSpan.FromMinutes(5));
    }

    [Tool("Draw counts and renderer metrics of a fresh frame of a 3D tab, the numbers behind the performance overlay. Zero counters are left out.")]
    private static async Task<object> GetRenderStats(GLSceneViewer viewer, CancellationToken cancellationToken = default)
    {
        viewer.CapturePerfStats = true;

        try
        {
            await RenderFrames(viewer, 2, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            viewer.CapturePerfStats = false;
        }

        return viewer.Renderer.PerfStats.Snapshot()
            .Where(static stat => stat.Value != 0 && double.IsFinite(stat.Value))
            .ToDictionary(static stat => JsonNamingPolicy.SnakeCaseLower.ConvertName(stat.Key), static stat => Math.Round(stat.Value, 3));
    }

    [Tool("Move the camera of a 3D tab to frame an entity, a node, a box, or with none of them the whole scene, landing there at once from where it has a clear view. With 'select', also selects the entity or node and turns on its layer and physics group, as double clicking it in the entity list does.", Redraws = true)]
    private async Task<object> Frame(
        GLSceneViewer viewer,
        [Description("Entity id from find_entities or pick.")] int? entity = null,
        [Description("Node id from pick or get_entity.")] int? node = null,
        [Description("One corner of a world box to frame, with 'max'.")] Vector3? min = null,
        [Description("The opposite corner of the box.")] Vector3? max = null,
        [Description("Select the entity or node.")] bool select = false,
        CancellationToken cancellationToken = default)
    {
        if ((entity != null ? 1 : 0) + (node != null ? 1 : 0) + (min != null || max != null ? 1 : 0) > 1)
        {
            throw new ArgumentException("Pass one of 'entity', 'node' or 'min' with 'max'.");
        }

        if ((min == null) != (max == null))
        {
            throw new ArgumentException("Pass both 'min' and 'max'.");
        }

        var (target, bounds) = await WithGl(viewer, () =>
        {
            var target = node is { } id ? NodeById(viewer, id) : null;
            AABB bounds;

            if (EntityData(viewer, entity) is { } data)
            {
                target = viewer.Renderer.FindNode(data);
                bounds = target == null ? ((GLWorldViewer)viewer).EntityOriginBounds(data) : GLSceneViewer.SelectionBounds(target);
            }
            else if (target != null)
            {
                bounds = GLSceneViewer.SelectionBounds(target);
            }
            else if (min is { } a && max is { } b)
            {
                bounds = new AABB(Vector3.Min(a, b), Vector3.Max(a, b));
            }
            else
            {
                bounds = viewer.Renderer.Scenes
                    .Where(static scene => scene.WorldGroup == null)
                    .SelectMany(static scene => scene.AllNodes
                        .Where(static node => node.LayerEnabled && node.Visible && node.BoundingBox.Size.MaxComponent() is > 0f and < 1e6f)
                        .Select(node => node.BoundingBox.Transform(scene.ToViewerWorld)))
                    .Aggregate((AABB?)null, static (all, nodeBounds) => all?.Union(nodeBounds) ?? nodeBounds)
                    ?? throw new ToolException("Nothing in the scene has bounds to frame.");
            }

            viewer.FocusCameraOnBounds(bounds);
            viewer.Input.EndTransition();

            if (select && target != null)
            {
                viewer.SelectedNodeRenderer?.SelectNode(target);
            }

            return (Selected: select ? target : null, Bounds: bounds);
        }, cancellationToken).ConfigureAwait(false);

        // Its layer and physics group are turned on through their lists in the sidebar
        if (target != null && viewer is GLWorldViewer world)
        {
            await OnUi(() => world.EnsureNodeVisible(target), cancellationToken).ConfigureAwait(false);
        }

        return await WithGl<object>(viewer, () => new
        {
            Camera = DescribeCamera(viewer),
            bounds.Center,
            bounds.Size,
            Selected = target == null ? null : DescribeNode(viewer, target),
        }, cancellationToken).ConfigureAwait(false);
    }
}
#endif
