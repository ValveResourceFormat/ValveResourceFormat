using System.Runtime.Intrinsics;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The random generator some particle functions draw from instead of the shared random table: a
    /// lagged Fibonacci generator of four lanes, started from a fixed seed. Each system holds its own,
    /// so its values do not depend on what other systems drew. Its draws never move
    /// <see cref="ParticleRandom"/>.
    /// </summary>
    internal sealed class ThreadRandom
    {
        private const int Entries = 55;
        private const int Lanes = 4;

        private readonly float[] ring = new float[Entries * Lanes];
        private int lag = 23;
        private int head = Entries - 1;

        /// <summary>Creates a generator at the start of its sequence.</summary>
        public ThreadRandom()
        {
            var state = 0x12345678u;

            for (var i = 0; i < ring.Length; i++)
            {
                ring[i] = (state >> 16) / 65536f;
                state = (state + 1) * 0xBB40E62Du;
            }
        }

        /// <summary>Takes the next four values in [0, 1), one per lane.</summary>
        public Vector4 Next() => NextLanes().AsVector4();

        /// <summary>Takes the next four values, each interpolated into [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public Vector4 NextBetween(float min, float max) => (Next() * (max - min)) + new Vector4(min);

        /// <summary>
        /// Takes four points in the unit ball, one per lane: x, y and z values in [-1, 1), drawn again
        /// for every lane while any lane's point lies outside the ball, with only those lanes taking the
        /// new values. The first test lets a point on the surface through, later ones do not.
        /// </summary>
        public void NextInUnitBall(Span<Vector3> points)
        {
            var x = NextSigned();
            var y = NextSigned();
            var z = NextSigned();
            var outside = Vector128.GreaterThan(LengthsSquared(x, y, z), Vector128<float>.One);

            while (outside.ExtractMostSignificantBits() != 0)
            {
                x = Vector128.ConditionalSelect(outside, NextSigned(), x);
                y = Vector128.ConditionalSelect(outside, NextSigned(), y);
                z = Vector128.ConditionalSelect(outside, NextSigned(), z);
                outside = Vector128.GreaterThanOrEqual(LengthsSquared(x, y, z), Vector128<float>.One);
            }

            for (var lane = 0; lane < Lanes; lane++)
            {
                points[lane] = new Vector3(x.GetElement(lane), y.GetElement(lane), z.GetElement(lane));
            }
        }

        private Vector128<float> NextLanes()
        {
            var headRow = ring.AsSpan(head * Lanes, Lanes);
            var value = Vector128.Create(ring.AsSpan(lag * Lanes, Lanes)) + Vector128.Create(headRow);
            var wrapped = Vector128.GreaterThanOrEqual(value, Vector128<float>.One);

            value = Vector128.ConditionalSelect(wrapped, value - Vector128<float>.One, value);
            value.CopyTo(headRow);

            lag = lag == 0 ? Entries - 1 : lag - 1;
            head = head == 0 ? Entries - 1 : head - 1;

            return value;
        }

        private Vector128<float> NextSigned() => (NextLanes() * 2f) - Vector128<float>.One;

        private static Vector128<float> LengthsSquared(Vector128<float> x, Vector128<float> y, Vector128<float> z)
            => (z * z) + ((y * y) + (x * x));
    }
}
