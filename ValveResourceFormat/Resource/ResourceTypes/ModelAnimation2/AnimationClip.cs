using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2
{
    public record struct QuantizationRange(float Start, float Length);
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

    public class AnimationClip : BinaryKV3
    {
        public string Name { get; private set; }
        public float Fps { get; private set; }

        public int NumFrames { get; private set; }
        public float Duration { get; private set; } = 1;

        public byte[] CompressedPoseData { get; private set; }
        public TrackCompressionSetting[] TrackCompressionSettings { get; private set; }
        public long[] CompressedPoseOffsets { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            Name = resource.FileName;
            NumFrames = Data.GetInt32Property("m_nNumFrames");
            Duration = Data.GetFloatProperty("m_flDuration");

            CompressedPoseData = Data.GetArray<byte>("m_compressedPoseData");

            var settings = Data.GetArray("m_trackCompressionSettings");
            TrackCompressionSettings = new TrackCompressionSetting[settings.Length];

            var i = 0;
            foreach (var setting in settings)
            {
                var rangeX = setting.GetSubCollection("m_translationRangeX");
                var rangeY = setting.GetSubCollection("m_translationRangeY");
                var rangeZ = setting.GetSubCollection("m_translationRangeZ");
                var scaleRange = setting.GetSubCollection("m_scaleRange");

                TrackCompressionSettings[i++] = new TrackCompressionSetting
                {
                    TranslationRangeX = new QuantizationRange(rangeX.GetFloatProperty("m_flRangeStart"), rangeX.GetFloatProperty("m_flRangeLength")),
                    TranslationRangeY = new QuantizationRange(rangeY.GetFloatProperty("m_flRangeStart"), rangeY.GetFloatProperty("m_flRangeLength")),
                    TranslationRangeZ = new QuantizationRange(rangeZ.GetFloatProperty("m_flRangeStart"), rangeZ.GetFloatProperty("m_flRangeLength")),
                    ScaleRange = new QuantizationRange(scaleRange.GetFloatProperty("m_flRangeStart"), scaleRange.GetFloatProperty("m_flRangeLength")),
                    IsRotationStatic = setting.GetProperty<bool>("m_bIsRotationStatic"),
                    IsTranslationStatic = setting.GetProperty<bool>("m_bIsTranslationStatic"),
                    IsScaleStatic = setting.GetProperty<bool>("m_bIsScaleStatic"),
                };
            }

            CompressedPoseOffsets = Data.GetIntegerArray("m_compressedPoseOffsets");

            // Calculate fps
            Fps = NumFrames / Duration;

            var bones = new FrameBone[122];
            ReadFrame(0, bones);
            ReadFrame(1, bones);
            ReadFrame(NumFrames - 1, bones);
        }

        public void ReadFrame(int frameIndex, FrameBone[] bones)
        {
            var frameData = MemoryMarshal.Cast<byte, ushort>(CompressedPoseData);
            frameData = frameData[(int)CompressedPoseOffsets[frameIndex]..];

            for (var i = 0; i < bones.Length; i++)
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
        static Quaternion DecodeQuaternion(ReadOnlySpan<ushort> data)
        {
            Debug.Assert(data.Length >= CompressedQuaternionSize);

            // TODO
            return Quaternion.Identity;
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
            var decodedValue = (normalizedValue * rangeLength) + rangeStart;
            return decodedValue;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float DecodeUnsignedNormalizedFloat(ushort encodedValue)
        {
            return encodedValue / (float)((1 << (sizeof(ushort))) - 1);
        }
    }
}
