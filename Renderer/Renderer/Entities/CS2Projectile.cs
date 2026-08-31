using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A thrown CS2 grenade. Created at runtime by the viewmodel rather than from a map, simulated on the
/// entity tick, and drawn through the model and effect nodes it owns.
/// </summary>
public sealed class CS2Projectile : BaseEntity
{
    /// <summary>Which grenade a projectile is.</summary>
    public enum GrenadeKind
    {
        /// <summary>Smoke grenade.</summary>
        Smoke,
        /// <summary>High explosive grenade.</summary>
        Explosive,
        /// <summary>Molotov.</summary>
        Fire,
    }

    private const float SvGravity = 800f;
    private const float GrenadeGravity = 0.4f;
    private const float GrenadeElasticity = 0.45f;
    private const float GrenadeTimer = 1.5f;
    private const float SleepSpeed = 20f;
    private const float StopEpsilon = 0.1f;
    private const float SurfaceEpsilon = Rubikon.SurfaceEpsilon;
    private const float MaxFlightTime = 20f;
    private const float TumblePitchRate = 600f;
    private const float TumbleYawRate = 1200f;
    private const float BouncePaddingSpeedSquared = 96000f;

    private const float BounceSoundMinSpeed = 30f;
    private const float BounceSoundVolume = 0.6f;

    private const float SmokeEffectDuration = 17f;
    private const float ExplosionEffectDuration = 6f;
    private const float FireEffectDuration = 7f;

    private const float FireMaxDetonateSlopeDegrees = 30f;
    private const float FireStillSpeed = 5f;
    private const float FireStillDetonateTime = 0.5f;
    private const float FireExpireTime = 2f;

    private const string EntitiesLayerName = "Entities";
    private const string ThrownAnimation = "thrown";
    private const string FlameAttachment = "molotov_particle";

    /// <summary>The grenade's collision hull half extents.</summary>
    public static readonly Vector3 HullHalfExtents = new(2f, 2f, 2f);

    /// <summary>Every sound event a grenade can play, for pre-caching.</summary>
    public static readonly string[] Sounds = BuildSoundList();

    private static string[] BuildSoundList()
    {
        var sounds = new List<string>();

        foreach (var kind in Enum.GetValues<GrenadeKind>())
        {
            var (bounce, detonate, _) = ProfileFor(kind);

            if (!sounds.Contains(bounce))
            {
                sounds.Add(bounce);
            }

            if (!sounds.Contains(detonate))
            {
                sounds.Add(detonate);
            }
        }

        return [.. sounds];
    }

    /// <summary>Gets which grenade this is.</summary>
    public GrenadeKind Kind { get; }

    /// <summary>Gets whether the grenade has been thrown and not finished its effect yet.</summary>
    public bool Live { get; private set; }

    /// <summary>Gets whether the grenade is on its way and has not detonated.</summary>
    public bool InFlight => Live && !detonated;

    /// <summary>Gets the position the grenade is drawn at this frame.</summary>
    public Vector3 Position => Transform.Translation;

    private readonly ModelSceneNode node;
    private readonly ParticleSceneNode? detonationEffect;
    private readonly ParticleSceneNode? flightEffect;
    private readonly string bounceSound;
    private readonly string detonateSound;
    private readonly float effectDuration;

    private bool onGround;
    private float fuse;
    private float stillTime;
    private bool shattered;
    private bool expired;
    private float flightTime;
    private bool detonated;
    private float effectTimeLeft;

    /// <summary>Creates a grenade projectile and its scene node; put it in the world with <see cref="EntitySystem.AddEntity"/>.</summary>
    public CS2Projectile(EntitySystem system, Model model, GrenadeKind kind, ParticleSystem? detonationEffect, ParticleSystem? flightEffect = null)
        : base(system, ClassnameFor(kind))
    {
        Kind = kind;

        (bounceSound, detonateSound, effectDuration) = ProfileFor(kind);

        node = new ModelSceneNode(Scene, model)
        {
            LayerName = EntitiesLayerName,
            Visible = false,
        };
        node.SetAnimationByName(ThrownAnimation);
        AddNode(node);

        this.detonationEffect = AddEffect(detonationEffect);
        this.flightEffect = AddEffect(flightEffect);

        if (this.flightEffect != null)
        {
            node.AttachNode(this.flightEffect, FlameAttachment);
        }
    }

    private ParticleSceneNode? AddEffect(ParticleSystem? effect)
    {
        if (effect == null)
        {
            return null;
        }

        var effectNode = new ParticleSceneNode(Scene, effect)
        {
            LayerName = EntitiesLayerName,
            LayerEnabled = false,
            Visible = false,
        };

        Scene.Add(effectNode, true);
        return effectNode;
    }

