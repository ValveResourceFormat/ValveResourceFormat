using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>sky_camera_volume_target</c>. The point in the map a <see cref="SkyCameraVolume"/> draws the 3D sky
/// from. Its origin is read every frame, so parenting it to a mover makes the sky travel.
/// </summary>
public sealed class SkyCameraVolumeTarget : BaseEntity
{
    /// <summary>Gets how many world units one unit around the target stands for; zero or less disables the sky.</summary>
    public int SkyScale { get; private set; }

    /// <summary>Gets the material that replaces the map's 2D sky while this target is in use, if any.</summary>
    public string? SkyMaterialName { get; private set; }

    /// <summary>Initializes a <c>sky_camera_volume_target</c> from its keyvalues.</summary>
    public SkyCameraVolumeTarget(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        SkyScale = KeyValues.GetInt32Property("scale", 16);

        var skyMaterial = KeyValues.GetStringProperty("sky_material");
        SkyMaterialName = string.IsNullOrEmpty(skyMaterial) ? null : skyMaterial;
    }
}
