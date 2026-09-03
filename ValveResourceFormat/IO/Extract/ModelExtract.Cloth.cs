using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Reconstructs editable ModelDoc cloth source from a compiled soft-body <see cref="FeModel"/>: the
/// <c>Softbody</c> node tree written into the vmdl, and the proxy-sheet and chain-grid DMX files it
/// references.
/// </summary>
partial class ModelExtract
{

    // Queues a cloth proxy-mesh DMX when the model carries a soft-body FeModel with a surface (quads/tris),
    // or generated sheet grids over the bone chains when the original cloth is chain-only.
    private void EnqueueClothProxyMesh()
    {
        if (model is null || physAggregateData?.FeModel is not { } feModel)
        {
            return;
        }

        var skeletonBoneNames = model.Skeleton.Bones
            .Select(static bone => bone.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        feModel.SkeletonBoneNames = skeletonBoneNames;

        // Culled cloth-only bones get re-declared in the exported skeleton, so the cloth pipeline
        // treats their names as real from here on.
        CulledClothBones.AddRange(feModel.GetCulledBoneCtrls());
        feModel.CulledBoneCtrlNodes = CulledClothBones.Select(static c => c.Node).ToHashSet();
        foreach (var (_, culledName) in CulledClothBones)
        {
            skeletonBoneNames.Add(culledName);
        }

        var boneParents = model.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().Parent?.Name, StringComparer.OrdinalIgnoreCase);
        feModel.SkeletonBoneParents = boneParents;
        feModel.SetSkeletonParents(boneParents);
        feModel.PrunePinnedRecoveries(boneParents);

        BuildClothRestBonePositions(feModel);

        // An imported PhysAuthFx cloth ships its own node/rod tables and is emitted as a single
        // ImportedCloth element, so neither a synthesised proxy sheet nor a chain grid has anything to
        // attach to and their DMX files would be written for nothing.
        if (feModel.IsImportedCloth)
        {
            return;
        }

        // The compiler assigns the $cloth_m<N> mesh index by ORDINAL STRING SORT of the proxy names rather
        // than by declaration order, so "cloth_proxy10" sorts before "cloth_proxy2". Zero-padding the
        // suffix to the model's own digit count keeps declaration order and sort order identical; a model
        // with up to 10 proxies keeps single-digit names.
        var proxyMeshes = feModel.BuildProxyMeshes().ToList();
        var suffixWidth = Math.Max(1, (proxyMeshes.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        var proxyIndex = 0;
        foreach (var proxyMesh in proxyMeshes)
        {
            // One proxy per island, like the originals (node names $cloth_mXpY encode the mesh index).
            var proxyName = proxyIndex > 0
                ? "cloth_proxy" + proxyIndex.ToString(CultureInfo.InvariantCulture).PadLeft(suffixWidth, '0')
                : "cloth_proxy";
            ClothProxyMeshesToExtract.Add((GetDmxFileName_ForEmbeddedMesh(proxyName), proxyName, proxyMesh));
            proxyIndex++;
        }

        // Regular sheet grids over the bone chains are generated in BOTH cases: as the only sheet for
        // chain-only cloth, and as an alternative clean editable grid next to a recovered surface.
        // They always ship disabled (see the vmdl emission) - purely a ready-made authoring asset.
        var gridIndex = 0;
        foreach (var grid in feModel.BuildChainGrids())
        {
            var name = "cloth_grid" + (gridIndex > 0 ? gridIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
            ClothChainGridsToExtract.Add((GetDmxFileName_ForEmbeddedMesh(name), name, grid));
            gridIndex++;
        }
    }

    // How far a control node's recorded rest position may sit from the same bone's compiled bind pose and
    // still be read as the same pose at better precision. Past a whole unit the node sits somewhere else
    // entirely and that bone keeps its compiled transform.
    const float ClothRestBoneTolerance = 1.0f;

    // And how far it has to sit before the disagreement is worth acting on: below the floor the round trip
    // holds the two poses equal.
    const float ClothRestBoneFloor = 1e-3f;

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
        void Measure(Bone bone, Vector3 compiledParent, Quaternion parentRotation)
        {
            var compiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
            var rotation = parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                if (apart <= ClothRestBoneTolerance)
                {
                    maxApart = Math.Max(maxApart, apart);
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

        if (maxApart <= ClothRestBoneModelGate)
        {
            return;
        }

        void Walk(Bone bone, Vector3 parentPosition, Quaternion parentRotation, Vector3 compiledParent)
        {
            var world = parentPosition + Vector3.Transform(bone.Position, parentRotation);
            var compiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
            var rotation = parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                if (apart > ClothRestBoneFloor && apart <= ClothRestBoneTolerance)
                {
                    world = target;
                }
            }

            var local = Vector3.Transform(world - parentPosition, Quaternion.Conjugate(parentRotation));
            if (local != bone.Position)
            {
                ClothRestBonePositions[bone.Name] = local;
            }

            foreach (var child in bone.Children)
            {
                Walk(child, world, rotation, compiled);
            }
        }

        foreach (var root in model.Skeleton.Roots)
        {
            Walk(root, Vector3.Zero, Quaternion.Identity, Vector3.Zero);
        }
    }

    // A ClothProxyMeshFile referencing the cloth-sheet DMX. With backSolveJoints=true the compiler
    // back-solves the skinned bone-chain joints from the simulated sheet, regenerating the bone-chain
    // FeModel nodes (so the proxy covers the WHOLE cloth and ClothChain is not needed - and must not be
    // emitted, or the bones would be driven twice). Generated chain grids use backSolveJoints=false:
    // there the ClothChains simulate the bones and the sheet only drives the render mesh between them.
    //
    // back_solve_joints_drive_meshes tracks backSolveJoints rather than being a blanket true or false: the
    // flag is not scoped to this proxy's own nodes when it disagrees with back_solve_joints, and the
    // compiler then back-solves fit matrices for unrelated bones elsewhere in the model. The disabled
    // ready-made grid is the exception: it passes backSolveJoints=false but driveMeshes=true, so a
    // re-author can enable it to drive the mesh directly.
    //
    // back_solve_influence_threshold is the minimum skin weight at which a vertex contributes to a joint's
    // back-solved fit. The value is derived per proxy from the original's own compiled fit data (see
    // FeModel.GetBackSolveInfluenceThreshold); the parameter default is the compiler's own, for the
    // generated grids that carry no proxy to derive from.
    static KVObject MakeClothProxyMeshFile(string name, string fileName, bool backSolveJoints, bool driveMeshes, bool addBonesToRenderMesh = false,
        float backSolveInfluenceThreshold = FeModel.DefaultBackSolveInfluenceThreshold, bool flexClothBorders = false)
    {
        var node = MakeNode("ClothProxyMeshFile",
            ("name", name),
            ("filename", fileName),
            ("import_scale", 1.0f),
            ("back_solve_joints", backSolveJoints),
            ("back_solve_joints_drive_meshes", driveMeshes),
            ("flex_cloth_borders", flexClothBorders),
            ("add_bones_to_render_mesh", addBonesToRenderMesh),
            ("back_solve_influence_threshold", backSolveInfluenceThreshold),
            ("cloth_friction_bias", 0.0f),
            ("cloth_friction_scale", 1.0f),
            ("lock_friction_0", false),
            ("lock_friction_1", false),
            ("cloth_goal_strength_bias", 0.0f),
            ("cloth_goal_strength_scale", 1.0f),
            ("lock_goal_strength_0", false),
            ("lock_goal_strength_1", false),
            ("cloth_drag_scale", 1.0f),
            ("cloth_mass_scale", 1.0f),
            ("cloth_gravity_scale", 1.0f),
            ("cloth_collision_radius_scale", 1.0f),
            ("cloth_ground_collision_scale", 1.0f),
            ("cloth_ground_friction_scale", 1.0f),
            ("cloth_use_rods_scale", 1.0f),
            ("cloth_make_rods_scale", 1.0f),
            ("cloth_anchor_free_rotate_scale", 1.0f),
            ("cloth_volumetric_scale", 1.0f),
            ("cloth_suspenders_scale", 1.0f),
            ("cloth_bend_stiffness_scale", 1.0f),
            ("cloth_stray_radius_inv_scale", 1.0f),
            ("cloth_stray_radius_scale", 1.0f),
            ("cloth_stray_radius_stretchiness_scale", 1.0f));

        // envelope_inches (how far the sheet reaches when DRIVING render meshes) is not emitted, matching
        // how hand-authored proxies ship. A large value drive-binds essentially the whole render mesh to
        // the sheet, and those bindings live in the compiled vmesh rather than in the PHYS block.

        var importFilter = KVObject.Collection();
        importFilter.Add("exclude_by_default", false);
        importFilter.Add("exception_list", KVObject.Array());
        node.Add("import_filter", importFilter);
        return node;
    }

    // Maps each global control-node index covered by an exported proxy mesh to the "$cloth_m{N}p{local}"
    // name the compiler will create for it in OUR export (declaration order; kept aligned with the
    // compiler's own name-sorted numbering by the padded proxy names, see EnqueueClothProxyMesh). Only
    // faced vertices are mapped: an unfaced vertex is silently dropped by the importer, so a reference to
    // it is a hard compile failure ("Cannot find node") - see TriangulateDominantPlane remarks.
    static Dictionary<int, string> BuildProxyNodeNameMap(
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies)
    {
        var proxyNodeNames = new Dictionary<int, string>();
        for (var proxyIndex = 0; proxyIndex < proxies.Count; proxyIndex++)
        {
            var proxy = proxies[proxyIndex].Proxy;
            var nodeIndices = proxy.NodeIndices;

            var faced = new HashSet<int>();
            foreach (var face in proxy.Faces)
            {
                foreach (var localIndex in face)
                {
                    faced.Add(localIndex);
                }
            }

            for (var localIndex = 0; localIndex < nodeIndices.Length; localIndex++)
            {
                if (faced.Contains(localIndex))
                {
                    proxyNodeNames[nodeIndices[localIndex]] = $"$cloth_m{proxyIndex}p{localIndex}";
                }
            }
        }

        return proxyNodeNames;
    }

    // The vertices of an exported proxy mesh that survive into the compiled node set. Two importer rules
    // remove the rest, and a removed vertex registers neither itself nor the bones it is skinned to:
    // an unfaced vertex is dropped outright (the same rule BuildProxyNodeNameMap maps around), and a
    // pinned vertex whose face-neighbours are all pinned belongs to a fully-static region the solver
    // discards (the first of the two conditions behind FeModel.ProxyMesh.IsDropRisk).
    static HashSet<int> SurvivingProxyVertices(FeModel.ProxyMesh proxy)
    {
        var hasSimulatedNeighbour = new bool[proxy.Positions.Length];
        var surviving = new HashSet<int>();

        foreach (var face in proxy.Faces)
        {
            foreach (var a in face)
            {
                surviving.Add(a);
                foreach (var b in face)
                {
                    if (a != b && proxy.ClothEnable[b] != 0f)
                    {
                        hasSimulatedNeighbour[a] = true;
                    }
                }
            }
        }

        surviving.RemoveWhere(v => proxy.ClothEnable[v] == 0f && !hasSimulatedNeighbour[v]);
        return surviving;
    }

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

    // A compiled model can carry two spellings of one bone: m_modelSkeleton's m_boneName and, for cloth
    // control nodes, the FeModel's m_CtrlName. Both are authored, and the compiler records each verbatim
    // because every bone lookup it does is case-insensitive. This export has one name per bone, so a bone
    // the compiler registers as a control node through a blend INDEX rather than a KV name string comes
    // back under the skeleton's spelling instead of the cloth data's.
    //
    // Re-spelling the joints of THIS sheet alone leaves everything else in place: the compiler still binds
    // each joint to the same bone case-insensitively, the model skeleton and every other DMX keep the
    // spelling they were compiled with, and the control node lands under the cloth data's name.
    static void RespellJointsAsClothControlNodes(DmeModel dmeModel, FeModel? feModel)
    {
        if (feModel is null || feModel.CtrlNames.Length == 0)
        {
            return;
        }

        var clothSpelling = new Dictionary<string, string>(feModel.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var ctrlName in feModel.CtrlNames)
        {
            clothSpelling.TryAdd(ctrlName, ctrlName);
        }

        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint
                && clothSpelling.TryGetValue(joint.Name, out var spelling) && spelling != joint.Name)
            {
                joint.Name = spelling;
                joint.Transform.Name = spelling;
            }
        }
    }

    /// <summary>
    /// Adds the culled cloth bones the vmdl re-declares (<see cref="AddCulledClothBones"/>) to a cloth
    /// DMX's joint list, as root joints at their control node's rest transform, and registers them in
    /// <paramref name="boneIndexByName"/> so the sheet's skin weights can reference them.
    /// </summary>
    void AppendCulledClothBoneJoints(DmeModel dmeModel, Dictionary<string, int> boneIndexByName)
    {
        if (physAggregateData?.FeModel is not { } feModel)
        {
            return;
        }

        foreach (var (node, culledName) in CulledClothBones)
        {
            if (node >= feModel.InitPosePositions.Length || boneIndexByName.ContainsKey(culledName))
            {
                continue;
            }

            var joint = new DmeJoint { Name = culledName };
            joint.Transform.Name = culledName;
            joint.Transform.Position = feModel.InitPosePositions[node];
            joint.Transform.Orientation = node < feModel.InitPoseRotations.Length
                ? feModel.InitPoseRotations[node]
                : Quaternion.Identity;
            boneIndexByName[culledName] = dmeModel.JointList.Count;
            dmeModel.JointList.Add(joint);
            dmeModel.Children.Add(joint);
        }
    }

    void AddCulledClothBones(KVObject skeletonChildren)
    {
        // Bones the compiled skeleton culled (unskinned cloth-only joints) but the cloth still
        // references. Re-declared WITHOUT do_not_discard so the compiler culls them again; the cloth
        // build resolves against the document skeleton, which is all these need to exist in.
        var culledSource = physAggregateData?.FeModel;
        foreach (var (node, name) in CulledClothBones)
        {
            if (culledSource is null || node >= culledSource.InitPosePositions.Length)
            {
                continue;
            }

            var boneAngles = node < culledSource.InitPoseRotations.Length
                ? EntityTransformHelper.ToEulerAngles(culledSource.InitPoseRotations[node])
                : Vector3.Zero;
            skeletonChildren.Add(MakeNode("Bone",
                ("name", name),
                ("origin", ToKVArray(culledSource.InitPosePositions[node])),
                ("angles", ToKVArray(boneAngles))));
        }
    }
}