    private static string ClassnameFor(GrenadeKind kind) => kind switch
    {
        GrenadeKind.Smoke => "smokegrenade_projectile",
        GrenadeKind.Fire => "molotov_projectile",
        _ => "hegrenade_projectile",
    };

    // The single home of each kind's sounds and timing; the pre-cache list derives from it
    private static (string BounceSound, string DetonateSound, float EffectDuration) ProfileFor(GrenadeKind kind) => kind switch
    {
        GrenadeKind.Smoke => ("SmokeGrenade.Bounce", "BaseSmokeEffect.Sound", SmokeEffectDuration),
        GrenadeKind.Fire => ("Molotov.Bounce", "Molotov.Start", FireEffectDuration),
        _ => ("HEGrenade.Bounce", "BaseGrenade.Explode", ExplosionEffectDuration),
    };

    /// <summary>Throws (or re-throws, for a pooled instance) the grenade.</summary>
    public void Launch(Vector3 origin, Vector3 velocity, BaseEntity? thrower)
    {
        Owner = thrower ?? EntitySystem.World;

        Teleport(origin, Vector3.Zero);
        Velocity = velocity;
        AngularVelocity = new Vector3(TumblePitchRate, float.Lerp(-TumbleYawRate, TumbleYawRate, Random.Shared.NextSingle()), 0f);

        onGround = false;
        detonated = false;
        Live = true;
        fuse = GrenadeTimer;
        stillTime = 0f;
        shattered = false;
        expired = false;
        flightTime = 0f;
        effectTimeLeft = 0f;

        node.Visible = true;

        if (flightEffect != null)
        {
            flightEffect.Visible = true;
            flightEffect.Play();
        }
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (!Live)
        {
            return;
        }

        if (detonated)
        {
            effectTimeLeft -= tickInterval;

            if (effectTimeLeft <= 0f)
            {
                Live = false;
                node.Visible = false;

                if (detonationEffect != null)
                {
                    detonationEffect.Stop();
                    detonationEffect.Visible = false;
                }
            }

            return;
        }

        flightTime += tickInterval;

        if (Kind == GrenadeKind.Fire && !shattered && flightTime > FireExpireTime && !expired)
        {
            expired = true;
            StopFlightEffect();
        }

        PhysicsToss(tickInterval);

        fuse -= tickInterval;
        stillTime = Velocity.Length() > FireStillSpeed ? 0f : stillTime + tickInterval;

        if (ShouldDetonate() || (flightTime > MaxFlightTime && !expired))
        {
            Detonate();
            return;
        }

        // Componentwise like Source 1's tumble, not TurnBody: the authored pitch/yaw rates spin about
        // the world axes together
        if (!onGround || Velocity != Vector3.Zero)
        {
            Angles += AngularVelocity * tickInterval;
        }
    }

    private void StopFlightEffect()
    {
        if (flightEffect != null)
        {
            flightEffect.Stop();
            flightEffect.Visible = false;
        }
    }

    private bool ShouldDetonate()
    {
        if (Kind == GrenadeKind.Fire)
        {
            return !expired && (shattered || stillTime > FireStillDetonateTime);
        }

        if (fuse > 0f)
        {
            return false;
        }

        return Kind != GrenadeKind.Smoke || Velocity.Length() <= 0.1f;
    }

    private void Detonate()
    {
        detonated = true;
        effectTimeLeft = effectDuration;

        Sound.Play(detonateSound, Origin);

        StopFlightEffect();

        // The smoke grenade model stays visible inside its own smoke; the others vanish into the effect
        node.Visible = Kind == GrenadeKind.Smoke;

        if (detonationEffect != null)
        {
            detonationEffect.Transform = Matrix4x4.CreateTranslation(Origin);
            detonationEffect.Visible = true;
            detonationEffect.Play();
        }
    }

    private static Vector3 RestingAngles(Vector3 normal)
    {
        // Pitch from the surface normal; the yaw a tossed grenade settles at is arbitrary
        var pitch = EntityTransformHelper.ForwardDirectionToEulerAngles(normal).X;

        return new Vector3(pitch, Random.Shared.NextSingle() * 360f, 0f);
    }

