using System.Linq;
using ValveResourceFormat.Animation;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Gameplay;

// Feeds the animation graph of a Deadlock hero from how the player moves, and plays the sounds its clips ask for.
internal sealed class HeroBodyAnimator
{
    // Which sets of aim poses a hero has of its own; one it lacks falls back to the standing set.
    [Flags]
    private enum UniqueAims
    {
        None = 0,
        Crouch = 1,
        InAir = 2,
        Run = 4,
        CrouchRun = 8,
        Slide = 16,
    }

    // An action that only suggests itself gives way to one that was asked for in the same update
    private const int IdlePriority = -1;
    private const int MovePriority = 0;

    private const float RunningSpeed = 90f;
    private const float LeanSpeed = 50f;

    // Stopping is noticed when the speed falls through one bound having been above the other
    private const float RunToStopArmSpeed = 85f;
    private const float RunToStopFireSpeed = 70f;

    private const float BodyTurnStrength = 75f;
    private const float TurnSpeedStrength = 20f;
    private const float CrouchStrength = 20f;

    // Two changes of direction this close together count as dithering
    private const float DirectionChangeWindow = 0.3f;
    private const float DirectionCommitTime = 0.15f;

    // How long the hero keeps its weapon up after something only done in a fight
    private const float CombatTime = 2f;

    // Clips blended together each bring the same sound, which is only heard once
    private const float TaggedSoundMinInterval = 0.15f;

    private readonly AnimationGraph Graph;
    private readonly float TurnThreshold = 90f;
    private readonly float TurnDuration = 0.33f;
    private readonly UniqueAims Aims;
    private readonly string[] Muzzles;

    // The sound event each sound tag of the clips stands for, which is the hero's own
    private readonly Dictionary<string, string> TaggedSounds;
    private readonly Dictionary<string, float> TaggedSoundTimes = [];

    // What a hero holds as its state for as long as it fires, where firing is more than single shots
    private readonly string? FiringState;

    private float BodyTurnSpringSpeed;
    private float TurnSpeed;
    private float TurnSpeedSpringSpeed;
    private float TurnDelta;
    private float TurnElapsed = -1f;

    private float CrouchFraction;
    private float CrouchSpeed;

    private float PreviousAimYaw;
    private float PreviousAimPitch;
    private bool HasPreviousAim;

    private Vector2 PreviousVelocity;
    private bool RunToStopArmed;
    private bool LeanTriggered;

    private Vector2 LastWishDirection;
    private int SuccessiveDirectionChanges;
    private float TimeSinceDirectionChange = float.MaxValue;

    private float CombatTimeLeft;
    private float FidgetTimeLeft = 5f;
    private float FireTimeLeft;
    private int BurstShotsLeft;
    private float MeleeTimeLeft;
    private float ParryTimeLeft;
    private bool SecondMelee;
    private float Time;

    private int SeenMoveCount;
    private bool WasSliding;
    private string PendingAction = string.Empty;
    private int PendingActionPriority = int.MinValue;

    // Which way the body faces, in degrees.
    public float BodyYaw { get; private set; }

    // How many shots, light melee swings and parries were made, which tells each new one apart.
    public int ShotCount { get; private set; }
    public int MeleeCount { get; private set; }
    public int ParryCount { get; private set; }

    public HeroBodyAnimator(AnimationGraph graph, KVObject? modelGameData, Dictionary<string, string> taggedSounds)
    {
        Graph = graph;
        TaggedSounds = taggedSounds;

        if (modelGameData != null)
        {
            TurnThreshold = modelGameData.GetFloatProperty("m_flTurnThreshold", TurnThreshold);
            TurnDuration = modelGameData.GetFloatProperty("m_flTurnDuration", TurnDuration);

            foreach (var aim in modelGameData.GetStringProperty("m_eUniqueAims", string.Empty).Split('|', StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<UniqueAims>(aim, out var flag))
                {
                    Aims |= flag;
                }
            }
        }

        Muzzles = [.. graph.GetParameterIdOptions("muzzle").Where(muzzle => muzzle.Length > 0).Order(StringComparer.Ordinal)];
        FiringState = graph.GetParameterIdOptions("hero_state").FirstOrDefault(state => state == "baton_spinning");

        Graph.IdParameters["camera_mode"] = "over_the_shoulder";
        Graph.FloatParameters["time_scale"] = 1f;
    }

