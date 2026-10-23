using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Gameplay;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Third-person player character for Deadlock walk mode. Follows the movement controller
/// and plays the hero's animation graph, fed from the controller's state.
/// </summary>
public class DeadlockPlayerSceneNode : ModelSceneNode
{
    internal const string ThirdPersonLayerName = "Internal - Third Person Player";

    // How far the aim looks for what it points at
    private const float AimRange = 16384f;

    // Bullets are flown in steps of this long, short enough that their arc is traced closely
    private const float BulletStepTime = 1f / 64f;

    private const string DefaultMuzzle = "muzzle_flash";

    private sealed class Bullet(Vector3 position, Vector3 velocity, ParticleSceneNode? tracer)
    {
        public Vector3 Position = position;
        public Vector3 Velocity = velocity;
        public float Age;
        public ParticleSceneNode? Tracer = tracer;
    }

    private readonly List<Bullet> bullets = [];

    private readonly HeroBodyAnimator? bodyAnimator;

    // The attachments shots leave from, taken in turn by heroes with more than one gun
    private readonly string[] muzzles;

    private int seenShots;
    private int seenMelees;
    private int seenParries;

    private DeadlockPlayerSceneNode(Scene scene, Model model)
        : base(scene, model, isWorldPreview: true)
    {
        LayerName = ThirdPersonLayerName;
        Flags |= ObjectTypeFlags.DisableVisCulling;

        // Models without a list of muzzles have one, an attachment or a bone named for it
        IEnumerable<string> muzzleNames = model.KeyValues.GetSubCollection("CitadelMuzzleSettings_t")?.GetArray("m_vecPrimaryMuzzles") is { Count: > 0 } listed
            ? listed.Select(muzzle => muzzle.GetStringProperty("m_strAttachmentName"))
            : [DefaultMuzzle];

        muzzles = [.. muzzleNames.Where(name => Attachments.ContainsKey(name) || AnimationController.Skeleton.GetBoneIndex(name) != -1)];

        if (AnimationGraphReferences.Count > 0 && LoadAnimationGraph(AnimationGraphReferences[0].GraphPath) is { } graph)
        {
            SetAnimationGraph(graph);
            bodyAnimator = new HeroBodyAnimator(graph, model.KeyValues.GetSubCollection("CCitadelHeroModelGameData_t"), LoadTaggedSounds(model));
        }
        else
        {
            Scene.RendererContext.Logger.LogWarning("The player model has no animation graph to play, it will not animate");
        }
    }

    // The sound events the model gives to the sound tags its clips carry, such as its own footstep
    private Dictionary<string, string> LoadTaggedSounds(Model model)
    {
        var sounds = new Dictionary<string, string>();
        var files = model.KeyValues.GetSubCollection("TaggedSoundSettings_t")?.GetArray<string>("m_taggedSounds") ?? [];

        foreach (var file in files)
        {
            if (Scene.RendererContext.FileLoader.LoadFileCompiled(file)?.DataBlock is not BinaryKV3 { Data.Root: { } tags })
            {
                continue;
            }

            foreach (var (tag, sound) in tags.Children)
            {
                if (sound.IsCollection && sound.GetStringProperty("m_soundEvent") is { Length: > 0 } soundEvent)
                {
                    sounds[tag] = soundEvent;
                }
            }
        }

        return sounds;
    }

    /// <summary>
    /// Loads the hero model and its animation graph, returning null when the model is not
    /// available in the loaded packages.
    /// </summary>
    public static DeadlockPlayerSceneNode? TryLoad(Scene scene, string modelPath)
    {
        var resource = scene.RendererContext.FileLoader.LoadFileCompiled(modelPath);

        if (resource?.DataBlock is not Model model)
        {
            return null;
        }

        var node = new DeadlockPlayerSceneNode(scene, model);
        scene.Add(node, true);
        scene.DeactivateLayer(ThirdPersonLayerName);

        scene.RendererContext.Logger.LogInformation("Loaded third person player model {Model}.", modelPath);

        return node;
    }

