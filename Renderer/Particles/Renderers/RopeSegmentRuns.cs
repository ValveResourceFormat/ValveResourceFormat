namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>One particle of a rope or cable chain, as far as splitting the chain into runs goes.</summary>
    internal interface IRopeChainEntry
    {
        /// <summary>The rope the particle belongs to.</summary>
        int SegmentId { get; }

        /// <summary>The particle's place along its rope.</summary>
        int Order { get; }
    }

    /// <summary>
    /// Splits the particles a rope or cable threads through into the runs drawn as separate strips.
    /// Particles sharing a <see cref="ValveResourceFormat.Particles.ParticleField.RopeSegmentId"/>
    /// form one run, so a system holding several ropes (a branching bolt, or ropes spawned from
    /// different parent particles) never gets a strip joining the end of one to the start of the next.
    /// A system that never sets the field keeps every particle in segment 0 and draws one strip.
    /// </summary>
    internal static class RopeSegmentRuns
    {
        /// <summary>Orders the chain by segment, then by place along the rope.</summary>
        public static void Group<T>(Span<T> chain) where T : struct, IRopeChainEntry
            => chain.Sort(static (a, b) => a.SegmentId != b.SegmentId
                ? a.SegmentId.CompareTo(b.SegmentId)
                : a.Order.CompareTo(b.Order));

        /// <summary>The end (exclusive) of the run starting at <paramref name="start"/> in a grouped chain.</summary>
        public static int RunEnd<T>(ReadOnlySpan<T> chain, int start) where T : struct, IRopeChainEntry
        {
            var end = start + 1;

            while (end < chain.Length && chain[end].SegmentId == chain[start].SegmentId)
            {
                end++;
            }

            return end;
        }
    }
}
