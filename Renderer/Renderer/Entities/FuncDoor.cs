using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_door</c>. A brush that slides open along its <c>movedir</c> and back again. Not simulated:
/// sounds, the <c>master</c>, damage to whatever blocks it, and door groups.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBaseDoor">CBaseDoor</seealso>
public class FuncDoor : BaseToggle
{
    /// <summary>What a <c>func_door</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>
        /// The legacy way to spawn open: spawns at the open end, with the fully open and fully closed
        /// outputs swapped.
        /// </summary>
        StartsOpen = 1,

        /// <summary>
        /// Non-solid to the player, who can never block it. In game the player is also carried along the whole
        /// push; only the never-blocking part is modeled.
        /// </summary>
        NonSolidToPlayer = 4,

        /// <summary>Things pass straight through it.</summary>
        Passable = 8,

        /// <summary>Stays open until told to close.</summary>
        Toggle = 32,

        /// <summary>The player may open it by pressing use.</summary>
        UseOpens = 256,

        /// <summary>Opens when the player walks into it.</summary>
        TouchOpens = 1024,

        /// <summary>Spawns locked until unlocked.</summary>
        StartsLocked = 2048,

        /// <summary>Refuses use entirely, whatever else is set.</summary>
        IgnoreUse = 32768,

        /// <summary>Also takes a use while moving, the way a prop door does.</summary>
        NewUseRules = 65536,
    }

    private enum MoveDoneFunction
    {
        None,
        HitTop,
        HitBottom,
        GoDown,
    }

    /// <summary>Gets whether the player can open this door by pressing it.</summary>
    public override EntityCapability ObjectCaps
        => HasSpawnFlags(SpawnFlag.UseOpens) && !HasSpawnFlags(SpawnFlag.IgnoreUse)
            ? EntityCapability.ImpulseUse
            : EntityCapability.None;

    /// <summary>Gets whether the door is open or on its way there.</summary>
    public bool IsOpen => State is ToggleState.AtTop or ToggleState.GoingUp;

    /// <summary>Gets whether the door refuses to open.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Gets the seconds the door stays open before closing; a negative wait keeps it open.</summary>
    public float Wait { get; private set; }

    /// <summary>
    /// Gets the <c>forceclosed</c> keyvalue: a blocked door holds its course instead of turning back,
    /// but still cannot move through the blocker.
    /// </summary>
    public bool ForceClosed { get; private set; }

    /// <summary>Gets where the door is in its travel.</summary>
    protected ToggleState State { get; set; }

    /// <summary>Gets the place the door rests when closed.</summary>
    protected Vector3 PositionClosed { get; private set; }

    /// <summary>Gets the place the door rests when open.</summary>
    protected Vector3 PositionOpen { get; private set; }

    /// <summary>
    /// Gets the entity that last set the door going. The outputs report it; reaching either end clears it.
    /// </summary>
    protected BaseEntity? LastActivator { get; private set; }

    /// <inheritdoc/>
    protected override bool IsUnblockableByPlayer => HasSpawnFlags(SpawnFlag.NonSolidToPlayer);

    private bool StaysOpen => HasSpawnFlags(SpawnFlag.Toggle);

    private BaseEntity OutputActivator => LastActivator ?? this;

    private MoveDoneFunction moveDoneFunction;
    private string? chainTarget;

    // A touch that sets the door going is the last until it comes back
    private bool isTouchArmed;

    // Stops chained doors passing a touch back
    private bool isChaining;

    /// <summary>Initializes a <c>func_door</c> from its keyvalues.</summary>
    public FuncDoor(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var localDirection = ResolveEntitySpaceMoveDirection();

        // Only an unset speed falls back; a negative one is kept
        Speed = KeyValues.GetFloatProperty("speed");

        if (Speed == 0f)
        {
            Speed = 100f;
        }

        Wait = KeyValues.GetFloatProperty("wait", 4f);
        Lip = KeyValues.GetFloatProperty("lip");
        ForceClosed = KeyValues.GetBooleanProperty("forceclosed");
        IsLocked = HasSpawnFlags(SpawnFlag.StartsLocked);
        chainTarget = KeyValues.GetStringProperty("chainstodoor");
        isTouchArmed = true;

        if (HasSpawnFlags(SpawnFlag.Passable))
        {
            IsSolid = false;
        }

        PositionClosed = Origin;
        PositionOpen = PositionClosed + MoveDirection * GetTravelDistance(localDirection);

        SetUpSpawnPosition();
    }

    /// <summary>
    /// Puts the door where it spawns. One that spawns open is moved to its open end, so closing it returns
    /// it to the authored position.
    /// </summary>
    protected virtual void SetUpSpawnPosition()
    {
        if (KeyValues.GetInt32Property("spawnpos") == 1 || HasSpawnFlags(SpawnFlag.StartsOpen))
        {
            JumpTo(PositionOpen, Angles);
            State = ToggleState.AtTop;
            return;
        }

        State = ToggleState.AtBottom;
    }

    /// <summary>Sets the door travelling towards one of its two ends.</summary>
    protected virtual void StartMove(bool opening) => LinearMove(opening ? PositionOpen : PositionClosed);

    /// <summary>Puts the door at one of its ends without travelling or changing its state.</summary>
    /// <param name="atOpenEnd">Whether to the open end rather than the closed one.</param>
    protected virtual void JumpToEnd(bool atOpenEnd) => JumpTo(atOpenEnd ? PositionOpen : PositionClosed, Angles);

    /// <inheritdoc/>
    public override void MoveDone()
    {
        FinishLinearMove();

        var next = moveDoneFunction;
        moveDoneFunction = MoveDoneFunction.None;

        switch (next)
        {
            case MoveDoneFunction.HitTop:
                HitTop();
                break;

            case MoveDoneFunction.HitBottom:
                HitBottom();
                break;

            case MoveDoneFunction.GoDown:
                GoDown();
                break;
        }
    }

    /// <summary>
    /// Ignores the press while the door is moving, or open and due to close by itself, unless
    /// <see cref="SpawnFlag.NewUseRules"/> is set.
    /// </summary>
    public override void Use(BaseEntity? activator)
    {
        LastActivator = activator;

        // Players cannot use a door without UseOpens (in game it only plays a locked sound)
        if (activator is PlayerEntity && !HasSpawnFlags(SpawnFlag.UseOpens))
        {
            return;
        }

        var allowed = HasSpawnFlags(SpawnFlag.NewUseRules)
            ? State is ToggleState.AtBottom or ToggleState.GoingDown
                || (StaysOpen && State is ToggleState.AtTop or ToggleState.GoingUp)
            : State == ToggleState.AtBottom
                || (StaysOpen && State == ToggleState.AtTop);

        if (!allowed)
        {
            return;
        }

        if (IsLocked)
        {
            EntitySystem.TriggerOutput(this, "OnLockedUse", activator);
            return;
        }

        DoorActivate();
    }

    /// <summary>
    /// Passes the touch on to chained doors, and opens this one for a player when
    /// <see cref="SpawnFlag.TouchOpens"/> is set.
    /// </summary>
    protected override void OnTouch(BaseEntity other)
    {
        if (!isTouchArmed)
        {
            return;
        }

        ChainTouch(other);

        if (other is not PlayerEntity || !HasSpawnFlags(SpawnFlag.TouchOpens))
        {
            return;
        }

        if (IsLocked)
        {
            EntitySystem.TriggerOutput(this, "OnLockedUse", other, caller: other);
            return;
        }

        LastActivator = other;
        DoorActivate();

        isTouchArmed = false;
    }

    // Only chains to doors in the same state, so an open door is not closed by it
    private void ChainTouch(BaseEntity other)
    {
        if (isChaining || string.IsNullOrEmpty(chainTarget))
        {
            return;
        }

        foreach (var entity in EntitySystem.FindAllByTargetName(chainTarget))
        {
            if (entity != this && entity is FuncDoor door && door.State == State)
            {
                door.isChaining = true;
                door.OnTouch(other);
                door.isChaining = false;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnStartBlocked(BaseEntity blocker)
        => EntitySystem.TriggerOutput(this, State == ToggleState.GoingDown ? "OnBlockedClosing" : "OnBlockedOpening", blocker);

    /// <inheritdoc/>
    protected override void OnEndBlocked()
        => EntitySystem.TriggerOutput(this, State == ToggleState.GoingDown ? "OnUnblockedClosing" : "OnUnblockedOpening", this);

    /// <summary>
    /// Runs every tick the door is blocked. Unless <see cref="ForceClosed"/> is set, turns the door around
    /// and every door sharing its name; doors with a negative <see cref="Wait"/> never turn.
    /// </summary>
    protected override void OnBlocked(BaseEntity blocker)
    {
        if (ForceClosed)
        {
            return;
        }

        if (Wait >= 0f)
        {
            Reverse();
        }

        if (string.IsNullOrEmpty(TargetName))
        {
            return;
        }

        foreach (var entity in EntitySystem.FindAllByTargetName(TargetName))
        {
            if (entity != this && entity is FuncDoor { Wait: >= 0f } door)
            {
                door.Reverse();
            }
        }
    }

    // A door that was closing opens again, anything else closes
    private void Reverse()
    {
        if (State == ToggleState.GoingDown)
        {
            GoUp();
        }
        else
        {
            GoDown();
        }
    }

    // A toggle door standing open closes; any other door opens unless it already is
    private void DoorActivate()
    {
        if (StaysOpen && State == ToggleState.AtTop)
        {
            GoDown();
        }
        else if (!IsOpen)
        {
            GoUp();
        }
    }

    // Protected rather than private, so a subclass's input table inherits them

    /// <summary>Opens the door. Ignored silently when locked or already open.</summary>
    [EntityInput("Open")]
    protected void InputOpen(EntityInputData data)
    {
        if (IsOpen || IsLocked)
        {
            return;
        }

        // Only inputs that start the door replace the activator
        LastActivator = data.Activator;
        GoUp();
    }

    /// <summary>Closes the door, even one already closing, which sets it off again.</summary>
    [EntityInput("Close")]
    protected void InputClose(EntityInputData data)
    {
        if (State != ToggleState.AtBottom)
        {
            LastActivator = data.Activator;
            GoDown();
        }
    }

    /// <summary>Opens a closed door and closes an open one. A moving or locked door ignores it.</summary>
    [EntityInput("Toggle")]
    protected void InputToggle(EntityInputData data)
    {
        if (IsLocked)
        {
            return;
        }

        if (State == ToggleState.AtBottom)
        {
            LastActivator = data.Activator;
            GoUp();
        }
        else if (State == ToggleState.AtTop)
        {
            LastActivator = data.Activator;
            GoDown();
        }
    }

    /// <summary>Stops the door opening until it is unlocked.</summary>
    [EntityInput("Lock")]
    protected void InputLock(EntityInputData data) => IsLocked = true;

    /// <summary>Lets the door open again.</summary>
    [EntityInput("Unlock")]
    protected void InputUnlock(EntityInputData data) => IsLocked = false;

    /// <summary>Changes how fast the door travels from its next move on.</summary>
    [EntityInput("SetSpeed")]
    protected void InputSetSpeed(EntityInputData data) => Speed = data.Float();

    /// <summary>Jumps the door to its open end for 0, else its closed end.</summary>
    [EntityInput("SetToggleState")]
    protected void InputSetToggleState(EntityInputData data) => JumpToEnd(data.Int() == 0);

    private void GoUp()
    {
        State = ToggleState.GoingUp;
        moveDoneFunction = MoveDoneFunction.HitTop;

        StartMove(opening: true);

        EntitySystem.TriggerOutput(this, "OnOpen", OutputActivator);
    }

    private void GoDown()
    {
        State = ToggleState.GoingDown;
        moveDoneFunction = MoveDoneFunction.HitBottom;

        StartMove(opening: false);

        EntitySystem.TriggerOutput(this, "OnClose", OutputActivator);
    }

    private void HitTop()
    {
        State = ToggleState.AtTop;

        // Toggle doors wait to be told and can be touched again; others close after Wait (never if negative)
        if (StaysOpen)
        {
            isTouchArmed = true;
        }
        else
        {
            moveDoneFunction = MoveDoneFunction.GoDown;
            SetMoveDoneTime(Wait);
        }

        EntitySystem.TriggerOutput(this, HasSpawnFlags(SpawnFlag.StartsOpen) ? "OnFullyClosed" : "OnFullyOpen", OutputActivator);

        LastActivator = null;
    }

    private void HitBottom()
    {
        State = ToggleState.AtBottom;
        isTouchArmed = true;

        EntitySystem.TriggerOutput(this, HasSpawnFlags(SpawnFlag.StartsOpen) ? "OnFullyOpen" : "OnFullyClosed", OutputActivator);

        LastActivator = null;
    }
}
