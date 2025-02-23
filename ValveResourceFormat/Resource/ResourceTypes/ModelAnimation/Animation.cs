using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Animation
    {
        public string Name { get; }
        public float Fps { get; }
        public int FrameCount { get; }
        public bool IsLooping { get; }
        public bool Hidden { get; init; }
        public bool Delta { get; init; }
        public bool Worldspace { get; init; }
        private AnimationFrameBlock[] FrameBlocks { get; }
        private AnimationSegmentDecoder[] SegmentArray { get; }
        public AnimationMovement[] Movements { get; }
        public AnimationEvent[] Events { get; }
        public AnimationActivity[] Activities { get; }
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

        public bool HasMovementData()
        {
            return Movements.Length > 0;
        }

        /// <summary>
        /// Returns interpolated root motion data
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
        /// Get the animation matrix for each bone.
        /// </summary>
        public void GetAnimationMatrices(Span<Matrix4x4> matrices, AnimationFrameCache frameCache, int frameIndex)
        {
            // Get bone transformations
            var frame = frameCache.GetFrame(this, frameIndex);

            GetAnimationMatrices(matrices, frame, frameCache.Skeleton);
        }

        /// <summary>
        /// Get the animation matrix for each bone.
        /// </summary>
        public void GetAnimationMatrices(Span<Matrix4x4> matrices, AnimationFrameCache frameCache, float time)
        {
            // Get bone transformations
            var frame = FrameCount != 0
                ? frameCache.GetInterpolatedFrame(this, time)
                : null;

            GetAnimationMatrices(matrices, frame, frameCache.Skeleton);
        }

        public static void GetAnimationMatrices(Span<Matrix4x4> matrices, Frame frame, Skeleton skeleton)
        {
            foreach (var root in skeleton.Roots)
            {
                if (root.IsProceduralCloth)
                {
                    continue;
                }

                GetAnimationMatrixRecursive(root, Matrix4x4.Identity, Matrix4x4.Identity, frame, matrices);
            }
        }

        // todo: remove this
        public AnimationClip Animation2 { get; }
        public Animation(AnimationClip animation2)
        {
            Name = animation2.Name;
            Fps = animation2.Fps;
            FrameCount = animation2.NumFrames;

            Animation2 = animation2;
        }

        public void DecodeFrame(Frame outFrame)
        {
            if (Animation2 != null)
            {
                Animation2.ReadFrame(outFrame.FrameIndex, outFrame.Bones);
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

        /// <summary>
        /// Get animation matrix recursively.
        /// </summary>
        private static void GetAnimationMatrixRecursive(Bone bone, Matrix4x4 bindPose, Matrix4x4 invBindPose, Frame frame, Span<Matrix4x4> matrices)
        {
            // Calculate world space inverse bind pose
            invBindPose *= bone.InverseBindPose;

            // Calculate and apply tranformation matrix
            if (frame != null)
            {
                var transform = frame.Bones[bone.Index];
                bindPose = Matrix4x4.CreateScale(transform.Scale)
                    * Matrix4x4.CreateFromQuaternion(transform.Angle)
                    * Matrix4x4.CreateTranslation(transform.Position)
                    * bindPose;
            }
            else
            {
                bindPose = bone.BindPose * bindPose;
            }

            // Store result
            var skinMatrix = invBindPose * bindPose;
            matrices[bone.Index] = skinMatrix;

            // Propagate to childen
            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(child, bindPose, invBindPose, frame, matrices);
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
