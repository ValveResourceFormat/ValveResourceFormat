#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
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
    private const string ControlPointsDescription = "Control point positions by index from 0 to 63, such as {\"1\": [0, 0, 100]}. Only the position is set; the orientation is kept.";
    private const int MaxControlPoint = 63;

    /// <summary>The particle systems spawn_particle added, which are the only ones set_particles removes.</summary>
    private readonly ConditionalWeakTable<ParticleSceneNode, object> spawnedParticles = [];

    [Tool("Freeze the simulation of every tab: entities, particles, animation and shader time. Or with paused false go back to real time, which abandons a running step. Frames still render, so the camera can move and screenshots are repeatable. Particles do not emit while paused, so step to run them.", Concurrent = true, Redraws = true)]
    private static object Pause([Description("False to go back to real time.")] bool paused = true)
    {
        AutomationClock.SetPaused(paused);
        return new { Paused = paused };
    }

    [Tool("Pause, then advance the simulation of a 3D tab by exactly this many seconds in fixed frames, and stay paused. Time is shared by everything in the scene: entities, animation, particles, shaders and auto exposure. A tab reloaded while paused starts again from time zero, so two runs stepped the same way show the same thing. Particle systems are seeded by effect and placement, and as in the engine a replay keeps the seed and draws on from where the last left off, so restarting one in place plays out differently each time unless set_particles pins its seed. Returns the scene time.")]
    private static async Task<object> Step(
        GLSceneViewer viewer,
        [Description("Seconds to simulate.")][Range(0d, 600d, MinimumIsExclusive = true)] float seconds,
        [Description("Seconds per simulated frame. Defaults to one entity tick, 1/64.")][Range(0.002, 0.125)] float timestep = 1f / 64f,
        CancellationToken cancellationToken = default)
    {
        var stepping = AutomationClock.Step(seconds, timestep);

        try
        {
            // In batches, because each request is a round trip to the render thread
            while (!stepping.IsCompleted)
            {
                await RenderFrames(viewer, 16, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            AutomationClock.Abandon();
            throw;
        }

        if (stepping.IsCanceled)
        {
            throw new ToolException("The step was abandoned by pause or by another step.");
        }

        // The last stepped frame simulated and drew, but one more makes sure the window shows it
        await RenderFrames(viewer, 1, cancellationToken).ConfigureAwait(false);

        return new { Time = viewer.Renderer.Uptime };
    }

    [Tool("Inspect the particle systems of a 3D tab: whether each is paused or finished, its age, live particle count with its children, control points, and the renderer classes it uses that are not implemented and so draw nothing.")]
    private async Task<object> GetParticles(
        GLSceneViewer viewer,
        [Description("Only systems played by this entity.")] int? entity = null,
        [Description("Only systems within 'radius' of this world point, nearest first.")] Vector3? near = null,
        [Description("Distance from 'near'.")][Range(0, double.MaxValue)] float radius = 512f,
        [Description("Most systems to return.")][Range(1, int.MaxValue)] int limit = 20,
        [Description("Also give each system's tree of child systems, each with its own particle count, age, and the average color, alpha, radius and position of its particles.")] bool tree = false,
        CancellationToken cancellationToken = default)
    {
        // Between frames, so each system is read in one consistent state
        return await WithGl<object>(viewer, () =>
        {
            var data = EntityData(viewer, entity);
            var systems = AllNodes(viewer).OfType<ParticleSceneNode>()
                .Where(node => data == null || ReferenceEquals(node.EntityData, data))
                .Select(node => (Node: node, Position: Vector3.Transform(node.Transform.Translation, node.Scene.ToViewerWorld)))
                .Where(system => near == null || Vector3.Distance(system.Position, near.Value) <= radius)
                .OrderBy(system => near == null ? 0f : Vector3.Distance(system.Position, near.Value))
                .ToList();

            return new
            {
                Systems = systems.Take(limit).Select(system =>
                {
                    var simulation = system.Node.ParticleSimulation;
                    var state = simulation.RenderState;

                    return new
                    {
                        Name = system.Node.Name ?? simulation.Name,
                        Node = nodeIds.IdOf(system.Node),
                        Entity = EntityId(viewer, system.Node.EntityData),
                        Hidden = HiddenReason(system.Node),
                        Paused = Flag(system.Node.IsPaused),
                        Finished = Flag(system.Node.IsFinished),
                        state.Age,
                        Particles = CountParticles(simulation),
                        system.Position,

                        // Points below the highest one used are created at zero whether or not anything sets them
                        ControlPoints = Enumerable.Range(0, state.HighestControlPoint + 1)
                            .Select(i => (Index: i, state.GetControlPoint(i).Position))
                            .Where(static point => point.Index == 0 || point.Position != Vector3.Zero)
                            .ToDictionary(static point => point.Index, static point => point.Position),
                        SkippedRenderers = system.Node.SkippedRendererClasses.ToList(),
                        Tree = tree ? DescribeParticleTree(simulation, system.Node.Scene.ToViewerWorld) : null,
                    };
                }).ToList(),
                Total = systems.Count,
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    private static int CountParticles(ParticleSystemSimulation simulation) => simulation.Particles.Count + simulation.Children.Sum(CountParticles);

    private static object DescribeParticleTree(ParticleSystemSimulation simulation, Matrix4x4 toViewerWorld)
    {
        var particles = simulation.Particles.Current.ToArray();

        return new
        {
            simulation.Name,
            Particles = particles.Length,
            simulation.RenderState.Age,
            Finished = Flag(simulation.IsFinished()),
            Average = particles.Length == 0 ? null : new
            {
                Color = new Vector3(particles.Average(static p => p.Color.X), particles.Average(static p => p.Color.Y), particles.Average(static p => p.Color.Z)),
                Alpha = particles.Average(static p => p.Alpha),
                Radius = particles.Average(static p => p.Radius),
                Position = Vector3.Transform(particles.Aggregate(Vector3.Zero, static (sum, p) => sum + p.Position) / particles.Length, toViewerWorld),
            },
            Children = simulation.Children.Select(child => DescribeParticleTree(child, toViewerWorld)).ToList(),
        };
    }

    [Tool("Add a particle system to a 3D tab, such as an effect on a map, loaded through the tab's files. Plays once by default, as an effect dispatched in game does. To place it on a map's ground, trace down onto the spot and pass the hit position. Particles do not emit while paused, so step to run it.")]
    private async Task<object> SpawnParticle(
        GLSceneViewer viewer,
        [Description("The particle system, such as particles/explosions_fx/explosion_basic.vpcf.")] string path,
        [Description("Where control point 0 goes.")] Vector3? position = null,
        [Description("Orientation of control point 0.")] Vector3? angles = null,
        [Description(ControlPointsDescription)] Dictionary<int, Vector3>? controlPoints = null,
        [Description("Random seed that every replay takes, so every run plays out the same.")][Range(0, 4095)] int? seed = null,
        [Description("Start over once finished.")] bool loop = false,
        [Description("Play it under its preview control points and with its preview model, as the particle viewer does, instead of its game configuration.")] bool preview = false,
        CancellationToken cancellationToken = default)
    {
        var scene = viewer.Renderer.Scene;
        CheckControlPoints(controlPoints);

        // Read before taking the GL context, which would hold up rendering for as long
        var system = await Task.Run(() => scene.RendererContext.FileLoader.LoadFileCompiled(path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal) ? path[..^GameFileLoader.CompiledFileSuffix.Length] : path)?.DataBlock as ParticleSystem, cancellationToken).ConfigureAwait(false)
            ?? throw new ToolException($"'{path}' could not be loaded as a particle system by this tab. get_log may say why.");

        var node = await WithGl(viewer, () =>
        {
            var spawned = new ParticleSceneNode(scene, system, preview: preview)
            {
                Name = path,
                LayerName = Scene.ParticlesLayerName,
                Loop = loop,
                Transform = EntityTransformHelper.ToRigidTransformationMatrix(angles ?? Vector3.Zero, position ?? Vector3.Zero),
            };

            SetUpParticles(spawned, seed, controlPoints, restart: seed != null);
            scene.Add(spawned, true);
            spawnedParticles.AddOrUpdate(spawned, path);

            // The node adds its preview model as it is built, before it has a layer to share
            spawned.PreviewModel?.LayerName = spawned.LayerName;

            return spawned;
        }, cancellationToken).ConfigureAwait(false);

        // Nodes get their id when the scene next updates
        await RenderFrames(viewer, 1, cancellationToken).ConfigureAwait(false);

        return new
        {
            Node = nodeIds.IdOf(node),
            SkippedRenderers = node.SkippedRendererClasses.ToList(),
        };
    }

    [Tool("Change the particle systems of a 3D tab, the ones named by 'nodes' or else all of them: pin their random seed, move their control points, restart them, or remove ones spawn_particle added. A control point a map entity drives is set back by that entity on the next frame. A particle tab's own playback buttons are sidebar controls.", Redraws = true)]
    private object SetParticles(
        GLSceneViewer viewer,
        [Description("Particle node ids from spawn_particle or get_particles.")] int[]? nodes = null,
        [Description("Random seed that every replay takes from now on, each child its own derived from it, or -1 to unpin, so that each replay draws on from the last as the engine does. Shows from the next restart.")][Range(-1, 4095)] int? seed = null,
        [Description(ControlPointsDescription)] Dictionary<int, Vector3>? controlPoints = null,
        [Description("Start the systems over. Defaults to true when 'seed' is given.")] bool? restart = null,
        [Description("Remove the systems from the tab, all that spawn_particle added when 'nodes' is not given. Only those can be removed.")] bool remove = false)
    {
        CheckControlPoints(controlPoints);

        var targets = nodes?.Select(id => NodeById(viewer, id) as ParticleSceneNode ?? throw new ToolException($"Node {id} is not a particle system.")).ToList()
            ?? [.. AllNodes(viewer).OfType<ParticleSceneNode>().Where(node => !remove || spawnedParticles.TryGetValue(node, out _))];

        if (!remove)
        {
            targets.ForEach(node => SetUpParticles(node, seed, controlPoints, restart ?? seed != null));
            return new { Nodes = targets.Select(nodeIds.IdOf).ToList() };
        }

        if (targets.FirstOrDefault(node => !spawnedParticles.TryGetValue(node, out _)) is { } mapSystem)
        {
            throw new ToolException($"Node {nodeIds.IdOf(mapSystem)} was not added by spawn_particle. hidden_nodes stops drawing it.");
        }

        viewer.SelectedNodeRenderer?.SelectNode(null);

        foreach (var node in targets)
        {
            spawnedParticles.Remove(node);

            if (node.PreviewModel is { } model)
            {
                node.Scene.Remove(model, true);
                model.Delete();
            }

            node.Scene.Remove(node, true);
            node.Delete();
        }

        return new { Removed = targets.Select(nodeIds.IdOf).ToList() };
    }

    private static void CheckControlPoints(Dictionary<int, Vector3>? controlPoints)
    {
        if (controlPoints?.Keys.Where(static index => index is < 0 or > MaxControlPoint).Select(static index => (int?)index).FirstOrDefault() is { } index)
        {
            throw new ArgumentException($"Control point {index} is out of range, which is 0 to {MaxControlPoint}.");
        }
    }

    /// <param name="seed">The seed to pin, -1 to go back to a fresh one per replay, or null to leave it.</param>
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

    [Tool("List the functions of a particle system tab by group, as its sidebar does: each class with whether the viewer implements it, the class it was upgraded from, and the ones the format upgrade removed, and its child systems.")]
    private static object ListParticleFunctions(GLParticleViewer viewer)
    {
        var lists = viewer.functionLists ?? throw new ToolException("The particle system has not loaded yet.");
        var system = viewer.particleSystem;

        return new
        {
            Groups = GLParticleViewer.FunctionGroups
                .Where(group => lists.GetValueOrDefault(group.ListName) is { Count: > 0 })
                .ToDictionary(static group => JsonNamingPolicy.SnakeCaseLower.ConvertName(group.ListName[2..]), group => lists[group.ListName].Select(function => new
                {
                    function.Class,
                    Status = function.RemovedByUpgrade ? "removed" : group.IsSupported(function.Class) ? "supported" : "unsupported",
                    Was = function.RemovedByUpgrade ? null : function.OriginalClass,
                }).ToList()),
            Children = system.GetChildren()
                .Where(static child => !string.IsNullOrEmpty(child.GetStringProperty("m_ChildRef")))
                .Select(child => new { Ref = child.GetStringProperty("m_ChildRef"), Disabled = Flag(!system.IsChildEnabled(child)) })
                .ToList(),
        };
    }
}
#endif