    public void Update(UserInput input, DeadlockHero hero, float deltaTime)
    {
        var ids = Graph.IdParameters;
        var floats = Graph.FloatParameters;
        var movement = input.PlayerMovement;

        var aimYaw = float.RadiansToDegrees(input.Camera.Yaw);
        var aimPitch = float.RadiansToDegrees(input.Camera.Pitch);

        var velocity = movement.Velocity;
        var horizontalVelocity = new Vector2(velocity.X, velocity.Y);
        var speed = horizontalVelocity.Length();
        var onGround = movement.OnGround || movement.IsMantling;

        var (aimSin, aimCos) = MathF.SinCos(input.Camera.Yaw);
        var aimForward = new Vector2(aimCos, aimSin);
        var aimLeft = new Vector2(-aimSin, aimCos);

        var inputForward = (input.Holding(TrackedKeys.W) ? 1f : 0f) - (input.Holding(TrackedKeys.S) ? 1f : 0f);
        var inputLeft = (input.Holding(TrackedKeys.A) ? 1f : 0f) - (input.Holding(TrackedKeys.D) ? 1f : 0f);
        var wishDirection = (aimForward * inputForward) + (aimLeft * inputLeft);
        var hasWish = wishDirection != Vector2.Zero;

        if (hasWish)
        {
            wishDirection = Vector2.Normalize(wishDirection);
        }

        UpdateBodyYaw(aimYaw, speed, onGround, deltaTime);

        var (bodySin, bodyCos) = MathF.SinCos(float.DegreesToRadians(BodyYaw));
        var bodyForward = new Vector2(bodyCos, bodySin);
        var bodyLeft = new Vector2(-bodySin, bodyCos);

        floats["forward_speed"] = Vector2.Dot(horizontalVelocity, bodyForward);
        floats["strafe_speed"] = Vector2.Dot(horizontalVelocity, bodyLeft);
        floats["vertical_speed"] = velocity.Z;
        floats["move_speed"] = velocity.Length();
        floats["turn_speed"] = TurnSpeed;

        // Heading is positive to the left of the body, pitch positive upwards
        floats["look_heading"] = ValveMath.AngleNormalize(aimYaw - BodyYaw);
        floats["look_pitch"] = -aimPitch;

        if (HasPreviousAim && deltaTime > 0f)
        {
            floats["look_heading_speed"] = ValveMath.AngleNormalize(aimYaw - PreviousAimYaw) / deltaTime;
            floats["look_pitch_speed"] = -(aimPitch - PreviousAimPitch) / deltaTime;
        }

        PreviousAimYaw = aimYaw;
        PreviousAimPitch = aimPitch;
        HasPreviousAim = true;

        // The names are the other way round from what they carry
        floats["input_right"] = inputForward;
        floats["input_forward"] = inputLeft;
        floats["direction_commitment"] = UpdateDirectionCommitment(wishDirection, hasWish, horizontalVelocity, deltaTime);
        floats["random_seed"] = Random.Shared.NextSingle() * int.MaxValue;

        var crouchTarget = (movement.CrouchBlend > 0f && onGround) || movement.IsSliding ? 1f : 0f;
        CrouchFraction = crouchTarget + MathUtils.CriticallyDampedSpring(CrouchFraction - crouchTarget, ref CrouchSpeed, CrouchStrength, deltaTime);
        CrouchFraction = MathF.Abs(CrouchFraction - crouchTarget) < 0.001f ? crouchTarget : MathUtils.Saturate(CrouchFraction);
        floats["crouch_fraction"] = CrouchFraction;

        var running = (speed > 1f && hasWish) || speed > RunningSpeed;
        var crouched = CrouchFraction >= 0.5f;

        ids["movement_type"] = !onGround ? "in_air" : running ? "running" : string.Empty;
        ids["aim_set"] = PickAimSet(onGround, running, crouched, movement.IsSliding);

        // Away from a fight the hero lowers its weapon, which sliding and dashing count as being in
        if (movement.IsSliding || movement.IsDashing)
        {
            CombatTimeLeft = CombatTime;
        }

        UpdateMoveActions(movement, aimYaw);
        UpdateIdleActions(horizontalVelocity, speed, hasWish, onGround, deltaTime);
        UpdateAttacks(input, hero, deltaTime);

        CombatTimeLeft = MathF.Max(0f, CombatTimeLeft - deltaTime);
        ids["base_state"] = CombatTimeLeft > 0f ? string.Empty : "out_of_combat";

        // An action is named for the one update it starts in. Left named it would start over when it ends
        // Held states are named every update, as long as nothing was asked for over them
        var heldAction = movement.ZiplineState switch
        {
            DeadlockZiplineState.Latching => "zipline_latched",
            DeadlockZiplineState.Riding => "zipline_attached",
            _ when movement.IsOnRope => movement.RopeDirection switch
            {
                > 0 => "rope_climb_up",
                < 0 => "rope_climb_down",
                _ => "rope_climb_idle",
            },
            _ => movement.IsSliding ? "Sliding" : null,
        };

        ids["base_action"] = heldAction != null && PendingActionPriority < MovePriority ? heldAction : PendingAction;
        PendingAction = string.Empty;
        PendingActionPriority = int.MinValue;

        PlayTaggedSounds(movement, deltaTime);
    }

