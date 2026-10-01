#if DEBUG
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that control simulation time, inspect particle systems and drive the particle viewer.</summary>
internal sealed partial class McpTools
{
    private const float MaxStepSeconds = 60f;
    private const float MinStepInterval = 1f / 480f;
    private const float MaxStepInterval = 0.125f;

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

        Add("resume", "Go back to real time simulation. Runs alongside a step that is still running and abandons it, so the step answers with an error.",
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
                ["timestep"] = Prop("number", "Seconds per simulated frame, from 1/480 to 0.125. Defaults to 1/64, one entity tick."),
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

        var interval = GetFloat(args, "timestep") ?? AutomationClock.DefaultStepInterval;

        // A tolerance, so that 1/480 written out as a decimal still counts as the smallest step.
        if (interval < MinStepInterval * 0.999f || interval > MaxStepInterval)
        {
            return McpToolResult.Error($"'timestep' must be from 1/480 ({MinStepInterval:0.######}) to {MaxStepInterval} seconds, got {interval}.");
        }

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
            catch (OperationCanceledException)
            {
                AutomationClock.Abandon(stepping);
                throw;
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

        var worldViewer = viewer as GLWorldViewer;
        var world = worldViewer?.LoadedWorld;
        MapEntity? entity = null;

        if (entityId != null)
        {
            if (worldViewer == null || world == null)
            {
                return McpToolResult.Error("'entity' needs a tab with a map loaded.");
            }

            entity = await OnUi(() => EntityById(worldViewer, world, entityId.Value), cancellationToken).ConfigureAwait(false);

            if (entity == null)
            {
                return NoSuchEntity(world, entityId.Value);
            }
        }

        var describer = await OnUi(() => new NodeDescriber(viewer!), cancellationToken).ConfigureAwait(false);

        // Off the UI thread, because holding a frame there can deadlock against a frame that is
        // waiting on the UI thread.
        return await Task.Run(() =>
        {
            using var frame = viewer!.HoldFrame();

            var systems = new List<(ParticleSceneNode Node, float Distance)>();

            foreach (var scene in viewer!.Renderer.Scenes)
            {
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

                    systems.Add((particles, distance));
                }
            }

            if (near != null)
            {
                systems.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            }

            var list = new JsonArray();
            var spawned = world != null ? SpawnedEntities(viewer) : null;

            foreach (var (node, _) in systems.Take(limit))
            {
                list.Add(DescribeParticles(node, describer, worldViewer, spawned));
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

    private JsonObject DescribeParticles(ParticleSceneNode node, NodeDescriber describer, GLWorldViewer? worldViewer, Dictionary<EntityLump.Entity, BaseEntity>? spawned)
    {
        var simulation = node.ParticleSimulation;

        var result = new JsonObject
        {
            ["name"] = node.Name ?? simulation.Name,
            ["node"] = describer.Id(node),
        };

        if (worldViewer is { LoadedWorld: { } world } && node.EntityData != null && EntityByData(worldViewer, world, node.EntityData) is { } entity)
        {
            result["entity"] = DescribeEntity(entity, spawned?.GetValueOrDefault(entity.Data));
        }

        if (node.Scene.WorldGroup != null)
        {
            result["sky"] = true;
        }

        if (describer.HiddenReasons(node) is { Count: > 0 } reasons)
        {
            result["hidden"] = new JsonArray([.. reasons.Select(static reason => (JsonNode?)reason)]);
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

    // The sidebar of the particle viewer lists these groups in this order, each checked against
    // the same support table it uses.
    private static readonly (string Name, string ListName, Func<string, bool> IsSupported)[] ParticleFunctionGroups =
    [
        ("pre_emission_operators", "m_PreEmissionOperators", ParticleSupportInfo.IsPreEmissionOperatorSupported),
        ("emitters", "m_Emitters", ParticleSupportInfo.IsEmitterSupported),
        ("initializers", "m_Initializers", ParticleSupportInfo.IsInitializerSupported),
        ("operators", "m_Operators", ParticleSupportInfo.IsOperatorSupported),
        ("force_generators", "m_ForceGenerators", ParticleSupportInfo.IsForceGeneratorSupported),
        ("constraints", "m_Constraints", ParticleSupportInfo.IsConstraintSupported),
        ("renderers", "m_Renderers", ParticleRendererFactory.IsSupported),
    ];

    private static bool ParticleViewer(GLBaseControl viewer) => viewer is GLParticleViewer;

    private void RegisterParticleTools()
    {
        Add("particle_playback", "Restart, pause, resume or play the endcap of a particle system tab, through the same buttons as its sidebar. Pausing here holds only this system, unlike pause, which freezes every tab; while pause is in effect a restart shows nothing new until step.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["action"] = Prop("string", "What to press.", "restart", "pause", "resume", "endcap"),
            }, "action"),
            ParticlePlayback, ParticleViewer);

        Add("list_particle_functions", "List the functions of a particle system tab by group, as its sidebar does: each class with whether the viewer implements it ('unsupported' draws or does nothing), the class it was upgraded from, and the ones the format upgrade removed. Also lists the child systems, with the disabled ones marked.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
            }),
            ListParticleFunctions, ParticleViewer);
    }

    private async Task<McpToolResult> ParticlePlayback(JsonObject args, CancellationToken cancellationToken)
    {
        var action = GetString(args, "action");

        if (action == null)
        {
            return MissingArgument("action", args);
        }

        if (action is not ("restart" or "pause" or "resume" or "endcap"))
        {
            return McpToolResult.Error($"Unknown action '{action}'. Use restart, pause, resume or endcap.");
        }

        return await WithViewer<GLParticleViewer>(args, viewer =>
        {
            // The pause button reads Resume while the system is paused.
            if (viewer.FindButtons("Pause", "Resume") is not [var pauseButton])
            {
                return McpToolResult.Error("The particle viewer has no pause button to read.");
            }

            var target = action switch
            {
                "restart" => viewer.FindButtons("Restart"),
                "endcap" => viewer.FindButtons("Play Endcap"),
                "pause" when pauseButton.Text == "Pause" => [pauseButton],
                "resume" when pauseButton.Text == "Resume" => [pauseButton],
                _ => [],
            };

            if (target.Count > 1)
            {
                return McpToolResult.Error($"The particle viewer has more than one button for '{action}'.");
            }

            if (target is [var button])
            {
                if (!button.CanSelect)
                {
                    return McpToolResult.Error($"The '{button.Text}' button cannot be clicked right now.");
                }

                button.PerformClick();
            }
            else if (action is "restart" or "endcap")
            {
                return McpToolResult.Error($"The particle viewer has no button for '{action}'.");
            }

            return McpToolResult.Json(new JsonObject
            {
                ["action"] = action,
                ["paused"] = pauseButton.Text == "Resume",
            });
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> ListParticleFunctions(JsonObject args, CancellationToken cancellationToken)
    {
        var (viewer, error) = await ActivateViewer<GLParticleViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var resource = await OnUi(() =>
        {
            foreach (TabPage page in Program.MainForm.Tabs.TabPages)
            {
                if (GLBaseControl.FindHostedIn(page) == viewer && page.Tag is ExportData { DisposableContents: Types.Viewers.Resource shown })
                {
                    return shown.LoadedResource;
                }
            }

            return null;
        }, cancellationToken).ConfigureAwait(false);

        // The same system the viewer was given: the resource's own, or one built from its snapshot.
        var system = resource?.DataBlock as ParticleSystem
            ?? (resource?.GetBlockByType(BlockType.SNAP) is ParticleSnapshot snapshot ? SnapshotParticleSystem.Create(snapshot) : null);

        if (system == null)
        {
            return McpToolResult.Error("The particle system of this tab could not be found.");
        }

        return await Task.Run(() =>
        {
            var trace = system.GetUpgradeTrace();
            var groups = new JsonObject();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (name, listName, isSupported) in ParticleFunctionGroups)
            {
                if (trace.GetValueOrDefault(listName) is not { Count: > 0 } functions)
                {
                    continue;
                }

                var list = new JsonArray();

                foreach (var function in functions)
                {
                    var status = function.RemovedByUpgrade ? "removed"
                        : isSupported(function.Class) ? "supported"
                        : "unsupported";

                    counts[status] = counts.GetValueOrDefault(status) + 1;

                    var entry = new JsonObject
                    {
                        ["class"] = function.Class,
                        ["status"] = status,
                    };

                    if (!function.RemovedByUpgrade && function.OriginalClass != null)
                    {
                        entry["was"] = function.OriginalClass;
                    }

                    list.Add(entry);
                }

                groups[name] = list;
            }

            var result = new JsonObject
            {
                ["counts"] = new JsonObject
                {
                    ["supported"] = counts.GetValueOrDefault("supported"),
                    ["unsupported"] = counts.GetValueOrDefault("unsupported"),
                    ["removed"] = counts.GetValueOrDefault("removed"),
                },
                ["groups"] = groups,
            };

            var children = new JsonArray();

            foreach (var child in system.GetChildren())
            {
                var childRef = child.GetStringProperty("m_ChildRef");

                if (string.IsNullOrEmpty(childRef))
                {
                    continue;
                }

                var entry = new JsonObject
                {
                    ["ref"] = childRef,
                };

                if (!system.IsChildEnabled(child))
                {
                    entry["disabled"] = true;
                }

                children.Add(entry);
            }

            if (children.Count > 0)
            {
                result["children"] = children;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }
}
#endif
