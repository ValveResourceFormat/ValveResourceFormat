using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // How far a control node's recorded rest position may sit from the same bone's compiled bind pose and
    // still be read as the same pose at better precision. Past a whole unit the node sits somewhere else
    // entirely and that bone keeps its compiled transform.
    const float ClothRestBoneTolerance = 1.0f;

    // And how far it has to sit before the disagreement is worth acting on: below the floor the round trip
    // holds the two poses equal.
    const float ClothRestBoneFloor = 1e-3f;

    // How far apart two control bones' corrections may sit and still be read as ONE pose difference.
    // A proxy mesh authored in a different pose moves as a unit, and it takes at least two bones
    // agreeing to witness that; one bone on its own is an isolated disagreement, not a pose, and the
    // exporter does not guess at it.
    const float ClothRestBoneRigidSpread = 1e-2f;

    // The correction runs per MODEL only when some bone disagrees by more than twice the floor. Once
    // enabled, every bone past the per-bone floor moves together: derived rest shapes span bones on both
    // sides of any per-bone cut, so a partial correction leaves them mixed.
    const float ClothRestBoneModelGate = 2e-3f;

    // Re-derives each bone's parent-space position from the cloth rest pose, root first: a bone the
    // FeModel registers as a control node is put back on its recorded world position, and every bone under
    // it keeps its compiled offset from that corrected parent, so a correction propagates down the
    // hierarchy exactly as the authored transform chain would. Whether a bone qualifies is judged on the
    // COMPILED pose, not the corrected one - the disagreement accumulates down a chain, and measuring
    // against an already-corrected parent would only ever see one link's worth of it.
    private void BuildClothRestBonePositions(FeModel feModel)
    {
        Debug.Assert(model is not null, "model required for cloth rest bones");

        var targets = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        for (var node = 0; node < feModel.CtrlNames.Length && node < feModel.InitPosePositions.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (!string.IsNullOrEmpty(name) && !feModel.IsGeneratedNodeName(name))
            {
                targets.TryAdd(name, feModel.InitPosePositions[node]);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        var maxApart = 0f;
        var maxApartUncapped = 0f;
        var farOffsets = new List<Vector3>();
        void Measure(Bone bone, Vector3 compiledParent, Quaternion parentRotation)
        {
            var compiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
            var rotation = parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                maxApartUncapped = Math.Max(maxApartUncapped, apart);
                if (apart <= ClothRestBoneTolerance)
                {
                    maxApart = Math.Max(maxApart, apart);
                }
                else
                {
                    farOffsets.Add(target - compiled);
                }
            }

            foreach (var child in bone.Children)
            {
                Measure(child, compiled, rotation);
            }
        }

        foreach (var root in model.Skeleton.Roots)
        {
            Measure(root, Vector3.Zero, Quaternion.Identity);
        }

        var farOffsetsAreRigid = farOffsets.Count > 1;
        foreach (var offset in farOffsets)
        {
            farOffsetsAreRigid &= Vector3.Distance(offset, farOffsets[0]) <= ClothRestBoneRigidSpread;
        }

        void Walk(Bone bone, Vector3 parentPosition, Quaternion parentRotation, Vector3 compiledParent,
            Dictionary<string, Vector3> into, float tolerance)
        {
            var world = parentPosition + Vector3.Transform(bone.Position, parentRotation);
            var compiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
            var rotation = parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                if (apart > ClothRestBoneFloor && apart <= tolerance)
                {
                    world = target;
                }
            }

            var local = Vector3.Transform(world - parentPosition, Quaternion.Conjugate(parentRotation));
            if (local != bone.Position)
            {
                into[bone.Name] = local;
            }

            foreach (var child in bone.Children)
            {
                Walk(child, world, rotation, compiled, into, tolerance);
            }
        }

        if (maxApart > ClothRestBoneModelGate)
        {
            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Quaternion.Identity, Vector3.Zero,
                    ClothRestBonePositions, ClothRestBoneTolerance);
            }
        }

        var proxyTolerance = farOffsetsAreRigid ? float.MaxValue : ClothRestBoneTolerance;
        if (maxApart > ClothRestBoneModelGate
            || (farOffsetsAreRigid && maxApartUncapped > ClothRestBoneModelGate))
        {
            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Quaternion.Identity, Vector3.Zero,
                    ClothProxyRestBonePositions, proxyTolerance);
            }
        }
    }

    /// <summary>
    /// Gets the rest-pose bone positions written into the cloth PROXY mesh only. The cloth import
    /// takes the transforms it records in <c>m_InitPose</c> from the proxy mesh file's own joint
    /// list, so a model authored with a proxy posed differently from the render mesh is reproduced
    /// by correcting that joint list alone. Unlike <see cref="ClothRestBonePositions"/> this one is
    /// not capped at <see cref="ClothRestBoneTolerance"/>, because nothing the render mesh is
    /// skinned to moves with it.
    /// </summary>
    public Dictionary<string, Vector3> ClothProxyRestBonePositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    static Dictionary<int, FeModel.CtrlOffset> BuildCtrlAnchorMap(FeModel feModel)
    {
        var anchorOf = new Dictionary<int, FeModel.CtrlOffset>();
        foreach (var offset in feModel.CtrlOffsets)
        {
            anchorOf[offset.CtrlChild] = offset;
        }

        return anchorOf;
    }

    // The bone a "$cloth_node_<name>" ctrl hangs off, plus the bone-local origin to re-author it at: the
    // m_CtrlOffsets entry the compiler wrote for it, or the skeleton parent when the model carries no such
    // entry. A node anchored to another generated node has no authorable root bone.
    static bool TryResolveClothNodeAnchor(FeModel feModel, Dictionary<int, FeModel.CtrlOffset> anchorOf,
        int node, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? rootBone, out Vector3 origin)
    {
        var names = feModel.CtrlNames;
        rootBone = null;
        origin = default;

        if (anchorOf.TryGetValue(node, out var anchor)
            && anchor.CtrlParent >= 0 && anchor.CtrlParent < names.Length)
        {
            rootBone = names[anchor.CtrlParent];
            origin = anchor.Offset;
        }
        else if (node < feModel.SkelParents.Length
            && feModel.SkelParents[node] >= 0 && feModel.SkelParents[node] < names.Length)
        {
            var parent = feModel.SkelParents[node];
            rootBone = names[parent];
            if (node < feModel.InitPosePositions.Length && parent < feModel.InitPosePositions.Length
                && parent < feModel.InitPoseRotations.Length)
            {
                origin = Vector3.Transform(
                    feModel.InitPosePositions[node] - feModel.InitPosePositions[parent],
                    Quaternion.Conjugate(feModel.InitPoseRotations[parent]));
            }
        }

        if (rootBone is not null && origin.Length() < ClothNodeMergeRadius)
        {
            // The compiler folds a free ClothNode into its root bone's own ctrl when the authored origin
            // is within ClothNodeMergeRadius of the bone, which loses the node the original still carries
            // its "$cloth_node_" ctrl for. Push it just outside, keeping its direction where it has one.
            var direction = origin == Vector3.Zero ? Vector3.One : origin;
            origin = Vector3.Normalize(direction) * (ClothNodeMergeRadius * 1.25f);
        }

        return rootBone is not null && !FeModel.IsProxyNodeName(rootBone);
    }

    // Bone-local euclidean distance under which the compiler merges a free ClothNode into its root bone's
    // control node instead of giving it one of its own. A node at exactly this distance keeps its own.
    const float ClothNodeMergeRadius = 1e-3f;
}
