using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A camera placed in the map: <c>point_camera</c>, <c>point_camera_vertical_fov</c> and
/// <c>point_devshot_camera</c>. The viewer offers each one as a viewpoint.
/// </summary>
public class PointCamera : BaseEntity
{
    /// <summary>Gets the name the viewer lists this camera under.</summary>
    public string CameraName { get; private set; } = string.Empty;

    /// <summary>Initializes a camera from its keyvalues.</summary>
    public PointCamera(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        CameraName = KeyValues.GetStringProperty("cameraname") ?? TargetName ?? Classname;
    }
}