    /// <summary>
    /// Follows the movement controller: places the model at the player's feet facing the way
    /// the body does, keeps the layer in sync with walk mode, and feeds the animation graph.
    /// </summary>
    public void ProcessInput(UserInput input, float deltaTime)
    {
        if (input.NoClip)
        {
            if (LayerEnabled)
            {
                Scene.DeactivateLayer(ThirdPersonLayerName);
            }

            return;
        }

        if (!LayerEnabled)
        {
            Scene.ActivateLayer(ThirdPersonLayerName);
        }

        bodyAnimator?.Update(input, input.PlayerMovement.Hero, deltaTime);

        var bodyYaw = bodyAnimator != null ? float.DegreesToRadians(bodyAnimator.BodyYaw) : input.Camera.Yaw;

        Transform = Matrix4x4.CreateRotationZ(bodyYaw)
            * Matrix4x4.CreateTranslation(input.PlayerMovement.Position);

        if (bodyAnimator != null && input.EntitySystem is { } entities)
        {
            PlayAttackEffects(input, entities.TempEntities, bodyAnimator);
            FlyBullets(input, entities.TempEntities, input.PlayerMovement.Hero, deltaTime);
        }
    }

    private Vector3 MuzzlePosition(UserInput input, int shot)
        => muzzles.Length > 0 ? GetChildFrame(muzzles[shot % muzzles.Length]).Translation : input.PlayerMovement.EyePosition;

    // What the middle of the view points at: the aim starts from the camera the view is drawn from
    private static Vector3 AimPoint(UserInput input)
    {
        var from = input.AimOrigin;
        var to = from + (input.Camera.Forward * AimRange);
        var trace = ViewmodelSceneNode.TraceWorldAndEntities(input, from, to, Rubikon.DeadlockBulletCollisionName);

        return trace.Hit ? trace.HitPosition : to;
    }

    // Bullets leave the gun for the point the aim is on
    private static Vector3 BulletVelocity(DeadlockHero hero, Vector3 muzzle, Vector3 target)
    {
        var toTarget = target - muzzle;

        return toTarget.LengthSquared() > 1f ? Vector3.Normalize(toTarget) * hero.BulletSpeed : Vector3.Zero;
    }

    private static Vector3 BulletGravity(DeadlockHero hero) => new(0f, 0f, -PlayerMovement.GravityValue * hero.BulletGravityScale);

    // One step of a bullet's flight, traced from where it was to where it gets to
    private static Rubikon.TraceResult StepBullet(UserInput input, Vector3 gravity, ref Vector3 position, ref Vector3 velocity, float deltaTime)
    {
        var next = position + (velocity * deltaTime) + (gravity * (0.5f * deltaTime * deltaTime));
        var trace = ViewmodelSceneNode.TraceWorldAndEntities(input, position, next, Rubikon.DeadlockBulletCollisionName);

        position = trace.Hit ? trace.HitPosition : next;
        velocity += gravity * deltaTime;

        return trace;
    }

    private void FlyBullets(UserInput input, TempEntities effects, DeadlockHero hero, float deltaTime)
    {
        var gravity = BulletGravity(hero);

        for (var i = bullets.Count - 1; i >= 0; i--)
        {
            var bullet = bullets[i];
            var trace = default(Rubikon.TraceResult);

            for (var timeLeft = deltaTime; timeLeft > 0f && !trace.Hit; timeLeft -= BulletStepTime)
            {
                trace = StepBullet(input, gravity, ref bullet.Position, ref bullet.Velocity, MathF.Min(timeLeft, BulletStepTime));
            }

            bullet.Age += deltaTime;

            if (bullet.Tracer != null)
            {
                bullet.Tracer.Transform = BulletFrame(bullet.Position, bullet.Velocity);
            }

            if (trace.Hit)
            {
                PlayBulletImpact(effects, hero, trace);
            }

            if (trace.Hit || bullet.Age >= hero.BulletLifetime)
            {
                // The tracer stops where the bullet does, and lingers while its endcap plays out
                bullet.Tracer?.PlayEndCap();
                bullets.RemoveAt(i);
            }
        }
    }

    private static Matrix4x4 BulletFrame(Vector3 position, Vector3 velocity)
        => EntityTransformHelper.ForwardDirectionToRotationMatrix(velocity) * Matrix4x4.CreateTranslation(position);

