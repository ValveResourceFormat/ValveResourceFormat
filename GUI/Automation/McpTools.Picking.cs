#if DEBUG
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using DepthRange = ValveResourceFormat.Renderer.Renderer.DepthRange;

namespace GUI.Automation;

/// <summary>Reading the picking buffer: what a pixel shows, and how far away it is.</summary>
internal sealed partial class McpTools
{
    /// <summary>
    /// One pixel of the picking buffer and what is needed to place it in the world, all read on the
    /// render thread in the frame that drew it.
    /// </summary>
    private sealed record PixelRead(
        int X,
        int Y,
        int Width,
        int Height,
        PickingTexture.PixelInfo Pixel,
        float Depth,
        Scene? Scene,
        SceneNode? Node,
        Vector3 CameraLocation,
        Matrix4x4 ViewProjection,
        Matrix4x4 SkyViewProjection,
        Matrix4x4 ViewmodelViewProjection);

    /// <summary>
    /// Renders the picking buffer for one pixel, the viewport centre when no coordinates are given, and
    /// reads it back. Unless <paramref name="viewerActs"/>, the viewer does not act on the pick the way
    /// it would on a click. <paramref name="onHit"/> runs on the render thread with the node hit.
    /// </summary>
    private static async Task<(PixelRead? Read, McpToolResult? Error)> ReadPixel(GLSceneViewer viewer, int? x, int? y,
        PickingTexture.PickingIntent intent, bool viewerActs, Action<SceneNode>? onHit, CancellationToken cancellationToken)
    {
        if (await CheckCanRender(cancellationToken).ConfigureAwait(false) is { } renderError)
        {
            return (null, McpToolResult.Error(renderError));
        }

        var (picker, width, height) = await OnUi(() =>
        {
            var picker = viewer.PickingTexture;
            return (picker, picker?.Width ?? 0, picker?.Height ?? 0);
        }, cancellationToken).ConfigureAwait(false);

        if (picker == null)
        {
            return (null, McpToolResult.Error("This viewer has no picker."));
        }

        var pixelX = x ?? width / 2;
        var pixelY = y ?? height / 2;

        if (pixelX < 0 || pixelX >= width || pixelY < 0 || pixelY >= height)
        {
            return (null, McpToolResult.Error($"Pixel ({pixelX}, {pixelY}) is outside the {width}x{height} render area. Coordinates run from the top left, x from 0 to {width - 1} and y from 0 to {height - 1}."));
        }

        var answered = new TaskCompletionSource<PixelRead>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPicked(object? sender, PickingTexture.PickingResponse response)
        {
            try
            {
                var pixel = response.PixelInfo;
                var renderer = viewer.Renderer;
                var scenes = renderer.Scenes;

                Scene? scene = null;
                SceneNode? node = null;

                // The viewer treats a set fourth channel as empty space too; translucent effects leave
                // garbage in the id otherwise.
                if (pixel.ObjectId != 0 && pixel.Unused2 == 0 && pixel.SceneIndex < scenes.Count)
                {
                    scene = scenes[(int)pixel.SceneIndex];
                    node = scene.Find(pixel.ObjectId);
                }

                if (node != null)
                {
                    onHit?.Invoke(node);
                }

                answered.TrySetResult(new PixelRead(pixelX, pixelY, width, height, pixel, ReadDepth(picker, pixelX, pixelY), scene, node,
                    renderer.Camera.Location, renderer.Camera.ViewProjectionMatrix, renderer.SkyCamera.ViewProjectionMatrix, renderer.ViewmodelCamera.ViewProjectionMatrix));
            }
            catch (Exception e)
            {
                answered.TrySetException(e);
            }
        }

        var ignoring = viewerActs ? null : await OnUi(viewer.IgnorePicks, cancellationToken).ConfigureAwait(false);

        picker.OnPicked += OnPicked;

        try
        {
            using var rendering = RenderLoopThread.BeginAutomationRendering();

            // The picker reads row Height - y from the bottom, one past the top row for y = 0.
            picker.RequestNextFrame(pixelX, pixelY + 1, intent);

            if (!await WaitOnRenderLoop(answered.Task, cancellationToken).ConfigureAwait(false))
            {
                return (null, McpToolResult.Error($"The picker did not answer within {FrameTimeout.TotalSeconds:F0}s."));
            }

            return (await answered.Task.ConfigureAwait(false), null);
        }
        finally
        {
            picker.OnPicked -= OnPicked;
            ignoring?.Dispose();
        }
    }

