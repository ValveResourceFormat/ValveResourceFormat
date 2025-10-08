using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model animation with frame data, events, and movement information.
    /// </summary>
    public class Animation
    {
        /// <summary>
        /// Gets the name of the animation.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the frames per second of the animation.
        /// </summary>
        public float Fps { get; }

        /// <summary>
        /// Gets the total number of frames in the animation.
        /// </summary>
        public int FrameCount { get; }

        /// <summary>
        /// Gets a value indicating whether the animation loops.
        /// </summary>
        public bool IsLooping { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the animation is hidden.
        /// </summary>
        public bool Hidden { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether this is a delta animation.
        /// </summary>
        public bool Delta { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether this animation is in world space.
        /// </summary>
        public bool Worldspace { get; init; }
        private AnimationFrameBlock[] FrameBlocks { get; }
        private AnimationSegmentDecoder[] SegmentArray { get; }

        /// <summary>
        /// Gets the movement data for this animation.
        /// </summary>
        public AnimationMovement[] Movements { get; }

        /// <summary>
        /// Gets the events defined in this animation.
        /// </summary>
        public AnimationEvent[] Events { get; }

        /// <summary>
        /// Gets the activities associated with this animation.
        /// </summary>
        public AnimationActivity[] Activities { get; }

        /// <summary>
        /// Gets the sequence parameters for this animation.
        /// </summary>
        public AnimationSequenceParams SequenceParams { get; }

        private Animation(KVObject animDesc, AnimationSegmentDecoder[] segmentArray)
        {
            // Get animation properties
            Name = animDesc.GetProperty<string>("m_name");
            Fps = animDesc.GetFloatProperty("fps");
            SegmentArray = segmentArray;

            var flags = animDesc.GetSubCollection("m_flags");
            IsLooping = flags.GetProperty<bool>("m_bLooping");
            Hidden = flags.GetProperty<bool>("m_bHidden");
            Delta = flags.GetProperty<bool>("m_bDelta");
            Worldspace = flags.GetProperty<bool>("m_bLegacyWorldspace");

            var pDataObject = animDesc.GetProperty<object>("m_pData");
            var pData = pDataObject as KVObject;
            FrameCount = pData.GetInt32Property("m_nFrames");

            var frameBlockArray = pData.GetArray("m_frameblockArray");
            FrameBlocks = new AnimationFrameBlock[frameBlockArray.Length];
            for (var i = 0; i < frameBlockArray.Length; i++)
            {
                FrameBlocks[i] = new AnimationFrameBlock(frameBlockArray[i]);
            }

            var movementArray = animDesc.GetArray("m_movementArray");
            Movements = new AnimationMovement[movementArray.Length];
            for (var i = 0; i < movementArray.Length; i++)
            {
                Movements[i] = new AnimationMovement(movementArray[i]);
            }

            Events = animDesc.GetArray("m_eventArray")
                                 .Select(x => new AnimationEvent(x))
                                 .ToArray();

            Activities = animDesc.GetArray("m_activityArray")
                                    .Select(x => new AnimationActivity(x))
                                    .ToArray();

            var sequenceParams = animDesc.GetSubCollection("m_sequenceParams");
            SequenceParams = new AnimationSequenceParams(sequenceParams);
        }

        /// <summary>
        /// Creates animation instances from the provided animation data and decode key.
        /// </summary>
        public static IEnumerable<Animation> FromData(KVObject animationData, KVObject decodeKey,
            Skeleton skeleton, FlexController[] flexControllers)
        {
            var animArray = animationData.GetArray<KVObject>("m_animArray");

            if (animArray.Length == 0)
            {
                return [];
            }

            var decoderArrayKV = animationData.GetArray("m_decoderArray");
            var decoderArray = new string[decoderArrayKV.Length];
            for (var i = 0; i < decoderArrayKV.Length; i++)
            {
                decoderArray[i] = decoderArrayKV[i].GetProperty<string>("m_szName");
            }

            //var channelElements = decodeKey.GetInt32Property("m_nChannelElements");
            var dataChannelArrayKV = decodeKey.GetArray("m_dataChannelArray");
            var dataChannelArray = new AnimationDataChannel[dataChannelArrayKV.Length];
            for (var i = 0; i < dataChannelArrayKV.Length; i++)
            {
                dataChannelArray[i] = new AnimationDataChannel(skeleton, flexControllers, dataChannelArrayKV[i]);
            }

            var segmentArrayKV = animationData.GetArray("m_segmentArray");
            var segmentArray = new AnimationSegmentDecoder[segmentArrayKV.Length];
            for (var i = 0; i < segmentArrayKV.Length; i++)
            {
                var segmentKV = segmentArrayKV[i];
                var container = segmentKV.GetArray<byte>("m_container");
                var containerSpan = container.AsSpan();
                var localChannel = dataChannelArray[segmentKV.GetInt32Property("m_nLocalChannel")];

                // Read header
                var decoder = decoderArray[BitConverter.ToInt16(containerSpan[0..2])];
                //var cardinality = BitConverter.ToInt16(containerSpan[2..4]);
                var numElements = BitConverter.ToInt16(containerSpan[4..6]);
                //var totalLength = BitConverter.ToInt16(containerSpan[6..8]);

                // Read bone list
                var end = 8 + numElements * 2;
                var elements = MemoryMarshal.Cast<byte, short>(containerSpan[8..end]);
                var remapTable = new int[localChannel.RemapTable.Length];

                for (var j = 0; j < remapTable.Length; j++)
                {
                    remapTable[j] = elements.IndexOf((short)localChannel.RemapTable[j]);
                }

                var wantedElements = remapTable.Where(boneID => boneID != -1).ToArray();
                remapTable = remapTable
                    .Select((boneID, i) => (boneID, i))
                    .Where(t => t.boneID != -1)
                    .Select(t => t.i)
                    .ToArray();

                if (localChannel.Attribute == AnimationChannelAttribute.Unknown)
                {
                    Console.Error.WriteLine($"Unknown channel attribute encountered with '{decoder}' decoder");
                    continue;
                }

                var containerSegment = new ArraySegment<byte>(container, end, container.Length - end);

                // Look at the decoder to see what to read
                segmentArray[i] = decoder switch
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

                if (segmentArray[i] != null)
                {
                    segmentArray[i].Initialize(containerSegment, wantedElements, remapTable, localChannel.Attribute, numElements);
                    continue;
                }

#if DEBUG
                Console.WriteLine($"Unhandled animation bone decoder type '{decoder}' for attribute '{localChannel.Attribute}'");
#endif
            }

            return animArray
                .Select(anim => new Animation(anim, segmentArray))
                .ToArray();
        }

        /// <summary>
        /// Creates animation instances from a resource file.
        /// </summary>
        public static IEnumerable<Animation> FromResource(Resource resource, KVObject decodeKey, Skeleton skeleton, FlexController[] flexControllers)
            => FromData(GetAnimationData(resource), decodeKey, skeleton, flexControllers);

        private static KVObject GetAnimationData(Resource resource)
            => resource.DataBlock.AsKeyValueCollection();

        private int GetMovementIndexForTime(float time)
        {
            var frame = (int)MathF.Floor(time * Fps);
            return GetMovementIndexForFrame(frame);
        }

        private int GetMovementIndexForFrame(int frame)
        {
            for (var i = 0; i < Movements.Length; i++)
            {
                var movement = Movements[i];
                if (movement.EndFrame > frame)
                {
                    return i;
                }
            }
            return Movements.Length - 1;
        }

        /// <summary>
        /// Determines whether this animation has movement data.
        /// </summary>
        public bool HasMovementData()
        {
            return Movements.Length > 0;
        }

        /// <summary>
        /// Returns interpolated root motion data at the specified time.
        /// </summary>
        public AnimationMovement.MovementData GetMovementOffsetData(float time)
        {
            if (!HasMovementData())
            {
                return new();
            }

            GetMovementForTime(time, out var movement, out var nextMovement, out var t);
            return AnimationMovement.Lerp(movement, nextMovement, time);
        }

        /// <summary>
        /// Returns interpolated root motion data at the specified frame.
        /// </summary>
        public AnimationMovement.MovementData GetMovementOffsetData(int frame)
        {
            if (!HasMovementData())
            {
                return new();
            }

            var movementIndex = GetMovementIndexForFrame(frame);
            var lastMovement = movementIndex == 0 ? null : Movements[movementIndex - 1];
            var movement = Movements[movementIndex];

            var movementTime = frame / (float)movement.EndFrame;
            return AnimationMovement.Lerp(lastMovement, movement, movementTime);
        }

        /// <summary>
        /// Returns root motion data at the specified animation time for interpolation.
        /// </summary>
        private void GetMovementForTime(float time, out AnimationMovement lastMovement, out AnimationMovement nextMovement, out float t)
        {
            time %= FrameCount / Fps;

            var nextMovementIndex = GetMovementIndexForTime(time);
            var lastMovementIndex = nextMovementIndex - 1;

            nextMovement = Movements[nextMovementIndex];
            if (nextMovementIndex == 0)
            {
                lastMovement = null;

                var movementTime = nextMovement.EndFrame / Fps;
                t = time / movementTime;
                return;
            }

            lastMovement = Movements[lastMovementIndex];

            var startTime = lastMovement.EndFrame / Fps;
            var endTime = nextMovement.EndFrame / Fps;

            var movementDuration = endTime - startTime;
            var elapsedTime = time - startTime;

            t = Math.Min(1f, elapsedTime / movementDuration);
        }

        /// <summary>
        /// Gets the animation clip data for ModelAnimation2 format.
        /// </summary>
        public AnimationClip Clip { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Animation"/> class from an animation clip.
        /// </summary>
        public Animation(AnimationClip clip)
        {
            Name = clip.Name;
            FrameCount = clip.NumFrames;
            Fps = clip.NumFrames / clip.Duration;

            Clip = clip;
            Movements = [];
            Events = [];
            Activities = [];
        }

        /// <summary>
        /// Decodes animation data for the specified frame.
        /// </summary>
        public void DecodeFrame(Frame outFrame)
        {
            if (Clip != null)
            {
                Clip.ReadFrame(outFrame.FrameIndex, outFrame.Bones);
                return;
            }

            // Read all frame blocks
            foreach (var frameBlock in FrameBlocks)
            {
                // Only consider blocks that actual contain info for this frame
                if (outFrame.FrameIndex >= frameBlock.StartFrame && outFrame.FrameIndex <= frameBlock.EndFrame)
                {
                    foreach (var segmentIndex in frameBlock.SegmentIndexArray)
                    {
                        var segment = SegmentArray[segmentIndex];
                        // Segment could be null for unknown decoders
                        segment?.Read(outFrame.FrameIndex - frameBlock.StartFrame, outFrame);
                    }
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the animation name.
        /// </remarks>
        public override string ToString()
        {
            return Name;
        }
    }
}
