using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_tracktrain</c>, Source's <c>CFuncTrackTrain</c>. A brush that runs along a chain of
/// <see cref="PathTrack"/> nodes, carrying and pushing whatever stands on it.
/// </summary>
/// <remarks>
/// Every tick it is moving, it looks 0.1 seconds of travel ahead along the path, passes the nodes in
/// between, and sets its velocity toward that point and its angular velocity toward the orientation it
/// should have; the pusher physics integrates both. Not simulated: driving it with the use key or train
/// controls, the damage and shove it gives what blocks it (the player never blocks a train), and the
/// pitch and volume its move sound takes from its speed.
/// </remarks>
public class FuncTrackTrain : BaseModelEntity
{
    /// <summary>What a <c>func_tracktrain</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>It does not pitch to follow the path.</summary>
        NoPitch = 0x1,

        /// <summary>The player cannot drive it, and it takes the speed of every node it passes.</summary>
        NoUserControl = 0x2,

        /// <summary>Things pass straight through it.</summary>
        Passable = 0x8,

        /// <summary>It does not turn to follow the path. Toggled by <c>LockOrientation</c>.</summary>
        FixedOrientation = 0x10,

        /// <summary>
        /// It collides as a plain brush rather than a physics mesh, Hammer's "HL1 Train", so turning pushes
        /// the player by the motion of their origin.
        /// </summary>
        LegacyTrain = 0x80,

        /// <summary>Its move sound scales with speed against its own top speed rather than 1000.</summary>
        UseMaxSpeedForPitch = 0x100,

