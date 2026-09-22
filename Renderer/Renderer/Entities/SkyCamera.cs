using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>sky_camera</c>. In a map loaded as another map's 3D sky, it is the point the sky is viewed from,
/// and its scale is how much the sky is magnified.
/// </summary>
public sealed class SkyCamera : PointCamera
{
    /// <summary>Gets the sky magnification; a missing or non-positive <c>scale</c> means 1.</summary>
    public float SkyScale { get; private set; } = 1f;

    /// <summary>Initializes a <c>sky_camera</c> from its keyvalues.</summary>
    public SkyCamera(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        var scale = KeyValues.GetFloatProperty("scale");
        SkyScale = scale > 0f ? scale : 1f;
    }
}
