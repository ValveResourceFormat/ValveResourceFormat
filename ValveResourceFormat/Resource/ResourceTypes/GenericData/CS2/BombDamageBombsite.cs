namespace ValveResourceFormat.ResourceTypes.GenericData.CS2;

/// <summary>
/// Stores information about a bombsite on the map, such as AABB and bomb power.
/// </summary>
public struct BombDamageBombsite
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
