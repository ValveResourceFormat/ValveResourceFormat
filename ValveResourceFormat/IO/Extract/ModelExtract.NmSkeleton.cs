using System.IO;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.IO;

/// <summary>
/// Writes a skeleton out as a DMX dag hierarchy, with the axis and sibling order fixups the NM skeleton
/// and clip extractors need to reproduce a compiled NM bone order.
/// </summary>
partial class ModelExtract
{
    private static readonly Quaternion NmSkelRotationFixup = new(-0.5f, -0.5f, -0.5f, 0.5f);
    private static readonly Quaternion NmSkelRotationFixupInverse = Quaternion.Inverse(NmSkelRotationFixup);

    /// <summary>
    /// NM skeletons compile the bones under root_motion in a permuted axis frame. This re-frames one of
    /// them, in root_motion's space, which is why the rotation is multiplied on the left.
    /// </summary>
    private static (Vector3 Position, Quaternion Rotation) NmAxisFixupChild(Vector3 position, Quaternion rotation)
        => (Vector3.Transform(position, NmSkelRotationFixup), NmSkelRotationFixup * rotation);

    /// <summary>
    /// The other half of <see cref="NmAxisFixupChild"/>. root_motion takes the inverse, so what its children
    /// gained cancels against it and the subtree below them does not move.
    /// </summary>
    /// <remarks>
    /// Both halves have to be applied to the bind pose and to every frame alike, since a clip writes a
    /// channel for each of these bones and the frame values override what the bind pose declared.
    /// </remarks>
    private static Quaternion NmAxisFixupRootMotion(Quaternion rotation)
        => rotation * NmSkelRotationFixupInverse;

    /// <summary>Emits cloth bones with the '_' prefix the compiler sanitizes '$' to, so round-trips don't duplicate them.</summary>
    internal static string GetExportBoneName(Bone bone)
        => bone.IsProceduralCloth && bone.Name.StartsWith('$')
            ? $"_{bone.Name[1..]}"
            : bone.Name;

    private static DmeModel BuildDmeDagSkeleton(Skeleton skeleton, out DmeTransform[] transforms, bool nmSkelAxisFixup = false, int nmLowLodBoneCount = -1)
    {
        var dmeSkeleton = new DmeModel();

        transforms = AppendDmeSkeletonJoints(dmeSkeleton, skeleton, nmLowLodBoneCount);

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
    /// joint transforms indexed by bone index. When <paramref name="nmLowLodBoneCount"/> is
    /// non-negative, DAG siblings are ordered to reproduce the skeleton's compiled NM bone order;
    /// otherwise they are appended in bone index order.
    /// </summary>
    private static DmeTransform[] AppendDmeSkeletonJoints(DmeModel dmeSkeleton, Skeleton skeleton, int nmLowLodBoneCount = -1)
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
            dag.Transform.Position = bone.Position;
            dag.Transform.Orientation = bone.Angle;

            boneDags[bone.Index] = dag;
            transforms[bone.Index] = dag.Transform;

            dmeSkeleton.JointList.Add(dag);
        }

        foreach (var bone in skeleton.Bones)
        {
            foreach (var child in OrderSiblings(bone.Children, minLow, minHigh))
            {
                boneDags[bone.Index].Children.Add(boneDags[child.Index]);
            }
        }

        foreach (var root in OrderSiblings(skeleton.Roots, minLow, minHigh))
        {
            dmeSkeleton.Children.Add(boneDags[root.Index]);
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
    /// CompileNmSkeleton emits bones as a hierarchy walk filtered to the first
    /// m_numBonesToSampleAtLowLOD bones, then the same walk filtered to the rest. Orders one
    /// sibling group so that walk reproduces the skeleton's compiled bone order. Without the
    /// subtree tables from <see cref="NmLodSubtreeMins"/> the group is returned unchanged, in
    /// bone index order.
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
