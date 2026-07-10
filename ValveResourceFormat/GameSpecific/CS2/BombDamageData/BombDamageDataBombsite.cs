namespace ValveResourceFormat.GameSpecific.CS2.BombDamageData;

/// <summary>
/// Stores information about a bombsite on the map, such as AABB and bomb power.
/// </summary>
public struct BombDamageDataBombsite
{
    /// <summary>
    /// Minimum worldspace bounds of the bombsite.
    /// </summary>
    public Vector3 BoundsMin { get; set; }
    /// <summary>
    /// Maximum worldspace bounds of the bombsite.
    /// </summary>
    public Vector3 BoundsMax { get; set; }
    /// <summary>
    /// The power of the bomb at this bombsite.
    /// </summary>
    public float BombPower { get; set; }
}
