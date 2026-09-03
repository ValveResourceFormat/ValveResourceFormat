using System.IO;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.IO;

/// <summary>
/// Writes a skeleton out as a DMX dag hierarchy, with the axis and sibling order fixups that reproduce
/// a compiled NM bone order.
/// </summary>
partial class ModelExtract
{
    private static readonly Quaternion NmSkelRotationFixup = new(-0.5f, -0.5f, -0.5f, 0.5f);
    private static readonly Quaternion NmSkelRotationFixupInverse = Quaternion.Inverse(NmSkelRotationFixup);

    /// <summary>
    /// NM skeletons compile the bones under root_motion in a permuted axis frame. Re-frames one of them,
    /// in root_motion's space.
    /// </summary>
    private static (Vector3 Position, Quaternion Rotation) NmAxisFixupChild(Vector3 position, Quaternion rotation)
        => (Vector3.Transform(position, NmSkelRotationFixup), NmSkelRotationFixup * rotation);

    /// <summary>
    /// The other half of <see cref="NmAxisFixupChild"/>: root_motion takes the inverse, cancelling what
    /// its children gained.
    /// </summary>
    /// <remarks>Both halves apply to the bind pose and to every frame alike.</remarks>
    private static Quaternion NmAxisFixupRootMotion(Quaternion rotation)
        => rotation * NmSkelRotationFixupInverse;

    /// <summary>Emits cloth bones with the '_' prefix the compiler sanitizes '$' to.</summary>
    internal static string GetExportBoneName(Bone bone)
        => bone.IsProceduralCloth && bone.Name.StartsWith('$')
            ? $"_{bone.Name[1..]}"
            : bone.Name;

    /// <summary>
    /// Whether the compiler generated this bone from a cloth proxy mesh rather than the author
    /// declaring it.
    /// </summary>
    internal static bool IsGeneratedClothProxyBone(Bone bone)
        => bone.IsProceduralCloth && bone.Name.StartsWith('$');

    private const ModelSkeletonBoneFlags UsedByVertex =
        ModelSkeletonBoneFlags.BoneUsedByVertexLod0 | ModelSkeletonBoneFlags.BoneUsedByVertexLod1
        | ModelSkeletonBoneFlags.BoneUsedByVertexLod2 | ModelSkeletonBoneFlags.BoneUsedByVertexLod3
        | ModelSkeletonBoneFlags.BoneUsedByVertexLod4 | ModelSkeletonBoneFlags.BoneUsedByVertexLod5
        | ModelSkeletonBoneFlags.BoneUsedByVertexLod6 | ModelSkeletonBoneFlags.BoneUsedByVertexLod7;

    /// <summary>
    /// Whether the compiler rebuilds this cloth proxy bone on its own, so the document must not
    /// declare it.
    /// </summary>
    /// <remarks>
    /// The generated family always. The family a round-tripped DMX carries only when no vertex binds
    /// it: those bones reach a compiled model as joints in the artist's mesh files and keep
    /// <c>FLAG_ANIMATION</c> alone, and a document that declares them with <c>do_not_discard</c>
    /// turns them into mesh-used bones instead. Where the family IS skinned it is real authored
    /// weighting and has to stay.
    /// </remarks>
    internal static bool IsCompilerOwnedClothBone(Bone bone)
        => IsGeneratedClothProxyBone(bone)
            || (IsClothProxyName(bone.Name) && (bone.Flags & UsedByVertex) == 0);

    /// <summary>
    /// Whether the name is in a cloth proxy family, under either spelling. A DMX cannot carry the
    /// '$' the compiler writes, so a model round-tripped through one arrives with the '_' form.
    /// </summary>
    internal static bool IsClothProxyName(string name)
    {
        var rest = name.AsSpan();

        if (rest.Length == 0 || (rest[0] != '$' && rest[0] != '_'))
        {
            return false;
        }

        rest = rest[1..];

        if (!rest.StartsWith("cloth_m", StringComparison.Ordinal))
        {
            return false;
        }

        rest = rest["cloth_m".Length..];
        var digits = 0;

        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits >= rest.Length || rest[digits] != 'p')
        {
            return false;
        }

