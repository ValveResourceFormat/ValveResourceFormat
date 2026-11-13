#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
using GUI.Utils;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that control simulation time and inspect, place and drive particle systems.</summary>
internal sealed partial class McpTools
{
    private const string ControlPointsDescription = "Control point positions by index, 0 to 63, such as {\"1\": [0, 0, 100]}. Orientations stay.";
    private const int MaxControlPoint = 63;

    // Stepped frames are requested in small batches, so a pause that ends the step stops them soon
    private const int StepBatchFrames = 64;

    /// <summary>The particle systems that spawn_particle added, the only ones set_particles removes. Only the keys are used.</summary>
    private readonly ConditionalWeakTable<ParticleSceneNode, object?> SpawnedParticles = [];

    [Tool("Freeze time in every tab: entities, particles, animation and shader time. Frames still render, so the camera can move and screenshots repeat. Particles do not emit while paused, so use step to run them.", Concurrent = true, Redraws = true)]
    private static object Pause([Description("False returns to real time and ends a running step.")] bool paused = true)
    {
        AutomationClock.SetPaused(paused);
        return new { Paused = paused };
    }

    [Tool("Pause, advance a 3D tab by exactly 'seconds' in fixed frames, and stay paused. Returns the scene time in seconds, which is short of the full step when pause ended it. A tab reloaded while paused restarts at time zero, so two runs with the same steps match. "
        + "A particle system restarted in place plays differently each time, unless set_particles pins its seed.")]
    private static async Task<object> Step(
        GLSceneViewer viewer,
        [Description("Seconds to simulate.")][Range(0d, 600d, MinimumIsExclusive = true)] float seconds,
        [Description("Seconds per frame. Defaults to one entity tick, 1/64.")][Range(0.002, 0.125)] float timestep = 1f / 64f,
        CancellationToken cancellationToken = default)
    {
        await ShowForFrames(viewer, readsPixels: false, cancellationToken).ConfigureAwait(false);
        var crash = ReportedErrors.NextAsync();

        // Each stepped frame would otherwise wait for the display, though only the last one is seen
        await WithGl(viewer, () => GLFW.SwapInterval(0), cancellationToken).ConfigureAwait(false);

        try
        {
            AutomationClock.Step(seconds, timestep);

            while (AutomationClock.IsStepping)
            {
                var frames = Math.Min(AutomationClock.StepFramesLeft, StepBatchFrames);
                await DrawShownFrames(viewer, frames, crash, cancellationToken).ConfigureAwait(false);
            }

            // The last stepped frame simulated and drew, and one more frame makes sure the window shows it
            await DrawShownFrames(viewer, 1, crash, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            AutomationClock.SetPaused(true);
            throw;
        }
        finally
        {
            await RestoreWithGl(viewer, () => GLFW.SwapInterval(Settings.Config.Vsync)).ConfigureAwait(false);
        }

        return new { Time = viewer.Renderer.Uptime };
    }

    [Tool("Read or set the world simulation of a map tab, as its Play, Pause and Stop buttons do. "
        + "Playing from stopped spawns the map's entities again at round start, runs their logic, and walks the camera as the player, with gravity. Stopping puts the world back as it loaded and frees the camera. Entities keep their ids. "
        + "A playing world advances only while time runs. So while the pause tool holds time, step to run logic such as logic_auto outputs and point_template spawns.", Redraws = true)]
    private static async Task<object> Simulation(
        GLWorldViewer viewer,
        [Description("State to set. Paused from stopped plays first, so the world spawns and holds. Omit to read the state.")] GLWorldViewer.SimulationState? state = null,
        CancellationToken cancellationToken = default)
    {
        if (state == null || state == viewer.Simulation)
        {
            return new UnchangedReply(new { State = viewer.Simulation });
        }

        await ShowForFrames(viewer, readsPixels: false, cancellationToken).ConfigureAwait(false);
        var crash = ReportedErrors.NextAsync();

        if (state == GLWorldViewer.SimulationState.Paused && viewer.Simulation == GLWorldViewer.SimulationState.Stopped)
        {
            viewer.QueueSimulation(GLWorldViewer.SimulationRequest.Play);
            await DrawShownFrames(viewer, 1, crash, cancellationToken).ConfigureAwait(false);
        }

        var request = state switch
        {
            GLWorldViewer.SimulationState.Playing => GLWorldViewer.SimulationRequest.Play,
            GLWorldViewer.SimulationState.Paused => GLWorldViewer.SimulationRequest.Pause,
            _ => GLWorldViewer.SimulationRequest.Stop,
        };

        // The render thread owns the entity world, so the next frame applies the change
        viewer.QueueSimulation(request);
        await DrawShownFrames(viewer, 1, crash, cancellationToken).ConfigureAwait(false);

        // Stopping lifts the camera with a fly-out, which would run on scene time
        if (state == GLWorldViewer.SimulationState.Stopped)
        {
            await WithGl(viewer, () => viewer.Input.SaveCameraForTransition(transitionDuration: 0f), cancellationToken).ConfigureAwait(false);
        }

        // World geometry opened without its map has no entities to run
        if (viewer.Simulation != state)
        {
            throw new ToolException($"The world stayed {viewer.Simulation.ToString().ToLowerInvariant()}. Only a map has entities to simulate.");
        }

        return new { State = viewer.Simulation };
    }

    [Tool("List the particle systems of a 3D tab. 'age' is in seconds, 'particles' counts the live particles of the system and its children, and 'skipped_renderers' are renderer classes that are not implemented and draw nothing.")]
    private object GetParticles(
        GLSceneViewer viewer,
        [Description("Only systems that this entity plays.")] int? entity = null,
        [Description("Only systems within 'radius' of this world point, nearest first.")] Vector3? near = null,
        [Description("Distance from 'near', in world units.")][Range(0, double.MaxValue)] float radius = 512f,
        [Description("Maximum systems to return.")][Range(1, int.MaxValue)] int limit = 20,
        [Description("Add the tree of child systems, each with its own particle count and the average color, alpha, radius and position of its particles.")] bool tree = false)
    {
        var entities = MapEntities(viewer);
        var data = EntityData(entities, entity);
        var systems = AllNodes(viewer).OfType<ParticleSceneNode>()
            .Where(node => data == null || ReferenceEquals(node.EntityData, data))
            .Select(node => (Node: node, Position: Vector3.Transform(node.Transform.Translation, node.Scene.ToViewerWorld)))
            .Where(system => near == null || Vector3.Distance(system.Position, near.Value) <= radius)
            .OrderBy(system => near == null ? 0f : Vector3.Distance(system.Position, near.Value))
            .ToList();

        return new
        {
            Systems = systems.Take(limit).Select(system => DescribeParticleSystem(entities, system.Node, system.Position, tree)).ToList(),
            Total = systems.Count,
        };
    }

    private object DescribeParticleSystem(EntityTable? entities, ParticleSceneNode node, Vector3 position, bool tree)
    {
        var simulation = node.ParticleSimulation;

        return new
        {
            Name = node.Name ?? simulation.Name,
            Node = NodeIds.IdOf(node),
            Entity = EntityId(entities, node.EntityData),
            Hidden = HiddenReason(node),
            Paused = Flag(node.IsPaused),
            Finished = Flag(node.IsFinished),
            simulation.RenderState.Age,
            Particles = NonZero(CountParticles(simulation)),
            Position = position,
            ControlPoints = UsedControlPoints(simulation.RenderState),
            SkippedRenderers = node.SkippedRendererClasses.Distinct().ToList(),
            Tree = tree ? DescribeParticleTree(simulation, node.Scene.ToViewerWorld) : null,
        };
    }

    /// <summary>
    /// The positions of the control points of a system, by index. Points below the highest used one
    /// start at zero whether or not something sets them, so the result omits points still at zero.
    /// </summary>
    private static Dictionary<int, Vector3> UsedControlPoints(ParticleSystemState state)
    {
        var points = new Dictionary<int, Vector3>
        {
            [0] = state.GetControlPoint(0).Position,
        };

        for (var i = 1; i <= state.HighestControlPoint; i++)
        {
            var position = state.GetControlPoint(i).Position;

            if (position != Vector3.Zero)
            {
                points[i] = position;
            }
        }

        return points;
    }

    private static int CountParticles(ParticleSystemSimulation simulation) => simulation.Particles.Count + simulation.Children.Sum(CountParticles);

    private static object DescribeParticleTree(ParticleSystemSimulation simulation, Matrix4x4 toViewerWorld)
    {
        var particles = simulation.Particles.Current;

        return new
        {
            simulation.Name,
            Particles = NonZero(particles.Length),
            simulation.RenderState.Age,
            Finished = Flag(simulation.IsFinished()),
            Average = particles.IsEmpty ? null : AverageParticle(particles, toViewerWorld),
            Children = simulation.Children.Select(child => DescribeParticleTree(child, toViewerWorld)).ToList(),
        };
    }

    private static object AverageParticle(ParticleSpan particles, Matrix4x4 toViewerWorld)
    {
        var color = Vector3.Zero;
        var alpha = 0f;
        var radius = 0f;
        var position = Vector3.Zero;

        for (var i = 0; i < particles.Length; i++)
        {
            color += particles.ReadVector(ParticleField.Color, i);
            alpha += particles.ReadFloat(ParticleField.Alpha, i);
            radius += particles.ReadFloat(ParticleField.Radius, i);
            position += particles.ReadVector(ParticleField.Position, i);
        }

        var count = particles.Length;

        return new
        {
            Color = color / count,
            Alpha = alpha / count,
            Radius = radius / count,
            Position = Vector3.Transform(position / count, toViewerWorld),
        };
    }

    [Tool("Add a particle system to a 3D tab, loaded from the tab's files. It plays once unless 'loop' is true. To put it on the ground of a map, trace down and pass the hit position.")]
    private async Task<object> SpawnParticle(
        GLSceneViewer viewer,
        [Description("Particle system, such as particles/explosions_fx/explosion_basic.vpcf.")] string path,
        [Description("Position of control point 0.")] Vector3? position = null,
        [Description("Orientation of control point 0.")] Vector3? angles = null,
        [Description(ControlPointsDescription)] Dictionary<int, Vector3>? controlPoints = null,
        [Description("Random seed pinned for every replay, so every run plays the same.")][Range(0, 4095)] int? seed = null,
        [Description("Restart when finished.")] bool loop = false,
        [Description("Use its preview control points and model, as the particle viewer does.")] bool preview = false,
        CancellationToken cancellationToken = default)
    {
        var scene = viewer.Renderer.Scene;
        CheckControlPoints(controlPoints);

        // The loader adds the compiled suffix itself
        var fileName = path;

        if (fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            fileName = fileName[..^GameFileLoader.CompiledFileSuffix.Length];
        }

        // Read before taking the GL context, which would stop rendering for the whole read
        var resource = await Task.Run(() => scene.RendererContext.FileLoader.LoadFileCompiled(fileName), cancellationToken).ConfigureAwait(false);

        if (resource?.DataBlock is not ParticleSystem system)
        {
            throw new ToolException($"This tab could not load '{path}' as a particle system. get_log may say why.");
        }

        var node = await WithGl(viewer, () =>
        {
            var spawned = new ParticleSceneNode(scene, system, preview: preview)
            {
                Name = path,
                Layers = [WorldLayer.Particles],
                Loop = loop,
                Transform = EntityTransformHelper.ToRigidTransformationMatrix(angles ?? Vector3.Zero, position ?? Vector3.Zero),
            };

            // The scene holds the preview model beside the system, so the model goes on the system's layer
            spawned.PreviewModel?.Layers = spawned.Layers;

            SetUpParticles(spawned, seed, controlPoints, restart: seed != null);
            scene.Add(spawned, true);
            SpawnedParticles.Add(spawned, null);

            // The constructor added the preview model before it had a layer, so it copies the layer state of the system
            spawned.PreviewModel?.LayerEnabled = spawned.LayerEnabled;

            return spawned;
        }, cancellationToken).ConfigureAwait(false);

        return new
        {
            Node = NodeIds.IdOf(node),
            SkippedRenderers = node.SkippedRendererClasses.Distinct().ToList(),
        };
    }

    [Tool("Change the particle systems in 'nodes', or every system of a 3D tab. A map entity that drives a control point resets it on the next frame. The playback buttons of a particle tab are sidebar controls.", Redraws = true)]
    private object SetParticles(
        GLSceneViewer viewer,
        [Description("Particle node ids from spawn_particle or get_particles.")] int[]? nodes = null,
        [Description("Random seed pinned for every replay, from the next restart. Each child derives its own from it. -1 unpins it, so each replay continues the random sequence of the last.")][Range(-1, 4095)] int? seed = null,
        [Description(ControlPointsDescription)] Dictionary<int, Vector3>? controlPoints = null,
        [Description("Restart the systems. Defaults to true with 'seed'.")] bool? restart = null,
        [Description("Remove the systems. Only those that spawn_particle added can be removed. Without 'nodes', removes all of them.")] bool remove = false)
    {
        CheckControlPoints(controlPoints);

        var targets = new List<ParticleSceneNode>();

        if (nodes != null)
        {
            var sceneNodes = AllNodes(viewer).ToHashSet();

            foreach (var id in nodes)
            {
                if (NodeById(sceneNodes, id) is not ParticleSceneNode node)
                {
                    throw new ToolException($"Node {id} is not a particle system.");
                }

                targets.Add(node);
            }
        }
        else
        {
            targets.AddRange(AllNodes(viewer).OfType<ParticleSceneNode>());
        }

        if (remove)
        {
            var removed = new List<ParticleSceneNode>();

            foreach (var node in targets)
            {
                if (SpawnedParticles.TryGetValue(node, out _))
                {
                    removed.Add(node);
                }
                else if (nodes != null)
                {
                    throw new ToolException($"spawn_particle did not add node {NodeIds.IdOf(node)}, so it cannot be removed. Use hidden_nodes to hide it.");
                }
            }

            viewer.Selection.Clear();

            foreach (var node in removed)
            {
                SpawnedParticles.Remove(node);
                node.Scene.Remove(node, true);
                node.Delete();

                if (node.PreviewModel is { } previewModel && previewModel.Scene.Remove(previewModel, true))
                {
                    previewModel.Delete();
                }
            }

            return new { Removed = removed.Select(NodeIds.IdOf).ToList() };
        }

        foreach (var node in targets)
        {
            SetUpParticles(node, seed, controlPoints, restart ?? seed != null);
        }

        return new { Nodes = targets.Select(NodeIds.IdOf).ToList() };
    }

    private static void CheckControlPoints(Dictionary<int, Vector3>? controlPoints)
    {
        if (controlPoints == null)
        {
            return;
        }

        foreach (var index in controlPoints.Keys)
        {
            if (index is < 0 or > MaxControlPoint)
            {
                throw new ArgumentException($"Control point {index} is out of the range 0 to {MaxControlPoint}.");
            }
        }
    }

    /// <param name="seed">The seed to pin, -1 to return to a new seed for each replay, or null to keep the current one.</param>
    private static void SetUpParticles(ParticleSceneNode node, int? seed, Dictionary<int, Vector3>? controlPoints, bool restart)
    {
        if (seed != null)
        {
            node.ParticleSimulation.PinRandomSeed(seed == -1 ? null : seed);
        }

        foreach (var (index, position) in controlPoints ?? [])
        {
            node.GetControlPoint(index).Position = position;
        }

        if (restart)
        {
            node.Restart();
        }
    }

    [Tool("List the functions of a particle system tab by group, and its child systems. 'status' is unsupported for a class the viewer does not implement, removed for one the format upgrade removed, and left out otherwise. 'was' is the class it was upgraded from.")]
    private static Task<object> ListParticleFunctions(GLParticleViewer viewer)
    {
        // Reads only what the system loaded with, so it returns a task to run on the thread pool instead of between frames
        var lists = viewer.functionLists ?? throw new ToolException("The particle system has not loaded yet.");
        var system = viewer.particleSystem;

        static GLParticleViewer.FunctionSupport? Status(GLParticleViewer.FunctionSupport support)
        {
            if (support == GLParticleViewer.FunctionSupport.Supported)
            {
                return null;
            }

            return support;
        }

        return Task.FromResult<object>(new
        {
            Groups = GLParticleViewer.FunctionGroups
                .Where(group => lists.GetValueOrDefault(group.ListName) is { Count: > 0 })
                .ToDictionary(static group => JsonNamingPolicy.SnakeCaseLower.ConvertName(group.ListName[2..]), group => lists[group.ListName].Select(function => new
                {
                    function.Class,
                    Status = Status(GLParticleViewer.GetFunctionSupport(function, group.IsSupported)),
                    Was = function.RemovedByUpgrade ? null : function.OriginalClass,
                }).ToList()),
            Children = system.GetChildren()
                .Where(static child => !string.IsNullOrEmpty(child.GetStringProperty("m_ChildRef")))
                .Select(child => new { Ref = child.GetStringProperty("m_ChildRef"), Disabled = Flag(!system.IsChildEnabled(child)) })
                .ToList(),
        });
    }
}
#endif