    /// <summary>The window depth the picking pass left at a pixel. Runs on the render thread, which has the context current.</summary>
    private static float ReadDepth(PickingTexture picker, int x, int y)
    {
        var previous = GL.GetInteger(GetPName.ReadFramebufferBinding);
        var depth = 0f;

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, picker.FboHandle);
        GL.ReadPixels(x, picker.Height - 1 - y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, ref depth);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous);

        return depth;
    }

    /// <summary>
    /// The world point a pixel's depth puts it at, where the main camera sees it. Each view draws into
    /// its own slice of the reverse-Z depth range, so the slice says which camera to unproject through.
    /// </summary>
    private static Vector3? UnprojectPixel(PixelRead read)
    {
        if (read.Depth <= 0f)
        {
            return null;
        }

        var inSkyView = read.Scene?.WorldGroup != null;

        var (range, viewProjection) = inSkyView
            ? (DepthRange.Sky, read.SkyViewProjection)
            : read.Depth > DepthRange.Scene.Far
                ? (DepthRange.Viewmodel, read.ViewmodelViewProjection)
                : (DepthRange.Scene, read.ViewProjection);

        if (!Matrix4x4.Invert(viewProjection, out var clipToWorld))
        {
            return null;
        }

        var clip = new Vector4(
            (read.X + 0.5f) / read.Width * 2f - 1f,
            1f - (read.Y + 0.5f) / read.Height * 2f,
            (read.Depth - range.Near) / (range.Far - range.Near),
            1f);

        var point = Vector4.Transform(clip, clipToWorld);

        if (MathF.Abs(point.W) < 1e-12f)
        {
            return null;
        }

        var position = new Vector3(point.X, point.Y, point.Z) / point.W;

        return inSkyView ? Vector3.Transform(position, read.Scene!.ToViewerWorld) : position;
    }

    /// <summary>What shows where no node was drawn: a sky, or only the plain background.</summary>
    private static string Background(GLSceneViewer viewer)
    {
        var renderer = viewer.Renderer;
        var hasSky = renderer.Scenes.Any(static scene => scene.Skybox2D != null)
            || (renderer.Skybox2D != null && renderer.Skybox2D != renderer.BaseBackground);

        return hasSky ? "sky" : "nothing";
    }

    private static JsonObject NoHit(GLSceneViewer viewer) => new()
    {
        ["hit"] = false,
        ["background"] = Background(viewer),
    };

    /// <summary>The mesh drawn at the pixel, by the mesh index the picking buffer holds.</summary>
    private static JsonObject? DescribeMesh(SceneNode node, uint meshId)
    {
        if (node is SceneAggregate.Fragment fragment)
        {
            var material = fragment.DrawCall.Material.Material;

            return new JsonObject
            {
                ["index"] = meshId,
                ["materials"] = new JsonArray(material.Name),
                ["shader"] = material.ShaderName,
            };
        }

        if (node is not MeshCollectionNode meshes)
        {
            return null;
        }

        var mesh = meshes.RenderableMeshes.FirstOrDefault(mesh => mesh.MeshIndex == meshId);

        if (mesh == null)
        {
            return null;
        }

        var result = new JsonObject
        {
            ["index"] = mesh.MeshIndex,
        };

        if (!string.IsNullOrEmpty(mesh.Name))
        {
            result["name"] = mesh.Name;
        }

        var materials = new JsonArray();
        var shaders = new JsonArray();

        foreach (var drawCall in mesh.DrawCalls.DistinctBy(static draw => draw.Material.Material.Name))
        {
            materials.Add(drawCall.Material.Material.Name);
            shaders.Add(drawCall.Material.Material.ShaderName);
        }

        result["materials"] = materials;
        result["shaders"] = shaders;

        return result;
    }

    /// <summary>The fields of a node the double click details window shows when the node has no entity.</summary>
    private static JsonObject DescribeDetails(SceneNode node)
    {
        static string ToRenderColor(Vector4 tint)
        {
            tint *= 255f;
            return FormattableString.Invariant($"{tint.X:F0} {tint.Y:F0} {tint.Z:F0}");
        }

        var details = new JsonObject();

        if (node is SceneAggregate.Fragment fragment)
        {
            var drawCall = fragment.DrawCall;
            var triangles = drawCall.IndexCount / 3;

            details["aggregate_model"] = fragment.Name;
            details["triangles"] = triangles;

            if (drawCall.NumMeshlets > 0)
            {
                details["clusters"] = drawCall.NumMeshlets;
            }

            details["model_tint"] = ToRenderColor(drawCall.TintColor);
            details["model_alpha"] = Math.Round(drawCall.TintColor.W, 6);

            if (fragment.TintAlpha != Vector4.One)
            {
                details["instance_tint"] = ToRenderColor(fragment.TintAlpha);
                details["final_tint"] = ToRenderColor(drawCall.TintColor * fragment.TintAlpha);
            }
        }
        else if (node is ModelSceneNode model)
        {
            details["model"] = model.Name;
            details["model_tint"] = ToRenderColor(model.TintAlpha);
            details["model_alpha"] = Math.Round(model.Alpha, 6);

            if (model.LightingOrigin is { } lightingOrigin)
            {
                details["lighting_origin"] = Round(lightingOrigin);
            }
        }

        if (node.CubeMapPrecomputedHandshake > 0)
        {
            details["cubemap_handshake"] = node.CubeMapPrecomputedHandshake;
        }

        if (node.LightProbeVolumePrecomputedHandshake > 0)
        {
            details["light_probe_handshake"] = node.LightProbeVolumePrecomputedHandshake;
        }

        details["flags"] = node.Flags.ToString();

        return details;
    }

    private async Task<McpToolResult> Pick(JsonObject args, CancellationToken cancellationToken)
    {
        var x = GetInt(args, "x");
        var y = GetInt(args, "y");

        if (x == null || y == null)
        {
            return MissingArgument("x and y", args);
        }

        var select = GetBool(args, "select") ?? false;
        var add = GetBool(args, "add") ?? false;
        var open = GetBool(args, "open") ?? false;

        if ((select ? 1 : 0) + (add ? 1 : 0) + (open ? 1 : 0) > 1)
        {
            return McpToolResult.Error("Pass at most one of 'select', 'add' and 'open'.");
        }

        var (viewer, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        if (open)
        {
            return await PickAndOpen(viewer!, x.Value, y.Value, cancellationToken).ConfigureAwait(false);
        }

        var (read, readError) = await ReadPixel(viewer!, x, y, PickingTexture.PickingIntent.Select,
            viewerActs: select, add ? viewer!.ToggleSelectedNode : null, cancellationToken).ConfigureAwait(false);

        if (readError != null)
        {
            return readError;
        }

        return await OnUi(() =>
        {
            var result = DescribePick(viewer!, read!);

            if (read!.Node != null && (select || add))
            {
                result["selected"] = read.Node.IsSelected;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject DescribePick(GLSceneViewer viewer, PixelRead read)
    {
        if (read.Node == null)
        {
            var miss = NoHit(viewer);

            if (read.Pixel.ObjectId != 0 && read.Pixel.Unused2 == 0)
            {
                miss["object_id"] = read.Pixel.ObjectId;
                miss["note"] = "Something was drawn here, but no scene node has its id.";
            }

            return miss;
        }

        var node = read.Node;

        var result = new JsonObject
        {
            ["hit"] = true,
            ["node"] = new NodeDescriber(viewer).Describe(node),
        };

        if (DescribeMesh(node, read.Pixel.MeshId) is { } mesh)
        {
            result["mesh"] = mesh;
        }

        result["details"] = DescribeDetails(node);

        if (node.EntityData != null && viewer is GLWorldViewer { LoadedWorld: { } world } worldViewer && EntityByData(worldViewer, world, node.EntityData) is { } entity)
        {
            result["entity"] = DescribeEntity(entity, SpawnedEntities(viewer).GetValueOrDefault(entity.Data));
        }

        return result;
    }

    /// <summary>Picks with the open intent, so the viewer opens what is under the pixel as Ctrl+double click does, and waits for the tab.</summary>
    private async Task<McpToolResult> PickAndOpen(GLSceneViewer viewer, int x, int y, CancellationToken cancellationToken)
    {
        if (viewer is not GLWorldViewer)
        {
            return McpToolResult.Error("Opening what is picked works in map tabs, as Ctrl+double click does there.");
        }

        var logCursor = AutomationLog.Cursor;
        var crash = UnhandledExceptions.NextAsync();
        var watcher = await OnUi(() => new NewTabWatcher(), cancellationToken).ConfigureAwait(false);

        try
        {
            var (read, readError) = await ReadPixel(viewer, x, y, PickingTexture.PickingIntent.Open, viewerActs: true, null, cancellationToken).ConfigureAwait(false);

            if (readError != null)
            {
                return readError;
            }

            // The viewer opens the tab from the same frame's pick event, so once the next frame is up it has.
            using (RenderLoopThread.BeginAutomationRendering())
            {
                await PresentFrames(1, cancellationToken).ConfigureAwait(false);
            }

            var (tab, picked) = await OnUi(() => (watcher.FindNewTab(), DescribePick(viewer, read!)), cancellationToken).ConfigureAwait(false);

            if (tab == null)
            {
                var what = read!.Node == null ? "Nothing is under that pixel" : $"Nothing was opened for {read.Node.Name ?? read.Node.GetType().Name}";

                return McpToolResult.Error($"{what}, so no tab was opened.{LoggedErrorsSince(logCursor)} The pick: {picked.ToJsonString(McpToolResult.SerializerOptions)}");
            }

            Exception? failure;

            try
            {
                var finished = await Task.WhenAny(watcher.Loaded, crash).WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false);

                if (finished == crash)
                {
                    return McpToolResult.Error($"Unhandled exception while loading the picked file: {UnhandledExceptions.Summarize(await crash.ConfigureAwait(false))}");
                }

                failure = await watcher.Loaded.ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return McpToolResult.Error($"Timed out after {LoadTimeout.TotalSeconds:F0}s loading the picked file.");
            }

            return await OnUi(() =>
            {
                if ((failure ?? ViewerFailure(tab)) is { } problem)
                {
                    return McpToolResult.Error($"Opened the picked file as tab {IdFor(tab)}, but it failed: {UnhandledExceptions.Summarize(problem.ToString())}");
                }

                var result = DescribeTab(tab);
                result["picked"] = picked;

                return McpToolResult.Json(result);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await OnUi(() =>
            {
                watcher.Dispose();
                return true;
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Notices the tab something opens and when it finishes loading, whichever order the two come in.
    /// Create, query and dispose it on the UI thread, which also delivers the load notifications.
    /// </summary>
    private sealed class NewTabWatcher : IDisposable
    {
        private readonly HashSet<TabPage> before = [];
        private readonly Dictionary<TabPage, Exception?> completed = [];
        private readonly TaskCompletionSource<Exception?> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TabPage? tab;

        public NewTabWatcher()
        {
            foreach (TabPage page in Program.MainForm.Tabs.TabPages)
            {
                before.Add(page);
            }

            Program.MainForm.TabLoadCompleted += OnTabLoaded;
        }

        public Task<Exception?> Loaded => loaded.Task;

        public TabPage? FindNewTab()
        {
            foreach (TabPage page in Program.MainForm.Tabs.TabPages)
            {
                if (before.Contains(page))
                {
                    continue;
                }

                tab = page;

                if (completed.TryGetValue(page, out var error))
                {
                    loaded.TrySetResult(error);
                }

                return page;
            }

            return null;
        }

        private void OnTabLoaded(TabPage page, Exception? error)
        {
            completed[page] = error;

            if (page == tab)
            {
                loaded.TrySetResult(error);
            }
        }

        public void Dispose() => Program.MainForm.TabLoadCompleted -= OnTabLoaded;
    }
}
#endif
