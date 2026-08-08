using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes;

internal sealed class CS2ProjectileSceneNode : ModelSceneNode
{
    public enum GrenadeKind
    {
        Smoke,
        Explosive,
        Fire,
    }

    public const string ProjectileLayerName = "Internal - Grenade Projectile";
    private const string ThrownAnimation = "thrown";
    private const string FlameAttachment = "molotov_particle";

    private const float TickInterval = 1f / 64f;
    private const int MaxTicksPerFrame = 8;
    private const float SvGravity = 800f;
    private const float GrenadeGravity = 0.4f;
    private const float GrenadeElasticity = 0.45f;
    private const float GrenadeTimer = 1.5f;
    private const float SleepSpeed = 20f;
    private const float StopEpsilon = 0.1f;
    private const float SurfaceEpsilon = 0.03125f;
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

    public static readonly Vector3 HullHalfExtents = new(2f, 2f, 2f);

    public static readonly string[] Sounds = [
        "SmokeGrenade.Bounce",
        "BaseSmokeEffect.Sound",
        "HEGrenade.Bounce",
        "BaseGrenade.Explode",
        "Molotov.Bounce",
        "Molotov.Start",
    ];

    public GrenadeKind Kind { get; }

    public bool Live { get; private set; }

    public bool InFlight => Live && !detonated;

    public Vector3 Position => renderPosition;

    private readonly ParticleSceneNode? detonationEffect;
    private readonly ParticleSceneNode? flightEffect;
    private readonly string bounceSound;
    private readonly string detonateSound;
    private readonly float effectDuration;

    private Vector3 position;
    private Vector3 previousTickPosition;
    private Vector3 renderPosition;
    private Vector3 velocity;
    private Vector3 angles;
    private Vector3 angularVelocity;
    private bool onGround;

    private float fuse;
    private float stillTime;
    private bool shattered;
    private bool expired;
    private float flightTime;
    private bool detonated;
    private float effectTimeLeft;
    private float tickAccumulator;

    public CS2ProjectileSceneNode(Scene scene, Model model, GrenadeKind kind, ParticleSystem? detonationEffect, ParticleSystem? flightEffect = null)
        : base(scene, model)
    {
        Kind = kind;
        LayerName = ProjectileLayerName;
        LayerEnabled = false;

        SetAnimationByName(ThrownAnimation);

        (bounceSound, detonateSound, effectDuration) = kind switch
        {
            GrenadeKind.Smoke => ("SmokeGrenade.Bounce", "BaseSmokeEffect.Sound", SmokeEffectDuration),
            GrenadeKind.Fire => ("Molotov.Bounce", "Molotov.Start", FireEffectDuration),
            _ => ("HEGrenade.Bounce", "BaseGrenade.Explode", ExplosionEffectDuration),
        };

        this.detonationEffect = AddEffect(scene, detonationEffect);
        this.flightEffect = AddEffect(scene, flightEffect);

        if (this.flightEffect != null)
        {
            AttachNode(this.flightEffect, FlameAttachment);
        }
    }

    private static ParticleSceneNode? AddEffect(Scene scene, ParticleSystem? effect)
    {
        if (effect == null)
        {
            return null;
        }

        var node = new ParticleSceneNode(scene, effect)
        {
            LayerName = ProjectileLayerName,
            LayerEnabled = false,
        };

        scene.Add(node, true);
        return node;
    }

    public void Launch(Vector3 origin, Vector3 velocity)
    {
        position = origin;
        previousTickPosition = origin;
        renderPosition = origin;
        this.velocity = velocity;
        onGround = false;
        detonated = false;
        Live = true;
        fuse = GrenadeTimer;
        stillTime = 0f;
        shattered = false;
        expired = false;
        flightTime = 0f;
        effectTimeLeft = 0f;
        tickAccumulator = 0f;
        angles = Vector3.Zero;
        angularVelocity = new Vector3(TumblePitchRate, float.Lerp(-TumbleYawRate, TumbleYawRate, Random.Shared.NextSingle()), 0f);

        LayerEnabled = true;
        flightEffect?.Play();
        ApplyTransform();
    }

    public override void Update(Scene.UpdateContext context)
    {
        if (Live && LayerEnabled)
        {
            base.Update(context);
        }
    }

