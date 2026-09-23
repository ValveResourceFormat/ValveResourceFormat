using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>sky_camera_volume</c>. While the camera is inside its oriented box, the 3D sky is the map's own
/// world seen from its <see cref="SkyCameraVolumeTarget"/>, replacing any <c>skybox_reference</c> sky.
/// </summary>
/// <remarks>
/// The blur effect and the two sun shadow sharing options are read but not drawn.
/// </remarks>
public sealed class SkyCameraVolume : BaseEntity
{
    /// <summary>Gets the local minimum corner of the box.</summary>
    public Vector3 BoxMins { get; private set; }

    /// <summary>Gets the local maximum corner of the box.</summary>
    public Vector3 BoxMaxs { get; private set; }

    /// <summary>Gets the priority among overlapping volumes; the highest wins.</summary>
    public int Priority { get; private set; }

    /// <summary>Gets or sets whether the volume can be chosen.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Gets whether the sky pixels are blurred towards <see cref="BlurOrigin"/>.</summary>
    public bool SkyboxBlurEffect { get; private set; }

    /// <summary>Gets where the blur points, as an offset from the volume origin.</summary>
    public Vector3 BlurOrigin { get; private set; }

    /// <summary>Gets the target the sky is seen from, once resolved.</summary>
    public SkyCameraVolumeTarget? Target { get; private set; }

    /// <summary>Initializes a <c>sky_camera_volume</c> from its keyvalues.</summary>
    public SkyCameraVolume(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        BoxMins = KeyValues.GetVector3Property("box_mins", new Vector3(-256f));
        BoxMaxs = KeyValues.GetVector3Property("box_maxs", new Vector3(256f));
        Priority = KeyValues.GetInt32Property("priority");
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
        SkyboxBlurEffect = KeyValues.GetBooleanProperty("skyboxblureffect");
        BlurOrigin = KeyValues.GetVector3Property("blur_origin");
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        var targetName = KeyValues.GetStringProperty("target");

        if (string.IsNullOrEmpty(targetName))
        {
            return;
        }

        Target = EntitySystem.FindByTargetName(targetName, Scene) as SkyCameraVolumeTarget;

        if (Target == null)
        {
            EntitySystem.Logger.LogWarning("sky_camera_volume target '{Target}' was not found", targetName);
        }
    }

    /// <summary>Tests a world position against the box, through the volume's full transform.</summary>
    public bool Contains(Vector3 position)
    {
        if (!Matrix4x4.Invert(Transform, out var worldToLocal))
        {
            return false;
        }

        var local = Vector3.Transform(position, worldToLocal);

        return local == Vector3.Clamp(local, BoxMins, BoxMaxs);
    }

    /// <summary>
    /// Picks the volume the 3D sky is seen through from <paramref name="eye"/>: an enabled one whose target
    /// is still there and whose box holds the eye. A higher priority wins, and a tie keeps the earlier one.
    /// </summary>
    /// <returns>The volume, or <see langword="null"/> when none applies or the winner's scale disables it.</returns>
    public static SkyCameraVolume? FindActive(IEnumerable<BaseEntity> entities, Vector3 eye)
    {
        ArgumentNullException.ThrowIfNull(entities);

        SkyCameraVolume? best = null;

        foreach (var entity in entities)
        {
            if (entity is not SkyCameraVolume { IsEnabled: true, IsRemoved: false, Target.IsRemoved: false } volume
                || (best != null && volume.Priority <= best.Priority)
                || !volume.Contains(eye))
            {
                continue;
            }

            best = volume;
        }

        return best?.Target!.SkyScale > 0 ? best : null;
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => IsEnabled = true;

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => IsEnabled = false;
}
