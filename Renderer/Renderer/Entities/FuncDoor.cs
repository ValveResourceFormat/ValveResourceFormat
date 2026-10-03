using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_door</c>. A brush that slides open along its <c>movedir</c> and back again, Source's
/// <c>CBaseDoor</c>. It opens when used, told to, or walked into, and a walk into it can be passed on to the
/// doors it chains to. Something in its way turns it around. Not simulated: the sounds, the <c>master</c>
/// that has to be triggered first, the damage it deals whatever blocks it, and the door groups that open together.
/// </summary>
public class FuncDoor : BaseToggle
{
    /// <summary>What a <c>func_door</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>
        /// The old way to spawn open: the door spawns at its open end and treats it as closed, so the fully
        /// open and fully closed outputs swap too.
        /// </summary>
        StartsOpen = 1,

        /// <summary>
        /// Non-solid to the player, which also means the player can never block it: a player in its way is
        /// carried the whole push, through anything else if need be. Only the never-blocking part is modeled.
        /// </summary>
        NonSolidToPlayer = 4,

        /// <summary>Things pass straight through it.</summary>
        Passable = 8,

        /// <summary>Stays open once opened, rather than coming back by itself. Source's <c>SF_DOOR_NO_AUTO_RETURN</c>.</summary>
        Toggle = 32,

        /// <summary>The player may open it by pressing use. Source's <c>SF_DOOR_PUSE</c>.</summary>
        UseOpens = 256,

        /// <summary>Opens when the player walks into it.</summary>
        TouchOpens = 1024,

        /// <summary>Spawns locked, and stays that way until something unlocks it.</summary>
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

    /// <summary>
    /// Gets whether the player can open this door by pressing it. Source's <c>CBaseDoor::ObjectCaps</c>:
    /// a door is only usable when the map said so.
    /// </summary>
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
    /// Gets whether a blocked door holds its course rather than turning back, the <c>forceclosed</c> keyvalue.
    /// It still cannot move through what blocks it.
    /// </summary>
    public bool ForceClosed { get; private set; }

    /// <summary>Gets where the door is in its travel.</summary>
    protected ToggleState State { get; set; }

    /// <summary>Gets the place the door rests when closed.</summary>
    protected Vector3 PositionClosed { get; private set; }

    /// <summary>Gets the place the door rests when open.</summary>
    protected Vector3 PositionOpen { get; private set; }

    /// <summary>
    /// Gets the entity that last set the door going, Source's <c>m_hActivator</c>. The outputs report it, and
    /// arriving at either end forgets it.
    /// </summary>
    protected BaseEntity? LastActivator { get; private set; }

    /// <inheritdoc/>
    protected override bool IsUnblockableByPlayer => HasSpawnFlags(SpawnFlag.NonSolidToPlayer);

    private bool StaysOpen => HasSpawnFlags(SpawnFlag.Toggle);

    // CS2 reports the activator on every output when there is one, where Source 1 mostly reported the door
    private BaseEntity OutputActivator => LastActivator ?? this;

    private MoveDoneFunction moveDoneFunction;
    private string? chainTarget;

    // Source's m_pfnTouch being DoorTouch: a touch that sets the door going is the last until it comes back
    private bool isTouchArmed;

    // Set while a chained door passes a touch on, so the doors it reaches do not pass it back
    private bool isChaining;

    /// <summary>Initializes a <c>func_door</c> from its keyvalues.</summary>
    public FuncDoor(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var localDirection = ResolveEntitySpaceMoveDirection();

        // Only an unset speed falls back; a negative one is kept, as the engine keeps it
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
    /// Puts the door where it spawns. A sliding door that spawns open is moved to its open end and counts as
    /// open, so closing it takes it back to where the map authored it.
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

    /// <summary>
    /// Puts the door at one of its ends outright, without travelling or changing what it thinks it is doing.
    /// Source's <c>SetToggleState</c>.
    /// </summary>
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
    /// Runs when something presses the door. Source's <c>CBaseDoor::Use</c>: a door that is moving, or open
    /// and due to close by itself, ignores the press unless <see cref="SpawnFlag.NewUseRules"/> is set.
    /// </summary>
    public override void Use(BaseEntity? activator)
    {
        LastActivator = activator;

        // A player pressing a door the map never made usable only hears that it is locked
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
    /// Runs when the player walks into the door. Source's <c>CBaseDoor::DoorTouch</c>: the touch is passed on
    /// to the doors this one chains to, and opens this one when <see cref="SpawnFlag.TouchOpens"/> is set.
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

    // Only the chained doors in the same state as this one, so a door already open is not closed by it
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
    /// Runs on every tick something blocks the door. Source's <c>CBaseDoor::Blocked</c>: unless it closes
    /// through or never comes back by itself, the door turns around, and so does every door sharing its name.
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

        foreach (var entity in EntitySystem.FindAllByTargetName(TargetName, Scene))
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

    /// <summary>Opens the door, unless it is locked or already open. A locked door says nothing.</summary>
    [EntityInput("Open")]
    protected void InputOpen(EntityInputData data)
    {
        if (IsOpen || IsLocked)
        {
            return;
        }

        // Only an input that sets the door going replaces who the outputs report
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

    /// <summary>Puts the door at its open end for 0 and its closed end otherwise, without moving it there.</summary>
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

        // A toggle door waits to be told, and can be walked into again; the rest close by themselves after
        // their wait, and a negative wait never comes due
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
