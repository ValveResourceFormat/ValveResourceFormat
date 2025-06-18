using System.Linq;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Channel mapping definition used in shaders.
    /// </summary>
    public class ChannelMapping : IEquatable<ChannelMapping>
    {
        public readonly struct Channel
        {
            public const byte R = 0x00;
            public const byte G = 0x01;
            public const byte B = 0x02;
            public const byte A = 0x03;
            public const byte NULL = 0xFF;
        }

        public static readonly ChannelMapping R = FromChannels(Channel.R); // new(0xFFFFFF00);
        public static readonly ChannelMapping G = FromChannels(Channel.G); // new(0xFFFFFF01);
        public static readonly ChannelMapping B = FromChannels(Channel.B); // new(0xFFFFFF02);
        public static readonly ChannelMapping A = FromChannels(Channel.A); // new(0xFFFFFF03);
        public static readonly ChannelMapping RG = FromChannels(R, G); // new(0xFFFF0100);
        public static readonly ChannelMapping AG = FromChannels(A, G); // new(0xFFFF0103);
        public static readonly ChannelMapping RGB = FromChannels(R, G, B); // new(0xFF020100);
        public static readonly ChannelMapping RGBA = FromChannels(R, G, B, A); // new(0x03020100);
        public static readonly ChannelMapping NULL = FromChannels(Channel.NULL); // new(0xFFFFFFFF);

        public const int MaxChannels = 4;
        private readonly byte[] _channels = new byte[MaxChannels];
        private readonly byte[] _indices = new byte[MaxChannels];

        public IReadOnlyList<byte> Channels => _channels;
        public IReadOnlyList<byte> ValidChannels => _channels[..Count];

        public IReadOnlyList<byte> Indices => _indices[..Count];

        public uint PackedValue { get; private init; }
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

        public static explicit operator ChannelMapping(uint value)
            => FromUInt32(value);

        public static ChannelMapping FromUInt32(uint packedValue)
            => new(packedValue);

        public static byte GetPackedValueComponent(uint packedValue, int index)
            => (byte)(packedValue >> (index * 8) & 0xff);

        public static implicit operator byte(ChannelMapping channelMapping)
            => ToByte(channelMapping);

        public static byte ToByte(ChannelMapping channelMapping)
            => channelMapping.Channels[0];

        public static byte ToComponent(ChannelMapping channelMapping)
            => channelMapping.Channels[0];

        public static ChannelMapping FromChannels(byte first, byte second = Channel.NULL, byte third = Channel.NULL, byte fourth = Channel.NULL)
        {
            var packedValue = (uint)0x0;
            packedValue ^= first;
            packedValue ^= (uint)second << 8;
            packedValue ^= (uint)third << 16;
            packedValue ^= (uint)fourth << 24;
            return (ChannelMapping)packedValue;
        }

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

        public static bool operator ==(ChannelMapping left, ChannelMapping right)
            => left.Channels.SequenceEqual(right.Channels);

        public static bool operator !=(ChannelMapping left, ChannelMapping right)
            => !left.Channels.SequenceEqual(right.Channels);

        public override bool Equals(object? obj)
            => Equals(obj as ChannelMapping);

        public bool Equals(ChannelMapping? other)
            => other is not null && this == other;

        public override int GetHashCode()
            => HashCode.Combine(PackedValue);
    }
}
