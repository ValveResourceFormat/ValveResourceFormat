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
        private readonly byte[] _indices = new byte[MaxChannels];

        /// <summary>Gets all channel bytes.</summary>
        public IReadOnlyList<byte> Channels => _channels;
        /// <summary>Gets valid channel bytes.</summary>
        public IReadOnlyList<byte> ValidChannels => _channels[..Count];

        /// <summary>Gets channel indices.</summary>
        public IReadOnlyList<byte> Indices => _indices[..Count];

        /// <summary>Gets the packed uint value.</summary>
        public uint PackedValue { get; private init; }
        /// <summary>Gets the number of valid channels.</summary>
        public int Count { get; private init; }

        private ChannelMapping(uint packedValue)
        {
            PackedValue = packedValue;

            for (var i = 0; i < MaxChannels; i++)
            {
                var component = GetPackedValueComponent(packedValue, i);
                if (component == Channel.NULL)
                {
                    break;
                }

                // Vcs version 67 adds an index for unknown reasons
                var componentWithoutIndex = (byte)((component & 0xF0) >> 4);
                var index = (byte)(component & 0x0F);
                if (componentWithoutIndex > 0)
                {
                    component = componentWithoutIndex;
                    _indices[i] = index;
                }

                if (component >= Channel.R && component <= Channel.A)
                {
                    _channels[i] = component;
                    Count++;
                    continue;
                }

                throw new ArgumentOutOfRangeException(
                    nameof(packedValue),
                    $"Packed value contains byte outside of range [0x00, 0x03] + 0xFF: 0x{component:X2} at index {i} (0x{packedValue:X8})."
                );
            }
        }

        /// <summary>
        /// Converts from uint to ChannelMapping.
        /// </summary>
        public static explicit operator ChannelMapping(uint value)
            => FromUInt32(value);

        /// <summary>
        /// Creates a ChannelMapping from a packed uint value.
        /// </summary>
        public static ChannelMapping FromUInt32(uint packedValue)
            => new(packedValue);

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
        /// Creates a ChannelMapping from channel bytes, filling missing slots with <see cref="Channel.NULL"/>.
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
        /// Determines whether the specified ChannelMapping is equal to this instance.
        /// </summary>
        public bool Equals(ChannelMapping? other)
            => other is not null && this == other;

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine(PackedValue);
    }
}
