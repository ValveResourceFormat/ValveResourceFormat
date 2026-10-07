namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Source's <c>CUniformRandomStream</c>: the Park-Miller minimal standard generator with a
    /// 32-entry Bays-Durham shuffle (Numerical Recipes <c>ran1</c>), single precision float output.
    /// </summary>
    public sealed class UniformRandomStream
    {
        private const int IA = 16807;
        private const int IM = 2147483647;
        private const int IQ = 127773;
        private const int IR = 2836;
        private const int NTAB = 32;
        private const int NDIV = 1 + (IM - 1) / NTAB;
        private const float AM = 4.656612873077393e-10f;
        private const float RNMX = 0.99999988f;

        private readonly int[] iv = new int[NTAB];
        private int idum;
        private int iy;

        /// <summary>
        /// Initializes a new stream with the given seed.
        /// </summary>
        /// <param name="seed">Seed; <c>s</c> and <c>-s</c> give the same stream, and so do 0, 1 and -1.</param>
        public UniformRandomStream(int seed)
        {
            SetSeed(seed);
        }

        /// <summary>
        /// Restarts the stream from a seed.
        /// </summary>
        /// <param name="seed">Seed; <c>s</c> and <c>-s</c> give the same stream, and so do 0, 1 and -1.</param>
        public void SetSeed(int seed)
        {
            iy = 0;
            idum = seed > 0 ? -seed : seed;
        }

        private static int Step(int x)
        {
            var k = x / IQ;
            x = IA * (x - k * IQ) - IR * k;

            if (x < 0)
            {
                x += IM;
            }

            return x;
        }

        /// <summary>
        /// Draws the next raw value, in the range [1, 2^31 - 2].
        /// </summary>
        /// <returns>The next value of the stream.</returns>
        public int GenerateRandomNumber()
        {
            if (idum <= 0 || iy == 0)
            {
                var x = unchecked(-idum);

                if (x < 1)
                {
                    x = 1;
                }

                for (var j = NTAB + 7; j >= 0; j--)
                {
                    x = Step(x);

                    if (j < NTAB)
                    {
                        iv[j] = x;
                    }
                }

                idum = x;
                iy = iv[0];
            }

            idum = Step(idum);

            var index = iy / NDIV;
            iy = iv[index];
            iv[index] = idum;

            return iy;
        }

        /// <summary>
        /// Draws a float in [<paramref name="low"/>, <paramref name="high"/>).
        /// </summary>
        /// <param name="low">Lower bound.</param>
        /// <param name="high">Upper bound.</param>
        /// <returns>The drawn value.</returns>
        public float RandomFloat(float low = 0f, float high = 1f)
        {
            var fraction = MathF.Min(GenerateRandomNumber() * AM, RNMX);
            var range = high - low;
            var scaled = fraction * range;
            return scaled + low;
        }

        /// <summary>
        /// Draws an integer in [<paramref name="low"/>, <paramref name="high"/>], both inclusive.
        /// Returns <paramref name="low"/> without drawing when the range holds one value or is invalid.
        /// </summary>
        /// <param name="low">Lower bound.</param>
        /// <param name="high">Upper bound.</param>
        /// <returns>The drawn value.</returns>
        public int RandomInt(int low, int high)
        {
            var range = unchecked((uint)(high - low));
            var count = unchecked(range + 1);

            if (count <= 1 || range > 0x7FFFFFFF)
            {
                return low;
            }

            var maxAcceptable = 0x7FFFFFFFu - (0x80000000u % count);
            uint value;

            do
            {
                value = (uint)GenerateRandomNumber();
            }
            while (value > maxAcceptable);

            return unchecked(low + (int)(value % count));
        }
    }
}
