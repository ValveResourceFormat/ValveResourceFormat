namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Converts fog distances and heights, which are authored in world units, into the space a view draws
/// in. Only the 3D sky view differs from the world.
/// </summary>
/// <param name="DistanceScale">What an authored distance is multiplied by.</param>
/// <param name="HeightOffset">What an authored height is shifted by, after the scale.</param>
public readonly record struct FogSpace(float DistanceScale, float HeightOffset)
{
    /// <summary>World space, which leaves authored values unchanged.</summary>
    public static FogSpace World { get; } = new(1f, 0f);

    /// <summary>Converts an authored distance.</summary>
    public float Distance(float distance) => distance * DistanceScale;

    /// <summary>Converts an authored height.</summary>
    public float Height(float height) => height * DistanceScale + HeightOffset;
}
