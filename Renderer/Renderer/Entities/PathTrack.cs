using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>path_track</c>, Source's <c>CPathTrack</c>. A node of the track a <see cref="FuncTrackTrain"/> runs
/// along, linked to the next one by <c>target</c> and optionally branching to an <c>altpath</c>.
/// </summary>
public class PathTrack : BaseEntity
{
    /// <summary>What a <c>path_track</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>A train looking ahead with <c>move</c> set cannot pass it.</summary>
        Disabled = 0x1,

        /// <summary>The alternate path is taken going backward rather than forward.</summary>
        AltReverse = 0x4,

        /// <summary>A train arriving here loses user control and takes node speeds from then on.</summary>
        DisableTrain = 0x8,

        /// <summary>A train reaching the node before this one jumps to it instead of travelling.</summary>
        TeleportToThis = 0x10,

        /// <summary>Set at runtime while the alternate path is enabled.</summary>
        AltPathEnabled = 0x8000,
    }

    /// <summary>How a train orients itself at this node, the <c>orientationtype</c> keyvalue.</summary>
    public enum Orientation
    {
        /// <summary>No orientation of its own: the direction of the path.</summary>
        Fixed = 0,

        /// <summary>The direction of the path.</summary>
        FacePath = 1,

        /// <summary>This node's own angles.</summary>
        FacePathAngles = 2,
    }

    /// <summary>Gets the speed a train takes on passing this node, or 0 to keep its own.</summary>
    public float Speed { get; private set; }

    /// <summary>Gets how a train orients itself at this node.</summary>
    public Orientation OrientationType { get; private set; } = Orientation.FacePath;

    /// <summary>Gets whether a train looking ahead with <c>move</c> set can pass this node.</summary>
    public bool IsEnabled => !HasSpawnFlags(SpawnFlag.Disabled);

    private PathTrack? next;
    private PathTrack? previous;
    private PathTrack? alternatePath;
    private string? altName;

    /// <summary>Initializes a <c>path_track</c> from its keyvalues.</summary>
    public PathTrack(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Speed = KeyValues.GetFloatProperty("speed");
        OrientationType = (Orientation)KeyValues.GetInt32Property("orientationtype", (int)Orientation.FacePath);

        var altPath = KeyValues.GetStringProperty("altpath");
        altName = string.IsNullOrEmpty(altPath) ? null : altPath;

        next = null;
        previous = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only a node with a name links itself, and only forward: the link back is made by the node before it.
    /// </remarks>
    public override void Activate()
    {
        if (TargetName != null)
        {
            Link();
        }
    }

    private void Link()
    {
        var target = KeyValues.GetStringProperty("target");

        if (!string.IsNullOrEmpty(target))
        {
            var found = Find(target);

            if (found == this)
            {
                EntitySystem.Logger.LogWarning("ERROR: path_track ({Name}) refers to itself as a target!", TargetName);
            }
            else if (found != null)
            {
                next = found as PathTrack;
                next?.SetPrevious(this);
            }
            else
            {
                EntitySystem.Logger.LogWarning("Dead end link: {Target}", target);
            }
        }

        if (altName != null && Find(altName) is { } alternate)
        {
            alternatePath = alternate as PathTrack;

            if (alternatePath != null && !NamesMatch(TargetName, alternatePath.altName))
            {
                alternatePath.previous = this;
            }
        }
    }

    // A branch that points back at its own alternate must not take over as the main predecessor
    private void SetPrevious(PathTrack? node)
    {
        if (node != null && !NamesMatch(node.TargetName, altName))
        {
            previous = node;
        }
    }

    private static bool NamesMatch(string? a, string? b)
        => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    // Only in this entity's own spawn group: a 3D sky shares names with the map it is placed in
    private BaseEntity? Find(string targetName) => EntitySystem.FindByTargetName(targetName, Scene);

    private bool IsAlternatePathUsable => alternatePath is { IsRemoved: false };

    /// <summary>Gets the node after this one, the alternate path while it is enabled going forward.</summary>
    public PathTrack? GetNext()
        => IsAlternatePathUsable && (SpawnFlags & 0x8004) == 0x8000 ? alternatePath : Live(next);

    /// <summary>Gets the node before this one, the alternate path while it is enabled going backward.</summary>
    public PathTrack? GetPrevious()
        => IsAlternatePathUsable && (SpawnFlags & 0x8004) == 0x8004 ? alternatePath : Live(previous);

    /// <summary>Gets <see cref="GetNext"/> going forward, otherwise <see cref="GetPrevious"/>.</summary>
    public PathTrack? GetNextInDir(bool forward) => forward ? GetNext() : GetPrevious();

    private static PathTrack? Live(PathTrack? node) => node is { IsRemoved: false } ? node : null;

    /// <summary>
    /// Gets <paramref name="node"/>, or <see langword="null"/> when there is none or <paramref name="testFlag"/> is set and it is disabled.
    /// </summary>
    public static PathTrack? ValidPath(PathTrack? node, bool testFlag)
    {
        if (node == null)
        {
            return null;
        }

        if (testFlag && node.HasSpawnFlags(SpawnFlag.Disabled))
        {
            return null;
        }

        return node;
    }

    /// <summary>
    /// Gets the angles a train has at this node: the node's own angles for <see cref="Orientation.FacePathAngles"/>,
    /// otherwise the direction of the path from this node, or into it at the end of the path.
    /// </summary>
    public Vector3 GetOrientation(bool forward)
    {
        if (OrientationType == Orientation.FacePathAngles)
        {
            return Angles;
        }

        var from = this;
        var to = GetNextInDir(forward);

        if (to == null)
        {
            from = GetNextInDir(!forward);
            to = this;
        }

        if (from == null)
        {
            return Angles;
        }

        return VectorAngles(to.Origin - from.Origin);
    }

    // Unlike EntityTransformHelper.ForwardDirectionToEulerAngles, this is only vertical when exactly so,
    // and gives angles in [0, 360)
    private static Vector3 VectorAngles(Vector3 forward)
    {
        float pitch;
        float yaw;

        if (forward.Y == 0f && forward.X == 0f)
        {
            yaw = 0f;
            pitch = forward.Z > 0f ? 270f : 90f;
        }
        else
        {
            yaw = MathF.Atan2(forward.Y, forward.X) * (180f / MathF.PI);

            if (yaw < 0f)
            {
                yaw += 360f;
            }

            pitch = MathF.Atan2(-forward.Z, MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y)) * (180f / MathF.PI);

            if (pitch < 0f)
            {
                pitch += 360f;
            }
        }

        return new Vector3(pitch, yaw, 0f);
    }

    /// <summary>
    /// Walks the path from this node, starting at <paramref name="origin"/>, for |<paramref name="dist"/>|
    /// units, forward for a positive distance and backward for a negative one.
    /// </summary>
    /// <param name="origin">Where to start, in the move parent frame; receives the point reached.</param>
    /// <param name="dist">The signed distance to walk.</param>
    /// <param name="move">Whether disabled nodes stop the walk. Without it, running off the end of the path
    /// carries on in a straight line past the last node.</param>
    /// <param name="nextNext">Receives the node after the returned one.</param>
    /// <returns>The last node passed, the one behind the point reached, or <see langword="null"/> at the end of the path.</returns>
    public PathTrack? LookAhead(ref Vector3 origin, float dist, bool move, out PathTrack? nextNext)
    {
        nextNext = null;

        var current = this;
        var position = origin;
        var remaining = MathF.Abs(dist);
        var forward = dist >= 0f;

        while (remaining > 0f)
        {
            var nextNode = current.GetNextInDir(forward);

            if (nextNode == null)
            {
                if (!move && current.GetNextInDir(!forward) is { } previousNode)
                {
                    var direction = current.Origin - previousNode.Origin;
                    var directionLength = Length(direction);

                    if (directionLength != 0f)
                    {
                        direction *= 1f / directionLength;
                    }

                    origin = current.Origin + direction * remaining;
                }

                return null;
            }

            if (move && nextNode.HasSpawnFlags(SpawnFlag.Disabled))
            {
                return null;
            }

            var delta = nextNode.Origin - position;
            var length = Length(delta);

            if (length == 0f && ValidPath(nextNode.GetNextInDir(forward), move) == null)
            {
                nextNext = null;
                return remaining != dist ? current.GetNextInDir(forward) : null;
            }

            if (length > remaining)
            {
                origin = position + delta * (remaining / length);
                break;
            }

            remaining -= length;
            position = nextNode.Origin;
            origin = position;
            current = nextNode;
        }

        nextNext = current.GetNextInDir(forward);
        return current;
    }

    private static float Length(Vector3 v) => MathF.Sqrt(v.Z * v.Z + v.Y * v.Y + v.X * v.X);

    /// <summary>Reports a train passing, the <c>InPass</c> input the train sends.</summary>
    internal void Pass(BaseEntity? activator) => EntitySystem.TriggerOutput(this, "OnPass", activator);

    /// <summary>Fires <c>OnPass</c>, as a train passing does.</summary>
    [EntityInput("InPass")]
    protected void InputInPass(EntityInputData data) => Pass(data.Activator);

    /// <summary>Lets trains pass.</summary>
    [EntityInput("EnablePath")]
    protected void InputEnablePath(EntityInputData data) => SpawnFlags &= ~(uint)SpawnFlag.Disabled;

    /// <summary>Stops trains passing.</summary>
    [EntityInput("DisablePath")]
    protected void InputDisablePath(EntityInputData data) => SpawnFlags |= (uint)SpawnFlag.Disabled;

    /// <summary>Toggles whether trains can pass.</summary>
    [EntityInput("TogglePath")]
    protected void InputTogglePath(EntityInputData data) => SpawnFlags ^= (uint)SpawnFlag.Disabled;

    /// <summary>Sends trains down the alternate path, when there is one.</summary>
    [EntityInput("EnableAlternatePath")]
    protected void InputEnableAlternatePath(EntityInputData data)
    {
        if (IsAlternatePathUsable)
        {
            SpawnFlags |= (uint)SpawnFlag.AltPathEnabled;
        }
    }

    /// <summary>Sends trains down the main path.</summary>
    [EntityInput("DisableAlternatePath")]
    protected void InputDisableAlternatePath(EntityInputData data)
    {
        if (IsAlternatePathUsable)
        {
            SpawnFlags &= ~(uint)SpawnFlag.AltPathEnabled;
        }
    }

    /// <summary>Toggles which path trains take, when there is an alternate one.</summary>
    [EntityInput("ToggleAlternatePath")]
    protected void InputToggleAlternatePath(EntityInputData data)
    {
        if (IsAlternatePathUsable)
        {
            SpawnFlags ^= (uint)SpawnFlag.AltPathEnabled;
        }
    }
}