    private void PhysicsToss(float tickInterval)
    {
        var velocity = Velocity;

        if (velocity.Z > 0f)
        {
            onGround = false;
        }

        if (onGround && velocity == Vector3.Zero)
        {
            return;
        }

        var move = new Vector3(velocity.X * tickInterval, velocity.Y * tickInterval, 0f);

        if (!onGround)
        {
            var newVelocityZ = velocity.Z - GrenadeGravity * SvGravity * tickInterval;
            move.Z = (velocity.Z + newVelocityZ) * 0.5f * tickInterval;
            velocity.Z = newVelocityZ;
            Velocity = velocity;
        }

        var trace = PushEntity(move);

        if (trace is { Hit: true, IsValid: true })
        {
            var moveLength = move.Length();
            var fraction = moveLength > 0f ? MathUtils.Saturate(trace.Distance / moveLength) : 0f;

            ResolveFlyCollisionCustom(trace, fraction, tickInterval);
        }
    }

    private Rubikon.TraceResult PushEntity(Vector3 move)
    {
        var trace = SweepHull(Scene.PhysicsWorld, EntitySystem, Origin, Origin + move);

        if (!trace.IsValid)
        {
            Origin += move;
            return trace;
        }

        Origin = trace.Hit ? trace.HitPosition : Origin + move;

        return trace;
    }

    /// <summary>Sweeps the grenade hull through the world and the brush entities, backing the hit off the surface.</summary>
    public static Rubikon.TraceResult SweepHull(Rubikon? physics, EntitySystem? entities, Vector3 from, Vector3 to)
    {
        var trace = physics?.TraceAABB(from, to, HullHalfExtents, Rubikon.GrenadeCollisionName)
            ?? new Rubikon.TraceResult();

        // The static world is the worldspawn, as the engine reports it
        if (trace.Hit)
        {
            trace.HitEntity = entities?.World;
        }

        entities?.TraceAABB(from, to, HullHalfExtents, detectStartSolid: false, ref trace);

        if (!trace.Hit || !trace.IsValid)
        {
            return trace;
        }

        var direction = Vector3.Normalize(to - from);
        var approach = -Vector3.Dot(direction, trace.HitNormal);

        var margin = approach > 0.001f ? SurfaceEpsilon / approach : 0f;

        trace.Distance = MathF.Max(trace.Distance - margin, 0f);
        trace.HitPosition = from + direction * trace.Distance;

        return trace;
    }

    private void ResolveFlyCollisionCustom(in Rubikon.TraceResult trace, float fraction, float tickInterval)
    {
        if (trace.HitNormal.LengthSquared() < 0.5f)
        {
            return;
        }

        if (Kind == GrenadeKind.Fire && !expired
            && trace.HitNormal.Z >= MathF.Cos(float.DegreesToRadians(FireMaxDetonateSlopeDegrees)))
        {
            shattered = true;
        }

        var impactSpeed = MathF.Abs(Vector3.Dot(Velocity, trace.HitNormal));

        var elasticity = Math.Clamp(GrenadeElasticity, 0f, 0.9f);
        var bounced = ClipVelocity(Velocity, trace.HitNormal, 2f) * elasticity;

        var speedSquared = bounced.LengthSquared();
        var slow = speedSquared < SleepSpeed * SleepSpeed;

        if (trace.HitNormal.Z > 0.7f || (trace.HitNormal.Z > 0.1f && slow))
        {
            if (speedSquared > BouncePaddingSpeedSquared)
            {
                var along = Vector3.Dot(Vector3.Normalize(bounced), trace.HitNormal);

                if (along > 0.5f)
                {
                    bounced *= 1.5f - along;
                }
            }

            Velocity = bounced;

            if (slow)
            {
                onGround = true;
                Velocity = Vector3.Zero;
                AngularVelocity = Vector3.Zero;
                Angles = RestingAngles(trace.HitNormal);
            }
            else
            {
                PushEntity(bounced * ((1f - fraction) * tickInterval));
            }
        }
        else if (slow)
        {
            Velocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
        }
        else
        {
            Velocity = bounced;
        }

        if (impactSpeed >= BounceSoundMinSpeed)
        {
            Sound.Play(bounceSound, trace.HitPosition, volume: BounceSoundVolume);
        }
    }

    private static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal, float overbounce)
    {
        var backoff = Vector3.Dot(velocity, normal) * overbounce;
        var clipped = velocity - normal * backoff;

        return new Vector3(
            MathF.Abs(clipped.X) < StopEpsilon ? 0f : clipped.X,
            MathF.Abs(clipped.Y) < StopEpsilon ? 0f : clipped.Y,
            MathF.Abs(clipped.Z) < StopEpsilon ? 0f : clipped.Z
        );
    }
}