    // What the surface leaves, falling back through the surfaces it inherits from to the default
    private string? FindBulletImpact(DeadlockHero hero, uint surfacePropertyHash, Func<DeadlockBulletImpact, string?> field)
    {
        foreach (var surface in Scene.ProjectedDecals.GetSurfaceLineage(surfacePropertyHash).Append("default"))
        {
            if (hero.BulletImpacts.TryGetValue(surface, out var impact) && field(impact) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private void PlayBulletImpact(TempEntities effects, DeadlockHero hero, Rubikon.TraceResult trace)
    {
        var position = trace.HitPosition;
        var normal = trace.HitNormal;
        var surface = trace.SurfacePropertyHash;

        if (FindBulletImpact(hero, surface, static impact => impact.Decal) is { } decal)
        {
            Scene.ProjectedDecals.SpawnGroupDecal(decal, position, normal, trace.HitEntity);
        }

        if (FindBulletImpact(hero, surface, static impact => impact.Particle) is { } particle)
        {
            effects.DispatchParticleEffect(particle, Scene, position, normal);
        }

        if (hero.ImpactEffect != null)
        {
            effects.DispatchParticleEffect(hero.ImpactEffect, Scene, position, normal);
        }

        if (FindBulletImpact(hero, surface, static impact => impact.Sound) is { } surfaceSound)
        {
            Sound.Play(surfaceSound, position, volumeScale: PlayerMovement.DeadlockSoundScale);
        }

        if (hero.ImpactSound != null)
        {
            Sound.Play(hero.ImpactSound, position, volumeScale: PlayerMovement.DeadlockSoundScale);
        }
    }

    // The effects of the attacks the animator started this update
    private void PlayAttackEffects(UserInput input, TempEntities effects, HeroBodyAnimator animator)
    {
        var hero = input.PlayerMovement.Hero;
        var aim = input.Camera.Forward;
        var aimPoint = seenShots < animator.ShotCount ? AimPoint(input) : Vector3.Zero;

        for (; seenShots < animator.ShotCount; seenShots++)
        {
            var muzzle = MuzzlePosition(input, seenShots);
            var velocity = BulletVelocity(hero, muzzle, aimPoint);

            if (hero.MuzzleFlashEffect != null)
            {
                effects.DispatchParticleEffect(hero.MuzzleFlashEffect, Scene, muzzle, aim);
            }

            if (velocity == Vector3.Zero)
            {
                continue;
            }

            for (var i = 0; i < hero.BulletsPerShot; i++)
            {
                var bulletVelocity = i > 0 || hero.SpreadFirstBullet ? Scatter(velocity, hero.Spread) : velocity;
                var tracer = hero.TracerEffect != null
                    ? effects.DispatchParticleEffect(hero.TracerEffect, Scene, muzzle, bulletVelocity)
                    : null;

                bullets.Add(new Bullet(muzzle, bulletVelocity, tracer));
            }
        }

        var meleeOrigin = hero.MeleeEffectAttachment is { } attachment && Attachments.ContainsKey(attachment)
            ? GetChildFrame(attachment).Translation
            : input.PlayerMovement.EyePosition;

        for (; seenMelees < animator.MeleeCount; seenMelees++)
        {
            if (hero.LightMeleeEffect != null)
            {
                effects.DispatchParticleEffect(hero.LightMeleeEffect, Scene, meleeOrigin, aim);
            }
        }

        for (; seenParries < animator.ParryCount; seenParries++)
        {
            if (hero.ParryEffect != null)
            {
                effects.DispatchParticleEffect(hero.ParryEffect, Scene, meleeOrigin, aim);
            }
        }
    }

    // Turns a direction by a random angle up to the spread, evenly over the cone
    private static Vector3 Scatter(Vector3 direction, float spreadDegrees)
    {
        var cosine = float.Lerp(1f, MathF.Cos(float.DegreesToRadians(spreadDegrees)), Random.Shared.NextSingle());
        var sine = MathF.Sqrt(1f - (cosine * cosine));
        var turn = Random.Shared.NextSingle() * MathF.Tau;

        var forward = Vector3.Normalize(direction);
        var side = MathUtils.GetOrthogonal(forward);
        var up = Vector3.Cross(forward, side);

        return ((forward * cosine) + (((side * MathF.Cos(turn)) + (up * MathF.Sin(turn))) * sine)) * direction.Length();
    }
}
