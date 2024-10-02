using System.IO;
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
        }
    }
}
