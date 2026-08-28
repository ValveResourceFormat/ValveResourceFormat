using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_button</c>. A brush that slides in when pressed and, unless it is told to stay in, slides back
/// out after <c>wait</c> seconds. Ported from Source's <c>CBaseButton</c>, movement included. Not
/// simulated: the button sounds, <c>health</c>-driven damage activation, and the sparks.
/// </summary>
public sealed class FuncButton : BaseToggle
{
    /// <summary>What a <c>func_button</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>No flags.</summary>
        None = 0,

        /// <summary>Fires without sliding anywhere.</summary>
        DontMove = 1,

        /// <summary>Stays in until pressed again, rather than returning after <c>wait</c>.</summary>
        Toggle = 32,

        /// <summary>Presses when something touches it.</summary>
        TouchActivates = 256,

        /// <summary>Presses when damaged. Nothing here deals damage.</summary>
        DamageActivates = 512,

        /// <summary>Presses when a player looks at it and presses use.</summary>
        UseActivates = 1024,

        /// <summary>Starts locked, refusing to press until unlocked.</summary>
        StartsLocked = 2048,

        /// <summary>Sparks while out. Not simulated.</summary>
        SparkIfOff = 4096,

        /// <summary>Every flag that names a way to press the button.</summary>
        AnyActivation = TouchActivates | DamageActivates | UseActivates,
    }

    /// <summary>Where a button is in its travel. Source's <c>m_toggle_state</c>.</summary>
    public enum ButtonState
    {
        /// <summary>Out, at rest.</summary>
        AtBottom,

        /// <summary>Sliding in.</summary>
        GoingUp,

        /// <summary>In, at rest.</summary>
        AtTop,

        /// <summary>Sliding back out.</summary>
        GoingDown,
    }

    /// <summary>Which state function the pending move-done runs, Source's <c>m_pfnCallWhenMoveDone</c>.</summary>
    private enum MoveDoneFunction
    {
        None,
        TriggerAndWait,
        ButtonReturn,
        ButtonBackHome,
    }

    /// <summary>Gets where the button is in its travel.</summary>
    public ButtonState State { get; private set; }

    /// <summary>Gets the seconds the button stays in before returning; -1 means it stays in for good.</summary>
    public float Wait { get; private set; }

    /// <summary>Gets whether the button refuses to press.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// Gets whether the button has been switched off. A disabled button is not drawn, not solid, and not
    /// something a trace can find, so a pair of them can share a spot and take turns.
    /// </summary>
    public bool IsDisabled { get; private set; }

    /// <summary>
    /// Gets whether a player can press this button. A button that names no way of being activated at all
    /// is treated as use-activated: the flag values are Source 1's and the Source 2 games this renders
    /// are not confirmed to match, so the permissive reading keeps the button working rather than leaving
    /// it inert. A locked button is still usable, because refusing the press is what produces
    /// <c>OnUseLocked</c>.
    /// </summary>
    public override EntityCapability ObjectCaps
        => !IsDisabled && (HasSpawnFlags(SpawnFlag.UseActivates) || !HasSpawnFlags(SpawnFlag.AnyActivation))
            ? EntityCapability.ImpulseUse
            : EntityCapability.None;

    private Vector3 positionOut;
    private Vector3 positionIn;
    private bool staysPushed;
    private MoveDoneFunction moveDoneFunction;
    private BaseEntity? lastActivator;

    /// <summary>Initializes a <c>func_button</c> from its keyvalues.</summary>
    public FuncButton(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        ResolveMoveDirection();

        Speed = KeyValues.GetFloatProperty("speed", 40f);

        if (Speed == 0f)
        {
            Speed = 40f;
        }

        Wait = KeyValues.GetFloatProperty("wait", 1f);

        if (Wait == 0f)
        {
            Wait = 1f;
        }

        Lip = KeyValues.GetFloatProperty("lip");

        if (Lip == 0f)
        {
            Lip = 4f;
        }

        IsLocked = HasSpawnFlags(SpawnFlag.StartsLocked);
        staysPushed = Wait == -1f;

        // Touch activation needs the button to report what is inside it, without giving up being solid
        IsTrigger = HasSpawnFlags(SpawnFlag.TouchActivates);

        positionOut = Origin;
        positionIn = positionOut + MoveDirection * GetTravelDistance();

        // A button with nowhere to go fires in place
        if (HasSpawnFlags(SpawnFlag.DontMove) || (positionIn - positionOut).Length() < 1f)
        {
            positionIn = positionOut;
        }

        State = ButtonState.AtBottom;
    }

    /// <inheritdoc/>
    public override void Use(BaseEntity? activator)
    {
        // A switched-off button is not there to press, however the press arrived
        if (IsDisabled)
        {
            return;
        }

        // Mid-travel presses are ignored, so a button cannot be interrupted
        if (State is ButtonState.GoingUp or ButtonState.GoingDown)
        {
            return;
        }

        lastActivator = activator;

        if (State == ButtonState.AtTop)
        {
            // Only a toggle button comes back out on a second press
            if (HasSpawnFlags(SpawnFlag.Toggle) && !staysPushed)
            {
                ButtonReturn();
            }

            return;
        }

        ButtonActivate();
    }

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        if (HasSpawnFlags(SpawnFlag.TouchActivates))
        {
            Use(other);
        }
    }

    /// <inheritdoc/>
    public override void MoveDone()
    {
        FinishLinearMove();

        var next = moveDoneFunction;
        moveDoneFunction = MoveDoneFunction.None;

        switch (next)
        {
            case MoveDoneFunction.TriggerAndWait:
                TriggerAndWait();
                break;

            case MoveDoneFunction.ButtonReturn:
                ButtonReturn();
                break;

            case MoveDoneFunction.ButtonBackHome:
                ButtonBackHome();
                break;
        }
    }

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data)
    {
        IsDisabled = true;
        IsSolid = false;
        IsDrawn = false;
    }

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data)
    {
        IsDisabled = false;
        IsSolid = true;
        IsDrawn = true;
    }

    [EntityInput("Lock")]
    private void InputLock(EntityInputData data) => IsLocked = true;

    [EntityInput("Unlock")]
    private void InputUnlock(EntityInputData data) => IsLocked = false;

    [EntityInput("Press")]
    private void InputPress(EntityInputData data) => Use(data.Activator);

    [EntityInput("PressIn")]
    private void InputPressIn(EntityInputData data)
    {
        if (State != ButtonState.AtBottom)
        {
            return;
        }

        staysPushed = true;
        ButtonActivate();
    }

    [EntityInput("PressOut")]
    private void InputPressOut(EntityInputData data)
    {
        if (State == ButtonState.AtTop)
        {
            ButtonReturn();
        }
    }

    /// <summary>Starts the button moving in, unless it is locked.</summary>
    private void ButtonActivate()
    {
        if (IsLocked)
        {
            EntitySystem.TriggerOutput(this, "OnUseLocked", lastActivator);
            return;
        }

        State = ButtonState.GoingUp;
        moveDoneFunction = MoveDoneFunction.TriggerAndWait;

        LinearMove(positionIn);
    }

    /// <summary>The button has arrived in: fire, then either stay or schedule the return.</summary>
    private void TriggerAndWait()
    {
        State = ButtonState.AtTop;

        EntitySystem.TriggerOutput(this, "OnPressed", lastActivator);
        EntitySystem.TriggerOutput(this, "OnIn", lastActivator);

        if (staysPushed || HasSpawnFlags(SpawnFlag.Toggle))
        {
            return;
        }

        moveDoneFunction = MoveDoneFunction.ButtonReturn;
        SetMoveDoneTime(Wait);
    }

    /// <summary>Starts the button moving back out.</summary>
    private void ButtonReturn()
    {
        State = ButtonState.GoingDown;
        moveDoneFunction = MoveDoneFunction.ButtonBackHome;

        LinearMove(positionOut);
    }

    /// <summary>The button has arrived back out.</summary>
    private void ButtonBackHome()
    {
        State = ButtonState.AtBottom;

        EntitySystem.TriggerOutput(this, "OnOut", lastActivator);
    }
}
