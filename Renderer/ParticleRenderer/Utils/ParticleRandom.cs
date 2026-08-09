namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// One particle system's access to the engine's shared random table. Every random value in the
    /// particle system comes from here, and nothing else reads <see cref="RandomFloats"/>.
    ///
    /// <para>There are three ways in, and which one a draw uses decides whether the value changes
    /// between frames:</para>
    /// <list type="bullet">
    /// <item><c>Next…</c> advances a counter that runs free for the life of the system, so
    /// consecutive draws land on unrelated slots. Every initializer draws this way.</item>
    /// <item><c>ForParticle…</c> reads a slot fixed by the particle, so the value is the same on
    /// every frame of that particle's life. Used by the operators that must not flicker.</item>
    /// <item><c>ForSample…</c> reads a slot the caller worked out in full. These are static, because
    /// such a draw ignores this system's seed and counter entirely.</item>
    /// </list>
    /// </summary>
    class ParticleRandom
    {
        /// <summary>
        /// The step by which <see cref="OperatorOffset"/> advances between operators.
        /// </summary>
        public const int OperatorStride = 17;

        /// <summary>
        /// The offset into the shared table that every deterministic draw by this system is displaced
        /// by, giving each system instance its own sequence. Child systems hold their own value rather
        /// than reading the root's.
        /// </summary>
        public int Seed { get; private set; } = System.Random.Shared.Next() & 0xFFF;

        /// <summary>
        /// Displaces the per-particle draws of the operator currently running, so two operators
        /// reading the same particle do not read the same slot. Reset before each operator list and
        /// advanced once per operator.
        /// </summary>
        public int OperatorOffset { get; set; }

        /// <summary>
        /// Counts every draw this system has taken. It runs free for the life of the system, so
        /// consecutive draws land on unrelated slots instead of every consumer of one particle
        /// reading the same one.
        /// </summary>
        private int queryCount;

        /// <summary>
        /// Gives the system a fresh identity, as a newly created one would have. The engine has no
        /// equivalent: a restart there keeps the seed, and only a new collection draws another.
        /// </summary>
        public void Reseed()
        {
            Seed = System.Random.Shared.Next() & 0xFFF;
            queryCount = 0;
        }

        /// <summary>Takes the next value from the table, in [0, 1).</summary>
        public float Next() => At(queryCount++ + Seed);

        /// <summary>
        /// Takes the next value, interpolated into [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        public float NextBetween(float min, float max) => float.Lerp(min, max, Next());

        /// <summary>
        /// Takes the next value, interpolating every component of the result from that one value.
        /// </summary>
        public Vector3 NextBetween(Vector3 min, Vector3 max) => Vector3.Lerp(min, max, Next());

        /// <summary>
        /// Takes the next value, raised to <paramref name="exponent"/> before being interpolated into
        /// [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        public float NextWithExponentBetween(float exponent, float min, float max)
            => float.Lerp(min, max, MathF.Pow(Next(), exponent));

        /// <summary>Takes three consecutive values, one per component.</summary>
        public Vector3 NextBetweenPerComponent(Vector3 min, Vector3 max)
            => new(NextBetween(min.X, max.X), NextBetween(min.Y, max.Y), NextBetween(min.Z, max.Z));

        /// <summary>
        /// Reads the slot fixed by the particle, so the value is the same on every frame.
        /// <paramref name="fieldOffset"/> separates the draws one operator makes for different fields
        /// of the same particle.
        /// </summary>
        public float ForParticle(int particleId, int fieldOffset = 0)
            => At(Seed + OperatorOffset + fieldOffset + particleId);

        /// <inheritdoc cref="ForParticle(int, int)"/>
        public float ForParticleBetween(int particleId, int fieldOffset, float min, float max)
            => float.Lerp(min, max, ForParticle(particleId, fieldOffset));

        /// <inheritdoc cref="ForParticle(int, int)"/>
        public Vector3 ForParticleBetween(int particleId, int fieldOffset, Vector3 min, Vector3 max)
            => Vector3.Lerp(min, max, ForParticle(particleId, fieldOffset));

        /// <inheritdoc cref="ForParticle(int, int)"/>
        public float ForParticleWithExponentBetween(int particleId, int fieldOffset, float exponent, float min, float max)
            => float.Lerp(min, max, MathF.Pow(ForParticle(particleId, fieldOffset), exponent));

        /// <summary>
        /// Reads the slot a caller has already worked out in full, for the draws that key on neither
        /// the running counter nor the particle id.
        /// </summary>
        public static float ForSample(int sampleId) => At(sampleId);

        /// <inheritdoc cref="ForSample(int)"/>
        public static float ForSampleBetween(int sampleId, float min, float max)
            => float.Lerp(min, max, At(sampleId));

        /// <inheritdoc cref="ForSample(int)"/>
        public static Vector3 ForSampleBetween(int sampleId, Vector3 min, Vector3 max)
            => Vector3.Lerp(min, max, At(sampleId));

        /// <summary>Reads three consecutive slots from <paramref name="sampleId"/>, one per component.</summary>
        public static Vector3 ForSampleBetweenPerComponent(int sampleId, Vector3 min, Vector3 max)
            => new(
                ForSampleBetween(sampleId, min.X, max.X),
                ForSampleBetween(sampleId + 1, min.Y, max.Y),
                ForSampleBetween(sampleId + 2, min.Z, max.Z));

        /// <inheritdoc cref="ForSample(int)"/>
        public static float ForSampleWithExponentBetween(int sampleId, float exponent, float min, float max)
            => float.Lerp(min, max, MathF.Pow(At(sampleId), exponent));

        // Unsigned modulo keeps the index valid for any id, including ids that wrapped negative
        private static float At(int index) => RandomFloats.List[(uint)index % RandomFloats.List.Length];
    }
}