    private void QueueAction(string action, int priority)
    {
        if (priority >= PendingActionPriority)
        {
            PendingAction = action;
            PendingActionPriority = priority;
        }
    }

    // Standing, the body stays put until the aim has turned a good way off it, then steps round to face it.
    // Moving or in the air it follows the aim almost rigidly.
    private void UpdateBodyYaw(float aimYaw, float speed, bool onGround, float deltaTime)
    {
        if (speed <= 0.01f && onGround)
        {
            BodyTurnSpringSpeed = 0f;

            if (TurnElapsed >= 0f)
            {
                TurnElapsed += deltaTime;

                if (TurnElapsed >= TurnDuration)
                {
                    TurnElapsed = -1f;
                    BodyYaw = aimYaw;
                }
                else
                {
                    BodyYaw = ValveMath.AngleNormalize(aimYaw - ((1f - (TurnElapsed / TurnDuration)) * TurnDelta));
                }
            }
            else
            {
                var lead = ValveMath.AngleNormalize(aimYaw - BodyYaw);

                if (MathF.Abs(lead) >= TurnThreshold)
                {
                    TurnDelta = MathF.Sign(lead) * TurnThreshold;
                    TurnElapsed = 0f;
                    BodyYaw = ValveMath.AngleNormalize(aimYaw - TurnDelta);
                    QueueAction(TurnDelta > 0f ? "turn_left_90" : "turn_right_90", MovePriority);
                }
            }
        }
        else
        {
            var lead = ValveMath.AngleNormalize(aimYaw - BodyYaw);

            lead = MathUtils.CriticallyDampedSpring(lead, ref BodyTurnSpringSpeed, BodyTurnStrength, deltaTime);
            BodyYaw = ValveMath.AngleNormalize(aimYaw - lead);
            TurnElapsed = -1f;
        }

        TurnSpeed = BodyTurnSpringSpeed
            + MathUtils.CriticallyDampedSpring(TurnSpeed - BodyTurnSpringSpeed, ref TurnSpeedSpringSpeed, TurnSpeedStrength, deltaTime);
    }