        /// <summary>Nothing the player does stops it.</summary>
        UnblockableByPlayer = 0x200,
    }

    /// <summary>How the train turns along the track, the <c>orientationtype</c> keyvalue.</summary>
    public enum OrientationType
    {
        /// <summary>It never turns.</summary>
        Fixed = 0,

        /// <summary>It turns toward the orientation of the node it last passed.</summary>
        AtPathTracks = 1,

        /// <summary>It blends linearly toward the orientation of the next node.</summary>
        LinearBlend = 2,

        /// <summary>It blends toward the orientation of the next node, easing in and out.</summary>
        EaseInEaseOut = 3,
    }

    /// <summary>How the train changes speed between nodes with speeds, the <c>velocitytype</c> keyvalue.</summary>
    public enum VelocityType
    {
        /// <summary>It keeps its speed.</summary>
        Instantaneous = 0,

        /// <summary>It blends linearly between node speeds, and ramps for <c>SetSpeedDirAccel</c>.</summary>
        LinearBlend = 1,

        /// <summary>It blends between node speeds easing in and out, and ramps for <c>SetSpeedDirAccel</c>.</summary>
        EaseInEaseOut = 2,
    }

    /// <summary>Gets the speed the start inputs go at, the <c>startspeed</c> keyvalue.</summary>
    public float MaxSpeed { get; private set; }

    /// <summary>Gets the current speed in units per second, negative when going backward along the track.</summary>
    public float Speed { get; private set; }

    /// <summary>Gets whether it goes along <c>target</c> links rather than back along them.</summary>
    public bool IsForward => direction == 1f;

    /// <summary>Gets the node it last passed in the direction it is going.</summary>
    public PathTrack? CurrentPath => path is { IsRemoved: false } ? path : null;

    private PathTrack? path;
    private float direction = 1f;

    // Distance between the wheels, how far ahead it looks to orient itself
    private float length;
    private float height;
    private float bank;

    // The speed it had before it last stopped, which Resume goes back to
    private float oldSpeed;

    // Where it was and how it faced when it last passed a node, which orientation blends start from
    private Vector3 positionPrevious;
    private Vector3 anglesPrevious;

    private OrientationType orientationType = OrientationType.AtPathTracks;
    private VelocityType velocityType;

    // The node MoveToPathNode or TeleportToPathNode was told to stop at
    private string? pathTarget;

    private bool manualSpeedChanges;
    private bool accelToSpeed;
    private float desiredSpeed;
    private float accelSpeed;
    private float decelSpeed;

    private string? soundMove;
    private string? soundMovePing;
    private string? soundStart;
    private string? soundStop;
    private float moveSoundMinDuration;
    private float moveSoundMaxDuration;
    private float nextMoveSoundTime;
    private float nextMPSoundTime;
    private SoundHandle moveSound;
    private bool isMoveSoundStarted;

    // Source schedules each think context on its own: Find runs once after activation, Next every tick
    // while moving. -1 is never.
    private float findThinkTime = -1f;
    private float nextThinkTime = -1f;

    // What MoveDone runs: DeadEnd once the train has glided to the end of the path
    private bool deadEndOnMoveDone;

    /// <summary>Initializes a <c>func_tracktrain</c> from its keyvalues.</summary>
    public FuncTrackTrain(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected internal override bool IsPusher => true;

    /// <inheritdoc/>
    protected override bool IsUnblockableByPlayer => HasSpawnFlags(SpawnFlag.UnblockableByPlayer);

    /// <inheritdoc/>
    protected override bool PushesPlayerAsTrain => true;

    /// <inheritdoc/>
    protected override bool HasVPhysicsSolid => !HasSpawnFlags(SpawnFlag.LegacyTrain);

    /// <inheritdoc/>
    public override EntityCapability ObjectCaps
        => HasSpawnFlags(SpawnFlag.NoUserControl) ? EntityCapability.None : EntityCapability.ImpulseUse;

    /// <inheritdoc/>
    public override void Spawn()
    {
        length = KeyValues.GetFloatProperty("wheels");
        height = KeyValues.GetFloatProperty("height");
        MaxSpeed = KeyValues.GetFloatProperty("startspeed");
        bank = KeyValues.GetFloatProperty("bank");
        Speed = KeyValues.GetFloatProperty("speed");
        soundMove = NonEmpty(KeyValues.GetStringProperty("movesound"));
        soundMovePing = NonEmpty(KeyValues.GetStringProperty("movepingsound"));
        soundStart = NonEmpty(KeyValues.GetStringProperty("startsound"));
        soundStop = NonEmpty(KeyValues.GetStringProperty("stopsound"));
        moveSoundMinDuration = KeyValues.GetFloatProperty("movesoundmintime");
        moveSoundMaxDuration = KeyValues.GetFloatProperty("movesoundmaxtime");
        velocityType = (VelocityType)KeyValues.GetInt32Property("velocitytype");
        orientationType = (OrientationType)KeyValues.GetInt32Property("orientationtype", (int)OrientationType.AtPathTracks);
        manualSpeedChanges = KeyValues.GetBooleanProperty("manualspeedchanges");
        accelSpeed = KeyValues.GetFloatProperty("manualaccelspeed");
        decelSpeed = KeyValues.GetFloatProperty("manualdecelspeed");

        if (MaxSpeed == 0f)
        {
            MaxSpeed = Speed != 0f ? Speed : 100f;
        }

        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        direction = 1f;

        if (HasSpawnFlags(SpawnFlag.Passable))
        {
            IsSolid = false;
        }

        foreach (var sound in (string?[])[soundStart, soundStop, soundMove, soundMovePing])
        {
            if (sound != null)
            {
                Sound.Cache(sound);
            }
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <inheritdoc/>
    public override void Activate() => SetContextThink(ref findThinkTime, EntitySystem.CurrentTime);

    /// <inheritdoc/>
    public override void Think()
    {
        var now = EntitySystem.CurrentTime;

        if (findThinkTime >= 0f && findThinkTime <= now)
        {
            findThinkTime = -1f;
            Find();
        }

        if (nextThinkTime >= 0f && nextThinkTime <= now)
        {
            nextThinkTime = -1f;
            Next();
        }

        UpdateNextThink();
    }

    private void SetContextThink(ref float context, float time)
    {
        context = time < 0f ? -1f : EntitySystem.SnapToTick(time);
        UpdateNextThink();
    }

    private void UpdateNextThink()
    {
        var earliest = findThinkTime;

        if (nextThinkTime >= 0f && (earliest < 0f || nextThinkTime < earliest))
        {
            earliest = nextThinkTime;
        }

        // A think due at time 0 still runs on the first tick
        SetNextThink(earliest < 0f ? -1f : MathF.Max(earliest, EntitySystem.TickInterval));
    }

    /// <inheritdoc/>
    public override void MoveDone()
    {
        if (deadEndOnMoveDone)
        {
            DeadEnd();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The engine only simulates a pusher while a move is pending, so once <see cref="BaseEntity.MoveDoneTime"/>
    /// has passed the train stands still whatever velocity it was left with. <see cref="Next"/> keeps a move
    /// pending for as long as it runs.
    /// </remarks>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (MoveDoneTime > 0f)
        {
            base.PhysicsSimulate(tickInterval);
        }

        // The engine plays the move sound on the entity, so it travels with it
        moveSound.Position = WorldOrigin;
    }

    /// <inheritdoc/>
    protected override void OnRemove() => SoundStop();

    // Puts the train on its first node and starts it if it has a speed
    private void Find()
    {
        var target = KeyValues.GetStringProperty("target");
        var found = string.IsNullOrEmpty(target) ? null : EntitySystem.FindByTargetName(target, Scene);

        if (found == null)
        {
            path = null;
            EntitySystem.Logger.LogWarning("CFuncTrackTrain::Find() : func_tracktrain '{Name}' failed to find path_track named '{Target}'.", TargetName, target);
            return;
        }

        if (found is not PathTrack node)
        {
            EntitySystem.Logger.LogWarning("func_track_train must be on a path of path_track");
            path = null;
            return;
        }

        path = node;

        Vector3 angles;

        if (HasSpawnFlags(SpawnFlag.FixedOrientation))
        {
            angles = Angles;
        }
        else
        {
            // The node's own angles, not the direction the path runs from it
            angles = node.Angles;

            if (HasSpawnFlags(SpawnFlag.NoPitch))
            {
                angles.X = 0f;
            }
        }

        Teleport(node.Origin + new Vector3(0f, 0f, height), angles);

        ArriveAtNode(node);

        if (Speed != 0f)
        {
            SetContextThink(ref nextThinkTime, EntitySystem.CurrentTime + 0.1f);
            SoundUpdate();
        }

        positionPrevious = Origin;
        anglesPrevious = Angles;
    }

    // The movement think, run every tick while the train moves
    private void Next()
    {
        if (Speed == 0f)
        {
            SoundStop();
            return;
        }

        if (CurrentPath is not { } currentPath)
        {
            SoundStop();
            Speed = 0f;
            return;
        }

        SoundUpdate();

        var speed = Speed;
        var origin = Origin;
        origin.Z -= height;

        var next = currentPath.LookAhead(ref origin, speed * 0.1f, move: true, out var nextNext);

        // Lets a train ramping to the opposite direction turn around at a dead end
        if (manualSpeedChanges && speed < 0f != desiredSpeed < 0f && next == null)
        {
            next = currentPath;
        }

        origin.Z += height;

        if (next != null)
        {
            if (next != currentPath)
            {
                path = next;
                positionPrevious = Origin;
                anglesPrevious = Angles;

                if (nextNext != null
                    && (IsForward ? nextNext : next).HasSpawnFlags(PathTrack.SpawnFlag.TeleportToThis))
                {
                    TeleportToPathTrack(nextNext);
                    ArriveAtNode(nextNext);
                }
                else
                {
                    ArriveAtNode(next);
                }

                EntitySystem.TriggerOutput(this, "OnNextPoint", next);
            }

            UpdateTrainVelocity(next, nextNext, origin);

            if (!HasSpawnFlags(SpawnFlag.FixedOrientation))
            {
                if (orientationType == OrientationType.AtPathTracks)
                {
                    UpdateOrientationAtPathTracks();
                }
                else if (orientationType is OrientationType.LinearBlend or OrientationType.EaseInEaseOut)
                {
                    UpdateOrientationBlend(orientationType, nextNext);
                }
            }

            SetMoveDoneTime(0.5f);
            SetContextThink(ref nextThinkTime, EntitySystem.CurrentTime);
            deadEndOnMoveDone = false;
            return;
        }

        // Off the end of the path: glide on to the point the lookahead ran out at, then stop
        var delta = origin - Origin;
        var distance = Length(delta);

        SoundStop();
        AngularVelocity = Vector3.Zero;
        oldSpeed = Speed;
        Speed = 0f;

        if (distance <= 0f || oldSpeed == 0f)
        {
            DeadEnd();
            return;
        }

        var time = distance / MathF.Abs(oldSpeed);
        Velocity = delta * (1f / time);
        deadEndOnMoveDone = true;
        SetContextThink(ref nextThinkTime, -1f);
        SetMoveDoneTime(time);
    }

    // Stops at the real end of the path, past any nodes the lookahead cut short
    private void DeadEnd()
    {
        var track = CurrentPath;

        if (track != null)
        {
            var start = track;

            while (PathTrack.ValidPath(oldSpeed >= 0f ? track.GetNext() : track.GetPrevious(), testFlag: true) is { } further
                && further != start)
            {
                track = further;
            }
        }

        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;

        if (track == null)
        {
            return;
        }

        track.Pass(this);

        if (pathTarget != null && EntitySystem.FindByTargetName(pathTarget, Scene) == track)
        {
            EntitySystem.TriggerOutput(this, "OnArrivedAtDestinationNode", track);
            pathTarget = null;
        }
    }

    private void ArriveAtNode(PathTrack node)
    {
        node.Pass(this);

        if (node.HasSpawnFlags(PathTrack.SpawnFlag.DisableTrain))
        {
            SpawnFlags |= (uint)SpawnFlag.NoUserControl;
        }

        if (pathTarget != null && EntitySystem.FindByTargetName(pathTarget, Scene) == node)
        {
            EntitySystem.TriggerOutput(this, "OnArrivedAtDestinationNode", node);

            // Not through Stop, so the train keeps its velocity and the move it has pending
            oldSpeed = Speed;
            Speed = 0f;
            pathTarget = null;
        }
        else if (HasSpawnFlags(SpawnFlag.NoUserControl) && node.Speed != 0f)
        {
            SetSpeed(node.Speed);
        }
    }

    // Aims the velocity at the lookahead point, after blending or ramping the speed for the blending velocity types
    private void UpdateTrainVelocity(PathTrack? previous, PathTrack? next, Vector3 nextPosition)
    {
        if (velocityType is VelocityType.LinearBlend or VelocityType.EaseInEaseOut)
        {
            if (accelToSpeed)
            {
                if (Speed != desiredSpeed)
                {
                    var rate = MathF.Abs(desiredSpeed) > MathF.Abs(Speed) ? accelSpeed : decelSpeed;
                    var step = rate * EntitySystem.TickInterval;
                    var change = desiredSpeed - Speed;

                    if (change > step)
                    {
                        Speed += step;
                    }
                    else if (-step > change)
                    {
                        Speed -= step;
                    }
                    else
                    {
                        Speed = desiredSpeed;
                    }
                }
            }
            else if (previous != null && next != null)
            {
                var fromSpeed = previous.Speed != 0f ? MathF.Abs(previous.Speed) * direction : Speed;
                var toSpeed = next.Speed != 0f ? MathF.Abs(next.Speed) * direction : fromSpeed;

                if (fromSpeed == toSpeed)
                {
                    Speed = fromSpeed;
                }
                else
                {
                    var segment = Length(next.Origin - previous.Origin);

                    if (segment != 0f)
                    {
                        var t = Length(Origin - previous.Origin) / segment;

                        if (velocityType == VelocityType.EaseInEaseOut)
                        {
                            t = (3f - (t + t)) * (t * t);
                        }

                        Speed = (1f - t) * fromSpeed + t * toSpeed;
                    }
                }
            }
        }
        else if (velocityType != VelocityType.Instantaneous)
        {
            return;
        }

        Velocity = Normalize(nextPosition - Origin) * MathF.Abs(Speed);
    }

    // Turns toward the orientation of the node it last passed
    private void UpdateOrientationAtPathTracks()
    {
        if (CurrentPath is not { } currentPath)
        {
            return;
        }

        var origin = Origin;
        origin.Z -= height;

        var reach = length > 0f ? length : 100f;
        currentPath.LookAhead(ref origin, IsForward ? reach : -reach, move: false, out var nextNext);

        var target = FixupAngles(currentPath.GetOrientation(IsForward));

        if (manualSpeedChanges && nextNext is { OrientationType: PathTrack.Orientation.FacePathAngles })
        {
            target = nextNext.GetOrientation(IsForward);
        }

        DoUpdateOrientation(FixupAngles(WorldAngles), target);
    }

    // Blends from how it faced passing the last node to the orientation of the next, by the distance covered since
    private void UpdateOrientationBlend(OrientationType type, PathTrack? next)
    {
        var from = FixupAngles(anglesPrevious);
        var to = next != null ? FixupAngles(next.GetOrientation(IsForward)) : from;

        if (HasSpawnFlags(SpawnFlag.NoPitch))
        {
            to.X = from.X;
        }

        var t = 0f;

        if (from != to)
        {
            var total = Length(next!.Origin - positionPrevious);

            if (total != 0f)
            {
                t = Length(Origin - positionPrevious) / total;
            }
        }

        if (type == OrientationType.EaseInEaseOut)
        {
            t = (1f - MathF.Cos(t * MathF.PI)) * 0.5f;
        }

        var blended = FastSlerp(EntityTransformHelper.EulerAnglesToQuaternion(from), EntityTransformHelper.EulerAnglesToQuaternion(to), t);
        var angles = EntityTransformHelper.ToEulerAngles(blended);

        if (HasSpawnFlags(SpawnFlag.NoPitch))
        {
            angles.X = from.X;
        }

        DoUpdateOrientation(Angles, angles);
    }

    // Mathlib's polynomial-corrected nlerp, which only approximates a slerp
    private static Quaternion FastSlerp(Quaternion from, Quaternion to, float t)
    {
        var dot = Quaternion.Dot(from, to);
        var a = MathF.Abs(dot);
        var c = t - 0.5f;

        var toWeight = ((((3.55645f - a * 1.43519f) * a - 3.2452f) * a + 1.0904f) * (c * c)
            + ((a * 0.215638f - 1.06021f) * a + 0.848013f)) * (c * t * (t - 1f)) + t;
        var fromWeight = 1f - toWeight;

        if (dot <= 0f)
        {
            toWeight = -toWeight;
        }

        var sum = to * toWeight + from * fromWeight;
        var length = MathF.Sqrt(Quaternion.Dot(sum, sum));

        return length == 0f ? Quaternion.Identity : new Quaternion(sum.X / length, sum.Y / length, sum.Z / length, sum.W / length);
    }

    // The angular velocity is the whole angle error per second, re-aimed every tick, so the train closes
    // most of the gap within a few ticks rather than over any set time
    private void DoUpdateOrientation(Vector3 current, Vector3 target)
    {
        var pitchRate = HasSpawnFlags(SpawnFlag.NoPitch) ? 0f : AngleDistance(target.X, current.X);
        var yawRate = AngleDistance(target.Y, current.Y);

        if (MathF.Abs(pitchRate) < 0.1f)
        {
            pitchRate = 0f;
        }

        if (MathF.Abs(yawRate) < 0.1f)
        {
            yawRate = 0f;
        }

        var rollRate = GetAbsAngularVelocity().X;

        if (bank != 0f)
        {
            if (!(yawRate >= -5f))
            {
                rollRate = AngleDistance(ApproachAngle(-bank, current.Z, bank + bank), current.Z);
            }
            else if (yawRate <= 5f)
            {
                rollRate = AngleDistance(ApproachAngle(0f, current.Z, bank * 4f), current.Z) * 4f;
            }
            else
            {
                rollRate = AngleDistance(ApproachAngle(bank, current.Z, bank + bank), current.Z);
            }
        }

        SetAbsAngularVelocity(new Vector3(rollRate, pitchRate, yawRate));
    }

    // Angular velocity as the engine's (roll, pitch, yaw) rate vector in world space. AngularVelocity
    // holds it reordered as a QAngle, in the frame of the move parent entity.
    private Vector3 GetAbsAngularVelocity()
    {
        var rates = new Vector3(AngularVelocity.Z, AngularVelocity.X, AngularVelocity.Y);

        return MoveParent != null ? Vector3.TransformNormal(rates, MoveParent.RigidTransform) : rates;
    }

    private void SetAbsAngularVelocity(Vector3 rates)
    {
        if (MoveParent != null)
        {
            rates = Vector3.TransformNormal(rates, Matrix4x4.Transpose(MoveParent.RigidTransform));
        }

        AngularVelocity = new Vector3(rates.Y, rates.Z, rates.X);
    }

    private void TeleportToPathTrack(PathTrack node)
    {
        var currentPitch = Angles.X;
        var look = node.Origin;
        node.LookAhead(ref look, IsForward ? length : -length, move: false, out _);

        Vector3 angles;

        if (HasSpawnFlags(SpawnFlag.FixedOrientation) || look == node.Origin)
        {
            angles = Angles;
        }
        else
        {
            angles = node.GetOrientation(IsForward);

            if (HasSpawnFlags(SpawnFlag.NoPitch))
            {
                angles.X = currentPitch;
            }
        }

        // Unlike Find, without the height
        Teleport(node.Origin, angles);
        AngularVelocity = Vector3.Zero;
    }

    // Keeps the path on the node behind the train in the new direction
    private void SetDirForward(bool forward)
    {
        if (forward)
        {
            if (direction == 1f)
            {
                return;
            }

            if (CurrentPath?.GetPrevious() is { } previous)
            {
                path = previous;
            }

            direction = 1f;
        }
        else
        {
            if (direction == -1f)
            {
                return;
            }

            if (CurrentPath?.GetNext() is { } next)
            {
                path = next;
            }

            direction = -1f;
        }
    }

    private void SetSpeed(float speed)
    {
        accelToSpeed = false;

        var previousSpeed = Speed;
        Speed = MathF.Abs(speed) * direction;

        if (Speed == previousSpeed)
        {
            return;
        }

        if (Speed == 0f)
        {
            Stop();
            return;
        }

        if (previousSpeed == 0f)
        {
            EntitySystem.TriggerOutput(this, "OnStart", this);
        }

        Next();
    }

    private void Stop()
    {
        Velocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        oldSpeed = Speed;
        Speed = 0f;
        SoundStop();
    }

    private void SoundUpdate()
    {
        if (soundMove == null && soundStart == null && soundMovePing == null)
        {
            return;
        }

        var now = EntitySystem.CurrentTime;

        // The game rules are multiplayer, which limits a playing train to one update a second
        if (isMoveSoundStarted)
        {
            if (now < nextMPSoundTime)
            {
                return;
            }

            nextMPSoundTime = now + 1f;
        }

        var speedFraction = MathF.Abs(Speed) / (HasSpawnFlags(SpawnFlag.UseMaxSpeedForPitch) ? MaxSpeed : 1000f);
        speedFraction = speedFraction >= 0f ? MathF.Min(1f, speedFraction) : 0f;

        if (!isMoveSoundStarted)
        {
            // Without a move sound nothing counts as started, so the start sound plays on every update
            if (soundStart != null)
            {
                Sound.Play(soundStart, WorldOrigin);
            }

            if (soundMove != null)
            {
                moveSound = Sound.Play(soundMove, WorldOrigin);
                isMoveSoundStarted = true;
            }
        }
        else
        {
            if (soundMovePing == null || !(now > nextMoveSoundTime))
            {
                return;
            }

            Sound.Play(soundMovePing, WorldOrigin);
        }

        nextMoveSoundTime = now + (moveSoundMinDuration - moveSoundMaxDuration) * speedFraction + moveSoundMaxDuration;
    }

    private void SoundStop()
    {
        if (isMoveSoundStarted)
        {
            moveSound.Stop();

            if (soundStop != null)
            {
                Sound.Play(soundStop, WorldOrigin);
            }
        }

        moveSound = default;
        isMoveSoundStarted = false;
    }

    private static float Length(Vector3 v) => MathF.Sqrt(v.Z * v.Z + v.Y * v.Y + v.X * v.X);

    private static Vector3 Normalize(Vector3 v)
    {
        var length = Length(v);
        return length == 0f ? Vector3.Zero : v * (1f / length);
    }

    // next - cur, wrapped to (-180, 180]
    private static float AngleDistance(float next, float current)
    {
        var delta = next - current;

        if (delta <= -180f)
        {
            return delta + 360f;
        }

        if (delta > 180f)
        {
            return delta - 360f;
        }

        return delta;
    }

    // Moves value toward target by at most |speed| degrees, both taken into [0, 360)
    private static float ApproachAngle(float target, float value, float speed)
    {
        target = AnglePositive(target);
        value = AnglePositive(value);

        var delta = target - value;

        if (speed < 0f)
        {
            speed = -speed;
        }

        if (delta < -180f)
        {
            delta += 360f;
        }
        else if (delta > 180f)
        {
            delta -= 360f;
        }

        if (delta > speed)
        {
            return value + speed;
        }

        if (-speed <= delta)
        {
            return target;
        }

        return value - speed;
    }

    private static float AnglePositive(float angle)
    {
        if (angle < -180f || angle > 180f)
        {
            angle = MathF.Min(MathF.Max(angle - MathF.Floor(angle * (1f / 360f) + 0.5f) * 360f, -180f), 180f);
        }

        return angle < 0f ? angle + 360f : angle;
    }

    // Wraps each component into [0, 360]
    private static Vector3 FixupAngles(Vector3 angles)
    {
        return new Vector3(Fixup(angles.X), Fixup(angles.Y), Fixup(angles.Z));

        static float Fixup(float angle)
        {
            while (angle < 0f)
            {
                angle += 360f;
            }

            while (angle > 360f)
            {
                angle -= 360f;
            }

            return angle;
        }
    }

    // The engine's clamp: the low bound wins when the bounds cross, and NaN clamps low
    private static float ClampFloat(float value, float low, float high)
        => low <= value ? (value <= high ? value : high) : low;

    /// <summary>Stops where it is.</summary>
    [EntityInput("Stop")]
    protected void InputStop(EntityInputData data) => Stop();

    /// <summary>Starts forward along the track at full speed.</summary>
    [EntityInput("StartForward")]
    protected void InputStartForward(EntityInputData data)
    {
        SetDirForward(true);
        SetSpeed(MaxSpeed);
    }

    /// <summary>Starts back along the track at full speed.</summary>
    [EntityInput("StartBackward")]
    protected void InputStartBackward(EntityInputData data)
    {
        SetDirForward(false);
        SetSpeed(MaxSpeed);
    }

    /// <summary>Stops when moving, otherwise starts at full speed the way it faces along the track.</summary>
    [EntityInput("Toggle")]
    protected void InputToggle(EntityInputData data) => SetSpeed(Speed == 0f ? MaxSpeed : 0f);

    /// <summary>Starts again at the speed it had before it last stopped.</summary>
    [EntityInput("Resume")]
    protected void InputResume(EntityInputData data)
    {
        Speed = oldSpeed;
        EntitySystem.TriggerOutput(this, "OnStart", this);
        Next();
    }

    /// <summary>Turns the way it goes along the track around, keeping its speed.</summary>
    [EntityInput("Reverse")]
    protected void InputReverse(EntityInputData data)
    {
        SetDirForward(direction != 1f);
        SetSpeed(Speed);
    }

    /// <summary>Sets the speed as a fraction of the top speed, keeping the direction.</summary>
    [EntityInput("SetSpeed")]
    protected void InputSetSpeed(EntityInputData data) => SetSpeed(ClampFloat(data.Float(), 0f, 1f) * MaxSpeed);

    /// <summary>Sets the speed as a fraction of the top speed, its sign choosing the direction.</summary>
    [EntityInput("SetSpeedDir")]
    protected void InputSetSpeedDir(EntityInputData data)
    {
        var value = data.Float();
        SetDirForward(value >= 0f);
        SetSpeed(SpeedFraction(value) * MaxSpeed);
    }

    /// <summary>Sets the speed in units per second, up to the top speed.</summary>
    [EntityInput("SetSpeedReal")]
    protected void InputSetSpeedReal(EntityInputData data) => SetSpeed(ClampFloat(data.Float(), 0f, MaxSpeed));

    /// <summary>Sets the top speed, without changing the current one.</summary>
    [EntityInput("SetMaxSpeed")]
    protected void InputSetMaxSpeed(EntityInputData data) => MaxSpeed = data.Float();

    /// <summary>
    /// Like <c>SetSpeedDir</c>, but trains with a blending <c>velocitytype</c> ramp to the speed at
    /// <c>ManualAccelSpeed</c> or <c>ManualDecelSpeed</c>.
    /// </summary>
    [EntityInput("SetSpeedDirAccel")]
    protected void InputSetSpeedDirAccel(EntityInputData data)
    {
        var value = data.Float();
        SetDirForward(value >= 0f);

        accelToSpeed = true;
        desiredSpeed = MathF.Abs(SpeedFraction(value) * MaxSpeed) * direction;

        // A nudge, so that Next has somewhere to go
        if (Speed == 0f && MathF.Abs(desiredSpeed) > 0f)
        {
            Speed = 0.1f;
        }

        EntitySystem.TriggerOutput(this, "OnStart", this);
        Next();
    }

    private static float SpeedFraction(float value)
    {
        var magnitude = MathF.Abs(value);
        return magnitude >= 0f ? MathF.Min(1f, magnitude) : 0f;
    }

    /// <summary>Travels to the named node, whichever way along the track it lies, and stops on reaching it.</summary>
    [EntityInput("MoveToPathNode")]
    protected void InputMoveToPathNode(EntityInputData data)
    {
        pathTarget = NonEmpty(data.Parameter);

        var destination = pathTarget != null ? EntitySystem.FindByTargetName(pathTarget, Scene) : null;

        if (CurrentPath is not { } currentPath || destination == null)
        {
            return;
        }

        var speed = currentPath.Speed != 0f ? currentPath.Speed : MaxSpeed;

        if (destination == currentPath)
        {
            // The node is behind the train, so it turns back to reach it
            if (IsForward ? currentPath.GetNext() != null : currentPath.GetPrevious() != null)
            {
                SetDirForward(!IsForward);
                SetSpeed(speed);
            }
            else
            {
                Stop();
            }

            return;
        }

        var node = currentPath;

        for (var i = 0; i < 1000 && (node = node.GetNext()) != null; i++)
        {
            if (node == destination)
            {
                SetDirForward(true);
                SetSpeed(speed);
                return;
            }
        }

        node = currentPath;

        for (var i = 0; i < 1000 && (node = node.GetPrevious()) != null; i++)
        {
            if (node == destination)
            {
                SetDirForward(false);
                SetSpeed(speed);
                return;
            }
        }
    }

    /// <summary>Jumps onto the named node, arriving at it as if it had travelled there.</summary>
    [EntityInput("TeleportToPathNode")]
    protected void InputTeleportToPathNode(EntityInputData data)
    {
        pathTarget = NonEmpty(data.Parameter);

        if (pathTarget == null || EntitySystem.FindByTargetName(pathTarget, Scene) is not { } found)
        {
            return;
        }

        path = found as PathTrack;

        if (path == null)
        {
            return;
        }

        ArriveAtNode(path);

        if (CurrentPath is { } currentPath)
        {
            TeleportToPathTrack(currentPath);
        }
    }

    /// <summary>Stops it turning to follow the path.</summary>
    [EntityInput("LockOrientation")]
    protected void InputLockOrientation(EntityInputData data)
    {
        SpawnFlags |= (uint)SpawnFlag.FixedOrientation;
        AngularVelocity = Vector3.Zero;
    }

    /// <summary>Lets it turn to follow the path again.</summary>
    [EntityInput("UnlockOrientation")]
    protected void InputUnlockOrientation(EntityInputData data) => SpawnFlags &= ~(uint)SpawnFlag.FixedOrientation;
}
