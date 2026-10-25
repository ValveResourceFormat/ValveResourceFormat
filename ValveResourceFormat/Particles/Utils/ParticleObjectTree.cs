using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Finds the pairs of particles whose boxes overlap, in the order an object tree over their boxes
    /// visits them. Each particle's box, its position grown by its radius plus a padding, is quantised to
    /// signed bytes within the system's bounds, and the particles are sorted into slots along a Morton
    /// curve through the box centres. Pairs come out by groups of 32 slots, then by slot.
    /// </summary>
    /// <remarks>
    /// A particle outside the bounds wraps around in the byte range, and without bounds every box is the same point, so every pair overlaps and the slots keep the
    /// particle order.
    /// </remarks>
    internal sealed class ParticleObjectTree
    {
        private const int GroupSize = 32;

        // Half of 255, the fraction of the range that maps to zero
        private const float OriginFraction = 0.50196081f;

        private int count;
        private int[] boxes = [];
        private long[] keys = [];
        private int[] slotToParticle = [];

        /// <summary>Gets the overlapping pairs as slot indices, the lower slot first, in visiting order.</summary>
        public List<(int First, int Second)> Pairs { get; } = [];

        /// <summary>Gets the particle each slot holds.</summary>
        public ReadOnlySpan<int> SlotToParticle => slotToParticle.AsSpan(0, count);

        /// <summary>
        /// Sorts the particles into slots and collects the overlapping <see cref="Pairs"/>.
        /// </summary>
        /// <param name="positions">The particle positions.</param>
        /// <param name="radii">The particle radii.</param>
        /// <param name="padding">How far every box reaches past the particle's radius.</param>
        /// <param name="bounds">The system's bounds from its last update, or null when it has none.</param>
        public void Build(ReadOnlySpan<Vector3> positions, ReadOnlySpan<float> radii, float padding, AABB? bounds)
        {
            count = positions.Length;
            Pairs.Clear();

            if (boxes.Length < count * 6)
            {
                boxes = new int[count * 6];
                keys = new long[count];
                slotToParticle = new int[count];
            }

            var pad = new Vector3(padding);
            var low = (bounds?.Min ?? new Vector3(float.MaxValue)) - pad - Vector3.One;
            var high = (bounds?.Max ?? new Vector3(-float.MaxValue)) + pad + Vector3.One;
            var range = high - low;
            var scale = new Vector3(255f) / range;
            var origin = (range * OriginFraction) + low;

            Span<int> particleBoxes = count * 6 <= 1024 ? stackalloc int[count * 6] : new int[count * 6];

            for (var i = 0; i < count; i++)
            {
                var reach = new Vector3(radii[i] + padding);
                var min = ((positions[i] - reach) - origin) * scale;
                var max = ((positions[i] + reach) - origin) * scale;

                for (var axis = 0; axis < 3; axis++)
                {
                    particleBoxes[(i * 6) + axis] = ToSignedByte(MathF.Floor(min[axis]));
                    particleBoxes[(i * 6) + 3 + axis] = ToSignedByte(MathF.Ceiling(max[axis]));
                }

                keys[i] = ((long)MortonCode(particleBoxes.Slice(i * 6, 6)) << 32) | (uint)i;
            }

            if (count > 1)
            {
                Array.Sort(keys, 0, count);
            }

            for (var slot = 0; slot < count; slot++)
            {
                var particle = (int)(keys[slot] & 0xFFFFFFFF);
                slotToParticle[slot] = particle;
                particleBoxes.Slice(particle * 6, 6).CopyTo(boxes.AsSpan(slot * 6, 6));
            }

            CollectPairs();
        }

        private void CollectPairs()
        {
            var groupCount = (count + GroupSize - 1) / GroupSize;
            Span<int> groupBoxes = groupCount * 6 <= 1024 ? stackalloc int[groupCount * 6] : new int[groupCount * 6];

            for (var group = 0; group < groupCount; group++)
            {
                var groupBox = groupBoxes.Slice(group * 6, 6);
                groupBox[..3].Fill(sbyte.MaxValue);
                groupBox[3..].Fill(sbyte.MinValue);

                var end = Math.Min((group + 1) * GroupSize, count);

                for (var slot = group * GroupSize; slot < end; slot++)
                {
                    for (var axis = 0; axis < 3; axis++)
                    {
                        groupBox[axis] = Math.Min(groupBox[axis], boxes[(slot * 6) + axis]);
                        groupBox[3 + axis] = Math.Max(groupBox[3 + axis], boxes[(slot * 6) + 3 + axis]);
                    }
                }
            }

            for (var firstGroup = 0; firstGroup < groupCount; firstGroup++)
            {
                for (var secondGroup = firstGroup; secondGroup < groupCount; secondGroup++)
                {
                    if (!Overlaps(groupBoxes.Slice(firstGroup * 6, 6), groupBoxes.Slice(secondGroup * 6, 6)))
                    {
                        continue;
                    }

                    var firstEnd = Math.Min((firstGroup + 1) * GroupSize, count);
                    var secondEnd = Math.Min((secondGroup + 1) * GroupSize, count);

                    for (var first = firstGroup * GroupSize; first < firstEnd; first++)
                    {
                        var secondStart = firstGroup == secondGroup ? first + 1 : secondGroup * GroupSize;

                        for (var second = secondStart; second < secondEnd; second++)
                        {
                            if (Overlaps(boxes.AsSpan(first * 6, 6), boxes.AsSpan(second * 6, 6)))
                            {
                                Pairs.Add((first, second));
                            }
                        }
                    }
                }
            }
        }

        private static bool Overlaps(ReadOnlySpan<int> first, ReadOnlySpan<int> second)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                if (first[axis] > second[3 + axis] || second[axis] > first[3 + axis])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Truncates to an integer, NaN and out of range values giving <see cref="int.MinValue"/>,
        /// and keeps the low byte as a signed value.
        /// </summary>
        private static int ToSignedByte(float value)
        {
            var truncated = value is >= -2147483648f and < 2147483648f ? (int)value : int.MinValue;
            return (sbyte)truncated;
        }

        /// <summary>
        /// Interleaves the bits of the box centre, each axis biased to ten bits so that -128 sorts
        /// between -1 and 0.
        /// </summary>
        private static uint MortonCode(ReadOnlySpan<int> box)
        {
            var code = 0u;

            for (var axis = 0; axis < 3; axis++)
            {
                var centre = (box[axis] + box[3 + axis]) >> 1;
                var biased = (uint)((centre + 127) & 0xFF) | (centre >= 0 ? 0x300u : 0x200u);
                code |= SpreadBits(biased) << axis;
            }

            return code;
        }

        /// <summary>Spreads the low ten bits of <paramref name="value"/> to every third bit.</summary>
        private static uint SpreadBits(uint value)
        {
            value &= 0x3FF;
            value = (value | (value << 16)) & 0x30000FF;
            value = (value | (value << 8)) & 0x300F00F;
            value = (value | (value << 4)) & 0x30C30C3;
            value = (value | (value << 2)) & 0x9249249;
            return value;
        }
    }
}
