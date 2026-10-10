using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_camera</c>, <c>point_camera_vertical_fov</c> and <c>point_devshot_camera</c>.
/// The viewer offers each as a viewpoint.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPointCamera">CPointCamera</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPointCameraVFOV">CPointCameraVFOV</seealso>
public class PointCamera : BaseEntity
{
    /// <summary>Gets the name the viewer lists this camera under.</summary>
    public string CameraName => KeyValues.GetStringProperty("cameraname") ?? TargetName ?? Classname;

    /// <summary>Initializes a camera from its keyvalues.</summary>
    public PointCamera(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }
}
