using System.Linq;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_tracktrain</c>, Source's <c>CFuncTrackTrain</c>. A brush that runs along a chain of
/// <see cref="PathTrack"/> nodes, carrying and pushing whatever stands on it. Not simulated: user control,
/// banking, and the damage it does to whatever blocks it.
/// </summary>
public class FuncTrackTrain : BaseModelEntity
{
    /// <summary>What a <c>func_tracktrain</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>It turns only about the vertical axis.</summary>
        NoPitch = 1,

        /// <summary>The player cannot drive it.</summary>
        NoUserControl = 2,

        /// <summary>Things pass straight through it.</summary>
        Passable = 8,

        /// <summary>It keeps the orientation it spawned with.</summary>
        FixedOrientation = 16,

        /// <summary>Nothing the player does stops it.</summary>
        UnblockableByPlayer = 512,
    }

    /// <summary>How the train turns along the track, the <c>orientationtype</c> keyvalue.</summary>
    public enum OrientationType
    {
        /// <summary>It keeps the orientation it spawned with.</summary>
        Fixed = 0,

        /// <summary>It faces along the track, between its front and back wheels.</summary>
        AtPathTracks = 1,

        /// <summary>It blends linearly between the orientations of the nodes either side.</summary>
        LinearBlend = 2,

        /// <summary>It blends between the orientations of the nodes either side, easing in and out.</summary>
        EaseInEaseOut = 3,
    }

    /// <summary>How the train changes speed between nodes with speeds, the <c>velocitytype</c> keyvalue.</summary>
    public enum VelocityType
    {
        /// <summary>It takes a node's speed on passing it.</summary>
        Instantaneous = 0,

        /// <summary>It blends linearly toward the speed of the node it heads to.</summary>
        LinearBlend = 1,

        /// <summary>It blends toward the speed of the node it heads to, easing in and out.</summary>
        EaseInEaseOut = 2,
    }

    /// <summary>Gets the most it is told to go, the <c>startspeed</c> keyvalue.</summary>
    public float MaxSpeed { get; private set; }

    /// <summary>Gets how fast it moves along the track, never negative.</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>Gets whether it moves along <c>target</c> links rather than back along them.</summary>
    public bool IsForward { get; private set; } = true;

    /// <summary>Gets the node it last passed, the start of the stretch of track it is on.</summary>
    public PathTrack? CurrentPath { get; private set; }

    private OrientationType orientation;
    private VelocityType velocityType;
    private float height;
    private float wheels;

    // How far past CurrentPath it is, toward the next node going forward
    private float distanceAlong;

    // The speed it had when it entered the current stretch, which a blend starts from
    private float segmentEntrySpeed;

    private PathTrack? destination;

    private string? soundStart;
    private string? soundStop;
    private string? soundMove;
    private SoundHandle moveSound;

    /// <summary>Initializes a <c>func_tracktrain</c> from its keyvalues.</summary>
    public FuncTrackTrain(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected internal override bool IsPusher => true;

    /// <inheritdoc/>
    protected override bool PusherForcesThrough => HasSpawnFlags(SpawnFlag.UnblockableByPlayer);

    /// <inheritdoc/>
    public override void Spawn()
    {
        MaxSpeed = KeyValues.GetFloatProperty("startspeed");
        CurrentSpeed = MathF.Abs(KeyValues.GetFloatProperty("speed"));
        height = KeyValues.GetFloatProperty("height");
        wheels = KeyValues.GetFloatProperty("wheels");
        orientation = HasSpawnFlags(SpawnFlag.FixedOrientation) ? OrientationType.Fixed : (OrientationType)KeyValues.GetInt32Property("orientationtype", 1);
        velocityType = (VelocityType)KeyValues.GetInt32Property("velocitytype");

        if (HasSpawnFlags(SpawnFlag.Passable))
        {
            IsSolid = false;
        }

        soundStart = NonEmpty(KeyValues.GetStringProperty("startsound"));
        soundStop = NonEmpty(KeyValues.GetStringProperty("stopsound"));
        soundMove = NonEmpty(KeyValues.GetStringProperty("movesound"));

        foreach (var sound in (string?[])[soundStart, soundStop, soundMove])
        {
            if (sound != null)
            {
                Sound.Cache(sound);
            }
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <inheritdoc/>
    public override void Activate()
    {
        // Without a first node it waits where it was built until something teleports it onto the track
        if (FindPath(KeyValues.GetStringProperty("target")) is { } start)
        {
            PlaceAt(start);
        }

        if (CurrentSpeed > 0f)
        {
            StartMoving();
        }
    }

    private PathTrack? FindPath(string? name)
        => string.IsNullOrEmpty(name) ? null : EntitySystem.FindAllByTargetName(name, Scene).OfType<PathTrack>().FirstOrDefault();

    /// <summary>Moves along the track by this tick's distance, passing nodes on the way.</summary>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (CurrentPath == null || CurrentSpeed <= 0f || tickInterval <= 0f)
        {
            Velocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            return;
        }

        var from = WorldOrigin;
        var fromAngles = WorldAngles;

        BlendSpeed();
        Advance((IsForward ? CurrentSpeed : -CurrentSpeed) * tickInterval);

        if (CurrentPath == null)
        {
            return;
        }

        var to = TrackPoint(0f) + new Vector3(0f, 0f, height);
        var toAngles = FacingAt(fromAngles);

        // Kept for riders: the surface velocity is what carries them along
        Velocity = (to - from) / tickInterval;
        AngularVelocity = new Vector3(
            MathUtils.Wrap(toAngles.X - fromAngles.X, -180f, 180f),
            MathUtils.Wrap(toAngles.Y - fromAngles.Y, -180f, 180f),
            MathUtils.Wrap(toAngles.Z - fromAngles.Z, -180f, 180f)) / tickInterval;

        SetWorldOriginAndAngles(to, toAngles);
    }

    // Walks the track by a signed distance, passing every node it crosses
    private void Advance(float distance)
    {
        var guard = 0;

        while (CurrentPath != null && distance != 0f && guard++ < 64)
        {
            if (distance > 0f)
            {
                var next = CurrentPath.GetNext(true);

                if (next == null || !next.IsEnabled)
                {
                    distanceAlong = 0f;
                    Stop();
                    return;
                }

                var length = Vector3.Distance(CurrentPath.WorldOrigin, next.WorldOrigin);

                if (distanceAlong + distance < length)
                {
                    distanceAlong += distance;
                    return;
                }

                distance -= length - distanceAlong;
                EnterNode(next, forward: true);
            }
            else
            {
                if (distanceAlong + distance > 0f)
                {
                    distanceAlong += distance;
                    return;
                }

                distance += distanceAlong;
                distanceAlong = 0f;

                var reached = CurrentPath;
                var previous = reached.GetNext(false);

                Arrive(reached);

                if (CurrentSpeed <= 0f || CurrentPath != reached)
                {
                    return;
                }

                if (previous == null || !previous.IsEnabled)
                {
                    Stop();
                    return;
                }

                CurrentPath = previous;
                distanceAlong = SegmentLength(previous);
                segmentEntrySpeed = CurrentSpeed;
            }

            if (CurrentSpeed <= 0f)
            {
                return;
            }
        }
    }

    // Reaching a node going forward: it becomes the start of the stretch, and a teleport node after it is jumped to
    private void EnterNode(PathTrack node, bool forward)
    {
        CurrentPath = node;
        distanceAlong = 0f;
        segmentEntrySpeed = CurrentSpeed;

        Arrive(node);

        if (CurrentSpeed <= 0f || CurrentPath != node)
        {
            return;
        }

        if (node.GetNext(forward) is { } after && after.HasSpawnFlags(PathTrack.SpawnFlag.TeleportToThis) && after.IsEnabled)
        {
            JumpToNode(after);
            Arrive(after);
        }
    }

    private void Arrive(PathTrack node)
    {
        node.Pass(this);
        EntitySystem.TriggerOutput(this, "OnNext", this);

        if (velocityType == VelocityType.Instantaneous && node.Speed != 0f && CurrentSpeed > 0f)
        {
            CurrentSpeed = MathF.Abs(node.Speed);
        }

        if (node == destination)
        {
            destination = null;
            Stop();
            EntitySystem.TriggerOutput(this, "OnArrivedAtDestinationNode", this);
        }
    }

    // A blending train eases from the speed it entered with toward the speed of the node ahead
    private void BlendSpeed()
    {
        if (velocityType == VelocityType.Instantaneous || CurrentPath == null)
        {
            return;
        }

        var ahead = IsForward ? CurrentPath.GetNext(true) : CurrentPath;
        var behind = IsForward ? CurrentPath : CurrentPath.GetNext(false);

        if (ahead == null || ahead.Speed == 0f)
        {
            return;
        }

        var length = SegmentLength(CurrentPath);
        var t = length > 0f ? Math.Clamp(distanceAlong / length, 0f, 1f) : 1f;

        if (!IsForward)
        {
            t = 1f - t;
        }

        if (velocityType == VelocityType.EaseInEaseOut)
        {
            t = t * t * (3f - 2f * t);
        }

        var from = behind is { Speed: not 0f } ? MathF.Abs(behind.Speed) : segmentEntrySpeed;
        CurrentSpeed = float.Lerp(from, MathF.Abs(ahead.Speed), t);
    }

    private static float SegmentLength(PathTrack node)
        => node.GetNext(true) is { } next ? Vector3.Distance(node.WorldOrigin, next.WorldOrigin) : 0f;

    // The point on the track a signed distance from where the train is, stopping at either end
    private Vector3 TrackPoint(float offset)
    {
        var node = CurrentPath!;
        var along = distanceAlong + offset;

        for (var guard = 0; guard < 64; guard++)
        {
            var next = node.GetNext(true);
            var length = next != null ? Vector3.Distance(node.WorldOrigin, next.WorldOrigin) : 0f;

            if (along < 0f)
            {
                var previous = node.GetNext(false);

                if (previous == null)
                {
                    return node.WorldOrigin;
                }

                along += SegmentLength(previous);
                node = previous;
                continue;
            }

            if (next == null)
            {
                return node.WorldOrigin;
            }

            if (along <= length)
            {
                return Vector3.Lerp(node.WorldOrigin, next.WorldOrigin, length > 0f ? along / length : 0f);
            }

            along -= length;
            node = next;
        }

        return node.WorldOrigin;
    }

    // Faces along the track ahead of the train, or blends between the orientations of the nodes either side
    private Vector3 FacingAt(Vector3 current)
    {
        switch (orientation)
        {
            case OrientationType.AtPathTracks:
            {
                var reach = wheels > 0f ? wheels : 100f;
                var direction = TrackPoint(reach) - TrackPoint(0f);

                if (direction.X == 0f && direction.Y == 0f)
                {
                    return current;
                }

                var angles = EntityTransformHelper.ForwardDirectionToEulerAngles(direction);
                return HasSpawnFlags(SpawnFlag.NoPitch) ? angles with { X = current.X } : angles;
            }

            case OrientationType.LinearBlend:
            case OrientationType.EaseInEaseOut:
            {
                var node = CurrentPath!;

                if (node.GetNext(true) is not { } next)
                {
                    return current;
                }

                var length = SegmentLength(node);
                var t = length > 0f ? Math.Clamp(distanceAlong / length, 0f, 1f) : 0f;

                if (orientation == OrientationType.EaseInEaseOut)
                {
                    t = t * t * (3f - 2f * t);
                }

                var blended = Quaternion.Slerp(
                    EntityTransformHelper.EulerAnglesToQuaternion(node.GetOrientation(true)),
                    EntityTransformHelper.EulerAnglesToQuaternion(next.GetOrientation(true)),
                    t);

                var angles = EntityTransformHelper.ToEulerAngles(blended);
                return HasSpawnFlags(SpawnFlag.NoPitch) ? angles with { X = current.X } : angles;
            }

            default:
                return current;
        }
    }

    // Snaps onto a node, facing the way the track runs from it
    private void PlaceAt(PathTrack node)
    {
        CurrentPath = node;
        distanceAlong = 0f;
        segmentEntrySpeed = CurrentSpeed;

        var angles = WorldAngles;
        JumpToNode(node);

        if (orientation != OrientationType.Fixed)
        {
            SetWorldOriginAndAngles(WorldOrigin, FacingAt(angles));
        }

        SnapInterpolation();
    }

    private void JumpToNode(PathTrack node)
    {
        CurrentPath = node;
        distanceAlong = 0f;

        SetWorldOriginAndAngles(node.WorldOrigin + new Vector3(0f, 0f, height), WorldAngles);

        // A jump is not movement, so it must not be interpolated across
        SnapInterpolation();
    }

    private void StartMoving()
    {
        if (soundStart != null)
        {
            Sound.Play(soundStart, WorldOrigin);
        }

        if (soundMove != null)
        {
            moveSound.Stop();
            moveSound = Sound.Play(soundMove, WorldOrigin);
        }

        EntitySystem.TriggerOutput(this, "OnStart", this);
    }

    private void SetSpeed(float speed)
    {
        var wasMoving = CurrentSpeed > 0f;

        CurrentSpeed = MathF.Max(speed, 0f);
        segmentEntrySpeed = CurrentSpeed;

        if (CurrentSpeed <= 0f)
        {
            if (wasMoving)
            {
                Stop();
            }

            return;
        }

        if (!wasMoving)
        {
            StartMoving();
        }
    }

    private void Stop()
    {
        var wasMoving = CurrentSpeed > 0f;

        CurrentSpeed = 0f;
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        moveSound.Stop();

        if (wasMoving && soundStop != null)
        {
            Sound.Play(soundStop, WorldOrigin);
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove() => moveSound.Stop();

    /// <summary>Starts along the track at full speed.</summary>
    [EntityInput("StartForward")]
    protected void InputStartForward(EntityInputData data)
    {
        IsForward = true;
        SetSpeed(MaxSpeed);
    }

    /// <summary>Starts back along the track at full speed.</summary>
    [EntityInput("StartBackward")]
    protected void InputStartBackward(EntityInputData data)
    {
        IsForward = false;
        SetSpeed(MaxSpeed);
    }

    /// <summary>Stops where it is.</summary>
    [EntityInput("Stop")]
    protected void InputStop(EntityInputData data) => Stop();

    /// <summary>Stops when moving, otherwise starts at full speed.</summary>
    [EntityInput("Toggle")]
    protected void InputToggle(EntityInputData data) => SetSpeed(CurrentSpeed > 0f ? 0f : MaxSpeed);

    /// <summary>Starts again at full speed the way it was going.</summary>
    [EntityInput("Resume")]
    protected void InputResume(EntityInputData data) => SetSpeed(MaxSpeed);

    /// <summary>Turns the way it goes along the track around.</summary>
    [EntityInput("Reverse")]
    protected void InputReverse(EntityInputData data) => IsForward = !IsForward;

    /// <summary>Sets the speed as a fraction of the top speed, as Source takes it.</summary>
    [EntityInput("SetSpeed")]
    protected void InputSetSpeed(EntityInputData data) => SetSpeed(MaxSpeed * Math.Clamp(data.Float(), 0f, 1f));

    /// <summary>Sets the speed in units per second.</summary>
    [EntityInput("SetSpeedReal")]
    protected void InputSetSpeedReal(EntityInputData data) => SetSpeed(Math.Clamp(data.Float(), 0f, MaxSpeed));

    /// <summary>Sets the speed as a fraction of the top speed, its sign choosing the way.</summary>
    [EntityInput("SetSpeedDir")]
    protected void InputSetSpeedDir(EntityInputData data) => SetSpeedDir(data.Float());

    /// <summary>Like SetSpeedDir; the acceleration is not simulated.</summary>
    [EntityInput("SetSpeedDirAccel")]
    protected void InputSetSpeedDirAccel(EntityInputData data) => SetSpeedDir(data.Float());

    private void SetSpeedDir(float signedFraction)
    {
        if (signedFraction != 0f)
        {
            IsForward = signedFraction > 0f;
        }

        SetSpeed(MaxSpeed * Math.Clamp(MathF.Abs(signedFraction), 0f, 1f));
    }

    /// <summary>Sets the top speed.</summary>
    [EntityInput("SetMaxSpeed")]
    protected void InputSetMaxSpeed(EntityInputData data)
    {
        MaxSpeed = MathF.Max(data.Float(), 0f);

        if (CurrentSpeed > MaxSpeed)
        {
            SetSpeed(MaxSpeed);
        }
    }

    /// <summary>Jumps onto the named node.</summary>
    [EntityInput("TeleportToPathTrack")]
    protected void InputTeleportToPathTrack(EntityInputData data) => TeleportToNode(data.Parameter);

    /// <summary>Jumps onto the named node and stops there.</summary>
    [EntityInput("TeleportToPathNode")]
    protected void InputTeleportToPathNode(EntityInputData data)
    {
        TeleportToNode(data.Parameter);
        Stop();
    }

    private void TeleportToNode(string? name)
    {
        if (FindPath(name) is { } node)
        {
            destination = null;
            PlaceAt(node);
        }
    }

    /// <summary>Travels to the named node at full speed, whichever way along the track it lies, and stops on it.</summary>
    [EntityInput("MoveToPathNode")]
    protected void InputMoveToPathNode(EntityInputData data)
    {
        if (FindPath(data.Parameter) is not { } node || CurrentPath == null)
        {
            return;
        }

        if (node == CurrentPath && distanceAlong == 0f)
        {
            destination = null;
            Stop();
            EntitySystem.TriggerOutput(this, "OnArrivedAtDestinationNode", this);
            return;
        }

        destination = node;
        IsForward = IsAhead(node);
        SetSpeed(MaxSpeed);
    }

    // Whether the node lies forward along the track from here, looking no further than the track runs
    private bool IsAhead(PathTrack node)
    {
        var visited = new HashSet<PathTrack>();

        for (var current = CurrentPath!.GetNext(true); current != null && visited.Add(current); current = current.GetNext(true))
        {
            if (current == node)
            {
                return true;
            }
        }

        return false;
    }
}
