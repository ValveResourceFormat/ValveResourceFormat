using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;
using Transform = ValveResourceFormat.ResourceTypes.ModelAnimation.FrameBone;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2
{
    /// <summary>
    /// Represents a quantization range with a start value and length.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/NmCompressionSettings_t::QuantizationRange_t">NmCompressionSettings_t::QuantizationRange_t</seealso>
    public record struct QuantizationRange(float Start, float Length);

    /// <summary>
    /// Represents compression settings for an animation track.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/NmCompressionSettings_t">NmCompressionSettings_t</seealso>
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
    /// A named period of a sync track. Times are fractions of the clip.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/CNmSyncTrack::Event_t">CNmSyncTrack::Event_t</seealso>
    public readonly record struct SyncEvent(string ID, float StartTime, float Duration);

    /// <summary>
    /// Divides a clip into named periods. Clips of different lengths use this for aligning.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/CNmSyncTrack">CNmSyncTrack</seealso>
    public readonly record struct SyncTrack(SyncEvent[] Events, int StartEventOffset);

    /// <summary>
    /// The root motion of a clip, as the transform of the root on each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/CNmRootMotionData">CNmRootMotionData</seealso>
    public readonly record struct RootMotionData
    (
        Transform[] Transforms,
        int NumFrames,
        float AverageLinearVelocity,
        float AverageAngularVelocityRadians,
        Transform TotalDelta
    );

    /// <summary>
    /// A link in the chain of bones that is evaluated to sample a bone in model space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/CNmClip::ModelSpaceSamplingChainLink_t">CNmClip::ModelSpaceSamplingChainLink_t</seealso>
    public readonly record struct ModelSpaceSamplingChainLink(int BoneIndex, int ParentBoneIndex, int ParentChainLinkIndex);

    /// <summary>
    /// Represents an animation clip with compressed pose data.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/CNmClip">CNmClip</seealso>
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
        /// Gets the offsets (in 16-bit / ushort units) into the compressed pose data for each frame.
        /// </summary>
        public long[] CompressedPoseOffsets { get; private set; } = [];

        /// <summary>
        /// Gets the secondary animations associated with this clip.
        /// </summary>
        public AnimationClip[] SecondaryAnimations { get; private set; } = [];

        /// <summary>
        /// Gets a value indicating whether this clip contains transforms that are to be blended additively.
        /// </summary>
        public bool IsAdditive { get; private set; }

        /// <summary>
        /// Gets the events fired during playback of this clip, ordered by start time. Times are in seconds.
        /// Consumers filter for the event types they are interested in (e.g. <see cref="NmSoundEvent"/>).
        /// </summary>
        public NmClipEvent[] Events { get; private set; } = [];

        /// <summary>
        /// Gets the accumulated root motion per frame (position plus unwrapped yaw in degrees).
        /// Empty when the clip stores no root motion; static clips store a single identity
        /// placeholder, which is treated as no root motion.
        /// </summary>
        public AnimationMovement.MovementData[] RootMotion { get; private set; } = [];

        /// <summary>
        /// Gets the clip's named scalar channels (float curves), decoded per frame.
        /// </summary>
        public AnimationFloatCurve[] FloatCurves { get; private set; } = [];

        /// <summary>
        /// Gets the sync track of the clip.
        /// </summary>
        public SyncTrack SyncTrack { get; private set; } = new([], 0);

        /// <summary>
        /// Gets the root motion as stored, which <see cref="RootMotion"/> is derived from.
        /// </summary>
        public RootMotionData RootMotionData { get; private set; } = new([], 0, 0f, 0f, Transform.Identity);

        /// <summary>
        /// Gets the chain of bones evaluated to sample bones in model space.
        /// </summary>
        public ModelSpaceSamplingChainLink[] ModelSpaceSamplingChain { get; private set; } = [];

        /// <summary>
        /// Gets the bones sampled in model space, as indices into <see cref="ModelSpaceSamplingChain"/>.
        /// </summary>
        public int[] ModelSpaceBoneSamplingIndices { get; private set; } = [];

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            Name = Resource.FileName ?? string.Empty;

            if (Name.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                Name = Name[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            ReadClip(Data);
            Data = null;
        }

        private void ReadClip(KVObject clipData)
        {
            SkeletonName = clipData.GetStringProperty("m_skeleton");
            NumFrames = clipData.GetInt32Property("m_nNumFrames");
            Duration = clipData.GetFloatProperty("m_flDuration");
            IsAdditive = clipData.GetBooleanProperty("m_bIsAdditive");

            if (clipData.GetSubCollection("m_rootMotion") is { } rootMotion)
            {
                RootMotionData = new RootMotionData(
                    [.. (rootMotion.GetArray("m_transforms") ?? []).Select(ReadTransform)],
                    rootMotion.GetInt32Property("m_nNumFrames"),
                    rootMotion.GetFloatProperty("m_flAverageLinearVelocity"),
                    rootMotion.GetFloatProperty("m_flAverageAngularVelocityRadians"),
                    rootMotion.TryGetValue("m_totalDelta", out var totalDelta) ? ReadTransform(totalDelta) : Transform.Identity);
            }

            var rootTransforms = RootMotionData.Transforms;
            if (rootTransforms.Length > 1)
            {
                RootMotion = new AnimationMovement.MovementData[rootTransforms.Length];
                var previousYaw = 0f;

                for (var t = 0; t < rootTransforms.Length; t++)
                {
                    var yaw = EntityTransformHelper.ToEulerAngles(rootTransforms[t].Angle).Y;
                    yaw += 360f * MathF.Round((previousYaw - yaw) / 360f);
                    previousYaw = yaw;

                    RootMotion[t] = new AnimationMovement.MovementData(rootTransforms[t].Position, yaw);
                }
            }

            if (clipData.GetSubCollection("m_syncTrack") is { } syncTrack)
            {
                SyncTrack = new SyncTrack(
                    [.. (syncTrack.GetArray("m_syncEvents") ?? []).Select(static e => new SyncEvent(
                        e.GetStringProperty("m_ID", string.Empty),
                        e.GetSubCollection("m_startTime")?.GetFloatProperty("m_flValue") ?? 0f,
                        e.GetSubCollection("m_duration")?.GetFloatProperty("m_flValue") ?? 0f))],
                    syncTrack.GetInt32Property("m_nStartEventOffset"));
            }

            ModelSpaceSamplingChain = [.. (clipData.GetArray("m_modelSpaceSamplingChain") ?? []).Select(static link => new ModelSpaceSamplingChainLink(
                link.GetInt32Property("m_nBoneIdx", -1),
                link.GetInt32Property("m_nParentBoneIdx", -1),
                link.GetInt32Property("m_nParentChainLinkIdx", -1)))];
            ModelSpaceBoneSamplingIndices = [.. clipData.GetIntegerArray("m_modelSpaceBoneSamplingIndices").Select(static i => (int)i)];

            TrackCompressionSettings = ReadTrackCompressionSettings(clipData);
            FloatCurves = ReadFloatCurves(clipData);
            CompressedPoseData = clipData.GetArray<byte>("m_compressedPoseData");

            var events = clipData.GetArray("m_events") ?? [];
            Events = new NmClipEvent[events.Count];

            for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                Events[eventIndex] = NmClipEvent.Build(events[eventIndex], Duration);
            }

            CompressedPoseOffsets = clipData.GetIntegerArray("m_compressedPoseOffsets");
            Debug.Assert(CompressedPoseOffsets.Length == NumFrames);

            var secondaryAnims = clipData.GetArray("m_secondaryAnimations") ?? [];
            SecondaryAnimations = new AnimationClip[secondaryAnims.Count];
            for (var j = 0; j < secondaryAnims.Count; j++)
            {
                var secondaryAnim = new AnimationClip()
                {
                    Resource = Resource,
                    // Not made unique, glTF animations can apply to multiple skeletons
                    Name = Name,
                };
                secondaryAnim.ReadClip(secondaryAnims[j]);
                SecondaryAnimations[j] = secondaryAnim;
            }
        }

        private static Transform ReadTransform(KVObject data)
        {
            var (position, scale, rotation) = data.ToTransform();
            return new Transform(position, scale, rotation);
        }

        private AnimationFloatCurve[] ReadFloatCurves(KVObject clipData)
        {
            var floatCurveIds = clipData.GetArray<string>("m_floatCurveIDs");
            if (floatCurveIds is not { Length: > 0 })
            {
                return [];
            }

            var curveDefs = clipData.GetArray("m_floatCurveDefs");
            var curveData = clipData.GetIntegerArray("m_compressedFloatCurveData");
            var curveOffsets = clipData.GetIntegerArray("m_compressedFloatCurveOffsets");

            var curves = new AnimationFloatCurve[floatCurveIds.Length];

            // Dynamic curves are packed densely per frame in declaration order; static curves
            // hold their range start and store no samples.
            var dynamicRank = 0;
            for (var c = 0; c < floatCurveIds.Length; c++)
            {
                var def = curveDefs[c];
                var range = def.GetSubCollection("m_range");
                var rangeStart = range.GetFloatProperty("m_flRangeStart");
                var rangeLength = range.GetFloatProperty("m_flRangeLength");

                var values = new float[NumFrames];
                Array.Fill(values, rangeStart);

                if (!def.GetBooleanProperty("m_bIsStatic"))
                {
                    var decodableFrames = Math.Min(NumFrames, curveOffsets.Length);
                    for (var f = 0; f < decodableFrames; f++)
                    {
                        var sampleIndex = curveOffsets[f] + dynamicRank;
                        if (sampleIndex < 0 || sampleIndex >= curveData.Length)
                        {
                            continue;
                        }

                        values[f] = DecodeFloat((ushort)curveData[sampleIndex], rangeStart, rangeLength);
                    }

                    dynamicRank++;
                }

                curves[c] = new AnimationFloatCurve(floatCurveIds[c], values);
            }

            return curves;
        }

        private static TrackCompressionSetting[] ReadTrackCompressionSettings(KVObject clipData)
        {
            var settings = clipData.GetArray("m_trackCompressionSettings");
            var trackSettings = new TrackCompressionSetting[settings.Count];

            var i = 0;
            foreach (var setting in settings)
            {
                var rangeX = setting.GetSubCollection("m_translationRangeX");
                var rangeY = setting.GetSubCollection("m_translationRangeY");
                var rangeZ = setting.GetSubCollection("m_translationRangeZ");
                var scaleRange = setting.GetSubCollection("m_scaleRange");
                var constantRotation = setting.GetFloatArray("m_constantRotation");

                trackSettings[i++] = new TrackCompressionSetting
                {
                    TranslationRangeX = new QuantizationRange(rangeX.GetFloatProperty("m_flRangeStart"), rangeX.GetFloatProperty("m_flRangeLength")),
                    TranslationRangeY = new QuantizationRange(rangeY.GetFloatProperty("m_flRangeStart"), rangeY.GetFloatProperty("m_flRangeLength")),
                    TranslationRangeZ = new QuantizationRange(rangeZ.GetFloatProperty("m_flRangeStart"), rangeZ.GetFloatProperty("m_flRangeLength")),
                    ScaleRange = new QuantizationRange(scaleRange.GetFloatProperty("m_flRangeStart"), scaleRange.GetFloatProperty("m_flRangeLength")),
                    ConstantRotation = new Quaternion(constantRotation[0], constantRotation[1], constantRotation[2], constantRotation[3]),
                    IsRotationStatic = setting.GetBooleanProperty("m_bIsRotationStatic"),
                    IsTranslationStatic = setting.GetBooleanProperty("m_bIsTranslationStatic"),
                    IsScaleStatic = setting.GetBooleanProperty("m_bIsScaleStatic"),
                };
            }

            return trackSettings;
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

            // The omitted component is the largest, so valid data never goes negative here; truncated data can
            vData.W = MathF.Sqrt(MathF.Max(0f, 1f - sum));

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
