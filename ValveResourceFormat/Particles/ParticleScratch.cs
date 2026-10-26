namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Per-thread arenas of temporary columns that particle functions fill while they run. A function
    /// takes what it needs with <see cref="Floats"/>, <see cref="Vectors"/> or <see cref="BoneIndices"/>,
    /// and everything taken inside a <see cref="Scope"/> is given back when the scope is disposed, which
    /// the simulation does around every function call. The buffers are kept between calls, so a step
    /// allocates nothing once they have grown, and a span stays valid until its scope ends.
    /// </summary>
    internal static class ParticleScratch
    {
        [ThreadStatic]
        private static Arena<float>? floats;

        [ThreadStatic]
        private static Arena<Vector3>? vectors;

        [ThreadStatic]
        private static Arena<ulong>? boneIndices;

        /// <summary>Gets <paramref name="length"/> floats, holding whatever was last written there.</summary>
        public static Span<float> Floats(int length) => (floats ??= new()).Take(length);

        /// <summary>Gets <paramref name="length"/> vectors, holding whatever was last written there.</summary>
        public static Span<Vector3> Vectors(int length) => (vectors ??= new()).Take(length);

        /// <summary>Gets <paramref name="length"/> packed bone index sets, holding whatever was last written there.</summary>
        public static Span<ulong> BoneIndices(int length) => (boneIndices ??= new()).Take(length);

        /// <summary>
        /// Starts a scope that gives back everything taken after it when disposed, as in
        /// <c>using var scratch = ParticleScratch.Scope();</c>.
        /// </summary>
        public static ScratchScope Scope() => new(floats?.Used ?? 0, vectors?.Used ?? 0, boneIndices?.Used ?? 0);

        /// <summary>How much of each arena was taken when a <see cref="Scope"/> started.</summary>
        public readonly ref struct ScratchScope
        {
            private readonly int floatsUsed;
            private readonly int vectorsUsed;
            private readonly int boneIndicesUsed;

            internal ScratchScope(int floatsUsed, int vectorsUsed, int boneIndicesUsed)
            {
                this.floatsUsed = floatsUsed;
                this.vectorsUsed = vectorsUsed;
                this.boneIndicesUsed = boneIndicesUsed;
            }

            /// <summary>Gives back everything taken since the scope started.</summary>
            public void Dispose()
            {
                floats?.Used = floatsUsed;
                vectors?.Used = vectorsUsed;
                boneIndices?.Used = boneIndicesUsed;
            }
        }

        private sealed class Arena<T>
        {
            private T[] items = new T[1024];

            public int Used { get; set; }

            public Span<T> Take(int length)
            {
                if (Used + length > items.Length)
                {
                    // Spans already taken keep the old array alive, so nothing has to be copied
                    items = new T[Math.Max(items.Length * 2, length)];
                    Used = 0;
                }

                var span = items.AsSpan(Used, length);
                Used += length;
                return span;
            }
        }
    }
}
