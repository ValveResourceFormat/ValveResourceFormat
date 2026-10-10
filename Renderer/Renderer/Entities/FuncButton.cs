using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_button</c>. A brush that slides in along its <c>movedir</c> when pressed, fires its outputs, and
/// either comes back out after <c>wait</c> seconds or stays in until pressed again. It can be pressed by
/// use, touch, damage or input. Not simulated: <c>master</c>, as nothing here can be one, and the spark
/// effect, of which only the sound plays.
/// </summary>
public sealed class FuncButton : BaseToggle
{
    /// <summary>What a <c>func_button</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Stays where it is when pressed, going through the motions without travelling.</summary>
        DontMove = 1,

        /// <summary>Spawns disabled, which only stops it being used.</summary>
        StartsDisabled = 2,

        /// <summary>Stays in once pressed, and a second press brings it back out.</summary>
        Toggle = 32,

        /// <summary>Pressed by the player walking into it.</summary>
        TouchActivates = 256,

        /// <summary>Pressed by taking damage.</summary>
        DamageActivates = 512,

        /// <summary>Pressed by the player's use.</summary>
        UseActivates = 1024,

        /// <summary>Spawns locked.</summary>
        StartsLocked = 2048,

        /// <summary>
        /// Sparks every so often while out. Also silences the locked and unlocked sounds, as the same bit is
        /// tested for those.
        /// </summary>
        Sparks = 4096,

