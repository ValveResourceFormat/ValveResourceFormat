using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_teleport</c>. Teleports the player to its <c>target</c> entity when they enter
/// the volume, keeping their velocity.
/// </summary>
public sealed class TriggerTeleport
{
    /// <summary>Spawnflag: keep the player's view angles instead of the destination's.</summary>
    private const int SF_TELEPORT_PRESERVE_ANGLES = 32;

    private readonly EntityCollider collider;
    private readonly Vector3 destination;
    private readonly float? yawDegrees;
    private bool wasInside;

    private TriggerTeleport(EntityCollider collider, Vector3 destination, float? yawDegrees)
    {
        this.collider = collider;
        this.destination = destination;
        this.yawDegrees = yawDegrees;
    }

    /// <summary>
    /// Loads every <c>trigger_teleport</c> in the map. The volume comes from the trigger's own
    /// <c>model</c>, which holds its brush hulls.
    /// </summary>
    /// <param name="loadedWorld">The loaded map.</param>
    /// <param name="fileLoader">Loader used to resolve the trigger models.</param>
    public static List<TriggerTeleport> LoadAll(WorldLoader loadedWorld, IFileLoader fileLoader)
    {
        var teleports = new List<TriggerTeleport>();

        foreach (var entity in loadedWorld.Entities)
        {
            if (entity.GetStringProperty("classname") is not "trigger_teleport")
            {
                continue;
            }

            if (loadedWorld.FindEntityByTargetName(entity.GetStringProperty("target")) is not { } destination)
            {
                continue;
            }

            if (fileLoader.LoadFileCompiled(entity.GetStringProperty("model"))?.DataBlock is not Model model)
            {
                continue;
            }

            if (EntityCollider.LoadPhysics(model, fileLoader) is not { } physics)
            {
                continue;
            }

            var preserveAngles = (entity.GetInt32Property("spawnflags") & SF_TELEPORT_PRESERVE_ANGLES) != 0;

            var collider = new EntityCollider(physics)
            {
                Transform = EntityTransformHelper.CalculateTransformationMatrix(entity),
            };

            teleports.Add(new TriggerTeleport(collider, destination.GetVector3Property("origin"), preserveAngles ? null : destination.GetVector3Property("angles").Y));
        }

        return teleports;
    }

    /// <summary>
    /// Teleports the player on the frame they enter the volume.
    /// </summary>
    /// <param name="movement">The player to test against the volume.</param>
    public void Touch(PlayerMovement movement)
    {
        var hullCenter = movement.Position + new Vector3(0, 0, movement.HullHalfExtents.Z);
        var inside = collider.Overlaps(hullCenter, movement.HullHalfExtents);

        if (inside && !wasInside)
        {
            // Lift a unit so the hull does not arrive embedded in the floor
            movement.Teleport(destination + new Vector3(0, 0, 1f), yawDegrees);
        }

        wasInside = inside;
    }
}
