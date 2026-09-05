using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // ModelDoc's ImportedCloth node ("Imported PhysAuthFx Cloth", CModelDocImportedCloth, wizard
    // wizard_import_legacy_cloth) carries a whole cloth in three raw KV members the compiler copies
    // verbatim out of the element: `fx` - a table holding the m_Nodes and m_Rods row arrays - plus
    // `bone_attrs`/`rod_attrs`, per-column tables whose "default" entry is merged into any row field the
    // row itself omits. Writing every field explicitly on each row leaves both attr tables empty.
    //
    // A node row is the compiler's own fx-bone struct. m_Name is looked up against the nodes already built
    // (case-insensitively) and appended when absent, so the compiled control-node order is the row order.
    // m_Transform is 7 floats, [px py pz qx qy qz qw] - a compiled m_InitPose entry with its w dropped.
    // A rod row addresses nodes by INDEX into m_Nodes, and its compiled flMaxDist is the rest distance
    // between the two rows' transforms with flMinDist that times m_flContractionFactor (default 0.05), so
    // both fall out of the transforms unless the original's own values disagree.
    //
    // Per-node mass reaches the compiled m_NodeInvMasses only under ClothParams explicit_masses; without it
    // the compiler derives inverse masses from rod geometry instead.
    static KVObject MakeImportedCloth(FeModel feModel)
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
        for (var node = 0; node < feModel.CtrlNames.Length; node++)
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

            // A row flagged m_bVirtual compiles to an offset of its m_nParent rather than to an
            // independent particle: it is excluded from the rope runs and from the extra node bases, and
            // it takes an m_CtrlOffsets entry holding its rest position in the parent's frame. Adding
            // m_bOsOffset moves that entry to m_CtrlOsOffsets, where the offset is the difference of the
            // two rows' m_Transform positions in object space instead.
            var isOsOffsetChild = osOffsetParents.TryGetValue(node, out var osOffsetParent);
            if (isOsOffsetChild)
            {
                row.Add("m_bVirtual", true);
                row.Add("m_bOsOffset", true);
            }

            // m_SkelParents is the compiled image of this exact field, so a model whose original still
            // carries one hands the authored parenting back directly. Older compiles ship none and leave
            // only the m_Ropes runs, which record the same chain a rope's worth at a time. Neither covers
            // an os-offset child: a virtual node sits in no rope run, and its own pair names the parent.
            if (isOsOffsetChild)
            {
                row.Add("m_nParent", osOffsetParent);
            }
            else if (feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length && feModel.SkelParents[node] >= 0)
            {
                row.Add("m_nParent", feModel.SkelParents[node]);
            }
            else if (!feModel.HasCompiledSkelParents && ropeParents.TryGetValue(node, out var parent))
            {
                row.Add("m_nParent", parent);
            }

            if (followLinks.TryGetValue(node, out var follow))
            {
                row.Add("m_nFollowParent", follow.Parent);
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
            var row = KVObject.Collection();
            row.Add("m_nNodes", MakeArray(rod.NodeA, rod.NodeB));

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

    const float ImportedClothDefaultContraction = 0.05f;

    bool EmitImportedClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 3: the cloth was imported from a Source 1 PhysAuthFx definition, whose node and rod
        // tables the .vmdl carries verbatim (see MakeImportedCloth). Recovering it as ClothChains
        // instead is always wrong: the strip's paired second column is not a chain ribbon, and the
        // extrude that recovery emits replaces one paired node with a three-node $cc ring.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel, explicitMasses: true));

        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);
        clothFolderChildren.Add(MakeImportedCloth(feModel));

        // Every real control node ships as a node row of the imported table, so all of them are cloth.
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
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
        return true;
    }
}
