namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>How <see cref="HitBoxCache.RandomPoint"/> picks a point.</summary>
    /// <param name="Tries">How many candidates past the first to draw.</param>
    /// <param name="DesiredHitbox">The only box to pick, or -1 for any.</param>
    /// <param name="Scale">The part of each box's extent to pick from, centred on the box, per axis.</param>
    /// <param name="DirectionBias">The direction from the origin candidates are scored along.</param>
    /// <param name="ShellSize">Below 1, pushes the point onto a face, keeping this fraction of the scaled extent.</param>
    /// <param name="EvenDistribution">Spread the positions within a box along a Halton sequence instead of at random.</param>
    /// <param name="WeightByVolume">Pick boxes in proportion to their volume.</param>
    internal readonly record struct HitBoxSampling(int Tries, int DesiredHitbox, Vector3 Scale, Vector3 DirectionBias, float ShellSize, bool EvenDistribution, bool WeightByVolume);
}
