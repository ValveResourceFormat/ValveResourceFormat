using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// The <c>ImportedCloth</c> node that carries a PhysAuthFx cloth's node and rod tables verbatim, every field written
    /// on its row. With <paramref name="tableNodes"/> only those nodes are rows, and a parent, follow parent or rod
    /// reaching outside them is dropped.
    /// </summary>
    private static KVObject MakeImportedCloth(FeModel feModel, IReadOnlySet<int>? tableNodes = null)
    {
        var ropeParents = feModel.RopeRunParents;
        var followLinks = feModel.FollowNodeLinks;
        var localForce = feModel.LocalForceValues;
        var localRotation = feModel.LocalRotationValues;
        var osOffsetParents = new Dictionary<int, int>(feModel.CtrlOsOffsets.Length);
        foreach (var pair in feModel.CtrlOsOffsets)
        {
            osOffsetParents.TryAdd(pair.CtrlChild, pair.CtrlParent);
        }

        var rowNodes = tableNodes is null ? Enumerable.Range(0, feModel.CtrlNames.Length).ToList() : tableNodes.Order().ToList();
        var rowOf = new Dictionary<int, int>(rowNodes.Count);
        for (var row = 0; row < rowNodes.Count; row++)
        {
            rowOf[rowNodes[row]] = row;
        }

        float PerDynamic(float[] values, int node)
        {
            if (values.Length == feModel.CtrlNames.Length)
            {
                return values[node];
            }

            var dynamicIndex = node - feModel.StaticNodeCount;
            return dynamicIndex >= 0 && dynamicIndex < values.Length ? values[dynamicIndex] : float.NaN;
        }

        var nodes = KVObject.Array();
        foreach (var node in rowNodes)
        {
            var row = KVObject.Collection();
            row.Add("m_Name", feModel.CtrlNames[node]);

            var position = node < feModel.InitPosePositions.Length ? feModel.InitPosePositions[node] : Vector3.Zero;
            var rotation = node < feModel.InitPoseRotations.Length ? feModel.InitPoseRotations[node] : Quaternion.Identity;
            row.Add("m_Transform", MakeArray(position.X, position.Y, position.Z,
                rotation.X, rotation.Y, rotation.Z, rotation.W));

            var invMass = node < feModel.NodeInvMasses.Length ? feModel.NodeInvMasses[node] : 0f;
            if (invMass == 0f)
            {
                row.Add("m_bSimulated", false);
                if (node < feModel.RotationLockedStaticNodeCount)
                {
                    row.Add("m_bFreeRotation", false);
                }
            }
            else
            {
                row.Add("m_flMass", 1f / invMass);
            }

            // m_bOsOffset moves the node's offset from m_CtrlOffsets to m_CtrlOsOffsets.
            var isOsOffsetChild = osOffsetParents.TryGetValue(node, out var osOffsetParent);
            if (isOsOffsetChild)
            {
                row.Add("m_bVirtual", true);
                row.Add("m_bOsOffset", true);
            }

            if (isOsOffsetChild)
            {
                if (rowOf.TryGetValue(osOffsetParent, out var osOffsetParentRow))
                {
                    row.Add("m_nParent", osOffsetParentRow);
                }
            }
            else if (feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length && feModel.SkelParents[node] >= 0)
            {
                if (rowOf.TryGetValue(feModel.SkelParents[node], out var skelParentRow))
                {
                    row.Add("m_nParent", skelParentRow);
                }
            }
            else if (!feModel.HasCompiledSkelParents && ropeParents.TryGetValue(node, out var parent)
                && rowOf.TryGetValue(parent, out var ropeParentRow))
            {
                row.Add("m_nParent", ropeParentRow);
            }

            if (followLinks.TryGetValue(node, out var follow) && rowOf.TryGetValue(follow.Parent, out var followParentRow))
            {
                row.Add("m_nFollowParent", followParentRow);
                row.Add("m_flFollowWeight", follow.Weight);
            }

            var integrator = feModel.GetIntegrator(node);
            var integratorRow = KVObject.Collection();
            integratorRow.Add("flPointDamping", integrator.PointDamping);
            integratorRow.Add("flAnimationForceAttraction", integrator.ForceAttraction);
            integratorRow.Add("flAnimationVertexAttraction", integrator.VertexAttraction);
            integratorRow.Add("flGravity", integrator.Gravity);
            row.Add("m_Integrator", integratorRow);

            if (node < feModel.LegacyStretchForce.Length && feModel.LegacyStretchForce[node] != 0f)
            {
                row.Add("m_flLegacyStretchForce", feModel.LegacyStretchForce[node]);
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

            var radius = feModel.GetCollisionRadius(node);
            if (radius != 0f)
            {
                row.Add("m_flCollisionRadius", radius);
            }

            var friction = feModel.GetNodeFriction(node);
            if (friction != 0f)
            {
                row.Add("m_flFriction", friction);
            }

            if (feModel.WorldCollisionNodes.Contains(node))
            {
                row.Add("m_bNeedsWorldCollision", true);
                if (feModel.WorldCollisionFriction.TryGetValue(node, out var worldFriction))
                {
                    row.Add("m_flWorldFriction", worldFriction.World);
                    row.Add("m_flGroundFriction", worldFriction.Ground);
                }
            }

            nodes.Add(row);
        }

        var rods = KVObject.Array();
        foreach (var rod in feModel.Rods)
        {
            if (!rowOf.TryGetValue(rod.NodeA, out var rowA) || !rowOf.TryGetValue(rod.NodeB, out var rowB))
            {
                continue;
            }

            var row = KVObject.Collection();
            row.Add("m_nNodes", MakeArray(rowA, rowB));

            var restLength = rod.NodeA < feModel.InitPosePositions.Length && rod.NodeB < feModel.InitPosePositions.Length
                ? Vector3.Distance(feModel.InitPosePositions[rod.NodeA], feModel.InitPosePositions[rod.NodeB])
                : 0f;
            if (Math.Abs(rod.MaxDist - restLength) > Math.Max(1e-3f, 1e-4f * Math.Max(rod.MaxDist, restLength)))
            {
                row.Add("m_bExplicitLength", true);
                row.Add("m_flLength", rod.MaxDist);
            }

            var contraction = rod.MaxDist != 0f ? rod.MinDist / rod.MaxDist : ImportedClothDefaultContraction;
            if (Math.Abs(contraction - ImportedClothDefaultContraction) > 1e-6f)
            {
                row.Add("m_flContractionFactor", contraction);
            }

            if (Math.Abs(rod.RelaxationFactor - 1f) > 1e-6f)
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

    private const float ImportedClothDefaultContraction = 0.05f;

    private static IEnumerable<string> ImportedStripBoneNames(FeModel feModel, IReadOnlySet<int> strip)
        => strip.Select(node => feModel.CtrlNames[node]).Where(name => !feModel.IsGeneratedNodeName(name));

    private bool EmitImportedClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel, explicitMasses: true));

        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);
        clothFolderChildren.Add(MakeImportedCloth(feModel));

        var clothBones = ClothBoneNames(feModel);
        foreach (var name in feModel.CtrlNames)
        {
            if (!feModel.IsGeneratedNodeName(name))
            {
                clothBones.Add(name);
            }
        }

        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
        AddShapeParentDefaultClothNodes(softbodyChildren, feModel);
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
        return true;
    }
}
