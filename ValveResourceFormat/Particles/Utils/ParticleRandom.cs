namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// One particle system's access to the shared random table. Every random value in the
    /// particle system comes from here, and nothing else reads <see cref="RandomFloats"/>.
    ///
    /// <para>There are three ways in, and which one a draw uses decides whether the value changes
    /// between frames:</para>
    /// <list type="bullet">
    /// <item><c>Next...</c> advances a counter that runs free for the life of the system, so
    /// consecutive draws land on unrelated slots. Every initializer draws this way.</item>
    /// <item><c>ForParticle...</c> reads a slot fixed by the particle, so the value is the same on
    /// every frame of that particle's life. Used by the operators that must not flicker, and by
    /// constant random inputs with their <see cref="InputSeed"/> as the displacement.</item>
    /// <item><c>ForSample...</c> reads a slot the caller worked out in full. These are static, because
    /// such a draw ignores this system's seed and counter entirely.</item>
    /// </list>
    /// </summary>
    internal class ParticleRandom
    {
        /// <summary>
        /// The step by which <see cref="OperatorOffset"/> advances between operators.
        /// </summary>
        private const int OperatorStride = 17;

        /// <summary>
        /// The offset into the shared table that every deterministic draw by this system is displaced
        /// by, giving each system instance its own sequence. Child systems hold their own value rather
        /// than reading the root's.
        /// </summary>
        public int Seed { get; private set; }

        /// <summary>
        /// Displaces the per-particle draws of the operator currently running, so two operators
        /// reading the same particle do not read the same slot. Reset before each operator list by
        /// <see cref="ResetOperatorOffset"/> and advanced once per operator by <see cref="AdvanceOperatorOffset"/>.
        /// </summary>
        public int OperatorOffset { get; private set; }

        /// <summary>Puts <see cref="OperatorOffset"/> back to 0 and starts a new function call.</summary>
        public void ResetOperatorOffset()
        {
            OperatorOffset = 0;
            BeginFunctionCall();
        }

        /// <summary>Advances <see cref="OperatorOffset"/> to the next operator and starts a new function call.</summary>
        public void AdvanceOperatorOffset()
        {
            OperatorOffset += OperatorStride;
            BeginFunctionCall();
        }

        /// <summary>
        /// Counts every draw this system has taken. It runs free for the life of the system, so
        /// consecutive draws land on unrelated slots instead of every consumer of one particle
        /// reading the same one.
        /// </summary>
        private int queryCount;

        private int functionCall;

        private int inputSeedGeneration;

        /// <summary>Takes <paramref name="seed"/> and starts the draws over from <paramref name="counter"/>.</summary>
        public void Start(int seed, int counter = 0)
        {
            Seed = seed & 0xFFF;
            queryCount = counter;
            RedrawInputSeeds();
            BeginFunctionCall();
        }

        /// <summary>
        /// Makes every <see cref="InputSeed"/> of this system draw a new value from the counter at its
        /// next use, as a restart does.
        /// </summary>
        public void RedrawInputSeeds() => inputSeedGeneration++;

        /// <summary>
        /// Marks the start of a function call. Inputs are evaluated once per call: a varying random input
        /// draws once per block of four particles within a call, and once per call at collection level.
        /// </summary>
        public void BeginFunctionCall() => functionCall++;

        /// <summary>
        /// Identifies one evaluation of an input within the current function call, for the block of four
        /// particles holding <paramref name="particleIndex"/>, or for the whole collection when it is negative.
        /// </summary>
        public EvaluationKey Evaluation(int particleIndex)
            => new(this, functionCall, particleIndex < 0 ? -1 : particleIndex >> 2);

        /// <summary>Takes the next value from the table, in [0, 1).</summary>
        public float Next() => At(queryCount++ + Seed);

        /// <summary>Takes the next value as an index in [0, <paramref name="count"/>).</summary>
        public int NextIndex(int count) => (int)(count * Next());

        /// <summary>
        /// Takes the next value from the shared counter, or, for a non-zero <paramref name="randomSeed"/>,
        /// from a sequence of its own that <paramref name="counter"/> walks, leaving the shared counter untouched.
        /// </summary>
        /// <param name="randomSeed">The function's authored seed, 0 for the shared counter.</param>
        /// <param name="counter">The function's own position in its seeded sequence, advanced by a seeded draw.</param>
        public float NextOrSeeded(int randomSeed, ref int counter)
            => randomSeed != 0 ? At(Seed + randomSeed + counter++) : Next();

        /// <summary>Takes the next value from the slot the seed displaces twice instead of once.</summary>
        public float NextSeededTwice() => At(queryCount++ + Seed + Seed);

        /// <summary>
        /// Takes <paramref name="count"/> consecutive draws at once and returns the counter of the first,
        /// for a caller that reads them out of order through <see cref="Drawn"/>.
        /// </summary>
        public int Reserve(int count)
        {
            var first = queryCount;
            queryCount += count;
            return first;
        }

        /// <summary>The value of the draw taken at <paramref name="counter"/>.</summary>
        public float Drawn(int counter) => At(counter + Seed);

        /// <summary>
        /// Takes the next value, remapped clear of both ends of the interval. Direction initializers
        /// use it so that a drawn polar cosine is never exactly +/-1.
        /// </summary>
        private float NextInterior() => (Next() * 0.9999f) + 0.0001f;

        /// <summary>
        /// Takes three consecutive values as a point drawn uniformly through the unit ball, in the
        /// order polar cosine, azimuth, then <paramref name="radiusFraction"/>. The returned point is
        /// already scaled by that fraction.
        /// </summary>
        public Vector3 NextInUnitBall(out float radiusFraction)
        {
            var interior = NextInterior();
            var cosPolar = interior + interior - 1f;
            var azimuth = NextInterior() * MathF.Tau;
            radiusFraction = MathF.Pow(NextInterior(), 1f / 3f);

            var sinPolar = MathF.Sqrt(MathF.Max(0f, 1f - (cosPolar * cosPolar)));
            var (sin, cos) = MathF.SinCos(azimuth);
            var ring = sinPolar * radiusFraction;

            return new Vector3(ring * cos, ring * sin, radiusFraction * cosPolar);
        }

        /// <summary>
        /// Takes three values per attempt as a point in the cube from -1 to 1 until one lands inside
        /// the unit ball.
        /// </summary>
        public Vector3 NextInUnitBallByRejection()
        {
            Vector3 point;

            do
            {
                point = NextBetweenPerComponent(-Vector3.One, Vector3.One);
            }
            while (point.LengthSquared() >= 1f);

            return point;
        }

        /// <summary>
        /// Takes the next value, interpolated into [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        public float NextBetween(float min, float max) => MathUtils.Lerp(min, max, Next());

        /// <summary>
        /// Takes the next value, interpolating every component of the result from that one value.
        /// </summary>
        public Vector3 NextBetween(Vector3 min, Vector3 max) => MathUtils.Lerp(min, max, Next());

        /// <summary>
        /// Takes the next value, raised to <paramref name="exponent"/> before being interpolated into
        /// [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        public float NextWithExponentBetween(float exponent, float min, float max)
            => MathUtils.Lerp(min, max, MathF.Pow(Next(), exponent));

        /// <summary>Takes three consecutive values, one per component.</summary>
        public Vector3 NextBetweenPerComponent(Vector3 min, Vector3 max)
            => new(NextBetween(min.X, max.X), NextBetween(min.Y, max.Y), NextBetween(min.Z, max.Z));

        /// <summary>
        /// Takes three consecutive values from slots the seed displaces twice, one per component, as
        /// the range-filling initializers draw a point in a box.
        /// </summary>
        public Vector3 NextBetweenPerComponentSeededTwice(Vector3 min, Vector3 max)
            => new(
                MathUtils.Lerp(min.X, max.X, NextSeededTwice()),
                MathUtils.Lerp(min.Y, max.Y, NextSeededTwice()),
                MathUtils.Lerp(min.Z, max.Z, NextSeededTwice()));

        /// <summary>
        /// Reads the slot fixed by the particle, so the value is the same on every frame.
        /// <paramref name="fieldOffset"/> separates the draws one operator makes for different fields
        /// of the same particle.
        /// </summary>
        public float ForParticle(int particleId, int fieldOffset = 0)
        {
            ParticleIdRead = true;
            return At(Seed + OperatorOffset + fieldOffset + particleId);
        }

        /// <summary>Set by every draw keyed by a particle id, so the system can tell whether a function read the ids.</summary>
        public bool ParticleIdRead { get; set; }

        /// <inheritdoc cref="ForParticle(int, int)"/>
        public float ForParticleBetween(int particleId, int fieldOffset, float min, float max)
            => MathUtils.Lerp(min, max, ForParticle(particleId, fieldOffset));

        /// <inheritdoc cref="ForParticle(int, int)"/>
        public Vector3 ForParticleBetween(int particleId, int fieldOffset, Vector3 min, Vector3 max)
            => MathUtils.Lerp(min, max, ForParticle(particleId, fieldOffset));

        /// <inheritdoc cref="ForParticle(int, int)"/>
        public float ForParticleWithExponentBetween(int particleId, int fieldOffset, float exponent, float min, float max)
            => MathUtils.Lerp(min, max, MathF.Pow(ForParticle(particleId, fieldOffset), exponent));

        /// <summary>
        /// Reads the slot a caller has already worked out in full, for draws keyed on neither the
        /// counter nor the particle id.
        /// </summary>
        public static float ForSample(int sampleId) => At(sampleId);

        /// <inheritdoc cref="ForSample(int)"/>
        public static float ForSampleBetween(int sampleId, float min, float max)
            => MathUtils.Lerp(min, max, At(sampleId));

        /// <summary>Reads three consecutive slots from <paramref name="sampleId"/>, one per component.</summary>
        public static Vector3 ForSampleBetweenPerComponent(int sampleId, Vector3 min, Vector3 max)
            => new(
                ForSampleBetween(sampleId, min.X, max.X),
                ForSampleBetween(sampleId + 1, min.Y, max.Y),
                ForSampleBetween(sampleId + 2, min.Z, max.Z));

        // Unsigned modulo keeps the index valid for any id, including ids that wrapped negative
        private static float At(int index) => RandomFloats.List[(uint)index % RandomFloats.List.Length];

        /// <summary>See <see cref="Evaluation(int)"/>.</summary>
        /// <param name="Random">The system the input is evaluated for.</param>
        /// <param name="FunctionCall">The function call the evaluation belongs to.</param>
        /// <param name="Block">The block of four particles, or -1 for the whole collection.</param>
        internal readonly record struct EvaluationKey(ParticleRandom Random, int FunctionCall, int Block);

        /// <summary>
        /// The per-function seed of an input whose random value is constant per particle. It is drawn
        /// from the counter at first use, and again after <see cref="RedrawInputSeeds"/>, so inputs of
        /// different functions read unrelated slots for the same particle.
        /// </summary>
        internal sealed class InputSeed
        {
            private ParticleRandom? owner;
            private int generation;
            private int value;

            public int Get(ParticleRandom random)
            {
                if (owner != random || generation != random.inputSeedGeneration)
                {
                    owner = random;
                    generation = random.inputSeedGeneration;
                    value = (int)(random.Next() * int.MinValue);
                }

                return value;
            }
        }
    }
}
