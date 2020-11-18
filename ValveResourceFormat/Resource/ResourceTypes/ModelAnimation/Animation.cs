using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Animation
    {
        public string Name { get; private set; }
        public float Fps { get; private set; }
        public long FrameCount { get; private set; }

        public Frame[] Frames { get; private set; }

        private Animation(
            IKeyValueCollection animDesc,
            IKeyValueCollection decodeKey,
            AnimDecoderType[] decoderArray,
            IKeyValueCollection[] segmentArray)
        {
            Name = string.Empty;
            Fps = 0;
            Frames = Array.Empty<Frame>();

            ConstructFromDesc(animDesc, decodeKey, decoderArray, segmentArray);
        }

        public static IEnumerable<Animation> FromData(IKeyValueCollection animationData, IKeyValueCollection decodeKey)
        {
            var animArray = animationData.GetArray<IKeyValueCollection>("m_animArray");

            if (animArray.Length == 0)
            {
                Console.WriteLine("Empty animation file found.");
                return Enumerable.Empty<Animation>();
            }

            var decoderArray = MakeDecoderArray(animationData.GetArray("m_decoderArray"));
            var segmentArray = animationData.GetArray("m_segmentArray");

            return animArray.Select(anim => new Animation(anim, decodeKey, decoderArray, segmentArray));
        }

        public static IEnumerable<Animation> FromResource(Resource resource, IKeyValueCollection decodeKey)
            => FromData(GetAnimationData(resource), decodeKey);

        private static IKeyValueCollection GetAnimationData(Resource resource)
            => resource.DataBlock.AsKeyValueCollection();

        /// <summary>
        /// Get animation matrices as an array.
        /// </summary>
        public float[] GetAnimationMatricesAsArray(float time, Skeleton skeleton)
        {
            var matrices = GetAnimationMatrices(time, skeleton);
            return Flatten(matrices);
        }

        /// <summary>
        /// Get the animation matrix for each bone.
        /// </summary>
        public Matrix4x4[] GetAnimationMatrices(float time, Skeleton skeleton)
        {
            // Create output array
            var matrices = new Matrix4x4[skeleton.AnimationTextureSize + 1];

            // Get bone transformations
            var transforms = GetTransformsAtTime(time);

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(root, Matrix4x4.Identity, Matrix4x4.Identity, transforms, ref matrices);
            }

            return matrices;
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
                transformMatrix = Matrix4x4.CreateFromQuaternion(transform.Angle) * Matrix4x4.CreateTranslation(transform.Position);
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
        /// Get the transformation matrices at a time.
        /// </summary>
        /// <param name="time">The time to get the transformation for.</param>
        private Frame GetTransformsAtTime(float time)
        {
            // Create output frame
            var frame = new Frame();

            if (FrameCount == 0)
            {
                return frame;
            }

            // Calculate the index of the current frame
            var frameIndex = (int)(time * Fps) % FrameCount;
            var t = ((time * Fps) - frameIndex) % 1;

            // Get current and next frame
            var frame1 = Frames[frameIndex];
            var frame2 = Frames[(frameIndex + 1) % FrameCount];

            // Interpolate bone positions and angles
            foreach (var bonePair in frame1.Bones)
            {
                var position = Vector3.Lerp(frame1.Bones[bonePair.Key].Position, frame2.Bones[bonePair.Key].Position, t);
                var angle = Quaternion.Slerp(frame1.Bones[bonePair.Key].Angle, frame2.Bones[bonePair.Key].Angle, t);
                frame.Bones[bonePair.Key] = new FrameBone(position, angle);
            }

            return frame;
        }

        /// <summary>
        /// Construct an animation class from the animation description.
        /// </summary>
        private void ConstructFromDesc(
            IKeyValueCollection animDesc,
            IKeyValueCollection decodeKey,
            AnimDecoderType[] decoderArray,
            IKeyValueCollection[] segmentArray)
        {
            // Get animation properties
            Name = animDesc.GetProperty<string>("m_name");
            Fps = animDesc.GetFloatProperty("fps");

            var pDataObject = animDesc.GetProperty<object>("m_pData");
            var pData = pDataObject is NTROValue[] ntroArray
                ? ntroArray[0].ValueObject as IKeyValueCollection
                : pDataObject as IKeyValueCollection;
            var frameBlockArray = pData.GetArray("m_frameblockArray");

            FrameCount = pData.GetIntegerProperty("m_nFrames");
            Frames = new Frame[FrameCount];

            // Figure out each frame
            for (var frame = 0; frame < FrameCount; frame++)
            {
                // Create new frame object
                Frames[frame] = new Frame();

                // Read all frame blocks
                foreach (var frameBlock in frameBlockArray)
                {
                    var startFrame = frameBlock.GetIntegerProperty("m_nStartFrame");
                    var endFrame = frameBlock.GetIntegerProperty("m_nEndFrame");

                    // Only consider blocks that actual contain info for this frame
                    if (frame >= startFrame && frame <= endFrame)
                    {
                        var segmentIndexArray = frameBlock.GetIntegerArray("m_segmentIndexArray");

                        foreach (var segmentIndex in segmentIndexArray)
                        {
                            var segment = segmentArray[segmentIndex];
                            ReadSegment(frame - startFrame, segment, decodeKey, decoderArray, ref Frames[frame]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Read segment.
        /// </summary>
        private void ReadSegment(long frame, IKeyValueCollection segment, IKeyValueCollection decodeKey, AnimDecoderType[] decoderArray, ref Frame outFrame)
        {
            // Clamp the frame number to be between 0 and the maximum frame
            frame = frame < 0 ? 0 : frame;
            frame = frame >= FrameCount ? FrameCount - 1 : frame;

            var localChannel = segment.GetIntegerProperty("m_nLocalChannel");
            var dataChannel = decodeKey.GetArray("m_dataChannelArray")[localChannel];
            var boneNames = dataChannel.GetArray<string>("m_szElementNameArray");

            var channelAttribute = dataChannel.GetProperty<string>("m_szVariableName");

            // Read container
            var container = segment.GetArray<byte>("m_container");
            using (var containerReader = new BinaryReader(new MemoryStream(container)))
            {
                var elementIndexArray = dataChannel.GetIntegerArray("m_nElementIndexArray");
                var elementBones = new int[decodeKey.GetProperty<int>("m_nChannelElements")];
                for (var i = 0; i < elementIndexArray.Length; i++)
                {
                    elementBones[elementIndexArray[i]] = i;
                }

                // Read header
                var decoder = decoderArray[containerReader.ReadInt16()];
                var cardinality = containerReader.ReadInt16();
                var numBones = containerReader.ReadInt16();
                var totalLength = containerReader.ReadInt16();

                // Read bone list
                var elements = new List<int>();
                for (var i = 0; i < numBones; i++)
                {
                    elements.Add(containerReader.ReadInt16());
                }

                // Skip data to find the data for the current frame.
                // Structure is just | Bone 0 - Frame 0 | Bone 1 - Frame 0 | Bone 0 - Frame 1 | Bone 1 - Frame 1|
                if (containerReader.BaseStream.Position + (decoder.Size() * frame * numBones) < containerReader.BaseStream.Length)
                {
                    containerReader.BaseStream.Position += decoder.Size() * frame * numBones;
                }

                // Read animation data for all bones
                for (var element = 0; element < numBones; element++)
                {
                    // Get the bone we are reading for
                    var bone = elementBones[elements[element]];

                    // Look at the decoder to see what to read
                    switch (decoder)
                    {
                        case AnimDecoderType.CCompressedStaticFullVector3:
                        case AnimDecoderType.CCompressedFullVector3:
                        case AnimDecoderType.CCompressedDeltaVector3:
                            outFrame.SetAttribute(boneNames[bone], channelAttribute, new Vector3(
                                containerReader.ReadSingle(),
                                containerReader.ReadSingle(),
                                containerReader.ReadSingle()));
                            break;
                        case AnimDecoderType.CCompressedAnimVector3:
                        case AnimDecoderType.CCompressedStaticVector3:
                            outFrame.SetAttribute(boneNames[bone], channelAttribute, new Vector3(
                                ReadHalfFloat(containerReader),
                                ReadHalfFloat(containerReader),
                                ReadHalfFloat(containerReader)));
                            break;
                        case AnimDecoderType.CCompressedAnimQuaternion:
                        case AnimDecoderType.CCompressedFullQuaternion:
                        case AnimDecoderType.CCompressedStaticQuaternion:
                            outFrame.SetAttribute(boneNames[bone], channelAttribute, ReadQuaternion(containerReader));
                            break;
#if DEBUG
                        default:
                            if (channelAttribute != "data")
                            {
                                Console.WriteLine($"Unhandled animation bone decoder type '{decoder}'");
                            }

                            break;
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Read a half-precision float from a binary reader.
        /// </summary>
        /// <param name="reader">Binary ready.</param>
        /// <returns>float.</returns>
        private static float ReadHalfFloat(BinaryReader reader)
        {
            return HalfTypeHelper.Convert(reader.ReadUInt16());
        }

        /// <summary>
        /// Read and decode encoded quaternion.
        /// </summary>
        /// <param name="reader">Binary reader.</param>
        /// <returns>Quaternion.</returns>
        private static Quaternion ReadQuaternion(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(6);

            // Values
            var i1 = bytes[0] + ((bytes[1] & 63) << 8);
            var i2 = bytes[2] + ((bytes[3] & 63) << 8);
            var i3 = bytes[4] + ((bytes[5] & 63) << 8);

            // Signs
            var s1 = bytes[1] & 128;
            var s2 = bytes[3] & 128;
            var s3 = bytes[5] & 128;

            var c = (float)Math.Sin(Math.PI / 4.0f) / 16384.0f;
            var t1 = (float)Math.Sin(Math.PI / 4.0f);
            var x = (bytes[1] & 64) == 0 ? c * (i1 - 16384) : c * i1;
            var y = (bytes[3] & 64) == 0 ? c * (i2 - 16384) : c * i2;
            var z = (bytes[5] & 64) == 0 ? c * (i3 - 16384) : c * i3;

            var w = (float)Math.Sqrt(1 - (x * x) - (y * y) - (z * z));

            // Apply sign 3
            if (s3 == 128)
            {
                w *= -1;
            }

            // Apply sign 1 and 2
            if (s1 == 128)
            {
                return s2 == 128 ? new Quaternion(y, z, w, x) : new Quaternion(z, w, x, y);
            }

            return s2 == 128 ? new Quaternion(w, x, y, z) : new Quaternion(x, y, z, w);
        }

        /// <summary>
        /// Transform the decoder array to a mapping of index to type ID.
        /// </summary>
        private static AnimDecoderType[] MakeDecoderArray(IKeyValueCollection[] decoderArray)
        {
            var array = new AnimDecoderType[decoderArray.Length];
            for (var i = 0; i < decoderArray.Length; i++)
            {
                var decoder = decoderArray[i];
                array[i] = AnimDecoder.FromString(decoder.GetProperty<string>("m_szName"));
            }

            return array;
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
