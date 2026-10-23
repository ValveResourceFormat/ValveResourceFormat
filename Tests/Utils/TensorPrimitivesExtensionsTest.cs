using System.Numerics.Tensors;
using System.Threading.Tasks;
using ValveResourceFormat.Utils;

namespace Tests.Utils
{
    public class TensorPrimitivesExtensionsTest
    {
        // Odd length so both the vector loop and the scalar tail run
        private const int Length = 1037;

        [Test]
        public async Task ConvertToSingleFromShort()
        {
            var random = new Random(1);
            var source = new short[Length];
            source.AsSpan().Fill(short.MaxValue);
            source[0] = short.MinValue;

            for (var i = 2; i < source.Length; i++)
            {
                source[i] = (short)random.Next(short.MinValue, short.MaxValue + 1);
            }

            var destination = new float[Length];
            TensorPrimitives.ConvertToSingle(source, destination);

            for (var i = 0; i < source.Length; i++)
            {
                await Assert.That(destination[i]).IsEqualTo((float)source[i]);
            }
        }

        [Test]
        public async Task ConvertToSingleFromUShort()
        {
            var random = new Random(2);
            var source = new ushort[Length];
            source[0] = ushort.MaxValue;

            for (var i = 1; i < source.Length; i++)
            {
                source[i] = (ushort)random.Next(ushort.MaxValue + 1);
            }

            var destination = new float[Length];
            TensorPrimitives.ConvertToSingle(source, destination);

            for (var i = 0; i < source.Length; i++)
            {
                await Assert.That(destination[i]).IsEqualTo((float)source[i]);
            }
        }

        [Test]
        public async Task ConvertToInt32FromSByte()
        {
            var random = new Random(3);
            var source = new sbyte[Length];
            source[0] = sbyte.MinValue;
            source[1] = sbyte.MaxValue;

            for (var i = 2; i < source.Length; i++)
            {
                source[i] = (sbyte)random.Next(sbyte.MinValue, sbyte.MaxValue + 1);
            }

            var destination = new int[Length];
            TensorPrimitives.ConvertToInt32(source, destination);

            for (var i = 0; i < source.Length; i++)
            {
                await Assert.That(destination[i]).IsEqualTo(source[i]);
            }
        }

        [Test]
        public async Task ConvertToUInt16FromInt()
        {
            var random = new Random(4);
            var source = new int[Length];
            source[0] = int.MinValue;
            source[1] = -1;

            for (var i = 2; i < source.Length; i++)
            {
                source[i] = random.Next(int.MinValue, int.MaxValue);
            }

            var destination = new ushort[Length];
            TensorPrimitives.ConvertToUInt16(source, destination);

            for (var i = 0; i < source.Length; i++)
            {
                await Assert.That(destination[i]).IsEqualTo((ushort)source[i]);
            }
        }

        [Test]
        public async Task ConvertToClampedUnorm8()
        {
            var random = new Random(5);
            float[] special = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0f, 0f, 1f, -1f, 0.5f / 255f, 1.5f / 255f, 254.5f / 255f, float.Epsilon];
            var source = new float[Length];
            special.CopyTo(source, 0);

            for (var i = special.Length; i < source.Length; i++)
            {
                source[i] = (random.NextSingle() * 1.5f) - 0.25f;
            }

            var destination = new byte[Length];
            TensorPrimitives.ConvertToClampedUnorm8(source, destination);

            for (var i = 0; i < source.Length; i++)
            {
                await Assert.That(destination[i]).IsEqualTo((byte)((MathUtils.Saturate(source[i]) * 255) + 0.5f));
            }
        }
    }
}
