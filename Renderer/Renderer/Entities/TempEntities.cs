using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>Where and how a dispatched effect happens.</summary>
/// <param name="Scene">The scene the effect shows in.</param>
/// <param name="Origin">Where the effect happens.</param>
/// <param name="Normal">The normal of the surface it happens on.</param>
/// <param name="Direction">The direction whatever caused it was travelling.</param>
/// <param name="SurfacePropertyHash">The hash of the surface property that was hit, or zero for the default surface.</param>
/// <param name="Entity">The entity that was hit, or null for the static world.</param>
/// <param name="Scale">How big the effect is, for effects that come in sizes.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEffectData">CEffectData</seealso>
public readonly record struct EffectData(Scene Scene, Vector3 Origin, Vector3 Normal, Vector3 Direction,
    uint SurfacePropertyHash = 0, BaseEntity? Entity = null, float Scale = 1f);

/// <summary>
/// Temporary entities: effects dispatched by name that play once and are gone, with no entity in the world.
/// </summary>
public sealed class TempEntities
{
    private const int MaxParticles = 64;

    private readonly EntitySystem entitySystem;
    private readonly Dictionary<string, ParticleSystem?> particleSystems = [];
    private readonly List<ParticleSceneNode> particles = [];

    // Finished effects stay in their scene, stopped, to play again: building one allocates its whole particle pool
    private readonly Dictionary<(Scene Scene, string Name), Stack<ParticleSceneNode>> idleParticles = [];

    internal TempEntities(EntitySystem entitySystem)
    {
        this.entitySystem = entitySystem;
    }

    /// <summary>Plays an effect.</summary>
    /// <param name="effectName">The name the effect is dispatched by: <c>Impact</c>, <c>KnifeSlash</c> or <c>gunshotsplash</c>.</param>
    /// <param name="data">Where and how it happens.</param>
    public void DispatchEffect(string effectName, in EffectData data)
    {
        switch (effectName)
        {
            case "Impact":
                Impact(data);
                break;

            case "KnifeSlash":
                data.Scene.ProjectedDecals.SpawnKnifeDecal(data.Origin, data.Normal, data.Direction, data.SurfacePropertyHash, data.Entity);
                ImpactParticle(data);
                break;

            case "gunshotsplash":
                GunshotSplash(data);
                break;

            default:
                entitySystem.Logger.LogDebug("No effect is dispatched by the name '{Effect}'", effectName);
                break;
        }
    }

    private void Impact(in EffectData data)
    {
        var decals = data.Scene.ProjectedDecals;

        decals.SpawnImpactDecal(data.Origin, data.Normal, data.Direction, data.SurfacePropertyHash, data.Entity);

        if (decals.FindBulletImpactSound(data.SurfacePropertyHash) is { } sound)
        {
            Sound.Play(sound, data.Origin);
        }

        ImpactParticle(data);
    }

    private const string WaterSplashSound = "Physics.WaterSplash";

    // The splash always goes straight up, whatever the surface normal
    private void GunshotSplash(in EffectData data)
    {
        var particleName = data.Scale switch
        {
            < 4f => "particles/water_impact/water_splash_01.vpcf",
            < 8f => "particles/water_impact/water_splash_02.vpcf",
            _ => "particles/water_impact/water_splash_03.vpcf",
        };

        DispatchParticleEffect(particleName, data.Scene, data.Origin, Vector3.UnitZ);
        Sound.Play(WaterSplashSound, data.Origin);
    }

    private void ImpactParticle(in EffectData data)
    {
        if (data.Scene.ProjectedDecals.FindImpactEffect(data.SurfacePropertyHash) is not { Length: > 0 } particleName)
        {
            return;
        }

        if (DispatchParticleEffect(particleName, data.Scene, data.Origin, data.Normal) is not { } particle)
        {
            return;
        }

        var direction = Vector3.Normalize(data.Direction);

        // The surface normal, the direction the bullet bounces off in, and the way back to the shooter
        particle.SetControlPoint(1, ControlPoint(data.Origin, Vector3.Reflect(direction, data.Normal)));
        particle.SetControlPoint(2, ControlPoint(data.Origin, -direction));

        // The effect scale, and the point just off the surface where effects sample the light that tints them
        particle.GetControlPoint(3).Position = Vector3.One;
        particle.GetControlPoint(4).Position = data.Origin + Vector3.One;
    }

    /// <summary>Plays a particle system once, removing it when it has finished.</summary>
    /// <param name="particleName">The particle system.</param>
    /// <param name="scene">The scene it shows in.</param>
    /// <param name="origin">Where it plays.</param>
    /// <param name="forward">The direction it faces.</param>
    /// <returns>The effect, or null when it could not be loaded.</returns>
    public ParticleSceneNode? DispatchParticleEffect(string particleName, Scene scene, Vector3 origin, Vector3 forward)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!particleSystems.TryGetValue(particleName, out var particleSystem))
        {
            particleSystem = entitySystem.FileLoader.LoadFileCompiled(particleName)?.DataBlock as ParticleSystem;
            particleSystems[particleName] = particleSystem;

            if (particleSystem == null)
            {
                entitySystem.Logger.LogWarning("Failed to load effect \"{Effect}\"", particleName);
            }
        }

        if (particleSystem == null)
        {
            return null;
        }

        // One that is never simulated never finishes, so the oldest makes way
        if (particles.Count >= MaxParticles)
        {
            Retire(0);
        }

        if (idleParticles.TryGetValue((scene, particleName), out var idle) && idle.TryPop(out var particle))
        {
            particle.Transform = ControlPoint(origin, forward);
            particle.Play();
        }
        else
        {
            particle = new ParticleSceneNode(scene, particleSystem)
            {
                Name = particleName,
                Transform = ControlPoint(origin, forward),
                LayerName = Scene.ParticlesLayerName,
            };

            scene.Add(particle, true);
        }

        particles.Add(particle);

        return particle;
    }

    internal void Update()
    {
        for (var i = particles.Count - 1; i >= 0; i--)
        {
            if (particles[i].IsFinished)
            {
                Retire(i);
            }
        }
    }

    private void Retire(int index)
    {
        var particle = particles[index];
        var key = (particle.Scene, particle.Name!);

        if (!idleParticles.TryGetValue(key, out var idle))
        {
            idleParticles[key] = idle = [];
        }

        particle.Stop();
        idle.Push(particle);
        particles.RemoveAt(index);
    }

    // Takes the effects out of the scenes that still hold them; a scene that was emptied has deleted its own
    internal void Clear()
    {
        foreach (var particle in particles.Concat(idleParticles.Values.SelectMany(static idle => idle)))
        {
            if (particle.Scene.Remove(particle, dynamic: true))
            {
                particle.Delete();
            }
        }

        particles.Clear();
        idleParticles.Clear();
        particleSystems.Clear();
    }

    private static Matrix4x4 ControlPoint(Vector3 origin, Vector3 forward)
        => EntityTransformHelper.ForwardDirectionToRotationMatrix(forward) * Matrix4x4.CreateTranslation(origin);
}
