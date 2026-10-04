using System.Linq;
using ValveKeyValue;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    private const float ImportedClothDefaultContraction = 0.05f;

    /// <summary>
    /// The <c>ImportedCloth</c> node carrying the node and rod tables verbatim. With <paramref name="tableNodes"/> only
    /// those nodes become rows, and a parent, follow parent or rod reaching outside them is dropped.
    /// </summary>
    private static KVObject MakeImportedCloth(ClothReconstruction cloth, IReadOnlySet<int>? tableNodes = null)
    {
        var ropeParents = cloth.RopeRunParents;
        var followLinks = cloth.Index.FollowNodeLinks;
        var localForce = cloth.Fe.LocalForces;
        var localRotation = cloth.Fe.LocalRotations;
        var osOffsetParents = new Dictionary<int, int>(cloth.Fe.CtrlOsOffsets.Length);
        foreach (var pair in cloth.Fe.CtrlOsOffsets)
        {
            osOffsetParents.TryAdd(pair.CtrlChild, pair.CtrlParent);
        }

        List<int> rowNodes = tableNodes is null ? [.. Enumerable.Range(0, cloth.Fe.CtrlName.Length)] : [.. tableNodes.Order()];
        var rowOf = new Dictionary<int, int>(rowNodes.Count);
        for (var row = 0; row < rowNodes.Count; row++)
        {
            rowOf[rowNodes[row]] = row;
        }

        float PerDynamic(float[] values, int node)
        {
            if (values.Length == cloth.Fe.CtrlName.Length)
            {
                return values[node];
            }

            var dynamicIndex = node - cloth.Fe.StaticNodes;
            return dynamicIndex >= 0 && dynamicIndex < values.Length ? values[dynamicIndex] : float.NaN;
        }

        var nodes = KVObject.Array();
        foreach (var node in rowNodes)
        {
            var row = KVObject.Collection();
            row.Add("m_Name", cloth.Fe.CtrlName[node]);

            var position = node < cloth.Index.InitPosePositions.Length ? cloth.Index.InitPosePositions[node] : Vector3.Zero;
            var rotation = node < cloth.Index.InitPoseRotations.Length ? cloth.Index.InitPoseRotations[node] : Quaternion.Identity;
            row.Add("m_Transform", MakeArray(position.X, position.Y, position.Z,
                rotation.X, rotation.Y, rotation.Z, rotation.W));

            var invMass = node < cloth.Fe.NodeInvMasses.Length ? cloth.Fe.NodeInvMasses[node] : 0f;
            if (invMass == 0f)
            {
                row.Add("m_bSimulated", false);
                if (node < cloth.Fe.RotLockStaticNodes)
                {
                    row.Add("m_bFreeRotation", false);
                }
            }
            else
            {
                row.Add("m_flMass", 1f / invMass);
            }

            if (osOffsetParents.TryGetValue(node, out var osOffsetParent))
            {
                row.Add("m_bVirtual", true);
                row.Add("m_bOsOffset", true);
                if (rowOf.TryGetValue(osOffsetParent, out var osOffsetParentRow))
                {
                    row.Add("m_nParent", osOffsetParentRow);
                }
            }
            else if (cloth.HasCompiledSkelParents && node < cloth.SkelParents.Length && cloth.SkelParents[node] >= 0)
            {
                if (rowOf.TryGetValue(cloth.SkelParents[node], out var skelParentRow))
                {
                    row.Add("m_nParent", skelParentRow);
                }
            }
            else if (!cloth.HasCompiledSkelParents && ropeParents.TryGetValue(node, out var parent)
                && rowOf.TryGetValue(parent, out var ropeParentRow))
            {
                row.Add("m_nParent", ropeParentRow);
            }

            if (followLinks.TryGetValue(node, out var follow) && rowOf.TryGetValue(follow.Parent, out var followParentRow))
            {
                row.Add("m_nFollowParent", followParentRow);
                row.Add("m_flFollowWeight", follow.Weight);
            }

            var integrator = cloth.Index.GetIntegrator(node);
            var integratorRow = KVObject.Collection();
            integratorRow.Add("flPointDamping", integrator.PointDamping);
            integratorRow.Add("flAnimationForceAttraction", integrator.AnimationForceAttraction);
            integratorRow.Add("flAnimationVertexAttraction", integrator.AnimationVertexAttraction);
            integratorRow.Add("flGravity", integrator.Gravity);
            row.Add("m_Integrator", integratorRow);

            if (node < cloth.Fe.LegacyStretchForce.Length && cloth.Fe.LegacyStretchForce[node] != 0f)
            {
                row.Add("m_flLegacyStretchForce", cloth.Fe.LegacyStretchForce[node]);
            }

            var force = PerDynamic(localForce, node);
            if (!float.IsNaN(force))
            {
                row.Add("m_flLocalForce", force);
            }

            var rotationScale = PerDynamic(localRotation, node);
            if (!float.IsNaN(rotationScale) && rotationScale != 0f)
            {
                row.Add("m_flLocalRotation", rotationScale);
            }

            var radius = cloth.Index.GetCollisionRadius(node);
            if (radius != 0f)
            {
                row.Add("m_flCollisionRadius", radius);
            }

            var friction = cloth.Index.GetNodeFriction(node);
            if (friction != 0f)
            {
                row.Add("m_flFriction", friction);
            }

            if (cloth.Index.WorldCollisionNodes.Contains(node))
            {
                row.Add("m_bNeedsWorldCollision", true);
                if (cloth.Index.WorldCollisionFriction.TryGetValue(node, out var worldFriction))
                {
                    row.Add("m_flWorldFriction", worldFriction.World);
                    row.Add("m_flGroundFriction", worldFriction.Ground);
                }
            }

            nodes.Add(row);
        }

        var rods = KVObject.Array();
        foreach (var rod in cloth.Index.Rods)
        {
            if (!rowOf.TryGetValue(rod.NodeA, out var rowA) || !rowOf.TryGetValue(rod.NodeB, out var rowB))
            {
                continue;
            }

            var row = KVObject.Collection();
            row.Add("m_nNodes", MakeArray(rowA, rowB));

            var restLength = rod.NodeA < cloth.Index.InitPosePositions.Length && rod.NodeB < cloth.Index.InitPosePositions.Length
                ? cloth.Index.RestDistance(rod.NodeA, rod.NodeB)
                : 0f;
            if (!FeModelIndex.Rod.IsAtRestLength(rod.MaxDist, restLength))
            {
                row.Add("m_bExplicitLength", true);
                row.Add("m_flLength", rod.MaxDist);
            }

            var contraction = rod.MaxDist != 0f ? rod.MinDist / rod.MaxDist : ImportedClothDefaultContraction;
            if (MathF.Abs(contraction - ImportedClothDefaultContraction) > 1e-6f)
            {
                row.Add("m_flContractionFactor", contraction);
            }

            if (MathF.Abs(rod.RelaxationFactor - 1f) > 1e-6f)
            {
                row.Add("m_flRelaxationFactor", rod.RelaxationFactor);
            }

            rods.Add(row);
        }

        var fx = KVObject.Collection();
        fx.Add("m_Nodes", nodes);
        fx.Add("m_Rods", rods);

        return MakeNode("ImportedCloth",
            ("name", "imported_cloth"),
            ("fx", fx),
            ("bone_attrs", KVObject.Collection()),
            ("rod_attrs", KVObject.Collection()));
    }

    private static IEnumerable<string> ImportedStripBoneNames(ClothReconstruction cloth, IReadOnlySet<int> strip)
        => strip.Select(node => cloth.Fe.CtrlName[node]).Where(name => !cloth.IsGeneratedNodeName(name));

    private void EmitImportedClothPhase(ClothReconstruction cloth, List<BoneChain> boneChains, KVObject rootChildren)
    {
        var (softbody, softbodyChildren) = MakeSoftbody(cloth);
        softbodyChildren.Add(MakeClothParams(cloth, forceExplicitMasses: true));
        AddClothFolder(softbodyChildren).Add(MakeImportedCloth(cloth));

        var clothBones = ClothBoneNames(cloth);
        foreach (var name in cloth.Fe.CtrlName)
        {
            if (!cloth.IsGeneratedNodeName(name))
            {
                clothBones.Add(name);
            }
        }

        AddClothPhaseTail(cloth, rootChildren, softbody, softbodyChildren, clothBones, boneChains);
    }
}
