#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using SkiaSharp;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that drive a loaded GL viewer.</summary>
internal sealed partial class McpTools
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

    // Reloading every shader takes a while, and it holds the UI thread the whole time because the
    // reload rebuilds the render mode list, so it does not get the usual marshal timeout.
    private static readonly TimeSpan ShaderReloadTimeout = TimeSpan.FromMinutes(5);

    // A full resolution frame is megabytes of base64, so the inline copy is downscaled and JPEG
    // encoded. The file written to 'path' stays full resolution and lossless.
    private const int MaxInlineWidth = 1600;
    private const int InlineJpegQuality = 85;

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);

        return data.ToArray();
    }

    private static SKBitmap Downscale(SKBitmap bitmap, int maxWidth)
    {
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * (double)maxWidth / bitmap.Width));

        return bitmap.Resize(new SKImageInfo(maxWidth, height), SKSamplingOptions.Default);
    }

    private void RegisterViewerTools()
    {
        Add("screenshot", "Capture what a rendered tab shows: a 3D view, texture, image or graph. Returns a downscaled JPEG to look at; pass 'path' to also get the full resolution PNG on disk. Selects the tab and renders a fresh frame first, so it works while the window is in the background. A 3D tab captures the frame as the window shows it; a texture or image tab captures the whole image at its selected mip, and a graph tab the whole graph, regardless of pan and zoom.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["settle_frames"] = Prop("integer", "Extra frames to render before capturing, for auto exposure to settle. Defaults to 2."),
                ["path"] = Prop("string", "Absolute path to write the full resolution PNG to. The inline image is a JPEG, so do not save that as .png."),
            }),
            Screenshot, AnyViewer);

        Add("clear_selection", "Drop the current selection of a 3D tab. The outline of a selected node, and the debug geometry of a selected light probe volume or envmap, stay in every screenshot until this is called.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            (args, ct) => WithViewer<GLSceneViewer>(args, viewer =>
            {
                viewer.ClearSelection();
                return McpToolResult.Json(new JsonObject());
            }, ct),
            SceneViewer);

        Add("get_camera", "Read the camera position, angles and field of view of a 3D tab.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            // The input camera is the authoritative one. Renderer.Camera is the interpolated view,
            // which lags behind it and does not update at all while the app is paused.
            (args, ct) => WithViewer<GLSceneViewer>(args, viewer => McpToolResult.Json(DescribeCamera(viewer.Input.Camera)), ct),
            SceneViewer);

        Add("set_camera", "Move the camera of a 3D tab instantly, with no fly-in transition. Takes the same shape get_camera returns.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["position"] = VectorProp("World position [x, y, z]."),
                ["angles"] = VectorProp("[pitch, yaw, roll] in degrees, pitch positive downwards. Defaults to the current pitch and yaw with no roll."),
                ["look_at"] = VectorProp("World point [x, y, z] to face, instead of 'angles'."),
                ["fov"] = Prop("number", "Field of view in degrees. Left alone when omitted."),
            }, "position"),
            SetCamera, SceneViewer);

        Add("list_layers", "List the world layers and physics groups of a map, each with whether it is drawn.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            (args, ct) => WithViewer<GLWorldViewer>(args, viewer =>
            {
                var result = new JsonObject
                {
                    ["layers"] = CheckedMap(viewer.GetWorldLayers()),
                };

                if (viewer.GetPhysicsGroups() is { Count: > 0 } groups)
                {
                    result["physics_groups"] = CheckedMap(groups);
                }

                return McpToolResult.Json(result);
            }, ct),
            WorldViewer);

        Add("set_layer", "Show or hide one world layer. The 3D sky scene follows.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Layer name from list_layers."),
                ["enabled"] = Prop("boolean", "Whether the layer should be drawn."),
            }, "name", "enabled"),
            (args, ct) => SetChecked(args, "layer", (viewer, name, enabled) => viewer.TrySetWorldLayer(name, enabled), ct),
            WorldViewer);

        Add("set_physics_group", "Show or hide one physics group, such as the collision hulls of triggers or player clips.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Physics group name from list_layers."),
                ["enabled"] = Prop("boolean", "Whether the group should be drawn."),
            }, "name", "enabled"),
            (args, ct) => SetChecked(args, "physics_group", (viewer, name, enabled) => viewer.TrySetPhysicsGroup(name, enabled), ct),
            WorldViewer);

        Add("list_render_modes", "List the debug render modes available for a 3D tab, and which one is active.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            (args, ct) => WithViewer<GLSceneViewer>(args, viewer =>
            {
                var modes = new JsonArray();

                foreach (var mode in viewer.AvailableRenderModes)
                {
                    modes.Add(mode);
                }

                var result = new JsonObject
                {
                    ["render_modes"] = modes,
                };

                if (viewer.CurrentRenderMode is { } current)
                {
                    result["render_mode"] = current;
                }

                return McpToolResult.Json(result);
            }, ct),
            SceneViewer);

        Add("set_render_mode", "Switch the debug render mode of a 3D tab, for isolating lighting, specular, overdraw and similar.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Render mode name from list_render_modes."),
            }, "name"),
            (args, ct) =>
            {
                var name = GetString(args, "name");

                if (name == null)
                {
                    return Task.FromResult(MissingArgument("name", args));
                }

                return WithViewer<GLSceneViewer>(args, viewer => viewer.TrySetRenderMode(name)
                    ? McpToolResult.Json(new JsonObject { ["render_mode"] = viewer.CurrentRenderMode })
                    : McpToolResult.Error($"No render mode named '{name}' is available here."), ct);
            },
            SceneViewer);

        Add("get_info", "Describe any tab: its viewer kind and file, and which of the tools that act on a tab work on it. A 3D tab adds its render mode and node counts, and a map its name, entity counts and 3D sky.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            GetInfo);

        Add("set_viewport", "Render at an exact pixel size regardless of the window size, so screenshots from two builds can be compared. Pass no size to go back to following the window.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["width"] = Prop("integer", "Render width. Omit along with height to follow the window again."),
                ["height"] = Prop("integer", "Render height."),
            }),
            SetViewport, AnyViewer);

        Add("reload_shaders", "Recompile shaders from the source tree and redraw, without restarting the viewer. A compile failure comes back as the compiler's own error text.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["name"] = Prop("string", "Only reload shaders derived from this file, for example complex.frag.slang. Omit to reload every shader, which is much slower."),
            }),
            ReloadShaders, ShaderViewer);

        Add("get_render_stats", "Per frame draw counts and renderer metrics of a fresh frame of a 3D tab, the numbers behind the performance overlay. Counters that are zero are left out.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            RenderStats, SceneViewer);
    }

    private static JsonObject CheckedMap(List<(string Name, bool Enabled)> items)
    {
        var map = new JsonObject();

        foreach (var (name, enabled) in items)
        {
            map[name] = enabled;
        }

        return map;
    }

    private Task<McpToolResult> SetChecked(JsonObject args, string kind, Func<GLWorldViewer, string, bool, bool> set, CancellationToken cancellationToken)
    {
        var name = GetString(args, "name");
        var enabled = GetBool(args, "enabled");

        if (name == null || enabled == null)
        {
            return Task.FromResult(MissingArgument("name and enabled", args));
        }

        return WithViewer<GLWorldViewer>(args, viewer => set(viewer, name, enabled.Value)
            ? McpToolResult.Json(new JsonObject { [kind] = name, ["enabled"] = enabled.Value })
            : McpToolResult.Error($"No {kind.Replace('_', ' ')} named '{name}'. Call list_layers for the names."), cancellationToken);
    }

    private async Task<McpToolResult> GetInfo(JsonObject args, CancellationToken cancellationToken)
    {
        var id = GetInt(args, "tab");

        return await OnUi(() =>
        {
            var page = id == null ? Program.MainForm.Tabs.SelectedTab : PageFor(id.Value);

            if (page == null)
            {
                return id == null ? McpToolResult.Error("No tab is open.") : NoSuchTab(id.Value);
            }

            var info = DescribeTab(page);
            var viewer = GLBaseControl.FindHostedIn(page);

            if (ToolsFor(viewer) is { } tools)
            {
                info["tools"] = tools;
            }

            if (viewer is not GLSceneViewer scene)
            {
                return McpToolResult.Json(info);
            }

            if (scene.CurrentRenderMode is { } renderMode)
            {
                info["render_mode"] = renderMode;
            }

            info["scene_nodes"] = scene.Scene.AllNodes.Count();

            if (scene.SkyboxScene is { } skyboxScene)
            {
                info["sky_nodes"] = skyboxScene.AllNodes.Count();
            }

            if (scene is GLWorldViewer { LoadedWorld: { } world })
            {
                info["map"] = world.MapName;
                info["entities"] = world.Entities.Count;

                if (world.Skybox3D is { } sky)
                {
                    info["sky_map"] = world.Entities
                        .FirstOrDefault(entity => entity.GetStringProperty("classname") == "skybox_reference")?
                        .GetStringProperty("targetmapname");
                    info["sky_entities"] = sky.Entities.Count;
                }
            }

            return McpToolResult.Json(info);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> ReloadShaders(JsonObject args, CancellationToken cancellationToken)
    {
        var (viewer, error) = await ActivateViewer<GLBaseControl>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        if (!viewer!.OffersShaderReload)
        {
            return McpToolResult.Error($"{viewer.GetType().Name} draws no shaders of its own, so there is nothing to reload.");
        }

        var name = GetString(args, "name");

        var failure = await OnUi(() =>
        {
            try
            {
                // Same path as the Reload shaders button and the file watcher, except a compile
                // failure is handed back to the caller rather than opening a modal error dialog.
                viewer!.ShaderHotReload.ReloadShaders(name);
                return null;
            }
            catch (ShaderLoader.ShaderCompilerException e)
            {
                return e.Message;
            }
        }, ShaderReloadTimeout, cancellationToken).ConfigureAwait(false);

        if (failure != null)
        {
            return McpToolResult.Error(failure);
        }

        using (RenderLoopThread.BeginAutomationRendering())
        {
            if (await PresentFrames(2, cancellationToken).ConfigureAwait(false) is { } frameError)
            {
                return McpToolResult.Error(frameError);
            }
        }

        return McpToolResult.Json(new JsonObject
        {
            ["reloaded"] = name ?? "all",
        });
    }

    private async Task<McpToolResult> SetViewport(JsonObject args, CancellationToken cancellationToken)
    {
        var width = GetInt(args, "width");
        var height = GetInt(args, "height");

        if ((width == null) != (height == null))
        {
            return McpToolResult.Error("Pass both 'width' and 'height', or neither to follow the window.");
        }

        if (width is < 1 or > 8192 || height is < 1 or > 8192)
        {
            return McpToolResult.Error("Width and height must be between 1 and 8192.");
        }

        var (viewer, error) = await ActivateViewer<GLBaseControl>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        if (width == null || height == null)
        {
            viewer!.RestoreViewportSize();
        }
        else
        {
            viewer!.SetViewportSize(width.Value, height.Value);
        }

        using (RenderLoopThread.BeginAutomationRendering())
        {
            if (await PresentFrames(2, cancellationToken).ConfigureAwait(false) is { } frameError)
            {
                return McpToolResult.Error(frameError);
            }
        }

        var (actualWidth, actualHeight) = viewer.ViewportSize;

        var result = new JsonObject
        {
            ["width"] = actualWidth,
            ["height"] = actualHeight,
        };

        if (width != null && height != null && (actualWidth != width || actualHeight != height))
        {
            // The frame is read back out of the window's own framebuffer, so a request larger than
            // the window would read past the pixels that exist and come back part black.
            result["note"] = $"Capped to the window, {width}x{height} does not fit.";
        }

        return McpToolResult.Json(result);
    }

    private async Task<McpToolResult> RenderStats(JsonObject args, CancellationToken cancellationToken)
    {
        var (scene, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var previous = await OnUi(() =>
        {
            var current = scene!.PerfDisplayMode;
            scene.PerfDisplayMode = GLSceneViewer.PerfDisplayStats;
            return current;
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            using (RenderLoopThread.BeginAutomationRendering())
            {
                // Counters are filled during a frame, so one has to happen with capture turned on.
                if (await PresentFrames(2, cancellationToken).ConfigureAwait(false) is { } frameError)
                {
                    return McpToolResult.Error(frameError);
                }
            }

            var stats = new JsonObject();

            foreach (var (name, value) in scene!.Renderer.PerfStats.Snapshot())
            {
                if (value != 0)
                {
                    stats[JsonNamingPolicy.SnakeCaseLower.ConvertName(name)] = Math.Round(value, 3);
                }
            }

            return McpToolResult.Json(stats);
        }
        finally
        {
            await OnUi(() => scene!.PerfDisplayMode = previous, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<McpToolResult> SetCamera(JsonObject args, CancellationToken cancellationToken)
    {
        var position = GetVector(args, "position");

        if (position == null)
        {
            return MissingArgument("position", args);
        }

        var angles = GetVector(args, "angles");
        var lookAt = GetVector(args, "look_at");
        var fov = GetFloat(args, "fov");

        if (angles != null && lookAt != null)
        {
            return McpToolResult.Error("Pass either 'angles' or 'look_at', not both.");
        }

        return await WithViewer<GLSceneViewer>(args, viewer =>
        {
            var camera = viewer.Input.Camera;

            if (lookAt != null)
            {
                var direction = lookAt.Value - position.Value;

                if (direction.LengthSquared() < 1e-6f)
                {
                    return McpToolResult.Error("'look_at' is the same point as 'position'.");
                }

                angles = EntityTransformHelper.ForwardDirectionToEulerAngles(Vector3.Normalize(direction));
            }
            else if (angles == null)
            {
                var current = camera.GetQAngle();
                angles = current with { Z = 0f };
            }

            viewer.Input.SetCameraImmediate(position.Value, angles.Value);

            if (fov != null)
            {
                camera.FieldOfView = fov.Value;
                camera.CreateProjectionMatrix();
            }

            return McpToolResult.Json(DescribeCamera(camera));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread with the tab's viewer, if it is a <typeparamref name="T"/>.</summary>
    private async Task<McpToolResult> WithViewer<T>(JsonObject args, Func<T, McpToolResult> action, CancellationToken cancellationToken)
        where T : GLBaseControl
    {
        var (viewer, error) = await ActivateViewer<T>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        return await OnUi(() => action(viewer!), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the viewer of the tab the arguments name, or of the active tab, and makes that tab
    /// active because only the active control renders. A tab that is still loading is waited for.
    /// When it is not a <typeparamref name="T"/>, the error names the tab that was looked at, so a
    /// caller that relied on the active tab can tell it was not the one it meant.
    /// </summary>
    private async Task<(T? Viewer, McpToolResult? Error)> ActivateViewer<T>(JsonObject args, CancellationToken cancellationToken)
        where T : GLBaseControl
    {
        var id = GetInt(args, "tab");

        while (true)
        {
            var (viewer, error, loading, tab) = await OnUi(() => TryActivateViewer<T>(id), cancellationToken).ConfigureAwait(false);

            if (loading == null)
            {
                return (viewer, error);
            }

            try
            {
                await loading.WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return (null, McpToolResult.Error($"{tab} is still loading after {LoadTimeout.TotalSeconds:F0}s."));
            }
        }
    }

    /// <summary>The UI thread half of <see cref="ActivateViewer{T}"/>, which hands back the load to wait for instead of waiting.</summary>
    private (T? Viewer, McpToolResult? Error, Task? Loading, string Tab) TryActivateViewer<T>(int? id)
        where T : GLBaseControl
    {
        var form = Program.MainForm;
        var page = id == null ? form.Tabs.SelectedTab : PageFor(id.Value);

        if (page == null)
        {
            return (null, id == null ? McpToolResult.Error("No tab is open.") : NoSuchTab(id.Value), null, string.Empty);
        }

        var tab = $"Tab {IdFor(page)} '{page.Text}'";

        if (TabLoads.Of(page) is { } loading)
        {
            return (null, null, loading, tab);
        }

        var hosted = GLBaseControl.FindHostedIn(page);

        if (hosted is not T viewer)
        {
            var needs = typeof(T) == typeof(GLWorldViewer) ? "has no map loaded"
                : typeof(T) == typeof(GLSceneViewer) ? "has no 3D scene"
                : "has no rendered view";

            var kind = DescribeViewer(page) is { } described ? $" ({described})" : string.Empty;
            var how = id == null ? " No 'tab' was given, so the active tab was used." : string.Empty;
            var instead = ToolsFor(hosted) is { } tools
                ? $" Tools that work on it: {string.Join(", ", tools.Select(tool => (string?)tool))}."
                : string.Empty;

            return (null, McpToolResult.Error($"{tab}{kind} {needs}.{how}{instead}"), null, tab);
        }

        if (form.Tabs.SelectedTab != page)
        {
            form.Tabs.SelectTab(page);
        }

        // Minimizing takes the control off the render loop and only a repaint puts it back.
        viewer.EnsureAttachedToRenderLoop();

        return (viewer, null, null, tab);
    }

    /// <summary>A minimized window draws nothing at all, so say so rather than sitting out the frame timeout.</summary>
    private static async Task<string?> CheckCanRender(CancellationToken cancellationToken)
    {
        return await OnUi(() => Program.MainForm.WindowState == FormWindowState.Minimized, cancellationToken).ConfigureAwait(false)
            ? "The viewer window is minimized, which stops rendering. Restore it and try again."
            : null;
    }

    /// <summary>
    /// Renders <paramref name="count"/> frames, or explains why it could not. Callers must already
    /// hold a scope from <see cref="RenderLoopThread.BeginAutomationRendering"/>.
    /// </summary>
    private static async Task<string?> PresentFrames(int count, CancellationToken cancellationToken)
    {
        if (await CheckCanRender(cancellationToken).ConfigureAwait(false) is { } error)
        {
            return error;
        }

        return RenderLoopThread.WaitForFrames(count, FrameTimeout)
            ? null
            : $"Timed out waiting for a rendered frame after {FrameTimeout.TotalSeconds:F0}s.";
    }

    /// <summary>
    /// Waits for something the render loop completes, waking the loop again whenever it goes quiet.
    /// Returns false after the timeout, <see cref="FrameTimeout"/> unless given.
    /// </summary>
    private static Task<bool> WaitOnRenderLoop(Task task, CancellationToken cancellationToken)
        => WaitOnRenderLoop(task, FrameTimeout, cancellationToken);

    private static async Task<bool> WaitOnRenderLoop(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var crash = UnhandledExceptions.NextAsync();

        while (true)
        {
            try
            {
                var finished = await Task.WhenAny(task, crash).WaitAsync(RenderLoopThread.NudgeInterval, cancellationToken).ConfigureAwait(false);

                if (finished == crash)
                {
                    throw new InvalidOperationException($"Unhandled exception while rendering: {UnhandledExceptions.Summarize(await crash.ConfigureAwait(false))}");
                }

                await task.ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                if (Stopwatch.GetElapsedTime(started) >= timeout)
                {
                    return false;
                }

                RenderLoopThread.Nudge();
            }
        }
    }

    private async Task<McpToolResult> Screenshot(JsonObject args, CancellationToken cancellationToken)
    {
        var (viewer, error) = await ActivateViewer<GLBaseControl>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var settle = Math.Clamp(GetInt(args, "settle_frames") ?? 2, 1, 120);
        var path = GetString(args, "path");

        byte[]? full;
        byte[] inline;
        int width;
        int height;

        SKBitmap? bitmap;

        // The rendering scope keeps the render loop awake while the app is in the background, so it
        // covers only the frames and the read back, not the encoding that follows.
        using (RenderLoopThread.BeginAutomationRendering())
        {
            // One frame to let the renderer ask for the textures this view needs, then drain the
            // streamer, then render again so the capture shows full resolution mips rather than
            // whatever the background app had got through.
            if (await PresentFrames(1, cancellationToken).ConfigureAwait(false) is { } frameError)
            {
                return McpToolResult.Error(frameError);
            }

            viewer!.FinishTextureStreaming(cancellationToken);

            if (await PresentFrames(settle, cancellationToken).ConfigureAwait(false) is { } settleError)
            {
                return McpToolResult.Error(settleError);
            }

            bitmap = viewer.CaptureBitmap();
        }

        if (bitmap == null)
        {
            return McpToolResult.Error("The viewer has no framebuffer to read yet.");
        }

        using (bitmap)
        {
            width = bitmap.Width;
            height = bitmap.Height;

            // Only encoded when it is going to be written; it is a lossless full resolution frame.
            full = string.IsNullOrEmpty(path) ? null : TextureExtract.ToPngImage(bitmap);

            using var downscaled = width > MaxInlineWidth ? Downscale(bitmap, MaxInlineWidth) : null;

            inline = Encode(downscaled ?? bitmap, SKEncodedImageFormat.Jpeg, InlineJpegQuality);
        }

        var result = new JsonObject
        {
            ["width"] = width,
            ["height"] = height,
        };

        if (full != null)
        {
            await File.WriteAllBytesAsync(path!, full, cancellationToken).ConfigureAwait(false);

            result["path"] = path;
        }

        return McpToolResult.Json(result).WithImage(inline, "image/jpeg");
    }
}
#endif
