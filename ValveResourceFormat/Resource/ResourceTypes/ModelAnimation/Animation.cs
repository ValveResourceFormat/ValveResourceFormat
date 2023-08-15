using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Animation
    {
        public string Name { get; }
        public float Fps { get; }
        public int FrameCount { get; }
        private AnimationFrameBlock[] FrameBlocks { get; }
        private AnimationSegmentDecoder[] SegmentArray { get; }

        private Animation(IKeyValueCollection animDesc, AnimationSegmentDecoder[] segmentArray)
        {
            // Get animation properties
            Name = animDesc.GetProperty<string>("m_name");
            Fps = animDesc.GetFloatProperty("fps");
            SegmentArray = segmentArray;

            var pDataObject = animDesc.GetProperty<object>("m_pData");
            var pData = pDataObject is NTROValue[] ntroArray
                ? ntroArray[0].ValueObject as IKeyValueCollection
                : pDataObject as IKeyValueCollection;
            FrameCount = pData.GetInt32Property("m_nFrames");

            var frameBlockArray = pData.GetArray("m_frameblockArray");
            FrameBlocks = new AnimationFrameBlock[frameBlockArray.Length];
            for (var i = 0; i < frameBlockArray.Length; i++)
            {
                FrameBlocks[i] = new AnimationFrameBlock(frameBlockArray[i]);
            }
        }

        public static IEnumerable<Animation> FromData(IKeyValueCollection animationData, IKeyValueCollection decodeKey,
            Skeleton skeleton)
        {
            var animArray = animationData.GetArray<IKeyValueCollection>("m_animArray");

            if (animArray.Length == 0)
            {
                return Enumerable.Empty<Animation>();
            }

            var decoderArrayKV = animationData.GetArray("m_decoderArray");
            var decoderArray = new string[decoderArrayKV.Length];
            for (var i = 0; i < decoderArrayKV.Length; i++)
            {
                decoderArray[i] = decoderArrayKV[i].GetProperty<string>("m_szName");
            }

            var channelElements = decodeKey.GetInt32Property("m_nChannelElements");
            var dataChannelArrayKV = decodeKey.GetArray("m_dataChannelArray");
            var dataChannelArray = new AnimationDataChannel[dataChannelArrayKV.Length];
            for (var i = 0; i < dataChannelArrayKV.Length; i++)
            {
                dataChannelArray[i] = new AnimationDataChannel(skeleton, dataChannelArrayKV[i], channelElements);
            }

            var segmentArrayKV = animationData.GetArray("m_segmentArray");
            var segmentArray = new AnimationSegmentDecoder[segmentArrayKV.Length];
            for (var i = 0; i < segmentArrayKV.Length; i++)
            {
                var segmentKV = segmentArrayKV[i];
                var container = segmentKV.GetArray<byte>("m_container");
                var localChannel = dataChannelArray[segmentKV.GetInt32Property("m_nLocalChannel")];
                using var containerReader = new BinaryReader(new MemoryStream(container));
                // Read header
                var decoder = decoderArray[containerReader.ReadInt16()];
                var cardinality = containerReader.ReadInt16();
                var numElements = containerReader.ReadInt16();
                var totalLength = containerReader.ReadInt16();

                // Read bone list
                var elements = new int[numElements];
                for (var j = 0; j < numElements; j++)
                {
                    elements[j] = containerReader.ReadInt16();
                }

                var containerSegment = new ArraySegment<byte>(
                    container,
                    (int)containerReader.BaseStream.Position,
                    container.Length - (int)containerReader.BaseStream.Position
                );

                var remapTable = localChannel.RemapTable
                    .Select(i => Array.IndexOf(elements, i))
                    .ToArray();
                var wantedElements = remapTable.Where(boneID => boneID != -1).ToArray();
                remapTable = remapTable
                    .Select((boneID, i) => (boneID, i))
                    .Where(t => t.boneID != -1)
                    .Select(t => t.i)
                    .ToArray();
                var channelAttribute = localChannel.ChannelAttribute switch
                {
                    "Position" => AnimationChannelAttribute.Position,
                    "Angle" => AnimationChannelAttribute.Angle,
                    "Scale" => AnimationChannelAttribute.Scale,
                    _ => AnimationChannelAttribute.Unknown,
                };

                if (channelAttribute == AnimationChannelAttribute.Unknown)
                {
                    if (localChannel.ChannelAttribute != "data")
                    {
                        Console.Error.WriteLine($"Unknown channel attribute '{localChannel.ChannelAttribute}' encountered with '{decoder}' decoder");
                    }

                    continue;
                }

                // Look at the decoder to see what to read
                switch (decoder)
                {
                    case "CCompressedStaticFullVector3":
                        segmentArray[i] = new CCompressedStaticFullVector3(containerSegment, wantedElements, remapTable, channelAttribute);
                        break;
                    case "CCompressedStaticVector3":
                        segmentArray[i] = new CCompressedStaticVector3(containerSegment, wantedElements, remapTable, channelAttribute);
                        break;
                    case "CCompressedStaticQuaternion":
                        segmentArray[i] = new CCompressedStaticQuaternion(containerSegment, wantedElements, remapTable, channelAttribute);
                        break;
                    case "CCompressedStaticFloat":
                        segmentArray[i] = new CCompressedStaticFloat(containerSegment, wantedElements, remapTable, channelAttribute);
                        break;

                    case "CCompressedFullVector3":
                        segmentArray[i] = new CCompressedFullVector3(containerSegment, wantedElements, remapTable, numElements, channelAttribute);
                        break;
                    case "CCompressedDeltaVector3":
                        segmentArray[i] = new CCompressedDeltaVector3(containerSegment, wantedElements, remapTable, numElements, channelAttribute);
                        break;
                    case "CCompressedAnimVector3":
                        segmentArray[i] = new CCompressedAnimVector3(containerSegment, wantedElements, remapTable, numElements, channelAttribute);
                        break;
                    case "CCompressedAnimQuaternion":
                        segmentArray[i] = new CCompressedAnimQuaternion(containerSegment, wantedElements, remapTable, numElements, channelAttribute);
                        break;
                    case "CCompressedFullQuaternion":
                        segmentArray[i] = new CCompressedFullQuaternion(containerSegment, wantedElements, remapTable, numElements, channelAttribute);
                        break;
                    case "CCompressedFullFloat":
                        segmentArray[i] = new CCompressedFullFloat(containerSegment, wantedElements, remapTable, numElements, channelAttribute);
                        break;
#if DEBUG
                    default:
                        if (localChannel.ChannelAttribute != "data")
                        {
                            Console.WriteLine($"Unhandled animation bone decoder type '{decoder}' for attribute '{localChannel.ChannelAttribute}'");
                        }

                        break;
#endif
                }
            }

            return animArray
                .Select(anim => new Animation(anim, segmentArray))
                .ToArray();
        }

        public static IEnumerable<Animation> FromResource(Resource resource, IKeyValueCollection decodeKey, Skeleton skeleton)
            => FromData(GetAnimationData(resource), decodeKey, skeleton);

        private static IKeyValueCollection GetAnimationData(Resource resource)
            => resource.DataBlock.AsKeyValueCollection();

        /// <summary>
        /// Get the animation matrix for each bone.
        /// </summary>
        public Matrix4x4[] GetAnimationMatrices(AnimationFrameCache frameCache, int frameIndex, Skeleton skeleton)
        {
            // Get bone transformations
            var frame = frameCache.GetFrame(this, frameIndex);

            return GetAnimationMatrices(frame, skeleton);
        }

        /// <summary>
        /// Get the animation matrix for each bone.
        /// </summary>
        public Matrix4x4[] GetAnimationMatrices(AnimationFrameCache frameCache, float time, Skeleton skeleton)
        {
            // Get bone transformations
            var frame = FrameCount != 0
                ? frameCache.GetFrame(this, time)
                : null;

            return GetAnimationMatrices(frame, skeleton);
        }

        private Matrix4x4[] GetAnimationMatrices(Frame frame, Skeleton skeleton)
        {
            // Create output array
            var matrices = new Matrix4x4[skeleton.Bones.Length];

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(root, Matrix4x4.Identity, Matrix4x4.Identity, frame, ref matrices);
            }

            return matrices;
        }

        public void DecodeFrame(int frameIndex, Frame outFrame)
        {
            // Read all frame blocks
            foreach (var frameBlock in FrameBlocks)
            {
                // Only consider blocks that actual contain info for this frame
                if (frameIndex >= frameBlock.StartFrame && frameIndex <= frameBlock.EndFrame)
                {
                    foreach (var segmentIndex in frameBlock.SegmentIndexArray)
                    {
                        var segment = SegmentArray[segmentIndex];
                        // Segment could be null for unknown decoders
                        if (segment != null)
                        {
                            segment.Read(frameIndex - frameBlock.StartFrame, outFrame);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get animation matrix recursively.
        /// </summary>
        private void GetAnimationMatrixRecursive(Bone bone, Matrix4x4 bindPose, Matrix4x4 invBindPose, Frame frame, ref Matrix4x4[] matrices)
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
                GetAnimationMatrixRecursive(child, bindPose, invBindPose, frame, ref matrices);
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
