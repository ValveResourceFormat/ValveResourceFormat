using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_door</c> and <c>func_movelinear</c>. A brush that slides open along its <c>movedir</c> and back
/// again, Source's <c>CBaseDoor</c>. Not simulated: the blocking behaviour that reverses a door onto
/// whoever stands in it, and the door groups that open together.
/// </summary>
public class FuncDoor : BaseToggle
{
    /// <summary>What a <c>func_door</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Things pass straight through it.</summary>
        Passable = 8,

        /// <summary>Stays open once opened, rather than coming back by itself.</summary>
        Toggle = 32,

        /// <summary>The player may open it by pressing use. Source's <c>SF_DOOR_PUSE</c>.</summary>
        UseOpens = 256,

        /// <summary>Opens when the player walks into it.</summary>
        TouchOpens = 1024,

        /// <summary>Spawns locked, and stays that way until something unlocks it.</summary>
        StartsLocked = 2048,

        /// <summary>Refuses use entirely, whatever else is set.</summary>
        IgnoreUse = 32768,
    }

    /// <summary>
    /// Gets whether the player can open this door by pressing it. Source's <c>CBaseDoor::ObjectCaps</c>:
    /// a door is only usable when the map said so.
    /// </summary>
    public override EntityCapability ObjectCaps
        => HasSpawnFlags(SpawnFlag.UseOpens) && !HasSpawnFlags(SpawnFlag.IgnoreUse)
            ? EntityCapability.ImpulseUse
            : EntityCapability.None;

    /// <summary>Gets where the door is in its travel.</summary>
    public bool IsOpen => State is ToggleState.AtTop or ToggleState.GoingUp;

    /// <summary>Gets whether the door refuses to open.</summary>
    public bool IsLocked { get; protected set; }

    /// <summary>Gets the seconds the door stays open before closing; -1 means it stays open.</summary>
    public float Wait { get; protected set; }

    /// <summary>Gets where the door is in its travel.</summary>
    protected ToggleState State { get; private set; }

    /// <summary>Gets the place the door rests when closed.</summary>
    protected Vector3 PositionClosed { get; set; }

    /// <summary>Gets the place the door rests when open.</summary>
    protected Vector3 PositionOpen { get; set; }

    /// <summary>Gets the entity that last set the door moving, for whatever the travel needs it for.</summary>
    protected BaseEntity? LastActivator { get; private set; }

    /// <summary>Gets whether the door closes whatever stands in it, the <c>forceclosed</c> keyvalue.</summary>
    public bool ForceClosed { get; private set; }

    /// <inheritdoc/>
    protected override bool PusherForcesThrough => ForceClosed;

    /// <summary>Initializes a <c>func_door</c> from its keyvalues.</summary>
    public FuncDoor(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // A door keeps its authored orientation: only a button spends its angles on the travel direction
        ResolveMoveDirection(consumeAngles: false);

        Speed = KeyValues.GetFloatProperty("speed", 100f);

        if (Speed <= 0f)
        {
            Speed = 100f;
        }

        Wait = ReadWait();
        Lip = KeyValues.GetFloatProperty("lip");
        ForceClosed = KeyValues.GetBooleanProperty("forceclosed");
        // Both spellings: the flag is how the compiled maps carry it, the keyvalue how the FGD offers it
        IsLocked = HasSpawnFlags(SpawnFlag.StartsLocked) || KeyValues.GetBooleanProperty("startlocked");

        if (HasSpawnFlags(SpawnFlag.Passable))
        {
            IsSolid = false;
        }

        PositionClosed = Origin;
        PositionOpen = PositionClosed + (MoveDirection * GetTravelDistance());

        SetUpTravel();

        // A door that spawns open is authored at its open position, so the two ends swap
        if (SpawnsOpen())
        {
            SwapEnds();
            State = ToggleState.AtBottom;
        }
    }

    /// <summary>Reads how long the door stays open, whichever keyvalue the class authors it as.</summary>
    protected virtual float ReadWait() => KeyValues.GetFloatProperty("wait", 4f);

    /// <summary>Reads whether the map authored the door at its open position.</summary>
    protected virtual bool SpawnsOpen() => KeyValues.GetBooleanProperty("spawnpos");

    /// <summary>
    /// Works out where the door's two ends are. A door that turns rather than slides overrides this.
    /// </summary>
    protected virtual void SetUpTravel()
    {
    }

    /// <summary>Exchanges the two ends, for a door the map authored in its open position.</summary>
    protected virtual void SwapEnds()
    {
        (PositionClosed, PositionOpen) = (PositionOpen, PositionClosed);
        Origin = PositionClosed;
    }

    /// <summary>Sets the door travelling towards one of its two ends.</summary>
    protected virtual void StartMove(bool opening) => LinearMove(opening ? PositionOpen : PositionClosed);

    /// <inheritdoc/>
    public override void MoveDone()
    {
        FinishLinearMove();

        if (State == ToggleState.GoingUp)
        {
            State = ToggleState.AtTop;

            OnArrived(open: true);
            EntitySystem.TriggerOutput(this, "OnFullyOpen");

            // A toggle door waits to be told; the rest close themselves after their wait
            if (!StaysOpen && Wait >= 0f)
            {
                SetNextThink(EntitySystem.CurrentTime + Wait);
            }

            return;
        }

        if (State == ToggleState.GoingDown)
        {
            State = ToggleState.AtBottom;

            OnArrived(open: false);
            EntitySystem.TriggerOutput(this, "OnFullyClosed");
        }
    }

    /// <summary>Gets whether the door stays open until told to close.</summary>
    private bool StaysOpen => HasSpawnFlags(SpawnFlag.Toggle);

    /// <summary>Runs when the door lands at either end, for a class with sounds to play.</summary>
    protected virtual void OnArrived(bool open)
    {
    }

    /// <summary>Runs as the door sets off, for a class with sounds to play.</summary>
    protected virtual void OnSetOff(bool opening)
    {
    }

    /// <summary>Closes the door once it has stood open for its wait.</summary>
    public override void Think() => Close();

    /// <inheritdoc/>
    public override void Use(BaseEntity? activator) => Toggle(activator);

    // Protected rather than private, so a subclass's input table inherits them

    /// <summary>Opens the door.</summary>
    [EntityInput("Open")]
    protected void InputOpen(EntityInputData data) => Open(data.Activator);

    /// <summary>Closes the door.</summary>
    [EntityInput("Close")]
    protected void InputClose(EntityInputData data) => Close();

    /// <summary>Opens a closed door, closes an open one.</summary>
    [EntityInput("Toggle")]
    protected void InputToggle(EntityInputData data) => Toggle(data.Activator);

    /// <summary>Stops the door opening until it is unlocked.</summary>
    [EntityInput("Lock")]
    protected void InputLock(EntityInputData data) => IsLocked = true;

    /// <summary>Lets the door open again.</summary>
    [EntityInput("Unlock")]
    protected void InputUnlock(EntityInputData data) => IsLocked = false;

    /// <summary>Changes how fast the door travels.</summary>
    [EntityInput("SetSpeed")]
    protected void InputSetSpeed(EntityInputData data) => Speed = MathF.Max(data.Float(Speed), 0f);

    /// <summary>Opens the door, unless it is locked or already going that way.</summary>
    public void Open(BaseEntity? activator = null)
    {
        LastActivator = activator;

        if (IsLocked)
        {
            OnLockedUse();
            EntitySystem.TriggerOutput(this, "OnLockedUse", activator);
            return;
        }

        if (State is ToggleState.AtTop or ToggleState.GoingUp)
        {
            return;
        }

        State = ToggleState.GoingUp;
        SetNextThink(-1f);

        EntitySystem.TriggerOutput(this, "OnOpen", activator);

        OnSetOff(opening: true);
        StartMove(opening: true);
    }

    /// <summary>Closes the door, unless it is already going that way.</summary>
    public void Close()
    {
        if (State is ToggleState.AtBottom or ToggleState.GoingDown)
        {
            return;
        }

        State = ToggleState.GoingDown;
        SetNextThink(-1f);

        EntitySystem.TriggerOutput(this, "OnClose");

        OnSetOff(opening: false);
        StartMove(opening: false);
    }

    /// <summary>Runs on a press that the lock refused, for a class with sounds to play.</summary>
    protected virtual void OnLockedUse()
    {
    }

    /// <summary>Opens a closed door and closes an open one, which is what a use or a toggle means.</summary>
    public void Toggle(BaseEntity? activator = null)
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open(activator);
        }
    }
}