        rest = rest[(digits + 1)..];
        return rest.Length > 0 && char.IsAsciiDigit(rest[0]);
    }

    /// <summary>
    /// Where each skeleton bone lands in an emitted joint list, once the compiler-generated cloth
    /// proxy bones are left out of it.
    /// </summary>
    /// <remarks>
    /// A kept bone maps to its own emitted index. A dropped one maps to <c>-1 - f</c>, where <c>f</c>
    /// is the emitted index of the nearest bone that survived: an influence on a dropped bone has to
    /// name something, and the compiler replaces those weights outright when it re-binds the mesh to
    /// the proxy nodes it regenerates.
    /// </remarks>
    internal static int[] BuildClothBoneCompaction(Skeleton skeleton)
    {
        var compaction = new int[skeleton.Bones.Length];
        var emitted = 0;

        foreach (var bone in skeleton.Bones)
        {
            compaction[bone.Index] = IsGeneratedClothProxyBone(bone) ? int.MinValue : emitted++;
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
                if (compaction[candidate.Index] < 0 || IsClothProxyName(candidate.Name))
                {
                    continue;
                }

                var distance = (candidate.BindPose.Translation - here).LengthSquared();

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

    /// <summary>Resolves one blend index through <see cref="BuildClothBoneCompaction"/>.</summary>
    internal static int CompactBoneIndex(int[] compaction, int bone)
    {
        if (bone < 0 || bone >= compaction.Length)
        {
            return 0;
        }

        var mapped = compaction[bone];
        return mapped < 0 ? -1 - mapped : mapped;
    }

    private static DmeModel BuildDmeDagSkeleton(Skeleton skeleton, out DmeTransform[] transforms,
        bool nmSkelAxisFixup = false, int nmLowLodBoneCount = -1,
        IReadOnlyDictionary<string, Vector3>? bonePositions = null)
    {
        var dmeSkeleton = new DmeModel();

        transforms = AppendDmeSkeletonJoints(dmeSkeleton, skeleton, nmLowLodBoneCount, bonePositions);

        var rootMotionBone = skeleton["root_motion"];

        if (nmSkelAxisFixup && rootMotionBone != null)
        {
            // dmeSkeleton.AxisSystem.UpAxis = 2;
            // dmeSkeleton.AxisSystem.ForwardParity = -1;
            // dmeSkeleton.AxisSystem.CoordSys = 2;

            transforms[rootMotionBone.Index].Orientation = NmAxisFixupRootMotion(transforms[rootMotionBone.Index].Orientation);

            foreach (var root in rootMotionBone.Children)
            {
                (transforms[root.Index].Position, transforms[root.Index].Orientation)
                    = NmAxisFixupChild(transforms[root.Index].Position, transforms[root.Index].Orientation);
            }
        }

        return dmeSkeleton;
    }

    /// <summary>
    /// Adds one skeleton's joints to a DmeModel, its roots as children of the model, and returns the
    /// joint transforms indexed by bone index. With <paramref name="nmLowLodBoneCount"/> non-negative,
    /// DAG siblings reproduce the compiled NM bone order, otherwise bone index order.
    /// </summary>
    private static DmeTransform[] AppendDmeSkeletonJoints(DmeModel dmeSkeleton, Skeleton skeleton,
        int nmLowLodBoneCount = -1, IReadOnlyDictionary<string, Vector3>? bonePositions = null)
    {
        int[]? minLow = null;
        int[]? minHigh = null;

        if (nmLowLodBoneCount >= 0)
        {
            (minLow, minHigh) = NmLodSubtreeMins(skeleton, nmLowLodBoneCount);
        }

        var transforms = new DmeTransform[skeleton.Bones.Length];
        var boneDags = new DmeJoint[skeleton.Bones.Length];

        foreach (var bone in skeleton.Bones)
        {
            var boneName = GetExportBoneName(bone);
            var dag = new DmeJoint
            {
                Name = boneName
            };

            dag.Transform.Name = boneName;
            dag.Transform.Position = BonePosition(bone, bonePositions);
            dag.Transform.Orientation = bone.Angle;

            transforms[bone.Index] = dag.Transform;

            if (IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            boneDags[bone.Index] = dag;
            dmeSkeleton.JointList.Add(dag);
        }

        foreach (var bone in skeleton.Bones)
        {
            if (boneDags[bone.Index] is not { } parentDag)
            {
                continue;
            }

            foreach (var child in OrderSiblings(bone.Children, minLow, minHigh))
            {
                if (boneDags[child.Index] is { } childDag)
                {
                    parentDag.Children.Add(childDag);
                }
            }
        }

        foreach (var root in OrderSiblings(skeleton.Roots, minLow, minHigh))
        {
            if (boneDags[root.Index] is { } rootDag)
            {
                dmeSkeleton.Children.Add(rootDag);
            }
        }

        return transforms;
    }

    /// <summary>
    /// Per-bone minimum compiled index within the bone's subtree, split into a low-LOD part
    /// (indices below <paramref name="nmLowLodBoneCount"/>) and a high-LOD part; entries are
    /// <see cref="int.MaxValue"/> where the subtree has no bone of that kind.
    /// </summary>
    private static (int[] MinLow, int[] MinHigh) NmLodSubtreeMins(Skeleton skeleton, int nmLowLodBoneCount)
    {
        var boneCount = skeleton.Bones.Length;
        var minLow = new int[boneCount];
        var minHigh = new int[boneCount];
        Array.Fill(minLow, int.MaxValue);
        Array.Fill(minHigh, int.MaxValue);

        for (var i = boneCount - 1; i >= 0; i--)
        {
            if (i < nmLowLodBoneCount)
            {
                minLow[i] = Math.Min(minLow[i], i);
            }
            else
            {
                minHigh[i] = Math.Min(minHigh[i], i);
            }

            var parent = skeleton.Bones[i].Parent;
            if (parent != null)
            {
                minLow[parent.Index] = Math.Min(minLow[parent.Index], minLow[i]);
                minHigh[parent.Index] = Math.Min(minHigh[parent.Index], minHigh[i]);
            }
        }

        return (minLow, minHigh);
    }

    /// <summary>
    /// Orders one sibling group into the compiled NM bone order: a hierarchy walk filtered to the first
    /// m_numBonesToSampleAtLowLOD bones, then the same walk filtered to the rest. Without the subtree
    /// tables from <see cref="NmLodSubtreeMins"/> the group comes back unchanged.
    /// </summary>
    private static IReadOnlyList<Bone> OrderSiblings(IReadOnlyList<Bone> siblings, int[]? minLow, int[]? minHigh)
    {
        if (minLow == null || minHigh == null || siblings.Count < 2)
        {
            return siblings;
        }

        var lowContaining = new List<Bone>();
        var highOnly = new List<Bone>();

        foreach (var sibling in siblings)
        {
            (minLow[sibling.Index] != int.MaxValue ? lowContaining : highOnly).Add(sibling);
        }

        lowContaining.Sort((a, b) => minLow[a.Index].CompareTo(minLow[b.Index]));
        highOnly.Sort((a, b) => minHigh[a.Index].CompareTo(minHigh[b.Index]));

        var merged = new List<Bone>(siblings.Count);
        var next = 0;

        foreach (var sibling in lowContaining)
        {
            if (minHigh[sibling.Index] != int.MaxValue)
            {
                while (next < highOnly.Count && minHigh[highOnly[next].Index] < minHigh[sibling.Index])
                {
                    merged.Add(highOnly[next++]);
                }
            }

            merged.Add(sibling);
        }

        merged.AddRange(highOnly.GetRange(next, highOnly.Count - next));
        return merged;
    }

    /// <summary>
    /// Produces a skeleton DMX file. <paramref name="nmLowLodBoneCount"/> is the skeleton's
    /// m_numBonesToSampleAtLowLOD; when non-negative, DAG siblings are ordered to reproduce the
    /// compiled NM bone order.
    /// </summary>
    public static byte[] ToDmxSkeleton(Skeleton skeleton, bool nmSkelAxisFixup = false, int nmLowLodBoneCount = -1)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms, nmSkelAxisFixup, nmLowLodBoneCount);

        using var stream = new MemoryStream();

        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeSkeleton,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["app"] = "sfm", // maya
                ["source"] = $"Generated with {StringToken.VRF_GENERATOR}",
            }
        };

        dmx.Save(stream, "keyvalues2", 4);
        return stream.ToArray();
    }
}
