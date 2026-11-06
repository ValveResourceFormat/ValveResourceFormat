using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class Source1AnimationSource
{
    private const int IkTypeSelf = 1;
    private const int IkTypeRelease = 4;
    private const int IkTypeUnlatch = 6;

    private const float MinIkWeight = 0.001f;
    private const float FullIkWeight = 0.999f;

    // The last frame of a looping animation is the first frame again, so a cycle never reaches 1
    private const float MaxIkCycle = 0.9999f;

    private const int IkErrorSize = 28;
    private const int CompressedIkErrorChannels = 6;
    private const int CompressedIkErrorOffsets = CompressedIkErrorChannels * sizeof(float);

    /// <summary>Chains of the base model.</summary>
    public MdlFile.IkChain[] IkChains { get; init; } = [];

    /// <summary>Index of the chain of the base model with the name of each chain of this file, -1 when there is none.</summary>
    public int[] IkChainMap { get; init; } = [];

    private readonly record struct IkChainRule(int Type, float Weight, int Bone, Vector3 Position, Quaternion Rotation);

    /// <summary>
    /// Moves the limbs of decoded frames of one animation to the targets of its self-relative IK rules, which hold a hand
    /// or a foot at a position relative to a bone of the same pose. Rules of other types and IK locks are not applied.
    /// </summary>
    public sealed class IkApplier
    {
        private static readonly int RuleSize = Marshal.SizeOf<MdlFile.StudioIkRule>();

        private readonly Source1AnimationSource source;
        private readonly int frameCount;
        private readonly byte[] data;
        private readonly int rulesOffset;
        private readonly MdlFile.StudioIkRule[] rules;
        private readonly List<IkChainRule>[] chainRules;
        private readonly Matrix4x4[] world;
        private readonly Vector3[] targetPositions;
        private readonly Quaternion[] targetRotations;
        private readonly float[] targetWeights;

        internal IkApplier(Source1AnimationSource source, int frameCount, byte[] data, int rulesOffset, MdlFile.StudioIkRule[] rules)
        {
            this.source = source;
            this.frameCount = frameCount;
            this.data = data;
            this.rulesOffset = rulesOffset;
            this.rules = rules;

            chainRules = new List<IkChainRule>[source.IkChains.Length];

            for (var i = 0; i < chainRules.Length; i++)
            {
                chainRules[i] = [];
            }

            world = new Matrix4x4[source.BaseBoneCount];
            targetPositions = new Vector3[chainRules.Length];
            targetRotations = new Quaternion[chainRules.Length];
            targetWeights = new float[chainRules.Length];
        }

        /// <summary>Moves the limbs of a decoded frame to the targets of the rules at that frame.</summary>
        public void Apply(int frame, Span<Vector3> positions, Span<Quaternion> rotations)
        {
            var cycle = MathF.Min(frame / (float)(frameCount - 1), MaxIkCycle);
            var hasRules = false;

            foreach (var list in chainRules)
            {
                list.Clear();
            }

            for (var i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                var chain = source.GetBaseIkChain(rule.Chain);

                if (chain < 0)
                {
                    continue;
                }

                var weight = GetIkRuleWeight(rule, cycle);

                if (weight <= MinIkWeight)
                {
                    continue;
                }

                var position = Vector3.Zero;
                var rotation = Quaternion.Identity;
                var bone = -1;

                if (rule.Type == IkTypeSelf)
                {
                    var ruleOffset = rulesOffset + (i * RuleSize);

                    if (!TryReadIkError(data, ruleOffset, rule, frameCount, cycle, out position, out rotation))
                    {
                        continue;
                    }

                    if (rule.Bone != -1)
                    {
                        bone = (uint)rule.Bone < (uint)source.BoneToBase.Length ? source.BoneToBase[rule.Bone] : -1;

                        if (bone < 0)
                        {
                            continue;
                        }
                    }
                }

                var list = chainRules[chain];

                // A rule at full strength replaces the rules before it
                if (weight > FullIkWeight && rule.Type != IkTypeUnlatch)
                {
                    list.Clear();

                    if (rule.Type == IkTypeRelease)
                    {
                        continue;
                    }
                }

                if (rule.Type == IkTypeSelf || rule.Type == IkTypeRelease)
                {
                    list.Add(new IkChainRule(rule.Type, weight, bone, position, rotation));
                    hasRules = true;
                }
            }

            if (!hasRules)
            {
                return;
            }

            source.SolveIkChains(chainRules, world, targetPositions, targetRotations, targetWeights, positions, rotations);
        }
    }

    /// <summary>Creates the applier of the IK rules of an animation, or null when there is nothing to apply.</summary>
    public IkApplier? CreateIkApplier(MdlFile.AnimDesc desc, int descOffset)
    {
        var flags = (Source1AnimationFlags)desc.Flags;

        if (IkChains.Length == 0 || desc.NumIkRules <= 0 || desc.NumFrames <= 1 || (flags & (Source1AnimationFlags.Delta | Source1AnimationFlags.AllZeros)) != 0)
        {
            return null;
        }

        if (!TryGetIkRuleData(desc, descOffset, out var data, out var rulesOffset))
        {
            return null;
        }

        var rules = LumpReader<MdlFile.StudioIkRule>.ReadLumpAt(data, rulesOffset, desc.NumIkRules);

        return new IkApplier(this, desc.NumFrames, data, rulesOffset, rules);
    }

    private void SolveIkChains(List<IkChainRule>[] chainRules, Matrix4x4[] world, Vector3[] targetPositions, Quaternion[] targetRotations, float[] targetWeights, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        for (var i = 0; i < world.Length; i++)
        {
            var local = Matrix4x4.CreateFromQuaternion(rotations[i]);
            local.Translation = positions[i];

            var parent = BaseParents[i];
            world[i] = (uint)parent < (uint)i ? local * world[parent] : local;
        }

        Array.Clear(targetWeights);

        // Every target is found in the pose before any chain moves
        for (var chain = 0; chain < IkChains.Length; chain++)
        {
            var rules = chainRules[chain];

            if (rules.Count == 0)
            {
                continue;
            }

            var footWorld = world[IkChains[chain].Foot];
            var originalPosition = footWorld.Translation;
            var originalRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(footWorld));
            var chainPosition = originalPosition;
            var chainRotation = originalRotation;
            var chainWeight = 0f;

            foreach (var rule in rules)
            {
                if (rule.Type == IkTypeRelease)
                {
                    chainPosition = MathUtils.Lerp(chainPosition, originalPosition, rule.Weight);
                    chainRotation = Source1PoseMath.Slerp(chainRotation, originalRotation, rule.Weight);
                    continue;
                }

                var local = Matrix4x4.CreateFromQuaternion(rule.Rotation);
                local.Translation = rule.Position;

                var target = rule.Bone >= 0 ? local * world[rule.Bone] : local;
                var targetPosition = target.Translation;
                var targetRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(target));

                chainWeight = (chainWeight * (1f - rule.Weight)) + rule.Weight;
                chainPosition = MathUtils.Lerp(chainPosition, targetPosition, rule.Weight);
                chainRotation = Source1PoseMath.Slerp(chainRotation, targetRotation, rule.Weight);
            }

            targetPositions[chain] = chainPosition;
            targetRotations[chain] = chainRotation;
            targetWeights[chain] = chainWeight;
        }

        for (var chain = 0; chain < IkChains.Length; chain++)
        {
            if (targetWeights[chain] <= 0f)
            {
                continue;
            }

            var hip = IkChains[chain].Hip;
            var knee = IkChains[chain].Knee;
            var foot = IkChains[chain].Foot;

            if (!Source1IkSolver.Solve(world, hip, knee, foot, targetPositions[chain]))
            {
                continue;
            }

            var footPosition = world[foot].Translation;
            world[foot] = Matrix4x4.CreateFromQuaternion(targetRotations[chain]);
            world[foot].Translation = footPosition;

            SetLocalFromWorld(foot, world, positions, rotations);
            SetLocalFromWorld(knee, world, positions, rotations);
            SetLocalFromWorld(hip, world, positions, rotations);
        }
    }

    private void SetLocalFromWorld(int bone, ReadOnlySpan<Matrix4x4> world, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var local = world[bone];
        var parent = BaseParents[bone];

        if (parent >= 0)
        {
            if (!Matrix4x4.Invert(world[parent], out var parentInverse))
            {
                return;
            }

            local *= parentInverse;
        }

        rotations[bone] = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(local));
        positions[bone] = local.Translation;
    }

    // The chain of the base model for a chain number of this file, -1 when it cannot be solved
    private int GetBaseIkChain(int fileChain)
    {
        if ((uint)fileChain >= (uint)IkChainMap.Length)
        {
            return -1;
        }

        var chain = IkChainMap[fileChain];

        if (chain < 0)
        {
            return -1;
        }

        var bones = IkChains[chain];

        if (!IsBaseBone(bones.Hip) || !IsBaseBone(bones.Knee) || !IsBaseBone(bones.Foot))
        {
            return -1;
        }

        return chain;
    }

    private bool IsBaseBone(int bone) => (uint)bone < (uint)BaseBoneCount;

    private bool TryGetIkRuleData(MdlFile.AnimDesc desc, int descOffset, out byte[] data, out int rulesOffset)
    {
        data = MdlData;
        rulesOffset = 0;

        if (desc.IkRuleIndex != 0)
        {
            rulesOffset = descOffset + desc.IkRuleIndex;
            return true;
        }

        var block = desc.AnimBlock;

        if (desc.AnimBlockIkRuleIndex == 0 || block <= 0 || AniData == null || block >= Blocks.Length)
        {
            return false;
        }

        data = AniData;
        rulesOffset = Blocks[block].DataStart + desc.AnimBlockIkRuleIndex;
        return true;
    }

    // Weight of a rule at a cycle of the animation, ramping in from the start to the peak and out from the tail to the end
    private static float GetIkRuleWeight(MdlFile.StudioIkRule rule, float cycle)
    {
        var start = rule.Start;
        var peak = rule.Peak;
        var tail = rule.Tail;
        var end = rule.End;

        if (start > 1f)
        {
            start -= 1f;
            peak -= 1f;
            tail -= 1f;
            end -= 1f;
        }
        else if (start < 0f)
        {
            start += 1f;
            peak += 1f;
            tail += 1f;
            end += 1f;
        }

        if (end > 1f && cycle < start)
        {
            cycle += 1f;
        }

        if (cycle < start)
        {
            return 0f;
        }

        if (cycle < peak)
        {
            return Source1PoseMath.SimpleSpline((cycle - start) / (peak - start));
        }

        if (cycle < tail)
        {
            return 1f;
        }

        if (cycle < end)
        {
            return Source1PoseMath.SimpleSpline(1f - ((cycle - tail) / (end - tail)));
        }

        return 0f;
    }

    // Position of a cycle in the error samples of a rule, which start at the start frame of the rule
    private static bool TryGetIkErrorFrame(MdlFile.StudioIkRule rule, int frameCount, float cycle, out int frame, out float fraction)
    {
        var start = rule.Start;
        var end = rule.End;

        if (end > 1f && cycle < start)
        {
            cycle += 1f;
        }

        frame = rule.StartFrame;
        fraction = 0f;

        if (cycle < start)
        {
            return false;
        }

        if (cycle >= end)
        {
            cycle = end;
        }

        var position = ((frameCount - 1) * (cycle - start)) + rule.StartFrame;
        frame = (int)position;
        fraction = position - frame;

        return true;
    }

    private static bool TryReadIkError(byte[] data, int ruleOffset, MdlFile.StudioIkRule rule, int frameCount, float cycle, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.Zero;
        rotation = Quaternion.Identity;

        if (!TryGetIkErrorFrame(rule, frameCount, cycle, out var frame, out var fraction))
        {
            return false;
        }

        var sample = Math.Max(frame - rule.StartFrame, 0);

        if (rule.IkErrorIndex != 0)
        {
            ReadRawIkError(data, ruleOffset + rule.IkErrorIndex, sample, fraction, out position, out rotation);
            return true;
        }

        if (rule.CompressedIkErrorIndex != 0)
        {
            ReadCompressedIkError(data, ruleOffset + rule.CompressedIkErrorIndex, sample, fraction, out position, out rotation);
            return true;
        }

        return false;
    }

    private static void ReadRawIkError(byte[] data, int errorsOffset, int sample, float fraction, out Vector3 position, out Quaternion rotation)
    {
        var offset = errorsOffset + (sample * IkErrorSize);
        var span = data.AsSpan();

        position = MemoryMarshal.Read<Vector3>(Slice(span, offset, 12));
        rotation = MemoryMarshal.Read<Quaternion>(Slice(span, offset + 12, 16));

        var hasNext = offset + (2 * IkErrorSize) <= data.Length;

        if (fraction < 0.001f || !hasNext)
        {
            return;
        }

        var nextPosition = MemoryMarshal.Read<Vector3>(Slice(span, offset + IkErrorSize, 12));
        var nextRotation = MemoryMarshal.Read<Quaternion>(Slice(span, offset + IkErrorSize + 12, 16));

        rotation = Source1PoseMath.Blend(rotation, nextRotation, fraction);
        position = MathUtils.Lerp(position, nextPosition, fraction);
    }

    private static void ReadCompressedIkError(byte[] data, int errorOffset, int sample, float fraction, out Vector3 position, out Quaternion rotation)
    {
        ReadOnlySpan<byte> span = data;

        var angles = ReadIkErrorAngles(span, errorOffset, sample);
        position = ReadIkErrorPosition(span, errorOffset, sample);
        rotation = IkErrorAnglesToQuaternion(angles);

        if (fraction <= 0.0001f)
        {
            return;
        }

        var nextAngles = ReadIkErrorAngles(span, errorOffset, sample + 1);
        var nextPosition = ReadIkErrorPosition(span, errorOffset, sample + 1);

        position = MathUtils.Lerp(position, nextPosition, fraction);

        if (nextAngles != angles)
        {
            rotation = Source1PoseMath.Blend(rotation, IkErrorAnglesToQuaternion(nextAngles), fraction);
        }
    }

    private static Quaternion IkErrorAnglesToQuaternion(Vector3 angles)
    {
        return EntityTransformHelper.EulerAnglesToQuaternion(Vector3.RadiansToDegrees(new Vector3(angles.Y, angles.Z, angles.X)));
    }

    private static Vector3 ReadIkErrorPosition(ReadOnlySpan<byte> data, int errorOffset, int sample) => ReadIkErrorVector(data, errorOffset, 0, sample);

    private static Vector3 ReadIkErrorAngles(ReadOnlySpan<byte> data, int errorOffset, int sample) => ReadIkErrorVector(data, errorOffset, 3, sample);

    private static Vector3 ReadIkErrorVector(ReadOnlySpan<byte> data, int errorOffset, int firstChannel, int sample)
    {
        return new Vector3(
            ReadIkErrorValue(data, errorOffset, firstChannel, sample),
            ReadIkErrorValue(data, errorOffset, firstChannel + 1, sample),
            ReadIkErrorValue(data, errorOffset, firstChannel + 2, sample));
    }

    // A channel without data or past the end of its stream reads as zero
    private static float ReadIkErrorValue(ReadOnlySpan<byte> data, int errorOffset, int channel, int sample)
    {
        var relative = ReadInt16(data, errorOffset + CompressedIkErrorOffsets + (channel * sizeof(short)));

        if (relative <= 0)
        {
            return 0f;
        }

        var scale = BinaryPrimitives.ReadSingleLittleEndian(Slice(data, errorOffset + (channel * sizeof(float)), sizeof(float)));

        return TryExtractAnimValue(data, errorOffset + relative, sample, scale, out var value) ? value : 0f;
    }
}
