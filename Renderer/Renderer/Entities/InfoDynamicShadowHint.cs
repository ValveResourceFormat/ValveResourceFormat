using System.Linq;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_dynamic_shadow_hint</c> and <c>info_dynamic_shadow_hint_box</c>. While the viewer is inside the
/// sphere or box, the hint names the stationary light that casts the one real-time mixed shadow.
/// </summary>
public sealed class InfoDynamicShadowHint : BaseEntity
{
    private readonly bool isBox;
    private float range;
    private Vector3 boxMins;
    private Vector3 boxMaxs;
    private bool lightResolved;
    private SceneLight? light;

    /// <summary>Gets whether the hint takes part in choosing the mixed shadow light.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Gets the priority of the hint; a higher importance wins over nearer hints.</summary>
    public int Importance { get; private set; }

    /// <summary>Initializes a shadow hint from its keyvalues.</summary>
    public InfoDynamicShadowHint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        isBox = Classname == "info_dynamic_shadow_hint_box";
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
        Importance = KeyValues.GetInt32Property("importance");
        range = KeyValues.GetFloatProperty("range", 256f);
        boxMins = KeyValues.GetVector3Property("box_mins", new Vector3(-64f));
        boxMaxs = KeyValues.GetVector3Property("box_maxs", new Vector3(64f));

        Scene.LightingInfo.MixedShadows.Hints.Add(this);
    }

    /// <summary>Gets the world position the hint is measured from.</summary>
    public Vector3 Position => RigidTransform.Translation;

    /// <summary>Gets whether a world position is inside the sphere or box of the hint.</summary>
    public bool Contains(Vector3 position)
    {
        var rigidTransform = RigidTransform;

        if (!isBox)
        {
            return range * range > Vector3.DistanceSquared(position, rigidTransform.Translation);
        }

        if (!Matrix4x4.Invert(rigidTransform, out var worldToLocal))
        {
            return false;
        }

        var local = Vector3.Transform(position, worldToLocal);

        return local.X >= boxMins.X && local.Y >= boxMins.Y && local.Z >= boxMins.Z
            && local.X <= boxMaxs.X && local.Y <= boxMaxs.Y && local.Z <= boxMaxs.Z;
    }

    /// <summary>
    /// Gets the light the hint names: by <c>lightchoice</c> 0 the light with the <c>light</c> name nearest to the
    /// hint, by 1 the nearest <c>light_environment</c>. Found once, on first use, after every light has spawned.
    /// </summary>
    public SceneLight? GetLight()
    {
        if (lightResolved)
        {
            return light;
        }

        lightResolved = true;

        var candidates = KeyValues.GetInt32Property("lightchoice") switch
        {
            0 => KeyValues.GetStringProperty("light") is { Length: > 0 } name
                ? EntitySystem.FindAllByTargetName(name)
                : [],
            1 => EntitySystem.Entities.Where(static entity => entity.Classname == "light_environment"),
            _ => [],
        };

        var origin = Position;
        var bestDistance = float.MaxValue;

        foreach (var entity in candidates)
        {
            if (entity is not LightEntity { Light: { } candidate } lightEntity || lightEntity.IsRemoved)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(origin, lightEntity.RigidTransform.Translation);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                light = candidate;
            }
        }

        return light;
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => IsEnabled = true;

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => IsEnabled = false;

    /// <inheritdoc/>
    protected override void OnRemove() => Scene.LightingInfo.MixedShadows.Hints.Remove(this);
}
