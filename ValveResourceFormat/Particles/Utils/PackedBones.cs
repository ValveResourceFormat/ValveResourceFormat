namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Four bone indices of 16 bits each packed into a <see cref="ulong"/> from the low bits up, as
    /// <see cref="ParticleField.BoneIndices"/> holds them.
    /// </summary>
    internal static class PackedBones
    {
        /// <summary>How many bones fit.</summary>
        public const int Count = 4;

        /// <summary>The index of a slot that holds no bone.</summary>
        public const int None = 0xFFFF;

        public const ulong AllNone = ulong.MaxValue;

        public static int Get(ulong bones, int slot) => (int)((bones >> (16 * slot)) & None);

        /// <summary><paramref name="bone"/> placed in <paramref name="slot"/>, for combining with the other slots.</summary>
        public static ulong Pack(int bone, int slot) => (ulong)(ushort)bone << (16 * slot);
    }
}