    // One while the held direction is kept. Reversing it twice in quick succession drops it to zero for a moment.
    private float UpdateDirectionCommitment(Vector2 wishDirection, bool hasWish, Vector2 velocity, float deltaTime)
    {
        if (TimeSinceDirectionChange < float.MaxValue)
        {
            TimeSinceDirectionChange += deltaTime;
        }

        if (hasWish)
        {
            if (Vector2.Dot(wishDirection, LastWishDirection) < 0f && Vector2.Dot(wishDirection, velocity) < 0f)
            {
                SuccessiveDirectionChanges = TimeSinceDirectionChange > DirectionChangeWindow ? 0 : SuccessiveDirectionChanges + 1;
                TimeSinceDirectionChange = 0f;
            }

            LastWishDirection = wishDirection;
        }

        if (SuccessiveDirectionChanges <= 0)
        {
            return 1f;
        }

        return MathUtils.RemapValClamped(TimeSinceDirectionChange - DirectionChangeWindow, 0f, DirectionCommitTime, 0f, 1f);
    }

    private string PickAimSet(bool onGround, bool running, bool crouched, bool sliding)
    {
        var (name, aim) = !onGround ? ("in_air", UniqueAims.InAir)
            : sliding && crouched ? ("slide", UniqueAims.Slide)
            : crouched && running ? ("crouch_run", UniqueAims.CrouchRun)
            : crouched ? ("crouch", UniqueAims.Crouch)
            : running ? ("RUN", UniqueAims.Run)
            : ("idle", UniqueAims.None);

        return Aims.HasFlag(aim) ? name : "idle";
    }

    private void UpdateMoveActions(PlayerMovement movement, float aimYaw)
    {
        var sliding = movement.IsSliding;

        if (movement.MoveCount != SeenMoveCount)
        {
            SeenMoveCount = movement.MoveCount;

            var direction = movement.LastMoveDirection;
            var moveYaw = float.RadiansToDegrees(MathF.Atan2(direction.Y, direction.X));

            // A dash is named by where it goes as seen from the aim, a wall jump as seen from the body
            var action = movement.LastMove switch
            {
                DeadlockMove.GroundJump => "ground_jump",
                DeadlockMove.AirJump => "air_jump",
                DeadlockMove.DashJump => "dash_jump",
                DeadlockMove.GroundDash => "Ground_Dash",
                DeadlockMove.AirDash => "air_dash_" + DirectionName(moveYaw - aimYaw),
                DeadlockMove.WallJump => "wall_jump_" + DirectionName(moveYaw - BodyYaw),
                DeadlockMove.Mantle => MantleName(movement.MantleHeight),
                DeadlockMove.Launched => "catapulted",
                DeadlockMove.ZiplineDismount => "zipline_dismount",
                _ => string.Empty,
            };

            if (action.Length > 0)
            {
                QueueAction(action, MovePriority);
            }
        }
        else if (WasSliding && !sliding && movement.OnGround)
        {
            QueueAction("slide_getup", MovePriority);
        }

        WasSliding = sliding;
    }

    // What the hero does unasked: leaning into a run, braking out of one, and shifting about when left standing.
    private void UpdateIdleActions(Vector2 velocity, float speed, bool hasWish, bool onGround, float deltaTime)
    {
        if (speed < LeanSpeed)
        {
            LeanTriggered = false;
        }
        else if (hasWish && !LeanTriggered)
        {
            LeanTriggered = true;
            QueueAction("loco_lean", IdlePriority);
        }

        var acceleration = deltaTime > 0f ? (velocity - PreviousVelocity) / deltaTime : Vector2.Zero;
        PreviousVelocity = velocity;

        if (onGround && acceleration.LengthSquared() > 1f)
        {
            var speedingUp = Vector2.Dot(acceleration, velocity) > 0f;

            if (speedingUp && speed > RunToStopArmSpeed)
            {
                RunToStopArmed = true;
            }
            else if (RunToStopArmed && !speedingUp && speed < RunToStopFireSpeed)
            {
                RunToStopArmed = false;

                if (!hasWish)
                {
                    QueueAction("run_to_stop", IdlePriority);
                }
            }
        }

        FidgetTimeLeft -= deltaTime;

        if (FidgetTimeLeft <= 0f)
        {
            FidgetTimeLeft = float.Lerp(5f, 10f, Random.Shared.NextSingle());
            QueueAction("fidget", IdlePriority);
        }
    }

