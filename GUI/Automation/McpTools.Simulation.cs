#if DEBUG
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Automation;

/// <summary>Tools that control simulation time and inspect particle systems.</summary>
internal sealed partial class McpTools
{
    private const float MaxStepSeconds = 60f;

    // Generous per frame, because a frame waits for vsync and a heavy map can take far longer to draw.
    private static readonly TimeSpan StepTimePerFrame = TimeSpan.FromMilliseconds(100);

    private void RegisterSimulationTools()
    {
        Add("pause", "Freeze the simulation of every tab: entities, particles, animation and shader time. Frames still render, so the camera can move and screenshots stay exactly repeatable. Particles do not emit while paused, so step after loading a map to see them.",
            Schema(),
            (_, _) =>
            {
                AutomationClock.Pause();
                return Task.FromResult(McpToolResult.Json(new JsonObject { ["paused"] = true }));
            });

        Add("resume", "Go back to real time simulation, abandoning a step that is still running.",
            Schema(),
            (_, _) =>
            {
                AutomationClock.Resume();
                return Task.FromResult(McpToolResult.Json(new JsonObject { ["paused"] = false }));
            });

        Add("step", "Pause, then advance the simulation of a 3D tab by exactly this many seconds in fixed frames, and stay paused. Two runs stepped the same way show the same thing. Returns the scene time afterwards.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["seconds"] = Prop("number", $"Seconds to simulate, at most {MaxStepSeconds:F0}."),
                ["timestep"] = Prop("number", "Seconds per simulated frame. Defaults to 1/64, one entity tick; at most 0.125."),
            }, "seconds"),
            Step, SceneViewer);

        Add("get_particles", "Inspect the particle systems of a 3D tab: whether each is paused or finished, its age and live particle count including child systems, its control points, bounds, and the renderer classes it uses that are not implemented and so draw nothing.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["entity"] = Prop("integer", "Only systems played by this entity, by id from find_entities."),
                ["near"] = VectorProp("Only systems within 'radius' of this world point [x, y, z], nearest first."),
                ["radius"] = Prop("number", "Distance from 'near'. Defaults to 512."),
                ["limit"] = Prop("integer", "Most systems to return. Defaults to 20, at most 500."),
            }),
            GetParticles, SceneViewer);
    }

    private async Task<McpToolResult> Step(JsonObject args, CancellationToken cancellationToken)
    {
        var seconds = GetFloat(args, "seconds");

        if (seconds == null)
        {
            return MissingArgument("seconds", args);
        }

        if (seconds.Value is <= 0f or > MaxStepSeconds)
        {
            return McpToolResult.Error($"'seconds' must be more than 0 and at most {MaxStepSeconds:F0}.");
        }

        var interval = Math.Clamp(GetFloat(args, "timestep") ?? AutomationClock.DefaultStepInterval, 1f / 480f, 0.125f);

        var (viewer, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        if (await CheckCanRender(cancellationToken).ConfigureAwait(false) is { } renderError)
        {
            return McpToolResult.Error(renderError);
        }

        var expectedFrames = (int)MathF.Ceiling(seconds.Value / interval);
        var timeout = FrameTimeout + StepTimePerFrame * expectedFrames;

        using (RenderLoopThread.BeginAutomationRendering())
        {
            var stepping = AutomationClock.Step(seconds.Value, interval);

            try
            {
                if (!await WaitOnRenderLoop(stepping, timeout, cancellationToken).ConfigureAwait(false))
                {
                    return McpToolResult.Error($"Timed out after {timeout.TotalSeconds:F0}s stepping {seconds.Value}s. The simulation stays paused wherever it got to.");
                }
            }
            catch (TaskCanceledException) when (stepping.IsCanceled)
            {
                return McpToolResult.Error("The step was abandoned by resume or by another step.");
            }

            // The last stepped frame simulates and then draws, but one more makes sure what is on
            // screen, and in the next screenshot, is that final state.
            if (await PresentFrames(1, cancellationToken).ConfigureAwait(false) is { } frameError)
            {
                return McpToolResult.Error(frameError);
            }

            return McpToolResult.Json(new JsonObject
            {
                ["paused"] = true,
                ["frames"] = await stepping.ConfigureAwait(false),
                ["time"] = Math.Round(viewer!.Renderer.Uptime, 3),
            });
        }
    }

    private async Task<McpToolResult> GetParticles(JsonObject args, CancellationToken cancellationToken)
    {
        var entityId = GetInt(args, "entity");
        var near = GetVector(args, "near");
        var radius = GetFloat(args, "radius") ?? 512f;
        var limit = Math.Clamp(GetInt(args, "limit") ?? 20, 1, 500);

        var (viewer, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var world = (viewer as GLWorldViewer)?.LoadedWorld;
        MapEntity? entity = null;

        if (entityId != null)
        {
            if (world == null)
            {
                return McpToolResult.Error("'entity' needs a tab with a map loaded.");
            }

            entity = EntityById(world, entityId.Value);

            if (entity == null)
            {
                return NoSuchEntity(world, entityId.Value);
            }
        }

        // Off the UI thread, because holding a frame there can deadlock against a frame that is
        // waiting on the UI thread.
        return await Task.Run(() =>
        {
            using var frame = viewer!.HoldFrame();

            var systems = new List<(ParticleSceneNode Node, bool InSky, float Distance)>();

            void Collect(Scene? scene, bool inSky)
            {
                if (scene == null)
                {
                    return;
                }

                foreach (var node in scene.AllNodes)
                {
                    if (node is not ParticleSceneNode particles)
                    {
                        continue;
                    }

                    if (entity != null && !ReferenceEquals(node.EntityData, entity.Value.Data))
                    {
                        continue;
                    }

                    var distance = near == null ? 0f : Vector3.Distance(Vector3.Transform(node.Transform.Translation, scene.ToViewerWorld), near.Value);

                    if (distance > radius)
                    {
                        continue;
                    }

                    systems.Add((particles, inSky, distance));
                }
            }

            Collect(viewer.Scene, inSky: false);
            Collect(viewer.SkyboxScene, inSky: true);

            if (near != null)
            {
                systems.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            }

            var list = new JsonArray();

            foreach (var (node, inSky, _) in systems.Take(limit))
            {
                list.Add(DescribeParticles(node, inSky, world));
            }

            var result = new JsonObject
            {
                ["systems"] = list,
            };

            if (systems.Count > list.Count)
            {
                result["total"] = systems.Count;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject DescribeParticles(ParticleSceneNode node, bool inSky, ValveResourceFormat.Renderer.World.WorldLoader? world)
    {
        var simulation = node.ParticleSimulation;

        var result = new JsonObject
        {
            ["name"] = node.Name ?? simulation.Name,
        };

        if (world != null && node.EntityData != null && EntityByData(world, node.EntityData) is { } entity)
        {
            result["entity"] = DescribeEntity(world, entity);
        }

        if (inSky)
        {
            result["sky"] = true;
        }

        if (!node.LayerEnabled || !node.Visible)
        {
            result["hidden"] = true;
        }

        if (node.IsPaused)
        {
            result["paused"] = true;
        }

        if (simulation.IsFinished())
        {
            result["finished"] = true;
        }

        result["age"] = Math.Round(simulation.RenderState.Age, 3);
        result["particles"] = CountParticles(simulation);
        result["position"] = Round(Vector3.Transform(node.Transform.Translation, node.Scene.ToViewerWorld));

        var bounds = node.BoundingBox;

        // An effect that has not simulated yet has unbounded or inverted bounds.
        if (float.IsFinite(bounds.Size.X + bounds.Size.Y + bounds.Size.Z) && bounds.Size.X >= 0f && bounds.Size.MaxComponent() < 1e6f)
        {
            bounds = bounds.Transform(node.Scene.ToViewerWorld);

            result["center"] = Round(bounds.Center);
            result["size"] = Round(bounds.Size);
        }

        var state = simulation.RenderState;
        var controlPoints = new JsonObject();

        for (var i = 0; i <= state.HighestControlPoint; i++)
        {
            var position = state.GetControlPoint(i).Position;

            // Points below the highest one used are created at zero whether or not anything sets them.
            if (i > 0 && position == Vector3.Zero)
            {
                continue;
            }

            controlPoints[i.ToString(CultureInfo.InvariantCulture)] = Round(position);
        }

        result["control_points"] = controlPoints;

        var skipped = new JsonArray();

        foreach (var rendererClass in node.SkippedRendererClasses.Distinct(StringComparer.Ordinal))
        {
            skipped.Add(rendererClass);
        }

        if (skipped.Count > 0)
        {
            result["skipped_renderers"] = skipped;
        }

        return result;
    }

    private static int CountParticles(ParticleSystemSimulation simulation)
    {
        var count = simulation.Particles.Count;

        foreach (var child in simulation.Children)
        {
            count += CountParticles(child);
        }

        return count;
    }
}
#endif
