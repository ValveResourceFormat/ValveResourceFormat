using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_teleport</c>. Moves its <c>target</c>, or the entity an input names, to a saved pose on command.
/// </summary>
/// <remarks>
/// <c>Teleport</c> goes to the pose saved in <see cref="Activate"/>, not to where this entity is now;
/// the <c>ToCurrentPos</c> inputs use its current pose. Maps rely on that to park an entity out of sight
/// and snap it into place later.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPointTeleport">CPointTeleport</seealso>
public sealed class PointTeleport : BaseEntity
{
    /// <summary>What a <c>point_teleport</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Saves the target's own pose at activation, so <c>Teleport</c> sends it back there.</summary>
        TeleportHome = 1,
    }

    // A player arrives this far up so its hull does not start embedded in the floor
    private const float PlayerLift = 1f / 32f;

    private Vector3 savedOrigin;
    private Vector3 savedAngles;
    private string? targetName;
    private bool teleportParentedEntities;
    private bool teleportUseCurrentAngle;

    /// <summary>Initializes a <c>point_teleport</c> from its keyvalues.</summary>
    public PointTeleport(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        targetName = KeyValues.GetStringProperty("target");
        teleportParentedEntities = KeyValues.GetBooleanProperty("teleport_parented_entities");
        teleportUseCurrentAngle = KeyValues.GetBooleanProperty("teleport_use_current_angle");
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        savedOrigin = WorldOrigin;
        savedAngles = WorldAngles;

        if (!HasSpawnFlags(SpawnFlag.TeleportHome))
        {
            return;
        }

        if (FindTarget(targetName, null, null) is not { } target)
        {
            EntitySystem.Logger.LogWarning("point_teleport '{TargetName}' target '{Target}' was not found, removing it", TargetName, targetName);
            EntitySystem.Remove(this);
            return;
        }

        if (!MayTeleport(target))
        {
            return;
        }

        savedOrigin = target.WorldOrigin;
        savedAngles = target.WorldAngles;
    }

    [EntityInput("Teleport")]
    private void InputTeleport(EntityInputData data) => DoTeleport(data, savedOrigin, savedAngles, overrideTarget: null);

    [EntityInput("TeleportEntity")]
    private void InputTeleportEntity(EntityInputData data) => DoTeleport(data, savedOrigin, savedAngles, data.Parameter);

    [EntityInput("TeleportToCurrentPos")]
    private void InputTeleportToCurrentPos(EntityInputData data) => TeleportToCurrentPos(data, overrideTarget: null);

    [EntityInput("TeleportEntityToCurrentPos")]
    private void InputTeleportEntityToCurrentPos(EntityInputData data) => TeleportToCurrentPos(data, data.Parameter);

    private void TeleportToCurrentPos(EntityInputData data, string? overrideTarget)
    {
        if (HasSpawnFlags(SpawnFlag.TeleportHome))
        {
            EntitySystem.Logger.LogWarning("point_teleport '{TargetName}' teleports to its current position, ignoring its Teleport Home spawnflag", TargetName);
        }

        DoTeleport(data, WorldOrigin, WorldAngles, overrideTarget);
    }

    private void DoTeleport(EntityInputData data, Vector3 origin, Vector3 angles, string? overrideTarget)
    {
        var name = string.IsNullOrEmpty(overrideTarget) ? targetName : overrideTarget;

        if (FindTarget(name, data.Activator, data.Caller) is not { } target || !MayTeleport(target))
        {
            return;
        }

        if (target is PlayerEntity)
        {
            origin.Z += PlayerLift;
        }

        if (target.MoveParent != null)
        {
            target.Teleport(origin, angles);
            return;
        }

        if (teleportUseCurrentAngle)
        {
            target.Teleport(origin, null);
            return;
        }
        if (target is not PlayerEntity)
        {
            var entitySpaceVelocity = Vector3.Transform(target.Velocity, Quaternion.Inverse(EntityTransformHelper.EulerAnglesToQuaternion(target.WorldAngles)));
            target.Velocity = Vector3.Transform(entitySpaceVelocity, EntityTransformHelper.EulerAnglesToQuaternion(angles));
        }

        target.Teleport(origin, angles);
    }

    private BaseEntity? FindTarget(string? name, BaseEntity? activator, BaseEntity? caller)
        => string.IsNullOrEmpty(name)
            ? null
            : EntitySystem.FindTargets(new EntityIOTarget(name, EntityIOTargetType.EntityName), activator, caller).FirstOrDefault();

    private bool MayTeleport(BaseEntity target)
    {
        if (target.MoveParent == null || teleportParentedEntities)
        {
            return true;
        }

        EntitySystem.Logger.LogWarning("point_teleport '{TargetName}' can't teleport '{Target}' as it has a parent '{Parent}'",
            TargetName, target.TargetName, target.MoveParent.TargetName);
        return false;
    }
}
