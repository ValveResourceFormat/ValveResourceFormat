using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// The segment decoders of one animation data block, shared by every animation in it.
    /// </summary>
    internal sealed class AnimationSegmentTable
    {
        private readonly AnimationSegmentDecoder?[] decoders;

        public AnimationSegmentTable(KVObject animationData, KVObject decodeKey, Skeleton skeleton, FlexController[] flexControllers)
        {
            var decoderArrayKV = animationData.GetArray("m_decoderArray");
            var decoderNames = new string[decoderArrayKV.Count];
            for (var i = 0; i < decoderArrayKV.Count; i++)
            {
                decoderNames[i] = decoderArrayKV[i].GetStringProperty("m_szName");
            }

            var userArrayKV = decodeKey.GetArray("m_userArray");
            var userNames = new string[userArrayKV?.Count ?? 0];
            for (var i = 0; i < userNames.Length; i++)
            {
                userNames[i] = userArrayKV![i].GetStringProperty("m_name");
            }

            var dataChannelArrayKV = decodeKey.GetArray("m_dataChannelArray");
            var dataChannels = new AnimationDataChannel[dataChannelArrayKV.Count];
            for (var i = 0; i < dataChannelArrayKV.Count; i++)
            {
                dataChannels[i] = new AnimationDataChannel(skeleton, flexControllers, userNames, dataChannelArrayKV[i]);
            }

            var segments = animationData.GetArray("m_segmentArray");
            var unhandledDecoders = new HashSet<string>();
            decoders = new AnimationSegmentDecoder?[segments.Count];

            for (var i = 0; i < decoders.Length; i++)
            {
                decoders[i] = Build(segments[i], decoderNames, dataChannels, unhandledDecoders);
            }
        }

        /// <summary>
        /// Gets the decoder of a segment, or <see langword="null"/> when its decoder or channel is not supported.
        /// </summary>
        public AnimationSegmentDecoder? Get(long index) => decoders[index];

        private static AnimationSegmentDecoder? Build(KVObject segmentKV, string[] decoderNames, AnimationDataChannel[] dataChannels, HashSet<string> unhandledDecoders)
        {
            var container = segmentKV.GetArray<byte>("m_container");
            var containerSpan = container.AsSpan();
            var localChannel = dataChannels[segmentKV.GetInt32Property("m_nLocalChannel")];

            // Read header
            var decoderName = decoderNames[BitConverter.ToInt16(containerSpan[0..2])];
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
                if (unhandledDecoders.Add(decoderName))
                {
                    Console.Error.WriteLine($"Unhandled animation bone decoder type '{decoderName}' for attribute '{localChannel.Attribute}'");
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
