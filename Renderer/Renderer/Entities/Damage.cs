namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The kinds of damage a prop's <c>prop_data</c> scales separately with its <c>dmg.*</c> keys.
/// </summary>
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
/// <param name="Amount">Damage before the target's own scaling.</param>
/// <param name="Type">What kind of damage it is.</param>
/// <param name="Attacker">Who dealt it, for the outputs it fires.</param>
public readonly record struct DamageInfo(float Amount, DamageType Type, BaseEntity? Attacker = null);

/// <summary>Something that has health and can be hurt.</summary>
public interface IDamageable
{
    /// <summary>Deals damage to the entity, which may break or kill it.</summary>
    void TakeDamage(in DamageInfo info);
}
