using System;
using System.Collections.Generic;
using System.IO;
using OpenTK;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;
using Vector3 = OpenTK.Vector3;

namespace GUI.Types.Renderer.Animation
{
    internal class Animation
    {
        public string Name { get; private set; }
        public float Fps { get; private set; }

        private int FrameCount;

        private Frame[] Frames;

        private Skeleton Skeleton;

        // Build animation from resource
        public Animation(Resource resource, NTROStruct decodeKey, Skeleton skeleton)
        {
            Name = string.Empty;
            Fps = 0;
            Frames = new Frame[0];

            Skeleton = skeleton;

            var animationData = (NTRO)resource.Blocks[BlockType.DATA];
            var animArray = (NTROArray)animationData.Output["m_animArray"];

            if (animArray.Count == 0)
            {
                Console.WriteLine("Empty animation file found.");
                return;
            }

            var decoderArray = MakeDecoderArray((NTROArray)animationData.Output["m_decoderArray"]);
            var segmentArray = (NTROArray)animationData.Output["m_segmentArray"];

            // Get the first animation description
            ConstructFromDesc(animArray.Get<NTROStruct>(0), decodeKey, decoderArray, segmentArray);
        }

        public float[] GetAnimationMatricesAsArray(float time, Skeleton skeleton)
        {
            var matrices = GetAnimationMatrices(time, skeleton);
            return Flatten(matrices);
        }

        // Get the animation matrix for each bone
        public Matrix4[] GetAnimationMatrices(float time, Skeleton skeleton)
        {
            // Create output array
            var matrices = new Matrix4[skeleton.Bones.Length];

            // Get bone transformations
            var transforms = GetTransformsAtTime(time);

            foreach (var root in skeleton.Roots)
            {
                GetAnimationMatrixRecursive(root, Matrix4.Identity, Matrix4.Identity, transforms, ref matrices);
            }

            return matrices;
        }

        private void GetAnimationMatrixRecursive(Bone bone, Matrix4 parentBindPose, Matrix4 parentInvBindPose, Frame transforms, ref Matrix4[] matrices)
        {
            // Calculate world space bind and inverse bind pose
            var bindPose = parentBindPose;
            var invBindPose = parentInvBindPose * bone.InverseBindPose;

            // Calculate transformation matrix
            var transformMatrix = Matrix4.Identity;
            if (transforms.Bones.ContainsKey(bone.Name))
            {
                var transform = transforms.Bones[bone.Name];
                transformMatrix = Matrix4.CreateFromQuaternion(transform.Angle) * Matrix4.CreateTranslation(transform.Position);
            }

            // Apply tranformation
            var transformed = transformMatrix * bindPose;

            // Store result
            if (bone.Index != -1)
            {
                matrices[bone.Index] = invBindPose * transformed;
            }

            // Propagate to childen
            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(child, transformed, invBindPose, transforms, ref matrices);
            }
        }

        // Get the transformation matrices at a time
        private Frame GetTransformsAtTime(float time)
        {
            // Calculate the index of the current frame
            var frameIndex = (int)(time * Fps) % FrameCount;
            var t = ((time * Fps) - frameIndex) % 1;

            // Get current and next frame
            var frame1 = Frames[frameIndex];
            var frame2 = Frames[(frameIndex + 1) % FrameCount];

            // Create output frame
            var frame = new Frame();

            var length = FrameCount / Fps;

            // Interpolate bone positions and angles
            foreach (var bonePair in frame1.Bones)
            {
                var position = Vector3.Lerp(frame1.Bones[bonePair.Key].Position, frame2.Bones[bonePair.Key].Position, t);
                var angle = Quaternion.Slerp(frame1.Bones[bonePair.Key].Angle, frame2.Bones[bonePair.Key].Angle, t);
                frame.Bones[bonePair.Key] = new FrameBone(position, angle);
            }

            return frame;
        }

        // Construct an animation class from the animation description
        private void ConstructFromDesc(NTROStruct animDesc, NTROStruct decodeKey, AnimDecoderType[] decoderArray, NTROArray segmentArray)
        {
            // Get animation properties
            Name = animDesc.Get<string>("m_name");
            Fps = animDesc.Get<float>("fps");

            // Only consider first frame block for now
            var pData = animDesc.Get<NTROArray>("m_pData").Get<NTROStruct>(0);
            var frameBlockArray = pData.Get<NTROArray>("m_frameblockArray").ToArray<NTROStruct>();

            FrameCount = pData.Get<int>("m_nFrames");
            Frames = new Frame[FrameCount];

            // Figure out each frame
            for (var frame = 0; frame < FrameCount; frame++)
            {
                // Create new frame object
                Frames[frame] = new Frame();

                // Read all frame blocks
                foreach (var frameBlock in frameBlockArray)
                {
                    var startFrame = frameBlock.Get<int>("m_nStartFrame");
                    var endFrame = frameBlock.Get<int>("m_nEndFrame");

                    // Only consider blocks that actual contain info for this frame
                    if (frame >= startFrame && frame <= endFrame)
                    {
                        var segmentIndexArray = frameBlock.Get<NTROArray>("m_segmentIndexArray").ToArray<int>();

                        foreach (var segmentIndex in segmentIndexArray)
                        {
                            var segment = segmentArray.Get<NTROStruct>(segmentIndex);
                            ReadSegment(frame - startFrame, segment, decodeKey, decoderArray, ref Frames[frame]);
                        }
                    }
                }
            }
        }

