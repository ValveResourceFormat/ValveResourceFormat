namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A player spawn marker: <c>info_player_start</c> and the per-game and per-team variants, and CS2's
/// <c>team_select</c>. The viewer starts its camera at the best one.
/// </summary>
public sealed class SpawnPoint : BaseEntity
{
    /// <summary>Spawn flags for <c>info_player_start</c>.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Marks the one to use when a map has several, as HL:A maps do.</summary>
        Master = 1,
    }

    /// <summary>Gets whether this is the <c>info_player_start</c> marked as the one to use.</summary>
    public bool IsMasterPlayerStart
        => Classname.Equals("info_player_start", StringComparison.OrdinalIgnoreCase) && HasSpawnFlags(SpawnFlag.Master);

    /// <summary>Initializes a spawn point from its keyvalues.</summary>
    public SpawnPoint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }
}
