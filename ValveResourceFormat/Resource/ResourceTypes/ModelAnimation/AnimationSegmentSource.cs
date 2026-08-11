using System.Buffers;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Owns the decoders for one ANIM block, and the per-skeleton bindings onto them. Every
    /// <see cref="SequenceAnimation"/> in the block shares a single instance.
    /// </summary>
    internal sealed class AnimationSegmentSource
    {
        /// <summary>
        /// One segment's decoder, and which remap group it draws its bone mapping from.
        /// Contains no reference to any target skeleton.
        /// </summary>
        internal readonly struct Segment
        {
            /// <summary>The decoder, or <see langword="null"/> for an unsupported or unknown segment.</summary>
            public AnimationSegmentDecoder? Decoder { get; init; }

            /// <summary>Index into the block's remap groups, or -1 when <see cref="Decoder"/> is null.</summary>
            public int GroupIndex { get; init; }
        }

        /// <summary>
        /// A distinct (channel, element list) pair. Frame blocks of the same channel repeat the same
        /// element list, so many segments share one group and therefore one resolved remap.
        /// </summary>
        private readonly struct RemapGroup
        {
            public int ChannelIndex { get; init; }
            public ReadOnlyMemory<byte> Elements { get; init; }
        }

        private readonly KVObject animationData;
        private readonly KVObject decodeKey;

        private AnimationDataChannel[]? channels;
        private RemapGroup[]? groups;
        private Segment[]? segments;

        // Keyed by skeleton reference: Skeleton does not override equality, and a model exposes a single
        // instance, so reference identity is the intended key.
        private readonly Dictionary<Skeleton, AnimationBinding> bindings = [];

        /// <summary>
        /// Gets the block's segments, building them on first access.
        /// </summary>
        public Segment[] Segments
        {
            get
            {
                EnsureSegments();
                return segments!;
            }
        }

        public AnimationSegmentSource(KVObject animationData, KVObject decodeKey)
        {
            this.animationData = animationData;
            this.decodeKey = decodeKey;
        }

        /// <summary>
        /// Gets the binding of this block's segments onto the given skeleton, building it on first request.
        /// </summary>
        public AnimationBinding GetBinding(Skeleton skeleton, FlexController[] flexControllers)
        {
            EnsureSegments();

            if (!bindings.TryGetValue(skeleton, out var binding))
            {
                binding = BuildBinding(skeleton, flexControllers);
                bindings[skeleton] = binding;
            }

            return binding;
        }

        private void EnsureSegments()
        {
            if (segments != null)
            {
                return;
            }

            var dataChannelArrayKV = decodeKey.GetArray("m_dataChannelArray");
            var builtChannels = new AnimationDataChannel[dataChannelArrayKV.Count];
            for (var i = 0; i < dataChannelArrayKV.Count; i++)
            {
                builtChannels[i] = new AnimationDataChannel(dataChannelArrayKV[i]);
            }

            var builtGroups = new List<RemapGroup>();
            var builtSegments = BuildSegments(builtChannels, builtGroups);

            channels = builtChannels;
            groups = [.. builtGroups];
            segments = builtSegments;
        }

        private Segment[] BuildSegments(AnimationDataChannel[] dataChannelArray, List<RemapGroup> groupList)
        {
            var decoderArrayKV = animationData.GetArray("m_decoderArray");
            var decoderArray = new string[decoderArrayKV.Count];
            for (var i = 0; i < decoderArrayKV.Count; i++)
            {
                decoderArray[i] = decoderArrayKV[i].GetStringProperty("m_szName");
            }

            var segmentArrayKV = animationData.GetArray("m_segmentArray");
            var segmentArray = new Segment[segmentArrayKV.Count];
            for (var i = 0; i < segmentArrayKV.Count; i++)
            {
                segmentArray[i] = new Segment { GroupIndex = -1 };

                var segmentKV = segmentArrayKV[i];
                var container = segmentKV.GetArray<byte>("m_container");
                var containerSpan = container.AsSpan();
                var localChannelIndex = segmentKV.GetInt32Property("m_nLocalChannel");
                var localChannel = dataChannelArray[localChannelIndex];

                // Read header
                var decoder = decoderArray[BitConverter.ToInt16(containerSpan[0..2])];
                //var cardinality = BitConverter.ToInt16(containerSpan[2..4]);
                var numElements = BitConverter.ToInt16(containerSpan[4..6]);
                //var totalLength = BitConverter.ToInt16(containerSpan[6..8]);

                if (localChannel.Attribute == AnimationChannelAttribute.Unknown)
                {
                    Console.Error.WriteLine($"Unknown channel attribute encountered with '{decoder}' decoder");
                    continue;
                }

                // Look at the decoder to see what to read
                AnimationSegmentDecoder? segment = decoder switch
                {
                    nameof(CCompressedStaticFullVector3) => new CCompressedStaticFullVector3(),
                    nameof(CCompressedStaticVector3) => new CCompressedStaticVector3(),
                    nameof(CCompressedStaticQuaternion) => new CCompressedStaticQuaternion(),
                    nameof(CCompressedStaticFloat) => new CCompressedStaticFloat(),

                    nameof(CCompressedFullVector3) => new CCompressedFullVector3(),
                    nameof(CCompressedDeltaVector3) => new CCompressedDeltaVector3(),
                    nameof(CCompressedAnimVector3) => new CCompressedAnimVector3(),
                    nameof(CCompressedAnimQuaternion) => new CCompressedAnimQuaternion(),
                    nameof(CCompressedFullQuaternion) => new CCompressedFullQuaternion(),
                    nameof(CCompressedFullFloat) => new CCompressedFullFloat(),
                    _ => null,
                };

                if (segment == null)
                {
#if DEBUG
                    Console.WriteLine($"Unhandled animation bone decoder type '{decoder}' for attribute '{localChannel.Attribute}'");
#endif
                    continue;
                }

                // Read bone list
                var end = 8 + numElements * 2;
                segment.Initialize(container.AsMemory(end), localChannel.Attribute, numElements);

                segmentArray[i] = new Segment
                {
                    Decoder = segment,
                    GroupIndex = FindOrAddGroup(groupList, localChannelIndex, container.AsMemory(8, end - 8)),
                };
            }

            return segmentArray;
        }

        /// <summary>
        /// Finds the group matching this channel and element list, adding it when new.
        /// </summary>
        /// <remarks>
        /// The search is linear: a block's distinct groups number in the single digits to low tens even
        /// when it holds hundreds of segments, which is small enough that hashing costs more than it saves.
        /// </remarks>
        private static int FindOrAddGroup(List<RemapGroup> groupList, int channelIndex, ReadOnlyMemory<byte> elements)
        {
            for (var i = 0; i < groupList.Count; i++)
            {
                var group = groupList[i];

                if (group.ChannelIndex == channelIndex && group.Elements.Span.SequenceEqual(elements.Span))
                {
                    return i;
                }
            }

            groupList.Add(new RemapGroup { ChannelIndex = channelIndex, Elements = elements });
            return groupList.Count - 1;
        }

        private AnimationBinding BuildBinding(Skeleton skeleton, FlexController[] flexControllers)
        {
            var channelRemapTables = new int[channels!.Length][];

            var groupRemaps = new ElementRemap[groups!.Length][];
            for (var i = 0; i < groups.Length; i++)
            {
                var group = groups[i];

                var channelRemapTable = channelRemapTables[group.ChannelIndex]
                    ??= channels[group.ChannelIndex].CreateRemapTable(skeleton, flexControllers);

                groupRemaps[i] = BuildRemap(MemoryMarshal.Cast<byte, short>(group.Elements.Span), channelRemapTable);
            }

            var segmentRemaps = new ElementRemap[segments!.Length][];
            for (var i = 0; i < segments.Length; i++)
            {
                var groupIndex = segments[i].GroupIndex;
                segmentRemaps[i] = groupIndex == -1 ? [] : groupRemaps[groupIndex];
            }

            return new AnimationBinding(skeleton, segmentRemaps);
        }

        /// <summary>
        /// Inverts a group's element list against a channel's remap table, producing one entry
        /// per bone or flex controller that its segments actually carry data for.
        /// </summary>
        private static ElementRemap[] BuildRemap(ReadOnlySpan<short> elements, int[] channelRemapTable)
        {
            var length = channelRemapTable.Length;
            int[]? rented = null;

            const int MaxStackElements = 512;

            var scratch = length <= MaxStackElements
                ? stackalloc int[MaxStackElements]
                : (rented = ArrayPool<int>.Shared.Rent(length)).AsSpan();

            try
            {
                var positions = scratch[..length];
                var wanted = 0;

                for (var i = 0; i < length; i++)
                {
                    var position = elements.IndexOf((short)channelRemapTable[i]);
                    positions[i] = position;

                    if (position != -1)
                    {
                        wanted++;
                    }
                }

                var remaps = new ElementRemap[wanted];
                var next = 0;

                for (var i = 0; i < length; i++)
                {
                    if (positions[i] != -1)
                    {
                        remaps[next++] = new ElementRemap(positions[i], i);
                    }
                }

                return remaps;
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<int>.Shared.Return(rented);
                }
            }
        }
    }
}
