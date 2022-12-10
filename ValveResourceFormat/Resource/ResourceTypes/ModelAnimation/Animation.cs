using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.NTRO;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;

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

        public static IEnumerable<Animation> FromData(IKeyValueCollection animationData, IKeyValueCollection decodeKey)
        {
            var animArray = animationData.GetArray<IKeyValueCollection>("m_animArray");

            if (animArray.Length == 0)
            {
                Console.WriteLine("Empty animation file found.");
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
                dataChannelArray[i] = new AnimationDataChannel(dataChannelArrayKV[i], channelElements);
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
                var numBones = containerReader.ReadInt16();
                var totalLength = containerReader.ReadInt16();

                // Read bone list
                var elements = new int[numBones];
                for (var j = 0; j < numBones; j++)
                {
                    elements[j] = containerReader.ReadInt16();
                }

                var containerSegment = new ArraySegment<byte>(
                    container,
                    (int)containerReader.BaseStream.Position,
                    container.Length - (int)containerReader.BaseStream.Position
                );

                // Look at the decoder to see what to read
                switch (decoder)
                {
                    case "CCompressedStaticFullVector3":
                        segmentArray[i] = new CCompressedStaticFullVector3(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedFullVector3":
                        segmentArray[i] = new CCompressedFullVector3(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedDeltaVector3":
                        segmentArray[i] = new CCompressedDeltaVector3(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedAnimVector3":
                        segmentArray[i] = new CCompressedAnimVector3(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedStaticVector3":
                        segmentArray[i] = new CCompressedStaticVector3(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedAnimQuaternion":
                        segmentArray[i] = new CCompressedAnimQuaternion(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedStaticQuaternion":
                        segmentArray[i] = new CCompressedStaticQuaternion(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedFullQuaternion":
                        segmentArray[i] = new CCompressedFullQuaternion(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedStaticFloat":
                        segmentArray[i] = new CCompressedStaticFloat(containerSegment, elements, localChannel);
                        break;
                    case "CCompressedFullFloat":
                        segmentArray[i] = new CCompressedFullFloat(containerSegment, elements, localChannel);
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

        public static IEnumerable<Animation> FromResource(Resource resource, IKeyValueCollection decodeKey)
            => FromData(GetAnimationData(resource), decodeKey);

        private static IKeyValueCollection GetAnimationData(Resource resource)
            => resource.DataBlock.AsKeyValueCollection();

        /// <summary>
        /// Get animation matrices as an array.
        /// </summary>
        public float[] GetAnimationMatricesAsArray(AnimationFrameCache frameCache, float time, Skeleton skeleton)
        {
            var matrices = GetAnimationMatrices(frameCache, time, skeleton);
            return Flatten(matrices);
        }

        /// <summary>
        /// Get the animation matrix for each bone.
        /// </summary>
        public Matrix4x4[] GetAnimationMatrices(AnimationFrameCache frameCache, float time, Skeleton skeleton)
        {
            // Create output array
            var matrices = new Matrix4x4[skeleton.AnimationTextureSize + 1];

            // Get bone transformations
            var transforms = frameCache.GetFrame(this, time);

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(root, Matrix4x4.Identity, Matrix4x4.Identity, transforms, ref matrices);
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
        private void GetAnimationMatrixRecursive(Bone bone, Matrix4x4 parentBindPose, Matrix4x4 parentInvBindPose, Frame transforms, ref Matrix4x4[] matrices)
        {
            // Calculate world space bind and inverse bind pose
            var bindPose = parentBindPose;
            var invBindPose = parentInvBindPose * bone.InverseBindPose;

            // Calculate transformation matrix
            var transformMatrix = Matrix4x4.Identity;
            if (transforms.Bones.ContainsKey(bone.Name))
            {
                var transform = transforms.Bones[bone.Name];
                transformMatrix = Matrix4x4.CreateScale(transform.Scale)
                    * Matrix4x4.CreateFromQuaternion(transform.Angle)
                    * Matrix4x4.CreateTranslation(transform.Position);
            }

            // Apply tranformation
            var transformed = transformMatrix * bindPose;

            // Store result
            var skinMatrix = invBindPose * transformed;
            foreach (var index in bone.SkinIndices)
            {
                matrices[index] = skinMatrix;
            }

            // Propagate to childen
            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(child, transformed, invBindPose, transforms, ref matrices);
            }
        }

        /// <summary>
        /// Flatten an array of matrices to an array of floats.
        /// </summary>
        private static float[] Flatten(Matrix4x4[] matrices)
        {
            var returnArray = new float[matrices.Length * 16];

            for (var i = 0; i < matrices.Length; i++)
            {
                var mat = matrices[i];
                returnArray[i * 16] = mat.M11;
                returnArray[(i * 16) + 1] = mat.M12;
                returnArray[(i * 16) + 2] = mat.M13;
                returnArray[(i * 16) + 3] = mat.M14;

                returnArray[(i * 16) + 4] = mat.M21;
                returnArray[(i * 16) + 5] = mat.M22;
                returnArray[(i * 16) + 6] = mat.M23;
                returnArray[(i * 16) + 7] = mat.M24;

                returnArray[(i * 16) + 8] = mat.M31;
                returnArray[(i * 16) + 9] = mat.M32;
                returnArray[(i * 16) + 10] = mat.M33;
                returnArray[(i * 16) + 11] = mat.M34;

                returnArray[(i * 16) + 12] = mat.M41;
                returnArray[(i * 16) + 13] = mat.M42;
                returnArray[(i * 16) + 14] = mat.M43;
                returnArray[(i * 16) + 15] = mat.M44;
            }

            return returnArray;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
