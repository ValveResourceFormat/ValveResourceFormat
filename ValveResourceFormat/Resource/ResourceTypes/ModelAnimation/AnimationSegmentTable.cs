using System.Runtime.InteropServices;
using System.Threading;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// The segment decoders of one animation data block, shared by every animation in it. A segment's
    /// decoder is built the first time it is asked for. Safe to use from several threads.
    /// </summary>
    internal sealed class AnimationSegmentTable
    {
        private static readonly AnimationSegmentDecoder Unhandled = new CCompressedStaticBool();

        private readonly KVObject animationData;
        private readonly KVObject decodeKey;
        private readonly Skeleton skeleton;
        private readonly FlexController[] flexControllers;
        private readonly IReadOnlyList<KVObject> segmentKVs;
        private readonly AnimationSegmentDecoder?[] decoders;
        private readonly Lock unhandledDecodersLock = new();
        private HashSet<string>? unhandledDecoders;
        private string[]? decoderNames;
        private AnimationDataChannel[]? dataChannels;
        private AnimationSegmentDecoder?[]? all;

        public AnimationSegmentTable(KVObject animationData, KVObject decodeKey, Skeleton skeleton, FlexController[] flexControllers)
        {
            this.animationData = animationData;
            this.decodeKey = decodeKey;
            this.skeleton = skeleton;
            this.flexControllers = flexControllers;
            segmentKVs = animationData.GetArray("m_segmentArray");
            decoders = new AnimationSegmentDecoder?[segmentKVs.Count];
        }

        /// <summary>
        /// Gets the decoder of a segment, or <see langword="null"/> when its decoder or channel is not supported.
        /// </summary>
        public AnimationSegmentDecoder? Get(long index)
        {
            var decoder = Volatile.Read(ref decoders[index]);

            if (decoder == null)
            {
                decoder = Build((int)index) ?? Unhandled;
                decoder = Interlocked.CompareExchange(ref decoders[index], decoder, null) ?? decoder;
            }

            return ReferenceEquals(decoder, Unhandled) ? null : decoder;
        }

        /// <summary>
        /// Gets the decoders of every segment, building any not built yet.
        /// </summary>
        public AnimationSegmentDecoder?[] All
        {
            get
            {
                if (Volatile.Read(ref all) is { } built)
                {
                    return built;
                }

                built = new AnimationSegmentDecoder?[decoders.Length];

                for (var i = 0; i < built.Length; i++)
                {
                    built[i] = Get(i);
                }

                Volatile.Write(ref all, built);
                return built;
            }
        }

        private string[] DecoderNames => LazyInitializer.EnsureInitialized(ref decoderNames, () =>
        {
            var decoderArrayKV = animationData.GetArray("m_decoderArray");
            var names = new string[decoderArrayKV.Count];
            for (var i = 0; i < decoderArrayKV.Count; i++)
            {
                names[i] = decoderArrayKV[i].GetStringProperty("m_szName");
            }

            return names;
        });

        private AnimationDataChannel[] DataChannels => LazyInitializer.EnsureInitialized(ref dataChannels, () =>
        {
            var userArrayKV = decodeKey.GetArray("m_userArray");
            var userNames = new string[userArrayKV?.Count ?? 0];
            for (var i = 0; i < userNames.Length; i++)
            {
                userNames[i] = userArrayKV![i].GetStringProperty("m_name");
            }

            var dataChannelArrayKV = decodeKey.GetArray("m_dataChannelArray");
            var channels = new AnimationDataChannel[dataChannelArrayKV.Count];
            for (var i = 0; i < dataChannelArrayKV.Count; i++)
            {
                channels[i] = new AnimationDataChannel(skeleton, flexControllers, userNames, dataChannelArrayKV[i]);
            }

            return channels;
        });

        private AnimationSegmentDecoder? Build(int index)
        {
            var segmentKV = segmentKVs[index];
            var container = segmentKV.GetArray<byte>("m_container");
            var containerSpan = container.AsSpan();
            var localChannel = DataChannels[segmentKV.GetInt32Property("m_nLocalChannel")];

            // Read header
            var decoderName = DecoderNames[BitConverter.ToInt16(containerSpan[0..2])];
            //var cardinality = BitConverter.ToInt16(containerSpan[2..4]);
            var numElements = BitConverter.ToInt16(containerSpan[4..6]);
            //var totalLength = BitConverter.ToInt16(containerSpan[6..8]);

            if (localChannel.Attribute == AnimationChannelAttribute.Unknown)
            {
                Console.Error.WriteLine($"Unknown channel attribute encountered with '{decoderName}' decoder");
                return null;
            }

            // Look at the decoder to see what to read
            AnimationSegmentDecoder? decoder = decoderName switch
            {
                nameof(CCompressedStaticFullVector3) => new CCompressedStaticFullVector3(),
                nameof(CCompressedStaticVector3) => new CCompressedStaticVector3(),
                nameof(CCompressedStaticQuaternion) => new CCompressedStaticQuaternion(),
                nameof(CCompressedStaticFloat) => new CCompressedStaticFloat(),
                nameof(CCompressedStaticBool) => new CCompressedStaticBool(),

                nameof(CCompressedFullVector3) => new CCompressedFullVector3(),
                nameof(CCompressedDeltaVector3) => new CCompressedDeltaVector3(),
                nameof(CCompressedAnimVector3) => new CCompressedAnimVector3(),
                nameof(CCompressedAnimQuaternion) => new CCompressedAnimQuaternion(),
                nameof(CCompressedFullQuaternion) => new CCompressedFullQuaternion(),
                nameof(CCompressedFullFloat) => new CCompressedFullFloat(),
                nameof(CCompressedFullBool) => new CCompressedFullBool(),
                _ => null,
            };

            if (decoder == null)
            {
                lock (unhandledDecodersLock)
                {
                    unhandledDecoders ??= [];

                    if (unhandledDecoders.Add(decoderName))
                    {
                        Console.Error.WriteLine($"Unhandled animation bone decoder type '{decoderName}' for attribute '{localChannel.Attribute}'");
                    }
                }

                return null;
            }

            // Read bone list
            var end = 8 + numElements * 2;
            var elements = MemoryMarshal.Cast<byte, short>(containerSpan[8..end]);
            var (wantedElements, remapTable) = BuildSegmentRemap(elements, localChannel.RemapTable);

            var containerSegment = new ArraySegment<byte>(container, end, container.Length - end);
            decoder.Initialize(containerSegment, wantedElements, remapTable, localChannel.Attribute, numElements);

            return decoder;
        }

        /// <summary>
        /// Pairs each channel element the segment stores with its position in the segment.
        /// </summary>
        /// <returns>The positions within the segment, and the matching channel element indices, in channel order.</returns>
        private static (int[] WantedElements, int[] RemapTable) BuildSegmentRemap(ReadOnlySpan<short> elements, int[] channelRemap)
        {
            // Searching the element list for every channel entry is quadratic, and dominated loading models with many animations
            var maxElement = -1;

            foreach (var element in elements)
            {
                maxElement = Math.Max(maxElement, element);
            }

            var positions = new int[maxElement + 1];
            positions.AsSpan().Fill(-1);

            for (var i = elements.Length - 1; i >= 0; i--)
            {
                if (elements[i] >= 0)
                {
                    positions[elements[i]] = i;
                }
            }

            var count = 0;

            foreach (var element in channelRemap)
            {
                if ((uint)element < (uint)positions.Length && positions[element] != -1)
                {
                    count++;
                }
            }

            var wantedElements = new int[count];
            var remapTable = new int[count];
            count = 0;

            for (var i = 0; i < channelRemap.Length; i++)
            {
                var element = channelRemap[i];

                if ((uint)element < (uint)positions.Length && positions[element] != -1)
                {
                    wantedElements[count] = positions[element];
                    remapTable[count] = i;
                    count++;
                }
            }

            return (wantedElements, remapTable);
        }
    }
}
