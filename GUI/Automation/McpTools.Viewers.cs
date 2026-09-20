#if DEBUG
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using SkiaSharp;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that drive a loaded GL viewer.</summary>
internal sealed partial class McpTools
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

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
        Add("screenshot", "Capture the rendered frame of a tab. Returns a downscaled JPEG to look at; pass 'path' to also get the full resolution PNG on disk. Selects the tab and renders a fresh frame first, so it works while the window is in the background.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["settle_frames"] = Prop("integer", "Extra frames to render before capturing, for auto exposure to settle. Defaults to 2."),
                ["path"] = Prop("string", "Absolute path to write the full resolution PNG to. The inline image is a JPEG, so do not save that as .png."),
            }),
            Screenshot);

        Add("clear_selection", "Drop the current selection. A selected light probe volume or envmap keeps drawing its debug geometry over the scene until this is called.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
            }),
            (args, ct) => WithSceneViewer(args, viewer =>
            {
                viewer.ClearSelection();
                return McpToolResult.Ok();
            }, ct));

        Add("get_camera", "Read the camera position and angles of a tab.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
            }),
            (args, ct) => WithSceneViewer(args, viewer =>
            {
                // The input camera is the authoritative one. Renderer.Camera is the interpolated
                // view, which lags behind it and does not update at all while the app is paused.
                var camera = viewer.Input.Camera;
                var angles = camera.GetQAngle();

                return McpToolResult.Json(new JsonObject
                {
                    ["x"] = camera.Location.X,
                    ["y"] = camera.Location.Y,
                    ["z"] = camera.Location.Z,
                    ["pitch"] = angles.X,
                    ["yaw"] = angles.Y,
                    ["roll"] = angles.Z,
                    ["fov"] = camera.FieldOfView,
                });
            }, ct));

        Add("set_camera", "Move the camera instantly, with no fly-in transition. Pitch is positive downwards.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["x"] = Prop("number", "World X."),
                ["y"] = Prop("number", "World Y."),
                ["z"] = Prop("number", "World Z."),
                ["pitch"] = Prop("number", "Pitch in degrees, positive downwards. Defaults to the current pitch."),
                ["yaw"] = Prop("number", "Yaw in degrees. Defaults to the current yaw."),
                ["roll"] = Prop("number", "Roll in degrees. Defaults to 0."),
                ["fov"] = Prop("number", "Field of view in degrees. Left alone when omitted."),
            }, "x", "y", "z"),
            SetCamera);

        Add("list_layers", "List the world layers of a map and whether each is enabled.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
            }),
            (args, ct) => WithWorldViewer(args, viewer =>
            {
                var layers = new JsonArray();

                foreach (var (name, enabled) in viewer.GetWorldLayers())
                {
                    layers.Add(new JsonObject
                    {
                        ["name"] = name,
                        ["enabled"] = enabled,
                    });
                }

                return McpToolResult.Json(new JsonObject
                {
                    ["layers"] = layers,
                });
            }, ct));

        Add("set_layer", "Enable or disable one world layer. The 3D sky scene follows.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["name"] = Prop("string", "Layer name from list_layers."),
                ["enabled"] = Prop("boolean", "Whether the layer should be drawn."),
            }, "name", "enabled"),
            (args, ct) =>
            {
                var name = GetString(args, "name");
                var enabled = GetBool(args, "enabled");

                if (name == null || enabled == null)
                {
                    return Task.FromResult(MissingArgument("name and enabled", args));
                }

                return WithWorldViewer(args, viewer => viewer.TrySetWorldLayer(name, enabled.Value)
                    ? McpToolResult.Ok()
                    : McpToolResult.Error($"No world layer named '{name}'."), ct);
            });

        Add("list_render_modes", "List the debug render modes available for a tab, and which one is active.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
            }),
            (args, ct) => WithSceneViewer(args, viewer =>
            {
                var modes = new JsonArray();

                foreach (var mode in viewer.AvailableRenderModes)
                {
                    modes.Add(mode);
                }

                return McpToolResult.Json(new JsonObject
                {
                    ["render_modes"] = modes,
                    ["current"] = viewer.CurrentRenderMode,
                });
            }, ct));

        Add("set_render_mode", "Switch the debug render mode, for isolating lighting, specular, overdraw and similar.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["name"] = Prop("string", "Render mode name from list_render_modes."),
            }, "name"),
            (args, ct) =>
            {
                var name = GetString(args, "name");

                if (name == null)
                {
                    return Task.FromResult(MissingArgument("name", args));
                }

                return WithSceneViewer(args, viewer => viewer.TrySetRenderMode(name)
                    ? McpToolResult.Ok()
                    : McpToolResult.Error($"No render mode named '{name}' is available here."), ct);
            });

        Add("get_info", "Describe what a tab has loaded: viewer kind, file, map name and 3D sky presence.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
            }),
            (args, ct) => WithSceneViewer(args, viewer =>
            {
                var info = new JsonObject
                {
                    ["viewer"] = viewer.GetType().Name,
                    ["file"] = viewer.GuiContext.FileName,
                    ["render_mode"] = viewer.CurrentRenderMode,
                    ["scene_nodes"] = viewer.Scene.AllNodes.Count(),
                    ["has_3d_sky"] = viewer.SkyboxScene != null,
                };

                if (viewer is GLWorldViewer { LoadedWorld: { } world })
                {
                    info["map_name"] = world.MapName;
                    info["entity_count"] = world.Entities.Count;
                }

                return McpToolResult.Json(info);
            }, ct));

        Add("find_entities", "Search the map's entities by classname or targetname.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["classname"] = Prop("string", "Match entities whose classname contains this."),
                ["targetname"] = Prop("string", "Match entities whose targetname contains this."),
                ["limit"] = Prop("integer", "Most matches to return. Defaults to 50."),
            }),
            FindEntities);

        Add("select_entity", "Select an entity and move the camera to it, as double clicking it in the entity list does.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["id"] = Prop("integer", "Entity id from find_entities."),
                ["instant"] = Prop("boolean", "Skip the fly-in and jump straight there. Defaults to true."),
            }, "id"),
            SelectEntity);

        Add("pick", "Identify the scene node or entity under a viewport pixel, as clicking it would. Coordinates are from the top left of the render area.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["x"] = Prop("integer", "Pixel X in the render area."),
                ["y"] = Prop("integer", "Pixel Y in the render area."),
            }, "x", "y"),
            Pick);

        Add("set_viewport", "Render at an exact pixel size regardless of the window size, so screenshots from two builds can be compared. Pass no size to go back to following the window.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["width"] = Prop("integer", "Render width. Omit along with height to follow the window again."),
                ["height"] = Prop("integer", "Render height."),
            }),
            SetViewport);

        Add("reload_shaders", "Recompile shaders from the source tree and redraw, without restarting the viewer. A compile failure comes back as the compiler's own error text.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
                ["name"] = Prop("string", "Only reload shaders derived from this file, for example complex.frag.slang. Omit to reload every shader, which is much slower."),
            }),
            ReloadShaders);

        Add("get_render_stats", "Per frame draw counts and renderer metrics, the numbers behind the performance overlay.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs. Defaults to the active tab."),
            }),
            RenderStats);
    }

    private async Task<McpToolResult> Pick(JsonObject args, CancellationToken cancellationToken)
    {
        var x = GetInt(args, "x");
        var y = GetInt(args, "y");

        if (x == null || y == null)
        {
            return MissingArgument("x and y", args);
        }

        var viewer = await ActivateViewer(args, cancellationToken).ConfigureAwait(false);

        if (viewer is not GLSceneViewer scene)
        {
            return McpToolResult.Error("That tab has no 3D scene viewer.");
        }

        var picker = await OnUi(() => scene.PickingTexture, cancellationToken).ConfigureAwait(false);

        if (picker == null)
        {
            return McpToolResult.Error("This viewer has no picker.");
        }

        var answered = new TaskCompletionSource<PickingTexture.PixelInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPicked(object? sender, PickingTexture.PickingResponse response) => answered.TrySetResult(response.PixelInfo);

        picker.OnPicked += OnPicked;

        try
        {
            using var rendering = RenderLoopThread.BeginAutomationRendering();

            // Select intent leaves the entity info window closed; the answer arrives a frame later.
            picker.RequestNextFrame(x.Value, y.Value, PickingTexture.PickingIntent.Select);

            PickingTexture.PixelInfo pixel;

            try
            {
                pixel = await answered.Task.WaitAsync(FrameTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return McpToolResult.Error("The picker did not answer within the frame timeout.");
            }

            return await OnUi(() => DescribePick(scene, pixel), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            picker.OnPicked -= OnPicked;
        }
    }

    private static McpToolResult DescribePick(GLSceneViewer scene, PickingTexture.PixelInfo pixel)
    {
        if (pixel.ObjectId == 0)
        {
            return McpToolResult.Json(new JsonObject
            {
                ["hit"] = false,
            });
        }

        var inSkybox = pixel.IsSkybox > 0;
        var node = inSkybox ? scene.SkyboxScene?.Find(pixel.ObjectId) : scene.Scene.Find(pixel.ObjectId);

        var result = new JsonObject
        {
            ["hit"] = true,
            ["object_id"] = pixel.ObjectId,
            ["mesh_id"] = pixel.MeshId,
            ["in_3d_sky"] = inSkybox,
        };

        if (node == null)
        {
            return McpToolResult.Json(result);
        }

        result["node"] = node.GetType().Name;
        result["name"] = node.Name;
        result["layer"] = node.LayerName;
        result["x"] = node.BoundingBox.Center.X;
        result["y"] = node.BoundingBox.Center.Y;
        result["z"] = node.BoundingBox.Center.Z;

        if (node.EntityData != null)
        {
            result["classname"] = node.EntityData.GetStringProperty("classname");
            result["targetname"] = node.EntityData.TargetName;
        }

        return McpToolResult.Json(result);
    }

#if DEBUG
    // Reloading every shader takes a while, and it holds the UI thread the whole time because the
    // reload rebuilds the render mode list, so it does not get the usual marshal timeout.
    private static readonly TimeSpan ShaderReloadTimeout = TimeSpan.FromMinutes(5);

    private async Task<McpToolResult> ReloadShaders(JsonObject args, CancellationToken cancellationToken)
    {
        var viewer = await ActivateViewer(args, cancellationToken).ConfigureAwait(false);

        if (viewer == null)
        {
            return McpToolResult.Error("That tab has no GL viewer.");
        }

        var name = GetString(args, "name");

        var failure = await OnUi(() =>
        {
            try
            {
                // Same path as the Reload shaders button and the file watcher, except a compile
                // failure is handed back to the caller rather than opening a modal error dialog.
                viewer.ShaderHotReload.ReloadShaders(name);
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

        return McpToolResult.Text(name == null ? "Reloaded all shaders." : $"Reloaded shaders from {name}.");
    }
#endif

    private async Task<McpToolResult> SetViewport(JsonObject args, CancellationToken cancellationToken)
    {
        var viewer = await ActivateViewer(args, cancellationToken).ConfigureAwait(false);

        if (viewer == null)
        {
            return McpToolResult.Error("That tab has no GL viewer.");
        }

        var width = GetInt(args, "width");
        var height = GetInt(args, "height");

        if (width == null && height == null)
        {
            viewer.RestoreViewportSize();
        }
        else if (width == null || height == null)
        {
            return McpToolResult.Error("Pass both 'width' and 'height', or neither to follow the window.");
        }
        else if (width < 1 || height < 1 || width > 8192 || height > 8192)
        {
            return McpToolResult.Error("Width and height must be between 1 and 8192.");
        }
        else
        {
            viewer.SetViewportSize(width.Value, height.Value);
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
        var viewer = await ActivateViewer(args, cancellationToken).ConfigureAwait(false);

        if (viewer is not GLSceneViewer scene)
        {
            return McpToolResult.Error("That tab has no 3D scene viewer.");
        }

        var previous = await OnUi(() =>
        {
            var current = scene.PerfDisplayMode;
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

            foreach (var (name, value) in scene.Renderer.PerfStats.Snapshot())
            {
                stats[name] = value;
            }

            return McpToolResult.Json(stats);
        }
        finally
        {
            await OnUi(() => scene.PerfDisplayMode = previous, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<McpToolResult> SetCamera(JsonObject args, CancellationToken cancellationToken)
    {
        var x = GetFloat(args, "x");
        var y = GetFloat(args, "y");
        var z = GetFloat(args, "z");

        if (x == null || y == null || z == null)
        {
            return MissingArgument("x, y and z", args);
        }

        return await WithSceneViewer(args, viewer =>
        {
            var current = viewer.Input.Camera.GetQAngle();

            var angles = new Vector3(
                GetFloat(args, "pitch") ?? current.X,
                GetFloat(args, "yaw") ?? current.Y,
                GetFloat(args, "roll") ?? 0f);

            viewer.Input.SetCameraImmediate(new Vector3(x.Value, y.Value, z.Value), angles);

            if (GetFloat(args, "fov") is { } fov)
            {
                viewer.Input.Camera.FieldOfView = fov;
                viewer.Input.Camera.CreateProjectionMatrix();
            }

            return McpToolResult.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> FindEntities(JsonObject args, CancellationToken cancellationToken)
    {
        var classname = GetString(args, "classname");
        var targetname = GetString(args, "targetname");
        var limit = Math.Clamp(GetInt(args, "limit") ?? 50, 1, 1000);

        return await WithWorldViewer(args, viewer =>
        {
            if (viewer.LoadedWorld is not { } world)
            {
                return McpToolResult.Error("This tab has no loaded world.");
            }

            var matches = new JsonArray();

            for (var i = 0; i < world.Entities.Count && matches.Count < limit; i++)
            {
                var entity = world.Entities[i];
                var entityClass = entity.GetStringProperty("classname") ?? string.Empty;
                var entityName = entity.TargetName ?? string.Empty;

                if (classname != null && !entityClass.Contains(classname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (targetname != null && !entityName.Contains(targetname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var origin = world.GetEntityWorldTransform(entity).Translation;

                matches.Add(new JsonObject
                {
                    ["id"] = i,
                    ["classname"] = entityClass,
                    ["targetname"] = entityName,
                    ["x"] = origin.X,
                    ["y"] = origin.Y,
                    ["z"] = origin.Z,
                });
            }

            return McpToolResult.Json(new JsonObject
            {
                ["entities"] = matches,
                ["searched"] = world.Entities.Count,
            });
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> SelectEntity(JsonObject args, CancellationToken cancellationToken)
    {
        var id = GetInt(args, "id");

        if (id == null)
        {
            return MissingArgument("id", args);
        }

        var instant = GetBool(args, "instant") ?? true;

        return await WithWorldViewer(args, viewer =>
        {
            if (viewer.LoadedWorld is not { } world)
            {
                return McpToolResult.Error("This tab has no loaded world.");
            }

            if (id.Value < 0 || id.Value >= world.Entities.Count)
            {
                return McpToolResult.Error($"No entity with id {id}, the map has {world.Entities.Count}.");
            }

            viewer.SelectAndFocusEntity(world.Entities[id.Value]);

            if (instant)
            {
                // SelectAndFocusEntity flies the camera in; land it now so a screenshot taken
                // straight after shows the destination rather than the journey.
                var camera = viewer.Input.Camera;
                viewer.Input.SetCameraImmediate(camera.Location, camera.GetQAngle());
            }

            var result = viewer.Input.Camera;
            var angles = result.GetQAngle();

            return McpToolResult.Json(new JsonObject
            {
                ["x"] = result.Location.X,
                ["y"] = result.Location.Y,
                ["z"] = result.Location.Z,
                ["pitch"] = angles.X,
                ["yaw"] = angles.Y,
                ["roll"] = angles.Z,
            });
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<McpToolResult> WithSceneViewer(JsonObject args, Func<GLSceneViewer, McpToolResult> action, CancellationToken cancellationToken)
        => WithViewer(args, action, "That tab has no 3D scene viewer.", cancellationToken);

    private Task<McpToolResult> WithWorldViewer(JsonObject args, Func<GLWorldViewer, McpToolResult> action, CancellationToken cancellationToken)
        => WithViewer(args, action, "That tab has no map loaded.", cancellationToken);

    /// <summary>Runs <paramref name="action"/> on the UI thread with the tab's viewer, if it is a <typeparamref name="T"/>.</summary>
    private async Task<McpToolResult> WithViewer<T>(JsonObject args, Func<T, McpToolResult> action, string notFound, CancellationToken cancellationToken)
        where T : GLBaseControl
    {
        var viewer = await ActivateViewer(args, cancellationToken).ConfigureAwait(false);

        if (viewer is not T typed)
        {
            return McpToolResult.Error(notFound);
        }

        return await OnUi(() => action(typed), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the GL viewer for the requested tab, selecting that tab first because only the
    /// active control renders.
    /// </summary>
    private async Task<GLBaseControl?> ActivateViewer(JsonObject args, CancellationToken cancellationToken)
    {
        var id = GetInt(args, "tab");

        return await OnUi(() =>
        {
            var form = Program.MainForm;
            var page = id == null ? form.Tabs.SelectedTab : PageFor(id.Value);

            if (page == null)
            {
                return null;
            }

            if (form.Tabs.SelectedTab != page)
            {
                form.Tabs.SelectTab(page);
            }

            var viewer = GLBaseControl.FindHostedIn(page);

            // Minimizing takes the control off the render loop, and only a repaint puts it back,
            // so without this a capture after the window was restored waits for frames forever.
            viewer?.EnsureAttachedToRenderLoop();

            return viewer;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders <paramref name="count"/> frames, or explains why it could not. Callers must already
    /// hold a scope from <see cref="RenderLoopThread.BeginAutomationRendering"/>.
    /// </summary>
    private static async Task<string?> PresentFrames(int count, CancellationToken cancellationToken)
    {
        // A minimized window draws nothing at all: the control is off the render loop, and the GL
        // surface has no size to draw into. Say so rather than sitting out the frame timeout.
        if (await OnUi(() => Program.MainForm.WindowState == FormWindowState.Minimized, cancellationToken).ConfigureAwait(false))
        {
            return "The viewer window is minimized, which stops rendering. Restore it and try again.";
        }

        return RenderLoopThread.WaitForFrames(count, FrameTimeout)
            ? null
            : $"Timed out waiting for a rendered frame after {FrameTimeout.TotalSeconds:F0}s.";
    }

    private async Task<McpToolResult> Screenshot(JsonObject args, CancellationToken cancellationToken)
    {
        var viewer = await ActivateViewer(args, cancellationToken).ConfigureAwait(false);

        if (viewer == null)
        {
            return McpToolResult.Error("That tab has no GL viewer to capture.");
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

            viewer.FinishTextureStreaming(cancellationToken);

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

        var description = $"{width}x{height} frame";

        if (full != null)
        {
            await File.WriteAllBytesAsync(path!, full, cancellationToken).ConfigureAwait(false);

            description += $", full resolution PNG written to {path}";
        }

        return McpToolResult.Text(description + ".").WithImage(inline, "image/jpeg");
    }
}
#endif
