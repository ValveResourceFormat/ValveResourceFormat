namespace ValveResourceFormat.ResourceTypes.GenericData.CS2;

/// <summary>
/// Baked bomb damage information for a specific position and bombsite on the map.
/// Stored exactly as packed in the resource file (4 bytes per value).
/// </summary>
public struct BombDamageDamageValue
{
    /// <summary>
    /// Effective distance from the bombsite used for damage falloff.
    /// The game clamps this to at most 1800 for the falloff computation; when at most 1800, damage works out to <c>100 * BombPower / Phase</c>, clamped to 0-255 (see <see cref="BombDamage.CalculateDamage"/>).
    /// </summary>
    public ushort Phase { get; set; }
    /// <summary>
    /// Yaw of the bomb blast direction, where 0-255 maps to a full turn. See <see cref="Rotation"/>.
    /// </summary>
    public byte Yaw { get; set; }
    /// <summary>
    /// Pitch of the bomb blast direction, where 0-255 maps to a full turn. See <see cref="Rotation"/>.
    /// </summary>
    public byte Pitch { get; set; }

    /// <summary>
    /// Rotation of the bomb blast direction. Rotating <see cref="Vector3.UnitX"/> by this yields the game's forward vector.
    /// </summary>
    public readonly Quaternion Rotation =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Tau / 255.0f * Yaw) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Tau / 255.0f * Pitch);
}