        /// <summary>Nothing collides with it.</summary>
        NonSolid = 16384,
    }

    private const float LockSoundWait = 0.5f;

    // Stops holding use from flooding OnUseLocked
    private const float UseLockedWait = 0.5f;

    private enum ThinkFunction
    {
        None,
        Spark,
        Return,
    }

    private enum MoveDoneFunction
    {
        None,
        TriggerAndWait,
        BackHome,
    }

    /// <summary>Gets whether the button refuses to be pressed. The <c>Lock</c> input sets it.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Gets whether use is ignored. The <c>Disable</c> input sets it; touch, damage and inputs still press it.</summary>
    public bool IsDisabled { get; private set; }

    /// <summary>
    /// Gets whether the client shows the button as usable, a hint only: <see cref="ObjectCaps"/> decides
    /// whether use reaches it.
    /// </summary>
    public bool IsUsable { get; private set; }

    /// <summary>Gets the seconds the button stays in before coming back out; -1 stays in for good.</summary>
    public float Wait { get; private set; }

    /// <summary>Gets whether the button stays in once pressed, which a <see cref="Wait"/> of -1 means.</summary>
    public bool StaysPushed { get; private set; }

    /// <summary>Gets the text shown when looking at the button, the <c>displaytext</c> keyvalue.</summary>
    public string? DisplayText { get; private set; }

    /// <summary>Gets the entity that glows to show the button can be used, named by the <c>glow</c> keyvalue.</summary>
    public BaseEntity? GlowEntity { get; private set; }

    /// <summary>Gets the position the button rests at, out.</summary>
    public Vector3 PositionOut { get; private set; }

    /// <summary>Gets the position the button is pressed in to.</summary>
    public Vector3 PositionIn { get; private set; }

    /// <summary>Gets whether the player can press this button with use.</summary>
    public override EntityCapability ObjectCaps
        => !IsDisabled && HasSpawnFlags(SpawnFlag.UseActivates) ? EntityCapability.ImpulseUse : EntityCapability.None;

    private ToggleState state;
    private ThinkFunction thinkFunction;
    private MoveDoneFunction moveDoneFunction;

    // Pressing by touch disarms it until the button is back out
    private bool isTouchArmed;

    // Last presser, reported by OnIn and OnOut
    private BaseEntity? activator;

    private string? useSound;
    private string? lockedSound;
    private string? unlockedSound;
    private string? glowEntityName;

    // One hold-off shared by the locked and unlocked sounds
    private float lockSoundNext;
    private float useLockedNext;

    /// <summary>Initializes a <c>func_button</c> from its keyvalues.</summary>
    public FuncButton(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        useSound = GetSoundKeyValue("use_sound");
        lockedSound = GetSoundKeyValue("locked_sound");
        unlockedSound = GetSoundKeyValue("unlocked_sound");
        glowEntityName = KeyValues.GetStringProperty("glow");
        DisplayText = KeyValues.GetStringProperty("displaytext");
        Speed = KeyValues.GetFloatProperty("speed");
        Wait = KeyValues.GetFloatProperty("wait");
        Lip = KeyValues.GetFloatProperty("lip");

        if (HasSpawnFlags(SpawnFlag.Sparks))
        {
            thinkFunction = ThinkFunction.Spark;
            SetNextThink(EntitySystem.CurrentTime + 0.5f);
        }

        // The travel is measured with the lip as authored, before the default below fills an unset one in
        var localDirection = ResolveEntitySpaceMoveDirection();
        var distance = GetTravelDistance(localDirection);

        state = ToggleState.AtBottom;
        PositionOut = Origin;
        PositionIn = PositionOut + MoveDirection * distance;

        // Only unset values fall back; a negative speed or wait is kept
        if (Speed == 0f)
        {
            Speed = 40f;
        }

        if (Wait == 0f)
        {
            Wait = 1f;
        }

        if (Lip == 0f)
        {
            Lip = 4f;
        }

        // A button that would barely move is treated as one that does not move at all
        if (Vector3.Distance(PositionOut, PositionIn) < 1f || HasSpawnFlags(SpawnFlag.DontMove))
        {
            PositionIn = PositionOut;
        }

        StaysPushed = Wait == -1f;
        IsDisabled = HasSpawnFlags(SpawnFlag.StartsDisabled);
        IsLocked = HasSpawnFlags(SpawnFlag.StartsLocked);
        IsUsable = HasSpawnFlags(SpawnFlag.UseActivates);
        isTouchArmed = HasSpawnFlags(SpawnFlag.TouchActivates);

        if (HasSpawnFlags(SpawnFlag.NonSolid))
        {
            IsSolid = false;
        }
    }

    private string? GetSoundKeyValue(string key)
    {
        var sound = KeyValues.GetStringProperty(key);

        return string.IsNullOrEmpty(sound) ? null : sound;
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        if (string.IsNullOrEmpty(glowEntityName))
        {
            return;
        }

        GlowEntity = EntitySystem.FindByTargetName(glowEntityName);
    }

    /// <summary>Presses the button on use, if it has <see cref="SpawnFlag.UseActivates"/>.</summary>
    public override void Use(BaseEntity? activator)
    {
        if (!HasSpawnFlags(SpawnFlag.UseActivates) || state is ToggleState.GoingUp or ToggleState.GoingDown)
        {
            return;
        }

        if (IsLocked)
        {
            PlayLockSound(locked: true);

            if (EntitySystem.CurrentTime > useLockedNext)
            {
                EntitySystem.TriggerOutput(this, "OnUseLocked", activator);
                useLockedNext = EntitySystem.CurrentTime + UseLockedWait;
            }

            return;
        }

        this.activator = activator;

        if (state == ToggleState.AtBottom)
        {
            EntitySystem.TriggerOutput(this, "OnPressed", activator);
            ButtonActivate();
        }
        else if (HasSpawnFlags(SpawnFlag.Toggle))
        {
            PlayUseSound();
            EntitySystem.TriggerOutput(this, "OnPressed", activator);
            ButtonReturn();
        }
    }

    /// <summary>Presses the button by touch, for as long as the touch is armed.</summary>
    protected override void OnTouch(BaseEntity other)
    {
        if (!isTouchArmed || other is not PlayerEntity || state is ToggleState.GoingUp or ToggleState.GoingDown)
        {
            return;
        }

        var returns = state == ToggleState.AtTop;

        if (returns && (StaysPushed || !HasSpawnFlags(SpawnFlag.Toggle)))
        {
            return;
        }

        if (!IsMasterTriggered() || IsLocked)
        {
            PlayLockSound(locked: true);
            return;
        }

        activator = other;
        isTouchArmed = false;

        if (returns)
        {
            PlayUseSound();
            EntitySystem.TriggerOutput(this, "OnPressed", activator);
            ButtonReturn();
        }
        else
        {
            EntitySystem.TriggerOutput(this, "OnPressed", activator);
            ButtonActivate();
        }
    }

    /// <summary>
    /// Reports damage dealt to the button, which presses it when it has <see cref="SpawnFlag.DamageActivates"/>.
    /// Nothing deals damage yet, so this waits for whatever does.
    /// </summary>
    /// <param name="attacker">Who dealt the damage, who becomes the activator of the press.</param>
    public void TakeDamage(BaseEntity? attacker)
    {
        // Reports the previous activator, not the attacker
        EntitySystem.TriggerOutput(this, "OnDamaged", activator);

        if (!HasSpawnFlags(SpawnFlag.DamageActivates)
            || state is ToggleState.GoingUp or ToggleState.GoingDown
            || (state == ToggleState.AtTop && (StaysPushed || !HasSpawnFlags(SpawnFlag.Toggle)))
            || attacker == null
            || IsLocked)
        {
            return;
        }

        activator = attacker;
        isTouchArmed = false;

        if (state == ToggleState.AtBottom)
        {
            EntitySystem.TriggerOutput(this, "OnPressed", activator);
            ButtonActivate();
        }
        else
        {
            PlayUseSound();
            EntitySystem.TriggerOutput(this, "OnPressed", activator);
            ButtonReturn();
        }
    }

    // Inputs only pass their activator to OnPressed; OnIn and OnOut keep reporting the last presser

    [EntityInput("Press")]
    private void InputPress(EntityInputData data)
    {
        if (state is ToggleState.GoingUp or ToggleState.GoingDown || RejectLocked())
        {
            return;
        }

        if (state == ToggleState.AtBottom)
        {
            EntitySystem.TriggerOutput(this, "OnPressed", data.Activator);
            ButtonActivate();
        }
        else
        {
            PlayUseSound();
            EntitySystem.TriggerOutput(this, "OnPressed", data.Activator);
            ButtonReturn();
        }
    }

    // Also turns a button on its way out back in
    [EntityInput("PressIn")]
    private void InputPressIn(EntityInputData data)
    {
        if (state is ToggleState.GoingUp or ToggleState.AtTop || RejectLocked())
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnPressed", data.Activator);
        ButtonActivate();
    }

    // Also turns a button on its way in back out
    [EntityInput("PressOut")]
    private void InputPressOut(EntityInputData data)
    {
        if (state is ToggleState.GoingDown or ToggleState.AtBottom || RejectLocked())
        {
            return;
        }

        PlayUseSound();
        EntitySystem.TriggerOutput(this, "OnPressed", data.Activator);
        ButtonReturn();
    }

    // Shared by the press inputs: a locked button rattles, and an unlocked one stops listening for touch
    private bool RejectLocked()
    {
        if (IsLocked)
        {
            PlayLockSound(locked: true);
            return true;
        }

        isTouchArmed = false;
        return false;
    }

    [EntityInput("Lock")]
    private void InputLock(EntityInputData data) => IsLocked = true;

    [EntityInput("Unlock")]
    private void InputUnlock(EntityInputData data)
    {
        IsLocked = false;

        // A button locked on its way in never triggered on arrival, so it tries again shortly
        if (state == ToggleState.GoingUp)
        {
            moveDoneFunction = MoveDoneFunction.TriggerAndWait;
            SetMoveDoneTime(0.1f);
        }
    }

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => IsDisabled = false;

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => IsDisabled = true;

    /// <inheritdoc/>
    public override void Think()
    {
        switch (thinkFunction)
        {
            case ThinkFunction.Spark:
                // Only the sound; there is no particle effect for the sparks
                SetNextThink(EntitySystem.CurrentTime + 0.1f + Random.Shared.NextSingle() * 1.5f);
                Sound.Play("DoSpark", WorldOrigin);
                break;

            case ThinkFunction.Return:
                ButtonReturn();
                break;
        }
    }

    /// <inheritdoc/>
    public override void MoveDone()
    {
        FinishLinearMove();

        switch (moveDoneFunction)
        {
            case MoveDoneFunction.TriggerAndWait:
                TriggerAndWait();
                break;

            case MoveDoneFunction.BackHome:
                ButtonBackHome();
                break;
        }
    }

    // Sets off inwards. The use sound plays even when the press is then refused.
    private void ButtonActivate()
    {
        PlayUseSound();

        if (!IsMasterTriggered() || IsLocked)
        {
            PlayLockSound(locked: true);
            return;
        }

        PlayLockSound(locked: false);

        moveDoneFunction = MoveDoneFunction.TriggerAndWait;
        state = ToggleState.GoingUp;
        LinearMove(PositionIn);
    }

    // Arrived in
    private void TriggerAndWait()
    {
        if (!IsMasterTriggered() || IsLocked)
        {
            return;
        }

        state = ToggleState.AtTop;
        IsUsable = HasSpawnFlags(SpawnFlag.Toggle);

        if (StaysPushed || HasSpawnFlags(SpawnFlag.Toggle))
        {
            isTouchArmed = HasSpawnFlags(SpawnFlag.TouchActivates);
        }
        else
        {
            // The one think slot is shared with the sparks, which stop until the button is back out
            thinkFunction = ThinkFunction.Return;
            SetNextThink(EntitySystem.CurrentTime + MathF.Max(Wait, 0f));
        }

        EntitySystem.TriggerOutput(this, "OnIn", activator);
    }

    // Sets off outwards
    private void ButtonReturn()
    {
        moveDoneFunction = MoveDoneFunction.BackHome;
        state = ToggleState.GoingDown;
        LinearMove(PositionOut);
    }

    // Arrived back out
    private void ButtonBackHome()
    {
        EntitySystem.TriggerOutput(this, "OnOut", activator);

        state = ToggleState.AtBottom;
        IsUsable = true;
        isTouchArmed = HasSpawnFlags(SpawnFlag.TouchActivates);

        if (HasSpawnFlags(SpawnFlag.Sparks))
        {
            thinkFunction = ThinkFunction.Spark;
            SetNextThink(EntitySystem.CurrentTime + 0.5f);
        }
    }

    // Nothing here can be a master, and a button with no master is let through
    private static bool IsMasterTriggered() => true;

    private void PlayUseSound()
    {
        if (useSound != null)
        {
            Sound.Play(useSound, WorldOrigin);
        }
    }

    private void PlayLockSound(bool locked)
    {
        if (HasSpawnFlags(SpawnFlag.Sparks))
        {
            return;
        }

        var sound = locked ? lockedSound : unlockedSound;

        if (sound == null || EntitySystem.CurrentTime <= lockSoundNext)
        {
            return;
        }

        Sound.Play(sound, WorldOrigin);
        lockSoundNext = EntitySystem.CurrentTime + LockSoundWait;
    }
}
