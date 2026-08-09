using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// Picks a point in a min/max vector range for a particle, configured by the
    /// <c>CRandomNumberGeneratorParameters</c> block the six range-filling initializers carry. The
    /// default is an ordinary draw from the system's shared random table. Asked for an even
    /// distribution it instead walks a stratified low-discrepancy sequence keyed to the particle's
    /// spawn ordinal, spreading the particles out rather than letting them clump, and takes nothing
    /// from the shared table.
    /// </summary>
    readonly struct RangeSampler
    {
        private readonly bool distributeEvenly;
        private readonly int seed;

        private RangeSampler(bool distributeEvenly, int seed)
        {
            this.distributeEvenly = distributeEvenly;
            this.seed = seed;
        }

        /// <summary>
        /// Reads a particle function's <c>m_randomnessParameters</c> block, falling back to an ordinary
        /// random table draw when the block is absent.
        /// </summary>
        public static RangeSampler Parse(ParticleDefinitionParser parse)
        {
            var block = parse.Data.GetSubCollection("m_randomnessParameters");

            if (block == null)
            {
                return default;
            }

            var parameters = parse.Nested(block);

            return new RangeSampler(parameters.Boolean("m_bDistributeEvenly"), parameters.Int32("m_nSeed"));
        }

        /// <summary>
        /// Interpolates per component between <paramref name="min"/> and <paramref name="max"/>, either from
        /// three consecutive values off the system's shared random table or, when an even distribution is
        /// asked for, from a stratified sample that takes nothing from that table. A negative authored seed
        /// is displaced by the system's own seed, giving each instance of an effect its own sequence.
        /// </summary>
        public Vector3 NextVectorBetween(ref Particle particle, ParticleSystemRenderState particleSystemState, Vector3 min, Vector3 max)
        {
            if (!distributeEvenly)
            {
                return particleSystemState.Random.NextBetweenPerComponent(min, max);
            }

            var start = seed < 0 ? seed + particleSystemState.Random.Seed : seed;
            var index = (start + particle.UniqueParticleId) & 0x7FFFFFFF;

            var fractions = new Vector3(
                RadicalInverse(index, 2),
                RadicalInverse(index, 3),
                RadicalInverse(index, 5));

            return min + ((max - min) * fractions);
        }

        /// <summary>
        /// The radical inverse of <paramref name="index"/> in <paramref name="radix"/>: the digits of the
        /// index in that base, mirrored about the radix point, giving a value in [0, 1).
        /// </summary>
        private static float RadicalInverse(int index, int radix)
        {
            var inverseRadix = 1f / radix;
            var digitScale = 1f;
            var result = 0f;

            do
            {
                digitScale *= inverseRadix;
                result += (index % radix) * digitScale;
                index /= radix;
            }
            while (index > 0);

            return result;
        }
    }
}
