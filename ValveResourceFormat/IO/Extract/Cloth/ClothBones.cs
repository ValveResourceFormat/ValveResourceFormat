using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.IO;

/// <summary>The skeleton bones a compiled cloth proxy generated, and how an export leaves them out.</summary>
internal static partial class ClothBones
{
    private const ModelSkeletonBoneFlags UsedByVertex =
        ModelSkeletonBoneFlags.BoneUsedByVertexLod0 | ModelSkeletonBoneFlags.BoneUsedByVertexLod1
        | ModelSkeletonBoneFlags.BoneUsedByVertexLod2 | ModelSkeletonBoneFlags.BoneUsedByVertexLod3
        | ModelSkeletonBoneFlags.BoneUsedByVertexLod4 | ModelSkeletonBoneFlags.BoneUsedByVertexLod5
        | ModelSkeletonBoneFlags.BoneUsedByVertexLod6 | ModelSkeletonBoneFlags.BoneUsedByVertexLod7;

    private static readonly ConditionalWeakTable<Skeleton, int[]> Compactions = new();

    /// <summary>Whether the compiler generated this bone from a cloth proxy mesh.</summary>
    internal static bool IsGeneratedProxyBone(Bone bone)
        => bone.IsProceduralCloth && bone.Name.StartsWith('$');

    /// <summary>
    /// Whether the compiler rebuilds this cloth proxy bone on its own, so the document must not declare it: a generated
    /// bone, or a proxy-named bone no vertex binds.
    /// </summary>
    internal static bool IsCompilerOwned(Bone bone)
        => IsGeneratedProxyBone(bone)
            || (IsProxyName(bone.Name) && (bone.Flags & UsedByVertex) == 0);

    /// <summary>Whether the name is a cloth proxy name <c>$cloth_m{N}p{L}</c>, or its DMX spelling with '_' for '$'.</summary>
    internal static bool IsProxyName(string name) => ProxyNameRegex().IsMatch(name);

    [GeneratedRegex("^[$_]cloth_m[0-9]+p[0-9]")]
    private static partial Regex ProxyNameRegex();

    /// <summary>
    /// Where each skeleton bone lands in an emitted joint list without the generated cloth proxy bones: a kept bone at
    /// its own index, a dropped one at <c>-1 - f</c> with <c>f</c> the index of the nearest kept bone. Built once per
    /// skeleton; callers must not modify it.
    /// </summary>
    internal static int[] Compaction(Skeleton skeleton) => Compactions.GetValue(skeleton, BuildCompaction);

    private static int[] BuildCompaction(Skeleton skeleton)
    {
        var compaction = new int[skeleton.Bones.Length];
        var emitted = 0;

        foreach (var bone in skeleton.Bones)
        {
            compaction[bone.Index] = IsGeneratedProxyBone(bone) ? int.MinValue : emitted++;
        }

        foreach (var bone in skeleton.Bones)
        {
            if (compaction[bone.Index] != int.MinValue)
            {
                continue;
            }

            var here = bone.BindPose.Translation;
            var nearest = 0;
            var nearestDistance = float.MaxValue;

            foreach (var candidate in skeleton.Bones)
            {
                if (compaction[candidate.Index] < 0 || IsProxyName(candidate.Name))
                {
                    continue;
                }

                var distance = Vector3.DistanceSquared(candidate.BindPose.Translation, here);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = compaction[candidate.Index];
                }
            }

            compaction[bone.Index] = -1 - nearest;
        }

        return compaction;
    }

    /// <summary>Resolves one blend index through <see cref="Compaction"/>.</summary>
    internal static int CompactBoneIndex(int[] compaction, int bone)
    {
        if (bone < 0 || bone >= compaction.Length)
        {
            return 0;
        }

        var mapped = compaction[bone];
        return mapped < 0 ? -1 - mapped : mapped;
    }
}
