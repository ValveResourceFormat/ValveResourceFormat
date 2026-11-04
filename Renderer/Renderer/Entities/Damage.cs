namespace ValveResourceFormat.Renderer.Entities;

/// <summary>Damage kinds that <c>prop_data</c> scales separately with <c>dmg.*</c> keys.</summary>
public enum DamageType
{
    /// <summary>Unscaled damage, such as a <c>RemoveHealth</c> input.</summary>
    Generic,

    /// <summary>Gunfire, scaled by <c>dmg.bullets</c>.</summary>
    Bullet,

    /// <summary>Melee, scaled by <c>dmg.club</c>.</summary>
    Club,

    /// <summary>Blasts, scaled by <c>dmg.explosive</c>.</summary>
    Explosive,

    /// <summary>Physics impacts, from falling or being struck. Not scaled.</summary>
    Crush,
}

/// <summary>A hit to be dealt to an entity.</summary>
/// <param name="Amount">Damage before per-type scaling of the target.</param>
/// <param name="Type">Kind of damage.</param>
/// <param name="Attacker">Entity that dealt it.</param>
/// <param name="Direction">Travel direction of the hit, zero when unknown.</param>
public readonly record struct DamageInfo(float Amount, DamageType Type, BaseEntity? Attacker = null, Vector3 Direction = default);

/// <summary>Something that has health and can be hurt.</summary>
public interface IDamageable
{
    /// <summary>Deals damage, possibly breaking or killing the entity.</summary>
    void TakeDamage(in DamageInfo info);
}