        private void ReadSegment(int frame, NTROStruct segment, NTROStruct decodeKey, AnimDecoderType[] decoderArray, ref Frame outFrame)
        {
            //Clamp the frame number to be between 0 and the maximum frame
            frame = frame < 0 ? 0 : frame;
            frame = frame >= FrameCount ? FrameCount - 1 : frame;

            var localChannel = segment.Get<int>("m_nLocalChannel");
            var dataChannel = decodeKey.Get<NTROArray>("m_dataChannelArray").Get<NTROStruct>(localChannel);
            var boneNames = dataChannel.Get<NTROArray>("m_szElementNameArray").ToArray<string>();

            var channelAttribute = dataChannel.Get<string>("m_szVariableName");

            // Read container
            var container = segment.Get<NTROArray>("m_container").ToArray<byte>();
            using (var containerReader = new BinaryReader(new MemoryStream(container)))
            {
                var elementIndexArray = dataChannel.Get<NTROArray>("m_nElementIndexArray").ToArray<int>();
                var elementBones = new int[decodeKey.Get<int>("m_nChannelElements")];
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
                containerReader.BaseStream.Position += decoder.Size() * frame * numBones;

                // Read animation data for all bones
                for (var element = 0; element < numBones; element++)
                {
                    //Get the bone we are reading for
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
                        case AnimDecoderType.CCompressedStaticVector:
                            outFrame.SetAttribute(boneNames[bone], channelAttribute, new Vector3(
                                ReadHalfFloat(containerReader),
                                ReadHalfFloat(containerReader),
                                ReadHalfFloat(containerReader)));
                            break;
                        case AnimDecoderType.CCompressedAnimQuaternion:
                            outFrame.SetAttribute(boneNames[bone], channelAttribute, ReadQuaternion(containerReader));
                            break;
                    }
                }
            }
        }

        // Read a half-precision float from a binary reader
        private float ReadHalfFloat(BinaryReader reader)
        {
            int i = reader.ReadInt16();

            var i1 = i & 0x7fff; // Non-sign bits
            var i2 = i & 0x8000; // Sign
            var i3 = i & 0x7c00; // Exponent

            i1 <<= 13; // Shift significand
            i2 <<= 16; // Shift sign bit;

            i1 += 0x38000000; // Adjust bias
            i1 = i3 == 0 ? 0 : i1; // Denormals as zero
            i1 |= i2; // Add the sign bit again

            return BitConverter.ToSingle(BitConverter.GetBytes(i1), 0);
        }

        //Read and decode encoded quaternion
        private Quaternion ReadQuaternion(BinaryReader reader)
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

        // Transform the decoder array to a mapping of index to type ID
        private AnimDecoderType[] MakeDecoderArray(NTROArray decoderArray)
        {
            var array = new AnimDecoderType[decoderArray.Count];
            for (var i = 0; i < decoderArray.Count; i++)
            {
                var decoder = decoderArray.Get<NTROStruct>(i);
                array[i] = AnimDecoder.FromString(decoder.Get<string>("m_szName"));
            }

            return array;
        }

        // Flatten an array of matrices to an array of floats
        private float[] Flatten(Matrix4[] matrices)
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

    internal class Frame
    {
        public Dictionary<string, FrameBone> Bones { get; set; }

        public Frame()
        {
            Bones = new Dictionary<string, FrameBone>();
        }

        public void SetAttribute(string bone, string attribute, object data)
        {
            switch (attribute)
            {
                case "Position":
                    InsertIfUnknown(bone);
                    Bones[bone].Position = (Vector3)data;
                    break;
                case "Angle":
                    InsertIfUnknown(bone);
                    Bones[bone].Angle = (Quaternion)data;
                    break;
                case "data":
                    //ignore
                    break;
#if DEBUG
                default:
                    Console.WriteLine($"Unknown frame attribute '{attribute}' encountered");
                    break;
#endif
            }
        }

        private void InsertIfUnknown(string name)
        {
            if (!Bones.ContainsKey(name))
            {
                Bones[name] = new FrameBone(Vector3.Zero, Quaternion.Identity);
            }
        }
    }

    internal class FrameBone
    {
        public Vector3 Position { get; set; }
        public Quaternion Angle { get; set; }

        public FrameBone(Vector3 pos, Quaternion a)
        {
            Position = pos;
            Angle = a;
        }
    }
}
