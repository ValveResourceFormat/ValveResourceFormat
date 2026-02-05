using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2
{
    /// <summary>
    /// Represents a quantization range with a start value and length.
    /// </summary>
    public record struct QuantizationRange(float Start, float Length);

    /// <summary>
    /// Represents compression settings for an animation track.
    /// </summary>
    public record struct TrackCompressionSetting
    (
        QuantizationRange TranslationRangeX,
        QuantizationRange TranslationRangeY,
        QuantizationRange TranslationRangeZ,
        QuantizationRange ScaleRange,
        Quaternion ConstantRotation,
        bool IsRotationStatic,
        bool IsTranslationStatic,
        bool IsScaleStatic
    );

    /// <summary>
    /// Represents an animation clip with compressed pose data.
    /// </summary>
    public class AnimationClip : BinaryKV3
    {
        /// <summary>
        /// Gets the name of the animation clip.
        /// </summary>
        public string Name { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the name of the skeleton this animation is for.
        /// </summary>
        public string SkeletonName { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the number of frames in the animation.
        /// </summary>
        public int NumFrames { get; private set; }

        /// <summary>
        /// Gets the duration of the animation in seconds.
        /// </summary>
        public float Duration { get; private set; } = 1;

        /// <summary>
        /// Gets the compressed pose data.
        /// </summary>
        public byte[] CompressedPoseData { get; private set; } = [];

        /// <summary>
        /// Gets the compression settings for each animation track.
        /// </summary>
        public TrackCompressionSetting[] TrackCompressionSettings { get; private set; } = [];

        /// <summary>
        /// Gets the byte offsets for each compressed pose frame.
        /// </summary>
        public long[] CompressedPoseOffsets { get; private set; } = [];

        /// <summary>
        /// Gets the secondary animations associated with this clip.
        /// </summary>
        public AnimationClip[] SecondaryAnimations { get; private set; } = [];

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            Name = Resource.FileName ?? string.Empty;

            ReadClip(Data);
        }

        private void ReadClip(KVObject clipData)
        {
            SkeletonName = clipData.GetStringProperty("m_skeleton");
            NumFrames = clipData.GetInt32Property("m_nNumFrames");
            Duration = clipData.GetFloatProperty("m_flDuration");

            CompressedPoseData = clipData.GetArray<byte>("m_compressedPoseData");

            var settings = clipData.GetArray("m_trackCompressionSettings");
            TrackCompressionSettings = new TrackCompressionSetting[settings.Length];

            var i = 0;
            foreach (var setting in settings)
            {
                var rangeX = setting.GetSubCollection("m_translationRangeX");
                var rangeY = setting.GetSubCollection("m_translationRangeY");
                var rangeZ = setting.GetSubCollection("m_translationRangeZ");
                var scaleRange = setting.GetSubCollection("m_scaleRange");
                var constantRotation = setting.GetFloatArray("m_constantRotation");

                TrackCompressionSettings[i++] = new TrackCompressionSetting
                {
                    TranslationRangeX = new QuantizationRange(rangeX.GetFloatProperty("m_flRangeStart"), rangeX.GetFloatProperty("m_flRangeLength")),
                    TranslationRangeY = new QuantizationRange(rangeY.GetFloatProperty("m_flRangeStart"), rangeY.GetFloatProperty("m_flRangeLength")),
                    TranslationRangeZ = new QuantizationRange(rangeZ.GetFloatProperty("m_flRangeStart"), rangeZ.GetFloatProperty("m_flRangeLength")),
                    ScaleRange = new QuantizationRange(scaleRange.GetFloatProperty("m_flRangeStart"), scaleRange.GetFloatProperty("m_flRangeLength")),
                    ConstantRotation = new Quaternion(constantRotation[0], constantRotation[1], constantRotation[2], constantRotation[3]),
                    IsRotationStatic = setting.GetProperty<bool>("m_bIsRotationStatic"),
                    IsTranslationStatic = setting.GetProperty<bool>("m_bIsTranslationStatic"),
                    IsScaleStatic = setting.GetProperty<bool>("m_bIsScaleStatic"),
                };
            }

            CompressedPoseOffsets = clipData.GetIntegerArray("m_compressedPoseOffsets");
            Debug.Assert(CompressedPoseOffsets.Length == NumFrames);

            var secondaryAnims = clipData.GetArray("m_secondaryAnimations") ?? [];
            SecondaryAnimations = new AnimationClip[secondaryAnims.Length];
            for (var j = 0; j < secondaryAnims.Length; j++)
            {
                var secondaryAnim = new AnimationClip()
                {
                    Resource = Resource,
                    Name = $"{Name}.secondary_{j}"
                };
                secondaryAnim.ReadClip(secondaryAnims[j]);
                SecondaryAnimations[j] = secondaryAnim;
            }
        }

        /// <summary>
        /// Reads and decompresses a specific frame of animation data into the provided bone array.
        /// </summary>
        /// <param name="frameIndex">The index of the frame to read.</param>
        /// <param name="bones">The array of bones to populate with frame data.</param>
        public void ReadFrame(int frameIndex, FrameBone[] bones)
        {
            var frameData = MemoryMarshal.Cast<byte, ushort>(CompressedPoseData);
            frameData = frameData[(int)CompressedPoseOffsets[frameIndex]..];

            for (var i = 0; i < TrackCompressionSettings.Length && i < bones.Length; i++)
            {
                var config = TrackCompressionSettings[i];

                var translationRangeStart = new Vector3(
                    config.TranslationRangeX.Start,
                    config.TranslationRangeY.Start,
                    config.TranslationRangeZ.Start
                );

                bones[i].Angle = config.ConstantRotation;
                bones[i].Position = translationRangeStart;
                bones[i].Scale = config.ScaleRange.Start;

                if (!config.IsRotationStatic)
                {
                    bones[i].Angle = DecodeQuaternion(frameData);
                    frameData = frameData[CompressedQuaternionSize..];
                }

                if (!config.IsTranslationStatic)
                {
                    var translationRangeLength = new Vector3(
                        config.TranslationRangeX.Length,
                        config.TranslationRangeY.Length,
                        config.TranslationRangeZ.Length
                    );

                    bones[i].Position = DecodeTranslation(frameData, translationRangeStart, translationRangeLength);
                    frameData = frameData[CompressedTranslationSize..];
                }

                if (!config.IsScaleStatic)
                {
                    bones[i].Scale = DecodeFloat(frameData[0], config.ScaleRange.Start, config.ScaleRange.Length);
                    frameData = frameData[1..];
                }
            }
        }

        const int CompressedQuaternionSize = 3;
        private static readonly float s_valueRangeMin = -1.0f / MathF.Sqrt(2.0f);
        private static readonly float s_valueRangeMax = 1.0f / MathF.Sqrt(2.0f);
        private static readonly float s_valueRangeLength = s_valueRangeMax - s_valueRangeMin;

        static Quaternion DecodeQuaternion(ReadOnlySpan<ushort> data)
        {
            Debug.Assert(data.Length >= CompressedQuaternionSize);

            var vValueRangeMin = new Vector4(s_valueRangeMin);
            var vRangeMultiplier15Bit = new Vector4(s_valueRangeLength / 0x7FFF);

            var vData = new Vector4(
                data[0] & 0x7FFF,
                data[1] & 0x7FFF,
                data[2],
                0
            );

            vData = Vector4.FusedMultiplyAdd(vData, vRangeMultiplier15Bit, vValueRangeMin);

            var sum = Vector3.Dot(vData.AsVector3(), vData.AsVector3());
            vData.W = MathF.Sqrt(1f - sum);

            // Vector128.Shuffle(vData.AsVector128(), Vector128.Create([3, 0, 1, 2]));

            var largestValueIndex = (ushort)((data[0] >> 14 & 0x0002) | data[1] >> 15);
            return largestValueIndex switch
            {
                0 => new Quaternion(vData[3], vData[0], vData[1], vData[2]),
                1 => new Quaternion(vData[0], vData[3], vData[1], vData[2]),
                2 => new Quaternion(vData[0], vData[1], vData[3], vData[2]),
                3 => new Quaternion(vData[0], vData[1], vData[2], vData[3]),
                _ => throw new InvalidOperationException("Invalid largest value index")
            };
        }

        const int CompressedTranslationSize = 3;
        static Vector3 DecodeTranslation(ReadOnlySpan<ushort> data, Vector3 rangeStart, Vector3 rangeLength)
        {
            Debug.Assert(data.Length >= CompressedTranslationSize);

            return new Vector3(
                DecodeFloat(data[0], rangeStart.X, rangeLength.X),
                DecodeFloat(data[1], rangeStart.Y, rangeLength.Y),
                DecodeFloat(data[2], rangeStart.Z, rangeLength.Z)
            );
        }

        static float DecodeFloat(ushort unorm, float rangeStart, float rangeLength)
        {
            Debug.Assert(rangeLength != 0);

            var normalizedValue = DecodeUnsignedNormalizedFloat(unorm);
            var decodedValue = normalizedValue * rangeLength + rangeStart;
            return decodedValue;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float DecodeUnsignedNormalizedFloat(ushort encodedValue)
        {
            return encodedValue / (float)ushort.MaxValue;
        }
    }
}
