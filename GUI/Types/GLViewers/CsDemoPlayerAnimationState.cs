using ValveResourceFormat.DemoPlayback;

namespace GUI.Types.GLViewers;

readonly record struct CsDemoPlayerAnimationState(
    Vector3 Velocity,
    bool OnGround,
    bool JustJumped,
    float CrouchBlend,
    bool IsWalking,
    Vector2 MovementInput,
    CsDemoPlayerWeaponAnimationGroup WeaponGroup)
{
    public static CsDemoPlayerAnimationState FromPlayer(
        CsDemoPlayerState player,
        int tick,
        (Vector3 Position, int Tick, bool OnGround)? previous)
    {
        var velocity = player.Velocity;
        if (velocity == Vector3.Zero && previous is { } previousState && tick > previousState.Tick)
        {
            var deltaSeconds = (tick - previousState.Tick) / (float)CsDemoPlayback.TickRate;
            if (deltaSeconds > 0f)
            {
                velocity = (player.Position - previousState.Position) / deltaSeconds;
            }
        }

        var onGround = player.OnGround;
        if (velocity.Z is > 30f or < -30f && previous.HasValue)
        {
            onGround = false;
        }

        var justJumped = previous is { OnGround: true } && !onGround && velocity.Z > 30f;

        return new(
            velocity,
            onGround,
            justJumped,
            Math.Clamp(player.CrouchBlend, 0f, 1f),
            player.IsWalking,
            player.MovementInput,
            CsDemoPlayerWeaponAnimationGroup.FromWeapon(player.ActiveWeapon));
    }
}

readonly record struct CsDemoPlayerWeaponAnimationGroup(string Item, string DefaultItem)
{
    public static CsDemoPlayerWeaponAnimationGroup Rifle { get; } = new("rifle", "rifle");

    public static CsDemoPlayerWeaponAnimationGroup FromWeapon(string? activeWeapon)
    {
        if (string.IsNullOrWhiteSpace(activeWeapon))
        {
            return Rifle;
        }

        var weapon = activeWeapon;
        if (weapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
        {
            weapon = weapon["weapon_".Length..];
        }

        if (weapon.StartsWith("Weapon", StringComparison.Ordinal))
        {
            weapon = weapon["Weapon".Length..];
        }

        return weapon switch
        {
            var name when name.Contains("Knife", StringComparison.OrdinalIgnoreCase) => new("knife", "knife"),
            var name when name.Contains("Pistol", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Glock", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Deagle", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Elite", StringComparison.OrdinalIgnoreCase)
                || name.Contains("FiveSeven", StringComparison.OrdinalIgnoreCase)
                || name.Contains("HKP2000", StringComparison.OrdinalIgnoreCase)
                || name.Contains("P250", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Tec9", StringComparison.OrdinalIgnoreCase)
                || name.Contains("USP", StringComparison.OrdinalIgnoreCase)
                || name.Contains("CZ75", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Revolver", StringComparison.OrdinalIgnoreCase) => new("pistol", "pistol"),
            var name when name.Contains("Grenade", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Flash", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Molotov", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Decoy", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Smoke", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Incendiary", StringComparison.OrdinalIgnoreCase)
                || name.Equals("C4", StringComparison.OrdinalIgnoreCase) => new("grenade", "rifle"),
            _ => Rifle,
        };
    }
}