    public void Simulate(float timestep)
    {
        if (!Live)
        {
            return;
        }

        if (detonated)
        {
            effectTimeLeft -= timestep;

            if (effectTimeLeft <= 0f)
            {
                Live = false;
                LayerEnabled = false;

                detonationEffect?.Stop();
            }

            return;
        }

        flightTime += timestep;

        if (Kind == GrenadeKind.Fire && !shattered && flightTime > FireExpireTime && !expired)
        {
            expired = true;
            flightEffect?.Stop();
        }

        tickAccumulator += timestep;

        var ticks = 0;

        while (tickAccumulator >= TickInterval && ticks < MaxTicksPerFrame)
        {
            tickAccumulator -= TickInterval;
            ticks++;

            previousTickPosition = position;

            PhysicsToss();

            fuse -= TickInterval;

            stillTime = velocity.Length() > FireStillSpeed ? 0f : stillTime + TickInterval;

            if (ShouldDetonate())
            {
                Detonate();
                return;
            }
        }

        if (ticks == MaxTicksPerFrame)
        {
            tickAccumulator = 0f;
        }

        if (flightTime > MaxFlightTime && !expired)
        {
            Detonate();
            return;
        }

        renderPosition = Vector3.Lerp(previousTickPosition, position, MathUtils.Saturate(tickAccumulator / TickInterval));

        if (!onGround || velocity != Vector3.Zero)
        {
            angles += angularVelocity * timestep;
        }

        ApplyTransform();
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

        return Kind != GrenadeKind.Smoke || velocity.Length() <= 0.1f;
    }

    private void ApplyTransform()
    {
        Transform = Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(renderPosition);
    }

    private void Detonate()
    {
        detonated = true;
        effectTimeLeft = effectDuration;
        flightEffect?.Stop();

        LayerEnabled = Kind == GrenadeKind.Smoke;

        Sound.Play(detonateSound, position);

        if (detonationEffect != null)
        {
            detonationEffect.Transform = Matrix4x4.CreateTranslation(position);
            detonationEffect.Play();
        }
    }

    private Quaternion Orientation
        => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, float.DegreesToRadians(angles.Y))
        * Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.DegreesToRadians(angles.X))
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(angles.Z));

    private static Vector3 RestingAngles(Vector3 normal)
    {
        var horizontal = MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
        var pitch = float.RadiansToDegrees(MathF.Atan2(-normal.Z, horizontal));

        return new Vector3(pitch, Random.Shared.NextSingle() * 360f, 0f);
    }

    private void PhysicsToss()
    {
        if (velocity.Z > 0f)
        {
            onGround = false;
        }

        if (onGround && velocity == Vector3.Zero)
        {
            return;
        }

        var move = new Vector3(velocity.X * TickInterval, velocity.Y * TickInterval, 0f);

        if (!onGround)
        {
            var newVelocityZ = velocity.Z - GrenadeGravity * SvGravity * TickInterval;
            move.Z = (velocity.Z + newVelocityZ) * 0.5f * TickInterval;
            velocity.Z = newVelocityZ;
        }

        var trace = PushEntity(move);

        if (trace is { Hit: true, IsValid: true })
        {
            var moveLength = move.Length();
            var fraction = moveLength > 0f ? MathUtils.Saturate(trace.Distance / moveLength) : 0f;

            ResolveFlyCollisionCustom(trace, fraction);
        }
    }

    private Rubikon.TraceResult PushEntity(Vector3 move)
    {
        var physics = Scene.PhysicsWorld;

        if (physics == null)
        {
            position += move;
            return new Rubikon.TraceResult { IsValid = false };
        }

        var trace = SweepHull(physics, position, position + move);

        if (!trace.IsValid)
        {
            return trace;
        }

        position = trace.Hit ? trace.HitPosition : position + move;

        return trace;
    }

    public static Rubikon.TraceResult SweepHull(Rubikon physics, Vector3 from, Vector3 to)
    {
        var trace = physics.TraceAABB(from, to, HullHalfExtents, Rubikon.GrenadeCollisionName);

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

    private void ResolveFlyCollisionCustom(in Rubikon.TraceResult trace, float fraction)
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

        var impactSpeed = MathF.Abs(Vector3.Dot(velocity, trace.HitNormal));

        var elasticity = Math.Clamp(GrenadeElasticity, 0f, 0.9f);
        var bounced = ClipVelocity(velocity, trace.HitNormal, 2f) * elasticity;

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

            velocity = bounced;

            if (slow)
            {
                onGround = true;
                velocity = Vector3.Zero;
                angularVelocity = Vector3.Zero;
                angles = RestingAngles(trace.HitNormal);
            }
            else
            {
                PushEntity(bounced * ((1f - fraction) * TickInterval));
            }
        }
        else if (slow)
        {
            velocity = Vector3.Zero;
            angularVelocity = Vector3.Zero;
        }
        else
        {
            velocity = bounced;
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
