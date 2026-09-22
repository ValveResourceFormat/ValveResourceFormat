using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// HL:A's <c>xen_flora_animatedmover</c>: a creature that travels a chain of <c>path_corner</c> entities,
/// optionally trailing <c>particle_effect</c>.
/// </summary>
public sealed class XenFloraAnimatedMover : BaseModelEntity
{
    /// <summary>A stop along the path, in the mover's own space.</summary>
    /// <param name="Position">Where the stop is.</param>
    /// <param name="Speed">Speed of the leg leaving this stop, or 0 for the mover's own speed.</param>
    /// <param name="Wait">Seconds to pause here before going on.</param>
    private readonly record struct PathNode(Vector3 Position, float Speed, float Wait);

    private readonly List<PathNode> path = [];
    private int loopBackIndex;
    private bool loop;
    private bool faceForward;
    private float speed;
    private float arrivalRadius;
    private Vector3 localOffset;

    private int segmentStartIndex;
    private float distanceIntoSegment;
    private float waitTimer;
    private float startDelay;
    private bool finished;

    /// <summary>Initializes a mover from its keyvalues.</summary>
    public XenFloraAnimatedMover(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // Flying scenery, which nothing collides with
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        loop = KeyValues.GetBooleanProperty("loop", true);
        faceForward = KeyValues.GetBooleanProperty("face_forward", true);
        speed = KeyValues.GetFloatProperty("speed");

        var interceptRadius = KeyValues.GetFloatProperty("intercept_radius", -1f);
        arrivalRadius = interceptRadius > 0f ? interceptRadius : 1f;

        var minDelay = KeyValues.GetFloatProperty("min_delay");
        var maxDelay = KeyValues.GetFloatProperty("max_delay");
        startDelay = maxDelay > minDelay ? minDelay + (Random.Shared.NextSingle() * (maxDelay - minDelay)) : minDelay;

        if (ModelNode is not { } modelNode)
        {
            return;
        }

        if (KeyValues.GetBooleanProperty("disable_shadows"))
        {
            modelNode.Flags |= ObjectTypeFlags.NoShadows;
        }

        if (modelNode.Animations.Values.FirstOrDefault() is { } animation)
        {
            modelNode.SetAnimation(animation);
        }

        // The model carries the trail, keeping the effect's own orientation
        if (CreateEffect(KeyValues.GetStringProperty("particle_effect"), playedByEntity: false) is { } effect)
        {
            AddNode(effect, followsEntity: false);
            modelNode.AttachNode(effect, rotation: Quaternion.Identity);
        }
    }

    // In Activate, as the path_corner chain may be authored after the mover
    /// <inheritdoc/>
    public override void Activate()
    {
        var pathStart = KeyValues.GetStringProperty("path_start");

        ResolvePath(pathStart);

        if (path.Count == 0)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{Target}' has no valid path starting at '{PathStart}', it will not move", Classname, TargetName, pathStart);
            return;
        }

        localOffset = KeyValues.GetBooleanProperty("uselocaloffset") ? Origin - path[0].Position : Vector3.Zero;

        SetOriginAndAngles(path[0].Position + localOffset, Angles);
        SnapInterpolation();
    }

    /// <summary>Moves along the path by one tick, waiting at stops and turning to face the way it goes.</summary>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (path.Count < 2 || finished)
        {
            return;
        }

        if (startDelay > 0f)
        {
            startDelay -= tickInterval;
            return;
        }

        if (waitTimer > 0f)
        {
            waitTimer -= tickInterval;
            return;
        }

        var fromNode = path[segmentStartIndex];
        var toIndex = segmentStartIndex + 1;

        if (toIndex >= path.Count)
        {
            if (!loop)
            {
                finished = true;
                return;
            }

            toIndex = loopBackIndex;
        }

        var toNode = path[toIndex];
        var segment = toNode.Position - fromNode.Position;
        var segmentLength = segment.Length();
        var travelSpeed = fromNode.Speed > 0f ? fromNode.Speed : speed;

        if (segmentLength <= arrivalRadius || travelSpeed <= 0f)
        {
            ArriveAt(toIndex);
            return;
        }

        distanceIntoSegment += travelSpeed * tickInterval;

        if (distanceIntoSegment >= segmentLength - arrivalRadius)
        {
            ArriveAt(toIndex);
            return;
        }

        var angles = faceForward
            ? EntityTransformHelper.ForwardDirectionToEulerAngles(segment)
            : Angles;

        SetOriginAndAngles(Vector3.Lerp(fromNode.Position, toNode.Position, distanceIntoSegment / segmentLength) + localOffset, angles);
    }

    private void ArriveAt(int index)
    {
        segmentStartIndex = index;
        distanceIntoSegment = 0f;
        waitTimer = path[index].Wait;

        SetOriginAndAngles(path[index].Position + localOffset, Angles);
    }

    // Walks the target chain starting at the path_corner named startName, in the same way path_track/
    // func_tracktrain follow theirs. The loop back index is set when the chain itself points back to an
    // already-visited node (an authored closed loop), so a looping mover can honor that entry point
    // instead of always restarting from the first node.
    private void ResolvePath(string? startName)
    {
        // Stops are placed in the world, the mover's origin is in the space its spawner put it in
        if (!Matrix4x4.Invert(ParentTransform, out var worldToLocal))
        {
            return;
        }

        var visited = new Dictionary<BaseEntity, int>();
        var current = FindPathCorner(startName);

        while (current != null)
        {
            if (visited.TryGetValue(current, out var existingIndex))
            {
                loopBackIndex = existingIndex;
                return;
            }

            visited[current] = path.Count;
            path.Add(new PathNode(
                Vector3.Transform(current.Transform.Translation, worldToLocal),
                current.Data?.GetFloatProperty("speed") ?? 0f,
                current.Data?.GetFloatProperty("wait") ?? 0f));

            current = FindPathCorner(current.Data?.GetStringProperty("target"));
        }
    }

    // Only in this entity's own spawn group: a 3D sky shares names with the map it is placed in
    private BaseEntity? FindPathCorner(string? targetName)
    {
        if (string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        var candidate = EntitySystem.FindAllByTargetName(targetName, Scene).FirstOrDefault();

        return candidate?.Classname.Equals("path_corner", StringComparison.OrdinalIgnoreCase) == true ? candidate : null;
    }
}
