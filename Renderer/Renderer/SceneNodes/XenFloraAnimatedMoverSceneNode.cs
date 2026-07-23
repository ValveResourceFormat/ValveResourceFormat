using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// A single stop along a <c>xen_flora_animatedmover</c>'s path, resolved from a <c>path_corner</c> entity.
/// </summary>
/// <param name="Position">World-space position of this path node.</param>
/// <param name="Speed">Speed override for the leg leaving this node, or 0 to use the mover's own speed.</param>
/// <param name="Wait">Seconds to pause at this node before continuing.</param>
public readonly record struct FloraMoverPathNode(Vector3 Position, float Speed, float Wait);

/// <summary>
/// Animates a <c>xen_flora_animatedmover</c> model along a chain of <c>path_corner</c> nodes.
/// </summary>
public class XenFloraAnimatedMoverSceneNode : ModelSceneNode
{
    private readonly IReadOnlyList<FloraMoverPathNode> path;
    private readonly int loopBackIndex;
    private readonly bool loop;
    private readonly bool faceForward;
    private readonly float speed;
    private readonly float arrivalRadius;
    private readonly Vector3 localOffset;
    private readonly Matrix4x4 scaleMatrix;

    private int segmentStartIndex;
    private float distanceIntoSegment;
    private float waitTimer;
    private float startDelay;
    private bool finished;

    private Vector3 currentPosition;
    private Matrix4x4 currentRotation;

    /// <summary>
    /// Initializes a new <see cref="XenFloraAnimatedMoverSceneNode"/> and places it at the start of its path.
    /// Reads <c>loop</c>, <c>face_forward</c>, <c>speed</c>, <c>intercept_radius</c>, <c>uselocaloffset</c>,
    /// <c>min_delay</c> and <c>max_delay</c> straight off <paramref name="entity"/>.
    /// </summary>
    /// <param name="scene">The scene this node belongs to.</param>
    /// <param name="model">The model resource to render.</param>
    /// <param name="skin">The material group (skin) name to activate, or <see langword="null"/> for the default.</param>
    /// <param name="entity">The <c>xen_flora_animatedmover</c> entity being spawned.</param>
    /// <param name="path">The <c>path_corner</c> chain to follow, in order. Fewer than 2 nodes disables movement.</param>
    /// <param name="loopBackIndex">Index to continue from after the last node, when the path data itself forms a cycle; -1 if none.</param>
    /// <param name="authoredTransform">The entity's authored world transform (scale, rotation, and origin).</param>
    public XenFloraAnimatedMoverSceneNode(
        Scene scene,
        Model model,
        string? skin,
        Entity entity,
        IReadOnlyList<FloraMoverPathNode> path,
        int loopBackIndex,
        Matrix4x4 authoredTransform)
        : base(scene, model, skin)
    {
        EntityData = entity;

        this.path = path;
        this.loopBackIndex = loopBackIndex;
        loop = entity.GetBooleanProperty("loop", true);
        faceForward = entity.GetBooleanProperty("face_forward", true);
        speed = entity.GetFloatProperty("speed");

        var interceptRadius = entity.GetFloatProperty("intercept_radius", -1f);
        arrivalRadius = interceptRadius > 0f ? interceptRadius : 1f;

        var useLocalOffset = entity.GetBooleanProperty("uselocaloffset");
        var minDelay = entity.GetFloatProperty("min_delay");
        var maxDelay = entity.GetFloatProperty("max_delay");

        var hasTransform = Matrix4x4.Decompose(authoredTransform, out var scale, out var rotation, out var authoredPosition);
        scaleMatrix = hasTransform ? Matrix4x4.CreateScale(scale) : Matrix4x4.Identity;
        currentRotation = hasTransform ? Matrix4x4.CreateFromQuaternion(rotation) : Matrix4x4.Identity;

        localOffset = useLocalOffset && path.Count > 0
            ? authoredPosition - path[0].Position
            : Vector3.Zero;

        currentPosition = path.Count > 0 ? path[0].Position + localOffset : authoredPosition;

        startDelay = maxDelay > minDelay ? minDelay + (Random.Shared.NextSingle() * (maxDelay - minDelay)) : minDelay;

        Transform = scaleMatrix * currentRotation * Matrix4x4.CreateTranslation(currentPosition);

        var firstAnimation = Animations.Values.FirstOrDefault();
        if (firstAnimation != null)
        {
            SetAnimation(firstAnimation);
        }
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        AdvancePath(context.Timestep);
        Transform = scaleMatrix * currentRotation * Matrix4x4.CreateTranslation(currentPosition);

        base.Update(context);
    }

    private void AdvancePath(float dt)
    {
        if (path.Count < 2 || finished)
        {
            return;
        }

        if (startDelay > 0f)
        {
            startDelay -= dt;
            return;
        }

        if (waitTimer > 0f)
        {
            waitTimer -= dt;
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

            toIndex = loopBackIndex >= 0 ? loopBackIndex : 0;
        }

        var toNode = path[toIndex];
        var segmentVector = toNode.Position - fromNode.Position;
        var segmentLength = segmentVector.Length();
        var travelSpeed = fromNode.Speed > 0f ? fromNode.Speed : speed;

        if (segmentLength <= arrivalRadius || travelSpeed <= 0f)
        {
            AdvanceToNode(toIndex, toNode);
            return;
        }

        distanceIntoSegment += travelSpeed * dt;

        if (distanceIntoSegment >= segmentLength - arrivalRadius)
        {
            AdvanceToNode(toIndex, toNode);
            return;
        }

        currentPosition = Vector3.Lerp(fromNode.Position, toNode.Position, distanceIntoSegment / segmentLength) + localOffset;

        if (faceForward)
        {
            currentRotation = DirectionToRotation(Vector3.Normalize(segmentVector));
        }
    }

    private void AdvanceToNode(int index, FloraMoverPathNode node)
    {
        segmentStartIndex = index;
        distanceIntoSegment = 0f;
        waitTimer = node.Wait;
        currentPosition = node.Position + localOffset;
    }

    // Builds the same pitch/yaw/roll rotation convention as EntityTransformHelper, starting from a travel
    // direction instead of authored angles (the inverse of EntityTransformHelper.QAngleToForwardDirection).
    private static Matrix4x4 DirectionToRotation(Vector3 direction)
    {
        var horizontalLength = MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        var yaw = float.RadiansToDegrees(MathF.Atan2(direction.Y, direction.X));
        var pitch = float.RadiansToDegrees(MathF.Atan2(-direction.Z, horizontalLength));

        return EntityTransformHelper.CreateRotationMatrixFromEulerAngles(new Vector3(pitch, yaw, 0f));
    }
}
