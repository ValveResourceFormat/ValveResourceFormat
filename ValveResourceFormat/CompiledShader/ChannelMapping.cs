using System.Linq;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Channel mapping definition used in shaders.
    /// </summary>
    public class ChannelMapping : IEquatable<ChannelMapping>
    {
        /// <summary>
        /// Channel constants.
        /// </summary>
        public readonly struct Channel
        {
            /// <summary>Red channel.</summary>
            public const byte R = 0x00;
            /// <summary>Green channel.</summary>
            public const byte G = 0x01;
            /// <summary>Blue channel.</summary>
            public const byte B = 0x02;
            /// <summary>Alpha channel.</summary>
            public const byte A = 0x03;
            /// <summary>Null channel.</summary>
            public const byte NULL = 0xFF;
        }

        /// <summary>Red channel mapping.</summary>
        public static readonly ChannelMapping R = FromChannels(Channel.R); // new(0xFFFFFF00);
        /// <summary>Green channel mapping.</summary>
        public static readonly ChannelMapping G = FromChannels(Channel.G); // new(0xFFFFFF01);
        /// <summary>Blue channel mapping.</summary>
        public static readonly ChannelMapping B = FromChannels(Channel.B); // new(0xFFFFFF02);
        /// <summary>Alpha channel mapping.</summary>
        public static readonly ChannelMapping A = FromChannels(Channel.A); // new(0xFFFFFF03);
        /// <summary>Red-green channel mapping.</summary>
        public static readonly ChannelMapping RG = FromChannels(R, G); // new(0xFFFF0100);
        /// <summary>Alpha-green channel mapping.</summary>
        public static readonly ChannelMapping AG = FromChannels(A, G); // new(0xFFFF0103);
        /// <summary>RGB channel mapping.</summary>
        public static readonly ChannelMapping RGB = FromChannels(R, G, B); // new(0xFF020100);
        /// <summary>RGBA channel mapping.</summary>
        public static readonly ChannelMapping RGBA = FromChannels(R, G, B, A); // new(0x03020100);
        /// <summary>Null channel mapping.</summary>
        public static readonly ChannelMapping NULL = FromChannels(Channel.NULL); // new(0xFFFFFFFF);

        /// <summary>Maximum number of channels.</summary>
        public const int MaxChannels = 4;
        private readonly byte[] _channels = new byte[MaxChannels];
        private readonly byte[] _destinations = new byte[MaxChannels];

        /// <summary>Gets all channel bytes.</summary>
        public IReadOnlyList<byte> Channels => _channels;
        /// <summary>Gets valid channel bytes.</summary>
        public IReadOnlyList<byte> ValidChannels => _channels[..Count];

        /// <summary>Gets the output channel each mapped source channel is written to.</summary>
        public IReadOnlyList<byte> Destinations => _destinations[..Count];

        /// <summary>Gets the packed uint value.</summary>
        public uint PackedValue { get; private init; }
        /// <summary>Gets the number of valid channels.</summary>
        public int Count { get; private init; }

        private ChannelMapping(uint packedValue, bool packedDestinations)
        {
            PackedValue = packedValue;

            for (var i = 0; i < MaxChannels; i++)
            {
                var component = GetPackedValueComponent(packedValue, i);
                if (component == Channel.NULL)
                {
                    break;
                }

                var channel = packedDestinations ? (byte)(component >> 4) : component;
                var destination = packedDestinations ? (byte)(component & 0x0F) : (byte)i;

                if (channel > Channel.A || destination >= MaxChannels)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(packedValue),
                        $"Packed value contains byte outside of range [0x00, 0x03] + 0xFF: 0x{component:X2} at index {i} (0x{packedValue:X8})."
                    );
                }

                _channels[i] = channel;
                _destinations[i] = destination;
                Count++;
            }
        }

        /// <summary>
        /// Converts from uint to <see cref="ChannelMapping"/>.
        /// </summary>
        public static explicit operator ChannelMapping(uint value)
            => FromUInt32(value);

        /// <summary>
        /// Creates a <see cref="ChannelMapping"/> from a packed uint value.
        /// </summary>
        /// <param name="packedValue">One byte per mapped channel, terminated by <see cref="Channel.NULL"/>.</param>
        /// <param name="packedDestinations">
        /// Whether each byte holds the source channel in the high nibble and the destination channel in the low nibble,
        /// which is how vcs version 67 and newer store the mapping. Older files store just the source channel,
        /// and the destination is the position of the byte.
        /// </param>
        public static ChannelMapping FromUInt32(uint packedValue, bool packedDestinations = false)
            => new(packedValue, packedDestinations);

        /// <summary>
        /// Gets a component byte from a packed value.
        /// </summary>
        public static byte GetPackedValueComponent(uint packedValue, int index)
            => (byte)(packedValue >> (index * 8) & 0xff);

        /// <summary>
        /// Returns the first mapped channel component.
        /// </summary>
        public static implicit operator byte(ChannelMapping channelMapping)
            => ToByte(channelMapping);

        /// <summary>
        /// Returns the first mapped channel component.
        /// </summary>
        public static byte ToByte(ChannelMapping channelMapping)
            => channelMapping.Channels[0];

        /// <summary>
        /// Returns the first mapped channel component.
        /// </summary>
        public static byte ToComponent(ChannelMapping channelMapping)
            => channelMapping.Channels[0];

        /// <summary>
        /// Creates a <see cref="ChannelMapping"/> from channel bytes, filling missing slots with <see cref="Channel.NULL"/>.
        /// Each channel is written to the output channel of the same position.
        /// </summary>
        public static ChannelMapping FromChannels(byte first, byte second = Channel.NULL, byte third = Channel.NULL, byte fourth = Channel.NULL)
        {
            var packedValue = (uint)0x0;
            packedValue ^= first;
            packedValue ^= (uint)second << 8;
            packedValue ^= (uint)third << 16;
            packedValue ^= (uint)fourth << 24;
            return (ChannelMapping)packedValue;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the channel letters (e.g., "RGBA", "RG", "A") or a hexadecimal representation if no channels are mapped.
        /// </remarks>
        public override string ToString()
        {
            Span<char> chars = stackalloc char[Count];
            for (var i = 0; i < Count; i++)
            {
                chars[i] = Channels[i] switch
                {
                    Channel.R => 'R',
                    Channel.G => 'G',
                    Channel.B => 'B',
                    Channel.A => 'A',
                    _ => 'X',
                };
            }

            if (Count == 0)
            {
                return $"0x{PackedValue:X8}";
            }

            return new string(chars);
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ChannelMapping left, ChannelMapping right)
            => left.Channels.SequenceEqual(right.Channels);

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ChannelMapping left, ChannelMapping right)
            => !left.Channels.SequenceEqual(right.Channels);

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => Equals(obj as ChannelMapping);

        /// <summary>
        /// Determines whether the specified <see cref="ChannelMapping"/> is equal to this instance.
        /// </summary>
        public bool Equals(ChannelMapping? other)
            => other is not null && this == other;

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine(PackedValue);
    }
}
