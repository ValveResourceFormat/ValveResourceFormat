using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_catapult</c>, Deadlock's jump pad. Throws the player that enters its volume so it lands on the
/// entity named by <c>target</c>, or along <c>launch_direction</c> when it has none, at <c>launch_speed</c>.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/server/CTriggerCatapult">CTriggerCatapult</seealso>
public sealed class TriggerCatapult : BaseTrigger
{
    private float launchSpeed;
    private Vector3? landing;

    /// <summary>Initializes a <c>trigger_catapult</c> from its keyvalues.</summary>
    public TriggerCatapult(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        InitTrigger();
        launchSpeed = KeyValues.GetFloatProperty("launch_speed", 1000f);
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        var targetName = KeyValues.GetStringProperty("target");

        if (string.IsNullOrEmpty(targetName))
        {
            return;
        }

        if (EntitySystem.FindByTargetName(targetName) is not { } target)
        {
            EntitySystem.Logger.LogWarning("trigger_catapult '{TargetName}' target '{Target}' was not found", TargetName, targetName);
            return;
        }

        landing = target.WorldOrigin;
    }

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        base.OnStartTouch(other);

        if (other is not PlayerEntity { Controller: var controller } || !PassesTriggerFilters(other))
        {
            return;
        }

        controller.Launch(landing is { } target
            ? BallisticVelocity(controller.Position, target, launchSpeed, controller.Gravity)
            : DirectionalVelocity());
    }

    // Covers the ground to the target at the launch speed, and rises enough to come down on it
    private static Vector3 BallisticVelocity(Vector3 from, Vector3 to, float speed, float gravity)
    {
        var offset = to - from;
        var across = new Vector2(offset.X, offset.Y);
        var distance = across.Length();

        if (distance < 1f || speed <= 0f)
        {
            return new Vector3(0f, 0f, speed);
        }

        var time = distance / speed;
        var horizontal = across / time;

        return new Vector3(horizontal.X, horizontal.Y, (offset.Z / time) + (0.5f * gravity * time));
    }

    private Vector3 DirectionalVelocity()
    {
        var angles = KeyValues.ContainsKey("launch_direction") ? KeyValues.GetVector3Property("launch_direction") : WorldAngles;

        return EntityTransformHelper.EulerAnglesToForwardDirection(angles) * launchSpeed;
    }
}
