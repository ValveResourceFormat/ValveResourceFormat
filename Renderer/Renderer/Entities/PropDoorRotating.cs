using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>prop_door_rotating</c>, Source's <c>CPropDoorRotating</c>: the swinging model door on maps like
/// de_inferno. It is usable by default, swings away from whoever uses it unless <c>opendir</c> forces a side,
/// can spawn open or ajar, and opens and closes the other half of a double door with it. Breaking, and the
/// blocker that keeps NPCs out of the swing, are not simulated. Alyx's <c>prop_door_rotating_physics</c> is
/// played as the same thing: the hand that swings it in VR is a use press here.
/// </summary>
public class PropDoorRotating : BaseToggle
{
    /// <summary>What a <c>prop_door_rotating</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>The old way to spawn open, which opens it forward.</summary>
        StartsOpen = 1,

        /// <summary>Spawns locked.</summary>
        StartsLocked = 2048,

        /// <summary>Makes no sound.</summary>
        Silent = 4096,

        /// <summary>Pressing use while the door is open or opening closes it. Checked by default.</summary>
        UseCloses = 8192,

        /// <summary>Refuses player use entirely.</summary>
        IgnorePlayerUse = 32768,
    }

    /// <summary>How <c>opendir</c> constrains the swing.</summary>
    public enum OpenDirection
    {
        /// <summary>Away from whoever opens it, the standard door behavior.</summary>
        Both = 0,

        /// <summary>Forward only.</summary>
        ForwardOnly = 1,

        /// <summary>Backward only.</summary>
        BackwardOnly = 2,
    }

    /// <summary>Where a prop door is in its swing.</summary>
    public enum DoorState
    {
        /// <summary>At rest at its authored angle.</summary>
        Closed,

        /// <summary>Swinging open.</summary>
        Opening,

        /// <summary>At rest open, to one side or the other.</summary>
        Open,

        /// <summary>Swinging shut.</summary>
        Closing,

        /// <summary>At rest part open, where the map spawned it.</summary>
        Ajar,
    }

    // Which of the swept volumes a clearance test covers, Source's doorCheck_e
    private enum SwingSide
    {
        Forward,
        Backward,
        Both,
    }

    private enum MoveDoneFunction
    {
        None,
        OpenMoveDone,
        CloseMoveDone,
        AutoClose,
    }

    // Source's DOOR_SOUNDWAIT, how long a lock sound keeps another from playing
    private const float LockSoundWait = 1f;

    /// <summary>Gets where the door is in its swing.</summary>
    public DoorState State { get; private set; }

    /// <summary>Gets whether the door refuses to open.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Gets which way the door may swing.</summary>
    public OpenDirection OpenDir { get; private set; }

    /// <summary>Gets how far the door swings, in degrees.</summary>
    public float Distance { get; private set; }

    /// <summary>Gets the seconds the door stays open before closing, the <c>returndelay</c> keyvalue; -1 never closes.</summary>
    public float ReturnDelay { get; private set; }

    /// <summary>Gets the door of a double door this one follows, or <see langword="null"/> for one that leads or stands alone.</summary>
    public PropDoorRotating? Master { get; private set; }

    /// <summary>Usable by default; only the ignore flag switches it off.</summary>
    public override EntityCapability ObjectCaps
        => HasSpawnFlags(SpawnFlag.IgnorePlayerUse) ? EntityCapability.None : EntityCapability.ImpulseUse;

    private readonly List<PropDoorRotating> slaves = [];

    private Vector3 angleClosed;
    private Vector3 angleOpenForward;
    private Vector3 angleOpenBack;
    private Vector3 goalAngles;

    // The QAngle deltas one degree of swing makes
    private Vector3 moveAngles;

    // The space each swing sweeps, relative to the hinge
    private AABB forwardVolume;
    private AABB backVolume;

    private bool ajarNeedsFarSide;
    private MoveDoneFunction moveDoneFunction;

    // Source's m_hActivator: who set the door going, which the outputs report and arriving forgets
    private BaseEntity? lastActivator;

    private string? soundOpen;
    private string? soundClose;
    private string? soundMove;
    private string? soundLocked;
    private string? soundUnlocked;
    private SoundHandle moveSound;
    private bool isMoveSoundOn;
    private float lockSoundNext;

    /// <summary>Initializes a <c>prop_door_rotating</c> from its keyvalues.</summary>
    public PropDoorRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        angleClosed = Angles;

        // CS2 swings about the door's own up axis, and still spreads it over the QAngle components the way
        // Source did for a hinge along Z, so an upright door only ever yaws
        var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(angleClosed);
        moveAngles = new Vector3(rotation.M32, rotation.M33, rotation.M31);

        Distance = KeyValues.GetFloatProperty("distance");
        CalcOpenAngles();

        OpenDir = (OpenDirection)KeyValues.GetInt32Property("opendir");
        ReturnDelay = KeyValues.GetFloatProperty("returndelay", -1f);
        IsLocked = HasSpawnFlags(SpawnFlag.StartsLocked);
        ajarNeedsFarSide = KeyValues.GetBooleanProperty("ajardoorshouldntalwaysopen");

        Speed = KeyValues.GetFloatProperty("speed");

        if (Speed == 0f)
        {
            Speed = 100f;
        }

        TeleportToSpawnPosition();
        CalculateDoorVolumes(angleClosed);
        CalcDoorSounds();
    }

    /// <summary>
    /// Links a double door. A named door takes as its slaves the other doors called its <c>slavename</c>, or
    /// failing that its own name, and they open and close with it.
    /// </summary>
    public override void Activate()
    {
        if (string.IsNullOrEmpty(TargetName))
        {
            return;
        }

        var slaveName = KeyValues.GetStringProperty("slavename");
        var searchName = string.IsNullOrEmpty(slaveName) ? TargetName : slaveName;

        foreach (var entity in EntitySystem.FindAllByTargetName(searchName, Scene))
        {
            if (entity != this && entity is PropDoorRotating door && door.slaves.Count == 0)
            {
                slaves.Add(door);
                door.Master = this;
                door.Owner = this;
            }
        }
    }

    /// <summary>Runs when something presses the door; the leading door of a double door answers for both.</summary>
    public override void Use(BaseEntity? activator)
    {
        if (Master != null)
        {
            Master.Use(activator);
            return;
        }

        OnUse(activator);
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        StopMoveSound();

        base.OnRemove();
    }

    /// <inheritdoc/>
    public override void MoveDone()
    {
        FinishLinearMove();

        var next = moveDoneFunction;
        moveDoneFunction = MoveDoneFunction.None;

        switch (next)
        {
            case MoveDoneFunction.OpenMoveDone:
                OpenMoveDone();
                break;

            case MoveDoneFunction.CloseMoveDone:
                CloseMoveDone();
                break;

            case MoveDoneFunction.AutoClose:
                AutoClose();
                break;
        }
    }

    // Protected rather than private, so a subclass's input table inherits them

    /// <summary>Opens the door forward, unless it is locked or already open. A locked door says nothing.</summary>
    [EntityInput("Open")]
    protected void InputOpen(EntityInputData data) => OpenIfUnlocked(data.Activator, null);

    /// <summary>Opens the door away from the entity the parameter names.</summary>
    [EntityInput("OpenAwayFrom")]
    protected void InputOpenAwayFrom(EntityInputData data)
    {
        var awayFrom = string.IsNullOrEmpty(data.Parameter)
            ? null
            : EntitySystem.FindTargets(new EntityIOTarget(data.Parameter, EntityIOTargetType.EntityName), data.Activator, data.Caller).FirstOrDefault();

        OpenIfUnlocked(data.Activator, awayFrom);
    }

    /// <summary>Opens the door away from the activator, at the speed the parameter gives.</summary>
    [EntityInput("OpenAwayFromActivator")]
    protected void InputOpenAwayFromActivator(EntityInputData data)
    {
        // The parameter is the speed for this swing alone. A missing one would never arrive, so the
        // door's own is kept for it
        var speed = Speed;
        var overrideSpeed = data.Float();

        if (overrideSpeed > 0f)
        {
            Speed = overrideSpeed;
        }

        OpenIfUnlocked(data.Activator, data.Activator);

        Speed = speed;
    }

    /// <summary>Closes the door, unless it is already shut.</summary>
    [EntityInput("Close")]
    protected void InputClose(EntityInputData data)
    {
        if (State == DoorState.Closed)
        {
            return;
        }

        // Fired here as well as by the close itself, so a door not already closing reports it twice
        EntitySystem.TriggerOutput(this, "OnClose", data.Activator);
        DoorClose();
    }

    /// <summary>Opens a closed door forward and closes an open one. A moving or ajar door ignores it.</summary>
    [EntityInput("Toggle")]
    protected void InputToggle(EntityInputData data)
    {
        if (State == DoorState.Closed)
        {
            if (!IsLocked)
            {
                DoorOpen(null);
            }
        }
        else if (State == DoorState.Open)
        {
            DoorClose();
        }
    }

    /// <summary>Stops the door opening until it is unlocked.</summary>
    [EntityInput("Lock")]
    protected void InputLock(EntityInputData data) => IsLocked = true;

    /// <summary>Lets the door open again.</summary>
    [EntityInput("Unlock")]
    protected void InputUnlock(EntityInputData data) => IsLocked = false;

    /// <summary>Presses the door as a player would, unless it ignores player use.</summary>
    [EntityInput("PlayerOpen")]
    protected void InputPlayerOpen(EntityInputData data) => PlayerUse(data.Activator);

    /// <summary>Presses the door as a player would, the same as <c>PlayerOpen</c>.</summary>
    [EntityInput("PlayerClose")]
    protected void InputPlayerClose(EntityInputData data) => PlayerUse(data.Activator);

    /// <summary>Changes the speed, carrying on at the new one if the door is swinging.</summary>
    [EntityInput("SetSpeed")]
    protected void InputSetSpeed(EntityInputData data)
    {
        Speed = data.Float();

        if (State is DoorState.Opening or DoorState.Closing)
        {
            RotateTo(goalAngles);
        }
    }

    /// <summary>Changes how far the door swings, from its next swing on.</summary>
    [EntityInput("SetRotationDistance")]
    protected void InputSetRotationDistance(EntityInputData data)
    {
        Distance = data.Float();
        CalcOpenAngles();
        CalculateDoorVolumes(Angles);
    }

    /// <summary>
    /// A use press. Source's <c>CBasePropDoor::OnUse</c>: a closed door opens away from the one pressing it,
    /// one swinging shut reopens, and an open or opening one closes only when <see cref="SpawnFlag.UseCloses"/>
    /// allows it.
    /// </summary>
    private void OnUse(BaseEntity? activator)
    {
        switch (State)
        {
            case DoorState.Opening:
                if (HasSpawnFlags(SpawnFlag.UseCloses))
                {
                    lastActivator = activator;
                    DoorClose();
                }

                return;

            // Neither of these asks the lock, so a locked door swinging shut still reopens
            case DoorState.Closing:
                lastActivator = activator;
                DoorOpen(activator);
                return;

            case DoorState.Ajar:
                lastActivator = activator;

                if (ShouldAjarOpen(activator))
                {
                    DoorOpen(activator);
                }
                else
                {
                    DoorClose();
                }

                return;

            case DoorState.Open when !HasSpawnFlags(SpawnFlag.UseCloses):
                return;
        }

        if (IsLocked)
        {
            PlayLockSound(locked: true);
            EntitySystem.TriggerOutput(this, "OnLockedUse", activator);
            return;
        }

        UnlockedUse(activator);

        if (State == DoorState.Open && DoorCanClose(autoClose: false))
        {
            DoorClose();
        }
        else
        {
            DoorOpen(lastActivator);
        }
    }

    // PlayerOpen and PlayerClose both press the door, through the door leading a double door
    private void PlayerUse(BaseEntity? activator)
    {
        if (HasSpawnFlags(SpawnFlag.IgnorePlayerUse))
        {
            return;
        }

        var door = this;

        while (door.Master is { IsRemoved: false } master)
        {
            door = master;

            if (door.HasSpawnFlags(SpawnFlag.IgnorePlayerUse))
            {
                return;
            }
        }

        door.OnUse(activator);
    }

    private void OpenIfUnlocked(BaseEntity? activator, BaseEntity? awayFrom)
    {
        if (IsLocked || State is DoorState.Opening or DoorState.Open)
        {
            return;
        }

        if (State == DoorState.Closed)
        {
            UnlockedUse(activator);
        }

        DoorOpen(awayFrom);
    }

    private void UnlockedUse(BaseEntity? activator)
    {
        lastActivator = activator;

        if (State == DoorState.Closed)
        {
            PlayLockSound(locked: false);
        }
    }

    private void DoorOpen(BaseEntity? awayFrom)
    {
        if (State == DoorState.Ajar)
        {
            StartMoveSound();
            EntitySystem.TriggerOutput(this, "OnAjarOpen", lastActivator);
        }

        if (State is DoorState.Opening or DoorState.Open)
        {
            return;
        }

        var previous = State;

        State = DoorState.Opening;
        moveDoneFunction = MoveDoneFunction.OpenMoveDone;

        RotateTo(ChooseSwingSide(awayFrom) == SwingSide.Forward ? angleOpenForward : angleOpenBack);

        // Only setting off from shut announces itself and takes the rest of a double door along
        if (previous != DoorState.Closed)
        {
            return;
        }

        StartMoveSound();
        EntitySystem.TriggerOutput(this, "OnOpen", lastActivator);

        foreach (var slave in slaves)
        {
            if (!slave.IsRemoved)
            {
                slave.lastActivator = lastActivator;
                slave.DoorOpen(awayFrom);
            }
        }
    }

    private void DoorClose()
    {
        if (State is DoorState.Closed or DoorState.Closing)
        {
            return;
        }

        StartMoveSound();

        State = DoorState.Closing;
        moveDoneFunction = MoveDoneFunction.CloseMoveDone;

        RotateTo(angleClosed);

        EntitySystem.TriggerOutput(this, "OnClose", this);

        foreach (var slave in slaves)
        {
            if (!slave.IsRemoved)
            {
                slave.DoorClose();
            }
        }
    }

    private void OpenMoveDone()
    {
        StopMoveSound();
        PlaySound(soundOpen);

        State = DoorState.Open;

        if (ReturnDelay != -1f)
        {
            ScheduleAutoClose();
        }

        EntitySystem.TriggerOutput(this, "OnFullyOpen", this);

        lastActivator = null;
    }

    private void CloseMoveDone()
    {
        StopMoveSound();
        PlaySound(soundClose);

        State = DoorState.Closed;

        EntitySystem.TriggerOutput(this, "OnFullyClosed", lastActivator);

        lastActivator = null;
    }

    // Closing by itself checks both sides of the doorway, and waits another delay while anything stands in it
    private void AutoClose()
    {
        if (DoorCanClose(autoClose: true))
        {
            DoorClose();
            return;
        }

        if (ReturnDelay != -1f)
        {
            ScheduleAutoClose();
        }
    }

    // A delay below the 0.1 it is padded by never comes due
    private void ScheduleAutoClose()
    {
        moveDoneFunction = MoveDoneFunction.AutoClose;
        SetMoveDoneTime(ReturnDelay + 0.1f);
    }

    private void RotateTo(Vector3 angles)
    {
        goalAngles = angles;
        AngularMove(angles, Speed);
    }

    private void CalcOpenAngles()
    {
        if (Distance == 0f)
        {
            Distance = 90f;
        }

        Distance = MathF.Abs(Distance);

        angleOpenForward = angleClosed - moveAngles * Distance;
        angleOpenBack = angleClosed + moveAngles * Distance;
    }

    private void TeleportToSpawnPosition()
    {
        var spawnPosition = KeyValues.GetInt32Property("spawnpos");
        Vector3 angles;

        if (HasSpawnFlags(SpawnFlag.StartsOpen) || spawnPosition == 1)
        {
            angles = angleOpenForward;
            State = DoorState.Open;
        }
        else if (spawnPosition == 2)
        {
            angles = angleOpenBack;
            State = DoorState.Open;
        }
        else if (spawnPosition == 3)
        {
            angles = GetAjarAngles();
            State = DoorState.Ajar;
        }
        else
        {
            angles = angleClosed;
            State = DoorState.Closed;
        }

        JumpTo(Origin, angles);
    }

    /// <summary>
    /// Where an ajar door rests: turned <c>ajarangle</c> degrees about its own up axis, or at the older
    /// <c>ajarangles</c> outright when that is not set.
    /// </summary>
    private Vector3 GetAjarAngles()
    {
        var ajarAngle = KeyValues.GetFloatProperty("ajarangle");

        if (ajarAngle == 0f)
        {
            return KeyValues.GetVector3Property("ajarangles");
        }

        var closed = EntityTransformHelper.EulerAnglesToRotationMatrix(angleClosed);
        var up = new Vector3(closed.M31, closed.M32, closed.M33);
        var ajar = closed * Matrix4x4.CreateFromAxisAngle(up, float.DegreesToRadians(ajarAngle));

        return EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(ajar));
    }

    // The engine measures both from the closed angles at spawn, but from wherever the door stands when the
    // distance changes later
    private void CalculateDoorVolumes(Vector3 fromAngles)
    {
        forwardVolume = CalculateDoorVolume(fromAngles, angleOpenForward);
        backVolume = CalculateDoorVolume(fromAngles, angleOpenBack);
    }

    private AABB CalculateDoorVolume(Vector3 fromAngles, Vector3 toAngles)
    {
        if (Collider is not { IsEmpty: false } collider)
        {
            return new AABB(Vector3.Zero, Vector3.Zero);
        }

        var from = collider.LocalBounds.Transform(EntityTransformHelper.EulerAnglesToRotationMatrix(fromAngles));
        var to = collider.LocalBounds.Transform(EntityTransformHelper.EulerAnglesToRotationMatrix(toAngles));

        return from.Union(to);
    }

    /// <summary>
    /// Which way the door opens. A forced <c>opendir</c> decides it; otherwise it opens away from
    /// <paramref name="awayFrom"/>, and without one it opens forward, which for an upright door is always a
    /// negative yaw.
    /// </summary>
    private SwingSide ChooseSwingSide(BaseEntity? awayFrom)
    {
        if (OpenDir == OpenDirection.ForwardOnly)
        {
            return SwingSide.Forward;
        }

        if (OpenDir == OpenDirection.BackwardOnly)
        {
            return SwingSide.Backward;
        }

        var side = SwingSide.Forward;

        if (awayFrom != null && IsOnLeft(awayFrom) == IsOpenForwardOnLeft())
        {
            side = SwingSide.Backward;
        }

        // A player who would swing it into something on the far side gets it opened towards them instead,
        // unless that side is in the way too
        if (lastActivator is PlayerEntity && awayFrom is PlayerEntity && !CheckDoorClear(side))
        {
            var other = side == SwingSide.Forward ? SwingSide.Backward : SwingSide.Forward;

            if (CheckDoorClear(other))
            {
                side = other;
            }
        }

        return side;
    }

    // Whether an entity stands to the left of the door, looking from the hinge along the panel
    private bool IsOnLeft(BaseEntity entity)
    {
        var alongPanel = MathUtils.SafeNormalize(((Collider?.WorldBounds.Center ?? WorldOrigin) - WorldOrigin) with { Z = 0f });
        var toEntity = MathUtils.SafeNormalize((entity.WorldOrigin - WorldOrigin) with { Z = 0f });

        return alongPanel.X * toEntity.Y - alongPanel.Y * toEntity.X >= 0f;
    }

    private bool IsOpenForwardOnLeft() => MathUtils.Wrap(angleOpenForward.Y - Angles.Y, -180f, 180f) >= 0f;

    // With ajardoorshouldntalwaysopen set, an ajar door only opens further for someone on the side it
    // would swing away from, and shuts for anyone else. The side is the one the door already stands open
    // to, measured from closed.
    private bool ShouldAjarOpen(BaseEntity? activator)
        => !ajarNeedsFarSide || activator == null
            || IsOnLeft(activator) != MathUtils.Wrap(Angles.Y - angleClosed.Y, -180f, 180f) >= 0f;

    private SwingSide OpenSide => goalAngles == angleOpenForward ? SwingSide.Forward : SwingSide.Backward;

    private bool DoorCanClose(bool autoClose)
    {
        if (Master != null)
        {
            return Master.DoorCanClose(autoClose);
        }

        foreach (var slave in slaves)
        {
            if (!slave.IsRemoved && !slave.CheckDoorClear(autoClose ? SwingSide.Both : slave.OpenSide))
            {
                return false;
            }
        }

        return CheckDoorClear(autoClose ? SwingSide.Both : OpenSide);
    }

    /// <summary>
    /// Whether the space a swing sweeps is free of solid entities. The engine traces the swept box against
    /// entities only, leaving out the world, players and NPCs, the door's own parts, and whoever opened it.
    /// </summary>
    private bool CheckDoorClear(SwingSide side)
    {
        var volume = side switch
        {
            SwingSide.Forward => forwardVolume,
            SwingSide.Backward => backVolume,
            _ => forwardVolume.Union(backVolume),
        };

        if (volume.Size == Vector3.Zero)
        {
            return true;
        }

        var ignore = lastActivator ?? Master?.lastActivator;

        // Shrunk by the surface margin, so a neighbour resting exactly against the swept box does not count
        var center = WorldOrigin + volume.Center;
        var halfExtents = volume.Size * 0.5f - new Vector3(Rubikon.SurfaceEpsilon);

        foreach (var entity in EntitySystem.Entities)
        {
            if (entity == this || entity == ignore || entity == Owner || entity.MoveParent == this
                || entity is WorldEntity or PlayerEntity || !entity.IsCollidable)
            {
                continue;
            }

            if (entity.Collider!.OverlapsVolume(center, halfExtents))
            {
                return false;
            }
        }

        return true;
    }

    private void CalcDoorSounds()
    {
        soundOpen = NonEmpty(KeyValues.GetStringProperty("soundopenoverride"));
        soundClose = NonEmpty(KeyValues.GetStringProperty("soundcloseoverride"));
        soundMove = NonEmpty(KeyValues.GetStringProperty("soundmoveoverride"));
        soundLocked = NonEmpty(KeyValues.GetStringProperty("soundlockedoverride"));
        soundUnlocked = NonEmpty(KeyValues.GetStringProperty("soundunlockedoverride"));

        // What the map leaves out comes from the model's door_sounds, or failing that the metal door's. The
        // skin and hardware blocks of door_options are consulted after the metal defaults are already in
        // place, so they never supply any of these.
        if (LoadedModel?.KeyValues.GetSubCollection("door_sounds") is { ValueType: KVValueType.Collection } doorSounds)
        {
            soundOpen ??= NonEmpty(doorSounds.GetStringProperty("opened"));
            soundClose ??= NonEmpty(doorSounds.GetStringProperty("closed"));
            soundMove ??= NonEmpty(doorSounds.GetStringProperty("move"));
            soundLocked ??= NonEmpty(doorSounds.GetStringProperty("locked"));
            soundUnlocked ??= NonEmpty(doorSounds.GetStringProperty("unlatch"));
        }
        else
        {
            soundOpen ??= "DoorMetal.FullyOpened";
            soundClose ??= "DoorMetal.FullyClosed";
            soundMove ??= "DoorMetal.PhysMovementLp";
            soundLocked ??= "DoorMetal.HandleJiggle";
            soundUnlocked ??= "DoorHandles.Unlocked1";
        }

        foreach (var sound in (string?[])[soundOpen, soundClose, soundMove, soundLocked, soundUnlocked])
        {
            if (sound != null)
            {
                Sound.Cache(sound);
            }
        }
    }

    /// <summary>
    /// Runs on the first tick something blocks the swing. The door holds where it is, falls quiet, and
    /// carries on once it is clear.
    /// </summary>
    protected override void OnStartBlocked(BaseEntity blocker)
    {
        StopMoveSound();
        EntitySystem.TriggerOutput(this, State == DoorState.Closing ? "OnBlockedClosing" : "OnBlockedOpening", blocker);
    }

    /// <inheritdoc/>
    protected override void OnEndBlocked()
    {
        StartMoveSound();
        EntitySystem.TriggerOutput(this, State == DoorState.Closing ? "OnUnblockedClosing" : "OnUnblockedOpening", this);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private bool IsSilent => HasSpawnFlags(SpawnFlag.Silent);

    // Started once per swing; reversing part way keeps the one already playing
    private void StartMoveSound()
    {
        if (IsSilent || isMoveSoundOn || soundMove == null)
        {
            return;
        }

        moveSound = Sound.Play(soundMove, WorldOrigin);
        isMoveSoundOn = true;
    }

    private void StopMoveSound()
    {
        moveSound.Stop();
        moveSound = default;
        isMoveSoundOn = false;
    }

    private void PlaySound(string? soundEvent)
    {
        if (!IsSilent && soundEvent != null)
        {
            Sound.Play(soundEvent, WorldOrigin);
        }
    }

    // Source's PlayLockSounds, which debounces so a held use does not rattle the handle every tick
    private void PlayLockSound(bool locked)
    {
        if (IsSilent || EntitySystem.CurrentTime <= lockSoundNext)
        {
            return;
        }

        var sound = locked ? soundLocked : soundUnlocked;

        if (sound == null)
        {
            return;
        }

        Sound.Play(sound, WorldOrigin);
        lockSoundNext = EntitySystem.CurrentTime + LockSoundWait;
    }
}
