namespace ValveResourceFormat.ResourceTypes.GenericData.CS2;

/// <summary>
/// Stores information about a bombsite on the map, such as AABB and bomb power.
/// The bounds are stored as baked in the file; the game expands them by 32 units on each axis when loading.
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
    /// The power of the bomb at this bombsite, approximately the distance at which the bomb deals 100 damage.
    /// The game computes damage as <c>100 * BombPower / Phase</c>.
    /// </summary>
    public float BombPower { get; set; }
}
