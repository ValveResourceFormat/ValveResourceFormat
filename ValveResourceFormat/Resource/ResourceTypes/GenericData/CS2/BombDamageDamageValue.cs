namespace ValveResourceFormat.ResourceTypes.GenericData.CS2;

/// <summary>
/// Baked bomb damage information for a specific position and bombsite on the map.
/// </summary>
public struct BombDamageDamageValue
{
    /// <summary>
    /// A value that increases with distance to the bombsite.
    /// </summary>
    public float Phase { get; set; }
    /// <summary>
    /// Angle in degrees that represents the direction of the bomb blast.
    /// </summary>
    public float Yaw { get; set; }
    /// <summary>
    /// Angle in degrees that represents the direction of the bomb blast.
    /// </summary>
    public float Pitch { get; set; }
}