    // Left mouse fires the weapon, Q swings a light melee and F parries.
    private void UpdateAttacks(UserInput input, DeadlockHero hero, float deltaTime)
    {
        var ids = Graph.IdParameters;
        var firing = input.Holding(TrackedKeys.MouseLeft);

        FireTimeLeft -= deltaTime;
        MeleeTimeLeft -= deltaTime;
        ParryTimeLeft -= deltaTime;
        ids["shoot"] = string.Empty;

        if (firing)
        {
            CombatTimeLeft = CombatTime;
        }

        // A burst, once started, fires all its shots; the cycle time runs from the last of them
        if ((firing || BurstShotsLeft > 0) && FireTimeLeft <= 0f)
        {
            if (BurstShotsLeft == 0)
            {
                BurstShotsLeft = hero.BurstShotCount;
            }

            BurstShotsLeft--;
            FireTimeLeft = BurstShotsLeft > 0 ? hero.BurstShotInterval : hero.FireInterval;
            ids["shoot"] = "shoot_first";

            if (Muzzles.Length > 0)
            {
                ids["muzzle"] = Muzzles[ShotCount % Muzzles.Length];
            }

            ShotCount++;

            if (hero.ShootSound != null)
            {
                Sound.Play(hero.ShootSound, input.PlayerMovement.EyePosition, volumeScale: PlayerMovement.DeadlockSoundScale);
            }
        }

        if (FiringState != null)
        {
            ids["hero_state"] = firing ? FiringState : string.Empty;
        }

        if (input.Pressed(TrackedKeys.Q) && MeleeTimeLeft <= 0f)
        {
            MeleeTimeLeft = hero.LightMeleeCooldown;
            CombatTimeLeft = CombatTime;
            QueueAction(SecondMelee ? "melee_light2" : "melee_light1", MovePriority);
            SecondMelee = !SecondMelee;
            MeleeCount++;
        }
        else if (input.Pressed(TrackedKeys.F) && ParryTimeLeft <= 0f)
        {
            ParryTimeLeft = hero.ParryCooldown;
            ParryCount++;
            CombatTimeLeft = CombatTime;
            QueueAction("parry_active", MovePriority);
        }
    }

    // The clips mark where a foot comes down or a swing cuts the air, and the sound is heard there
    private void PlayTaggedSounds(PlayerMovement movement, float deltaTime)
    {
        Time += deltaTime;

        var events = Graph.SampledEvents;

        for (var i = 0; i < events.Count; i++)
        {
            var sampled = events[i];

            if (sampled.IsIgnored || !sampled.IsFromActiveBranch
                || sampled.AnimEvent is not LegacyEvent { AnimEventClassName: "AE_TAGGED_SOUND" } tagged
                || tagged.KV.GetStringProperty("name") is not { } tag
                || !TaggedSounds.TryGetValue(tag, out var soundEvent)
                || (TaggedSoundTimes.TryGetValue(tag, out var lastTime) && Time - lastTime < TaggedSoundMinInterval))
            {
                continue;
            }

            TaggedSoundTimes[tag] = Time;
            Sound.Play(soundEvent, movement.Position, volumeScale: PlayerMovement.DeadlockSoundScale);
        }
    }

    // Within 30 degrees is forward, beyond 135 is back, and the sides lie between
    private static string DirectionName(float yaw)
    {
        var angle = ValveMath.AngleNormalize(yaw);

        return MathF.Abs(angle) switch
        {
            <= 30f => "forward",
            < 135f => angle > 0f ? "left" : "right",
            _ => "back",
        };
    }

    // The climbs are animated for ledges 32 units apart, and the nearest one is played
    private static string MantleName(float height) => height switch
    {
        <= 48f => "mantle_32",
        <= 80f => "mantle_64",
        <= 112f => "mantle_96",
        _ => "mantle_128",
    };
}
