namespace ValveResourceFormat.GameSpecific.CS2.BombDamageData;

/// <summary>
/// Baked bomb damage information for a specific position and bombsite on the map.
/// </summary>
public struct BombDamageDataDamageValue
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
