namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Seeded uniform random number generator (Park-Miller with a Bays-Durham shuffle).
    /// </summary>
    public sealed class UniformRandomStream
    {
        private const int Multiplier = 16807;
        private const int Modulus = int.MaxValue;
        private const int Quotient = 127773;
        private const int Remainder = 2836;
        private const int TableSize = 32;
        private const int TableDivisor = 1 + (Modulus - 1) / TableSize;
        private const uint MaxRandomRange = int.MaxValue;
        private const float Scale = 1f / Modulus;
        private const float MaxFraction = 1f - 1.2e-7f;

        private int state;
        private int previous;
        private readonly int[] table = new int[TableSize];

        /// <summary>
        /// Initializes a new instance of the <see cref="UniformRandomStream"/> class with a seed of zero.
        /// </summary>
        public UniformRandomStream()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UniformRandomStream"/> class.
        /// </summary>
        /// <param name="seed">Seed for the sequence.</param>
        public UniformRandomStream(int seed)
        {
            SetSeed(seed);
        }

        /// <summary>
        /// Restarts the sequence from a seed.
        /// </summary>
        /// <param name="seed">Seed for the sequence.</param>
        public void SetSeed(int seed)
        {
            state = seed < 0 ? seed : -seed;
            previous = 0;
        }

        /// <summary>
        /// Returns the next float in a range.
        /// </summary>
        /// <param name="low">Lower bound, inclusive.</param>
        /// <param name="high">Upper bound, exclusive.</param>
        public float RandomFloat(float low = 0f, float high = 1f)
        {
            var fraction = MathF.Min(Scale * Next(), MaxFraction);

            return fraction * (high - low) + low;
        }

        /// <summary>
        /// Returns the next integer in a range, or <paramref name="low"/> if the range is empty.
        /// </summary>
        /// <param name="low">Lower bound, inclusive.</param>
        /// <param name="high">Upper bound, inclusive.</param>
        public int RandomInt(int low, int high)
        {
            var range = unchecked((uint)(high - low + 1));

            if (range <= 1 || MaxRandomRange < range - 1)
            {
                return low;
            }

            // Numbers past the last whole multiple of the range are redrawn so every result is equally likely
            var maxAcceptable = MaxRandomRange - (MaxRandomRange + 1) % range;
            uint number;

            do
            {
                number = (uint)Next();
            }
            while (number > maxAcceptable);

            return unchecked(low + (int)(number % range));
        }

        private int Next()
        {
            if (state <= 0 || previous == 0)
            {
                state = -state < 1 ? 1 : -state;

                for (var i = TableSize + 7; i >= 0; i--)
                {
                    Advance();

                    if (i < TableSize)
                    {
                        table[i] = state;
                    }
                }

                previous = table[0];
            }

            Advance();

            var slot = previous / TableDivisor;
            previous = table[slot];
            table[slot] = state;

            return previous;
        }

        // Schrage's method: state * Multiplier % Modulus without overflowing 32 bits
        private void Advance()
        {
            var k = state / Quotient;
            state = Multiplier * (state - k * Quotient) - Remainder * k;

            if (state < 0)
            {
                state += Modulus;
            }
        }
    }
}
