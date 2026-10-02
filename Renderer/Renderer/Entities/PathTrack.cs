using System.Linq;
using ValveResourceFormat.ResourceTypes;
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
        /// <summary>A train cannot pass it until it is enabled.</summary>
        Disabled = 1,

        /// <summary><c>OnPass</c> fires only the first time a train passes.</summary>
        FireOnce = 2,

        /// <summary>The alternate path joins here from behind rather than leaving forward.</summary>
        BranchReverse = 4,

        /// <summary>A train passing it loses user control.</summary>
        DisableTrain = 8,

        /// <summary>A train heading here jumps to it instead of travelling.</summary>
        TeleportToThis = 16,
    }

    /// <summary>How a train blending its orientation between nodes faces at this one.</summary>
    public enum Orientation
    {
        /// <summary>No orientation of its own: the direction of travel.</summary>
        None = 0,

        /// <summary>The direction of travel.</summary>
        FaceDirectionOfMotion = 1,

        /// <summary>This node's own angles.</summary>
        FaceAngles = 2,
    }

    /// <summary>Gets the node after this one along <c>target</c>.</summary>
    public PathTrack? Next { get; private set; }

    /// <summary>Gets the node whose <c>target</c> is this one.</summary>
    public PathTrack? Previous { get; private set; }

    /// <summary>Gets the branch taken while the alternate path is enabled.</summary>
    public PathTrack? AlternatePath { get; private set; }

    /// <summary>Gets or sets whether trains take <see cref="AlternatePath"/>.</summary>
    public bool AlternatePathEnabled { get; set; }

    /// <summary>Gets or sets whether a train can pass this node.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the speed a train takes on passing this node, or 0 to keep its own.</summary>
    public float Speed { get; private set; }

    /// <summary>Gets how a blending train faces at this node.</summary>
    public Orientation OrientationType { get; private set; }

    private bool hasFired;

    /// <summary>Initializes a <c>path_track</c> from its keyvalues.</summary>
    public PathTrack(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Speed = KeyValues.GetFloatProperty("speed");
        OrientationType = (Orientation)KeyValues.GetInt32Property("orientationtype", 1);
        IsEnabled = !HasSpawnFlags(SpawnFlag.Disabled);
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        Next = Find(KeyValues.GetStringProperty("target"));
        AlternatePath = Find(KeyValues.GetStringProperty("altpath"));

        // Every node links back, so a train can run either way
        if (Next != null && Next.Previous == null)
        {
            Next.Previous = this;
        }
    }

    // Only in this entity's own spawn group: a 3D sky shares names with the map it is placed in
    private PathTrack? Find(string? targetName)
        => string.IsNullOrEmpty(targetName) ? null : EntitySystem.FindAllByTargetName(targetName, Scene).OfType<PathTrack>().FirstOrDefault();

    /// <summary>Gets the node a train moving the given way goes to next, taking the branch while it is enabled.</summary>
    public PathTrack? GetNext(bool forward)
    {
        var branch = AlternatePathEnabled && AlternatePath != null && HasSpawnFlags(SpawnFlag.BranchReverse) != forward;

        return forward
            ? branch ? AlternatePath : Next
            : branch ? AlternatePath : Previous;
    }

    /// <summary>Gets the angles a blending train has at this node when moving the given way.</summary>
    public Vector3 GetOrientation(bool forward)
    {
        if (OrientationType == Orientation.FaceAngles)
        {
            return WorldAngles;
        }

        var from = this;
        var to = GetNext(forward);

        if (to == null)
        {
            from = GetNext(!forward);
            to = this;
        }

        if (from == null)
        {
            return WorldAngles;
        }

        var direction = to.WorldOrigin - from.WorldOrigin;

        if (!forward)
        {
            direction = -direction;
        }

        return direction == Vector3.Zero ? WorldAngles : EntityTransformHelper.ForwardDirectionToEulerAngles(direction);
    }

    /// <summary>Reports a train passing, firing <c>OnPass</c> unless it only fires once and already has.</summary>
    internal void Pass(BaseEntity train)
    {
        if (HasSpawnFlags(SpawnFlag.FireOnce) && hasFired)
        {
            return;
        }

        hasFired = true;
        EntitySystem.TriggerOutput(this, "OnPass", train);
    }

    /// <summary>Lets trains pass.</summary>
    [EntityInput("EnablePath")]
    protected void InputEnablePath(EntityInputData data) => IsEnabled = true;

    /// <summary>Stops trains passing.</summary>
    [EntityInput("DisablePath")]
    protected void InputDisablePath(EntityInputData data) => IsEnabled = false;

    /// <summary>Toggles whether trains can pass.</summary>
    [EntityInput("TogglePath")]
    protected void InputTogglePath(EntityInputData data) => IsEnabled = !IsEnabled;

    /// <summary>Sends trains down the alternate path.</summary>
    [EntityInput("EnableAlternatePath")]
    protected void InputEnableAlternatePath(EntityInputData data) => AlternatePathEnabled = true;

    /// <summary>Sends trains down the main path.</summary>
    [EntityInput("DisableAlternatePath")]
    protected void InputDisableAlternatePath(EntityInputData data) => AlternatePathEnabled = false;

    /// <summary>Toggles which path trains take.</summary>
    [EntityInput("ToggleAlternatePath")]
    protected void InputToggleAlternatePath(EntityInputData data) => AlternatePathEnabled = !AlternatePathEnabled;
}
