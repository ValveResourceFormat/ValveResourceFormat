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

    // Bits of m_nDynamicNodeFlags that carry a ClothParams boolean. The remaining ClothParams switches
    // leave no bit behind and fall back to the modern Source 2 defaults.
    const uint ClothFlagUninertialRods = 0x10;
    const uint ClothFlagFollowTheLead = 0x20;
    const uint ClothFlagImmovable = 0x4000;
    const uint ClothFlagCollideWorldCapsulesAndSpheres = 0x30000;
    const uint ClothFlagCollideWorldHulls = 0x40000;
    const uint ClothFlagCollideWorldMeshes = 0x80000;

    // Bits of m_nDynamicNodeFlags that carry a Softbody node boolean rather than a ClothParams one.
    const uint ClothFlagPerBoneScaleEnabled = 0x8000;
    const uint ClothFlagKeychainMotion = 0x1000000;

    // The Softbody node's own attributes, as opposed to the ClothParams child below. The two
    // switches are omitted unless their bit is present.
    static void AddSoftbodyAttributes(KVObject softbody, FeModel fe)
    {
        softbody.Add("motion_smooth_cdt", fe.MotionSmoothCdt);

        if ((fe.DynamicNodeFlags & ClothFlagPerBoneScaleEnabled) != 0)
        {
            softbody.Add("cloth_per_bone_scale_enabled", true);
        }

        if ((fe.DynamicNodeFlags & ClothFlagKeychainMotion) != 0)
        {
            softbody.Add("cloth_keychain_motion", true);
        }
    }

    // Global cloth solver parameters, populated from the FeModel scalars. Field names match the compiled
    // ClothParams source node; the compiler re-derives everything not emitted here.
    static KVObject MakeClothParams(FeModel fe, bool generatesBendRods = false, bool generatesBendOnlyRods = false,
        float addCurvature = 0f, bool explicitMasses = false)
    {
        var flags = fe.DynamicNodeFlags;
        bool Flag(uint bits) => (flags & bits) != 0;

        return MakeNode("ClothParams",
            ("default_stretch", fe.DefaultSurfaceStretch),
            // Recovered from the rod relaxation factors, NOT from m_flDefaultThreadStretch, which tracks
            // m_flDefaultSurfaceStretch whatever the shear is.
            ("additional_shear_stretch", fe.AdditionalShearStretch),
            ("extra_iterations", fe.ExtraIterations),
            ("extra_goal_iterations", fe.ExtraGoalIterations),
            ("extra_pressure_iterations", fe.ExtraPressureIterations),
            ("goal_strength_bias", 0.0f),
            ("default_gravity_scale", fe.DefaultGravityScale),
            ("default_vel_air_drag", fe.DefaultVelAirDrag),
            ("default_exp_air_drag", fe.DefaultExpAirDrag),
            ("velocity_smooth_rate", fe.VelocitySmoothRate),
            ("internal_pressure", fe.InternalPressure),
            ("windage", fe.Windage),
            ("wind_drag", fe.WindDrag),
            ("velocity_smooth_iterations", fe.VelocitySmoothIterations),
            ("default_ground_friction", fe.DefaultGroundFriction),
            ("default_world_collision_penetration", 0.0f),
            ("add_world_collision_radius", fe.AddWorldCollisionRadius),
            ("local_force", fe.LocalForce),
            ("local_rotation", fe.LocalRotation),
            ("add_curvature", addCurvature),
            ("quad_bend_tolerance", 0.05f),
            ("local_drag1", fe.LocalDrag1),
            ("follow_the_lead", Flag(ClothFlagFollowTheLead)),
            ("use_per_node_local_force_and_rotation", fe.HasPerNodeLocalForce),
            ("uninertial_rods", Flag(ClothFlagUninertialRods)),
            ("explicit_masses", explicitMasses),
            ("unitless_damping", true),
            ("force_world_collision_on_all_nodes", fe.ForcesWorldCollisionOnAllNodes),
            ("new_style", true),
            ("can_collide_with_world_hulls", Flag(ClothFlagCollideWorldHulls)),
            ("can_collide_with_world_meshes", Flag(ClothFlagCollideWorldMeshes)),
            ("can_collide_with_world_capsule_and_spheres", Flag(ClothFlagCollideWorldCapsulesAndSpheres)),
            // A sheet whose compiled rods reach beyond its own face edges and diagonals was authored with
            // the extra bend network switched on. Recovering it lets the compiler regenerate those rods
            // from the surface, where declaring them as explicit springs would instead add a source
            // element per pair and leave the sheet heavier than the original.
            ("add_stiffness_rods", generatesBendRods),
            ("rigid_edge_hinges", fe.HasAxialEdges),
            ("add_bend_only_rods", generatesBendOnlyRods),
            ("immovable", Flag(ClothFlagImmovable)));
    }

    const float ClothSourceBaseGravity = FeModel.ClothSourceBaseGravity;

    const float ClothDragPointDampingScale = FeModel.ClothDragPointDampingScale;

    // An unrolled proxy ring sits on the joint frame's +Y, so an authored twist counts down from 90 degrees.
    const float ClothExtrudeTwistBase = 90f;

    // Explicitly declares a two-node distance constraint (a "rod") by NODE NAME: the ClothSpring node, the
    // analogue of ClothQuad for edges instead of faces. is_length_explicit=false, the default, pins
    // min_length = max_length = the rest distance, a fully rigid edge. Both is_length_explicit and
    // enable_advanced_parameters are needed together for min_length/max_length to take effect.
    //
    // weight0 and relaxation_factor are not ClothSpring inputs: it registers no attribute for either, so
    // an authored weight0 compiles to the builder's default of 0.5 while min_length/max_length stay exact
    // (see FeModel.Rod.Weight0). "stiffness" is the attribute a rod's flRelaxationFactor comes back on.
    static KVObject MakeClothSpring(string name, string n0, string n1, float minLength, float maxLength,
        float stiffness, int extraIterations = 0)
    {
        var kv = MakeNode("ClothSpring",
            ("name", name),
            ("cloth_node_0", n0),
            ("cloth_node_1", n1),
            ("stiffness", stiffness),
            ("enable_advanced_parameters", true),
            ("is_length_explicit", true),
            ("min_length", minLength),
            ("max_length", maxLength));

        if (extraIterations != 0)
        {
            kv.Add("extra_iterations", extraIterations);
        }

        return kv;
    }

    // A ClothSelfCollisionCluster's member pair compiles to exactly one m_Rods entry (flMinDist/flMaxDist
    // the summed member radii, flWeight0 the builder's own default) and leaves no other trace:
    // m_SelfCollisionLayers, m_NodeCollisionRadii and m_AnimStrayRadii are all unaffected. Unlike a
    // ClothSpring it registers no m_SourceElems entry, so it is the node to re-emit for a rod between two
    // chain joints that a chain does not itself regenerate. The per-member radius split the compiled rod
    // does not preserve (only the sum reaches m_Rods) is recovered as an even split.
    static KVObject MakeClothSelfCollisionCluster(string name, string joint0, string joint1, float radius,
        float strayRadius)
    {
        KVObject MakeJoint(string jointName)
        {
            var joint = KVObject.Collection();
            joint.Add("joint_name", jointName);
            joint.Add("collision_radius", radius);
            joint.Add("stray_radius", strayRadius);
            joint.Add("stiffness", 1.0f);
            return joint;
        }

        var joints = KVObject.Array();
        joints.Add(MakeJoint(joint0));
        joints.Add(MakeJoint(joint1));

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("selection", KVObject.Array());
        chainData.Add("version", 0);

        return MakeNode("ClothSelfCollisionCluster",
            ("name", name),
            ("algorithm", 0),
            ("chain", chainData));
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

    /// <summary>
    /// Re-declares the dynamic-to-kinematic links (<see cref="FeModel.DynKinLinks"/>) as
    /// <c>ClothFollowBone</c> nodes. One node per compiled entry, naming the link's parent node as
    /// <c>leader_bone</c> and its child node as <c>follower_bone</c>. Both endpoints must be bones this
    /// export declares in cloth (<paramref name="clothBones"/>) - the compiler rejects the whole compile
    /// over one naming a bone no cloth construct claims. Emitted in compiled order, which the compiler's
    /// own parent-before-child sort reproduces wherever the export's node order matches the original's.
    /// </summary>
    static void AddClothFollowBones(KVObject softbodyChildren, FeModel feModel, HashSet<string> clothBones)
    {
        var names = feModel.CtrlNames;
        foreach (var link in feModel.DynKinLinks)
        {
            if (link.Parent < 0 || link.Parent >= names.Length || link.Child < 0 || link.Child >= names.Length)
            {
                continue;
            }

            var leader = names[link.Parent];
            var follower = names[link.Child];
            if (!clothBones.Contains(leader) || !clothBones.Contains(follower)
                || feModel.IsGeneratedNodeName(leader) || feModel.IsGeneratedNodeName(follower))
            {
                continue;
            }

            softbodyChildren.Add(MakeNode("ClothFollowBone",
                ("name", $"follow_{link.Parent}_{link.Child}"),
                ("leader_type", ClothFollowBoneLeaderTypeBone),
                ("leader_bone", leader),
                ("follower_bone", follower)));
        }
    }

    // The only leader_type that compiles to an m_DynKinLinks entry; 2 and 3 are rejected outright.
    const int ClothFollowBoneLeaderTypeBone = 0;

    /// <summary>
    /// The bones an export declares in cloth, seeded with the collision-shape parents the compiler
    /// registers on its own. Each phase adds the bones its own constructs name.
    /// </summary>
    static HashSet<string> ClothBoneNames(FeModel feModel)
    {
        var bones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parentBone in CollisionShapeParentBones(feModel))
        {
            if (parentBone is not null)
            {
                bones.Add(parentBone);
            }
        }

        return bones;
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

    // Every bone a cloth collision shape hangs off. The compiler walks such a bone's ancestor chain and
    // registers them itself, so an explicit ClothNode on one is redundant, and it also parents the node
    // onto its nearest control-node ancestor, which the shape's own registration does not do.
    static HashSet<string?> CollisionShapeParentBones(FeModel feModel)
        => feModel.BuildCollisionCapsules().Select(static c => c.ParentBone)
            .Concat(feModel.BuildPlanarizeCapsules().Select(static c => c.ParentBone))
            .Concat(feModel.BuildCollisionSpheres().Select(static s => s.ParentBone))
            .Concat(feModel.BuildCollisionBoxes().Select(static b => b.ParentBone))
            .Where(static n => n is not null)
            .ToHashSet();

    static void AddClothCollisionShapes(KVObject softbodyChildren, FeModel feModel)
    {
        foreach (var capsule in feModel.BuildCollisionCapsules())
        {
            softbodyChildren.Add(MakeClothShapeCapsule(capsule));
        }

        foreach (var sphere in feModel.BuildCollisionSpheres())
        {
            softbodyChildren.Add(MakeClothShapeSphere(sphere));
        }

        foreach (var box in feModel.BuildCollisionBoxes())
        {
            softbodyChildren.Add(MakeClothShapeBox(box));
        }

        // Last: a planarized capsule is excluded from m_TaperedCapsuleRigids, but declaring it ahead of the
        // real ones still rotates their order in that array.
        foreach (var capsule in feModel.BuildPlanarizeCapsules())
        {
            softbodyChildren.Add(MakeClothPlanarizedShape(capsule));
        }
    }

    // A planarized shape whose end caps coincide is a sphere: the compiler drops a capsule of zero length
    // and the shape leaves no planes behind at all.
    static KVObject MakeClothPlanarizedShape(FeModel.CollisionCapsule capsule)
    {
        if ((capsule.Point1 - capsule.Point0).Length() > 1e-4f)
        {
            return MakeClothShapeCapsule(capsule);
        }

        return MakeClothShapeSphere(new FeModel.CollisionSphere
        {
            ParentBone = capsule.ParentBone,
            Center = capsule.Point0,
            Radius = capsule.Radius0,
            CollisionMask = capsule.CollisionMask,
            VertexMap = capsule.VertexMap,
            Inverted = capsule.Inverted,
            Priority = capsule.Priority,
        }, planarize: true);
    }

    // A selection solved as a volume carries its strength and the node it takes its scale from. Both are
    // authored on the container, and the volumetric strength also decides the covered nodes' masses.
    static void AddClothVertexMapAttributes(KVObject mapNode, FeModel feModel, string mapName,
        IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        var map = feModel.VertexMaps.FirstOrDefault(m => m.Name == mapName);
        if (map.Name != mapName || map.VolumetricSolveStrength <= 0f)
        {
            return;
        }

        mapNode.Add("volumetric_solve", map.VolumetricSolveStrength);

        if (ResolveAntiTunnelNodeName(feModel, map.ScaleSourceNode, proxyNodeNames) is { } scaleSource)
        {
            mapNode.Add("scale_source_node", scaleSource);
        }
    }

    // Puts a free cloth node into the ClothVertexMap containers of every selection covering it, and
    // returns where the node itself goes. Each container lists its members in the data.nodes table the
    // ClothNodeListEditor keeps, which is membership on its own (with a partial weight where the
    // selection has one) and the only route on which the compiler reads the container's
    // volumetric_solve and scale_source_node. A node covered by exactly one selection is also parented
    // under that container, the grouping the "Add Cloth Vertex Map" wizard builds, unless the caller
    // keeps it flat; a node in several selections stays flat, a child having one parent.
    static Func<int, bool, KVObject> ClothVertexMapFolders(FeModel feModel, KVObject clothFolderChildren)
    {
        var groups = new Dictionary<string, (KVObject Children, KVObject Members)>(StringComparer.Ordinal);

        (KVObject Children, KVObject Members) GroupFor(string mapName)
        {
            if (!groups.TryGetValue(mapName, out var group))
            {
                var (mapNode, mapChildren) = MakeListNode("ClothVertexMap");
                mapNode.Add("name", mapName);
                AddClothVertexMapAttributes(mapNode, feModel, mapName, proxyNodeNames: null);
                var members = KVObject.Collection();
                var data = KVObject.Collection();
                data.Add("nodes", members);
                mapNode.Add("data", data);
                clothFolderChildren.Add(mapNode);
                groups[mapName] = group = (mapChildren, members);
            }

            return group;
        }

        return (node, parentUnderMap) =>
        {
            var maps = feModel.GetVertexMapNames(node);
            if (maps is null)
            {
                return clothFolderChildren;
            }

            var memberName = ResolveAntiTunnelNodeName(feModel, node, proxyNodeNames: null);
            if (memberName is null || memberName.StartsWith('$'))
            {
                return parentUnderMap && !maps.Contains(',', StringComparison.Ordinal)
                    ? GroupFor(FeModel.VertexMapName(maps)).Children
                    : clothFolderChildren;
            }

            KVObject? home = null;
            foreach (var entry in maps.Split(','))
            {
                var mapName = FeModel.VertexMapName(entry);
                var group = GroupFor(mapName);
                var weight = feModel.VertexMapWeight(mapName, node);
                if (weight >= 1f)
                {
                    group.Members.Add(memberName, true);
                }
                else
                {
                    var member = KVObject.Collection();
                    member.Add("weight", weight);
                    group.Members.Add(memberName, member);
                }

                home = home is null ? group.Children : clothFolderChildren;
            }

            return parentUnderMap && home is not null ? home : clothFolderChildren;
        };
    }

    // A ClothAntiTunnelProbe's source_node/target names resolve through the same control-node namespace
    // as a ClothSpring endpoint: a proxy vertex needs OUR re-numbered "$cloth_m{N}p{L}" name, a free
    // ClothNode is referenced by its element name (the ctrl name with "$cloth_node_" stripped), and every
    // other ctrl (a real bone or ClothChain joint) is referenced by its plain ctrl name.
    static string? ResolveAntiTunnelNodeName(FeModel feModel, int node, IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        if (node < 0 || node >= feModel.CtrlNames.Length)
        {
            return null;
        }

        // IsProxyNodeName is too broad here (true for every generated "$..." name, not just proxy
        // vertices) - the proxy convention itself is "$cloth_m{N}p{L}", the same check MakeClothNode's
        // own BasisName uses to tell a proxy vertex apart from any other generated ctrl name.
        var name = feModel.CtrlNames[node];
        if (name.StartsWith("$cloth_m", StringComparison.Ordinal))
        {
            return proxyNodeNames?.GetValueOrDefault(node);
        }

        const string ClothNodePrefix = "$cloth_node_";
        return name.StartsWith(ClothNodePrefix, StringComparison.Ordinal) ? name[ClothNodePrefix.Length..] : name;
    }

    // ClothAntiTunnelProbe is a top-level sibling of Softbody, not a child: its class registers "Softbody"
    // as its only allowed parent and declares no allowed children of its own. The target list is not a
    // "children" array either - CModelDocClothNodeList's custom save/load stores it as a raw KV3 table at
    // data.nodes, keyed BY TARGET NAME (values unused). Target order must match
    // feModel.AntiTunnelTargetNodes exactly: the compiler round-trips a KV3 table's member order verbatim,
    // and the shipped originals do not always list targets in ascending node order.
    static void AddClothAntiTunnelProbes(KVObject rootChildren, FeModel feModel, IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        for (var i = 0; i < feModel.AntiTunnelProbes.Length; i++)
        {
            var probe = feModel.AntiTunnelProbes[i];
            var sourceName = ResolveAntiTunnelNodeName(feModel, probe.ProbeNode, proxyNodeNames);
            if (sourceName is null)
            {
                continue;
            }

            var targetNames = new List<string>();
            for (var t = probe.Begin; t < probe.Begin + probe.Count && t < feModel.AntiTunnelTargetNodes.Length; t++)
            {
                if (ResolveAntiTunnelNodeName(feModel, feModel.AntiTunnelTargetNodes[t], proxyNodeNames) is { } targetName)
                {
                    targetNames.Add(targetName);
                }
            }

            if (targetNames.Count == 0)
            {
                continue;
            }

            rootChildren.Add(MakeClothAntiTunnelProbe($"cloth_antitunnel_probe{i}", sourceName,
                animSource: probe.Flags != 0, probe.Weight, probe.ActivationDistance, targetNames));
        }
    }

    // flCurvatureRadius/flBias are 0.0 on every known compiled model (see FeModel.AntiTunnelProbes), so
    // use_curvature_drop/curvature/curvature_drop_distance/curvature_drop_amount always re-author to their
    // compiler defaults; there is no compiled signal to recover a nonzero curvature-drop setup from.
    static KVObject MakeClothAntiTunnelProbe(string name, string sourceNode, bool animSource, float weight,
        float activationDistance, IReadOnlyList<string> targetNames)
    {
        var nodes = KVObject.Collection();
        foreach (var targetName in targetNames.Distinct())
        {
            nodes.Add(targetName, true);
        }

        var data = KVObject.Collection();
        data.Add("nodes", nodes);

        return MakeNode("ClothAntiTunnelProbe",
            ("name", name),
            ("source_node", sourceNode),
            ("anim_source", animSource),
            ("ignore_missing_target_nodes", false),
            ("weight", weight),
            ("use_curvature_drop", false),
            ("curvature", 0.0f),
            ("curvature_drop_distance", 0.0f),
            ("curvature_drop_amount", 0.0f),
            ("activation_distance", activationDistance),
            ("data", data));
    }

    // Wind speeds are authored in mph and compiled to units per second.
    const float ClothWindSpeedToUnits = 17.6f;

    const int ClothEffectTypeWind = 1;
    const int ClothEffectTypeStiffen = 3;
    const int ClothEffectTypeDampenVelocity = 6;

    static void AddClothEffects(KVObject softbodyChildren, FeModel feModel, IReadOnlySet<string> availableMaps)
    {
        foreach (var effect in feModel.Effects)
        {
            if (MakeClothEffect(feModel, effect, availableMaps) is { } node)
            {
                softbodyChildren.Add(node);
            }
        }
    }

    /// <summary>
    /// The named vertex selections the export actually recreates: those painted into a proxy mesh, plus
    /// those named by a chain joint. An effect that references any other selection fails the whole compile.
    /// <para>
    /// A joint's <c>vertex_map</c> spells a partial membership <c>name=weight</c>, so each entry is
    /// reduced to its bare name - what an effect names the same selection by.
    /// </para>
    /// </summary>
    HashSet<string> AvailableVertexMaps(FeModel feModel, List<FeModel.BoneChain> chains)
    {
        var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, _, proxy) in ClothProxyMeshesToExtract)
        {
            foreach (var (mapName, _) in proxy.VertexMaps)
            {
                maps.Add(mapName);
            }
        }

        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            if (feModel.GetVertexMapNames(joint.Node) is { } names)
            {
                foreach (var name in names.Split(','))
                {
                    maps.Add(FeModel.VertexMapName(name.Trim()));
                }
            }
        }

        return maps;
    }

    static KVObject? MakeClothEffect(FeModel feModel, FeModel.Effect effect, IReadOnlySet<string> availableMaps)
    {
        var className = effect.Type switch
        {
            ClothEffectTypeWind => "ClothEffectWind",
            ClothEffectTypeStiffen => "ClothEffectStiffen",
            ClothEffectTypeDampenVelocity => "ClothEffectDampenVelocity",
            _ => null,
        };

        if (className is null || effect.Params is null)
        {
            return null;
        }

        var node = MakeNode(className, ("name", effect.Name));

        var mapHash = unchecked((uint)effect.Params.GetInt32Property("VertexMap"));
        foreach (var map in feModel.VertexMaps)
        {
            if (map.NameHash == mapHash && availableMaps.Contains(map.Name))
            {
                node.Add("vertex_map", map.Name);
                break;
            }
        }

        switch (effect.Type)
        {
            case ClothEffectTypeWind:
                AddClothWindParams(node, effect.Params);
                break;

            case ClothEffectTypeStiffen:
                node.Add("Stiffness", effect.Params.GetFloatProperty("Stiffness"));
                break;

            default:
                node.Add("drag", effect.Params.GetFloatProperty("Drag"));
                break;
        }

        return node;
    }

    static void AddClothWindParams(KVObject node, KVObject parameters)
    {
        // Strength is the authored speed scaled into units and rotated by the authored angles.
        var strength = parameters.GetSubCollection("Strength") is { } s ? s.ToVector3() : default;
        node.Add("wind_speed_mph", strength.Length() / ClothWindSpeedToUnits);
        node.Add("time_multiplier", 1.0f);

        if (strength != Vector3.Zero)
        {
            node.Add("angles", ToKVArray(EntityTransformHelper.ForwardDirectionToEulerAngles(strength)));
        }

        var airToCloth = parameters.GetFloatProperty("AirToCloth");
        if (airToCloth > 0f)
        {
            node.Add("cloth_air_density", 1f / airToCloth);
        }

        node.Add("vortex_choppiness", parameters.GetFloatProperty("Choppiness"));

        var vortices = parameters.GetArray("Vortices") ?? [];
        node.Add("vortex_count", vortices.Count);

        if (vortices.Count > 0)
        {
            node.Add("vortex_max_speed_mph", vortices[0].GetFloatProperty("MaxSpeed") / ClothWindSpeedToUnits);
            node.Add("vortex_cell_size", vortices[0].GetFloatProperty("MaxCell"));
        }
    }

    static KVObject MakeClothShapeBox(FeModel.CollisionBox box)
    {
        var node = MakeNode("ClothShapeBox",
            ("name", (box.ParentBone ?? "cloth") + "_clothBox"),
            ("parent_bone", box.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, box.CollisionMask);
        node.Add("cloth_collision_priority", box.Priority);
        node.Add("vertex_map", box.VertexMap ?? "");
        node.Add("inverted_collision", box.Inverted);
        node.Add("planarize", false);
        node.Add("bounciness", 0.0f);
        // The shape otherwise snaps to its parent bone, discarding the authored offset, and dimensions are
        // the full box size while the compiled vSize keeps half-extents.
        node.Add("recenter_on_parent_bone", false);
        node.Add("origin", ToKVArray(box.Origin));
        node.Add("angles", ToKVArray(EntityTransformHelper.ToEulerAngles(box.Rotation)));
        node.Add("dimensions", ToKVArray(box.Size * 2f));
        return node;
    }

    static KVObject MakeClothShapeCapsule(FeModel.CollisionCapsule capsule)
    {
        var node = MakeNode("ClothShapeCapsule",
            ("name", (capsule.ParentBone ?? "cloth") + (capsule.Planarize ? "_clothPlanarizedCapsule" : "_clothCapsule")),
            ("parent_bone", capsule.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, capsule.CollisionMask);
        node.Add("cloth_collision_priority", capsule.Priority);
        node.Add("vertex_map", capsule.VertexMap ?? "");
        node.Add("inverted_collision", capsule.Inverted);
        node.Add("planarize", capsule.Planarize);
        node.Add("bounciness", 0.0f);
        node.Add("radius0", capsule.Radius0);
        node.Add("radius1", capsule.Radius1);
        node.Add("point0", ToKVArray(capsule.Point0));
        node.Add("point1", ToKVArray(capsule.Point1));
        return node;
    }

    static KVObject MakeClothShapeSphere(FeModel.CollisionSphere sphere, bool planarize = false)
    {
        var node = MakeNode("ClothShapeSphere",
            ("name", (sphere.ParentBone ?? "cloth")
                + (planarize ? "_clothPlanarizedSphere" : "_clothSphere")),
            ("parent_bone", sphere.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, sphere.CollisionMask);
        node.Add("cloth_collision_priority", sphere.Priority);
        node.Add("vertex_map", sphere.VertexMap ?? "");
        node.Add("inverted_collision", sphere.Inverted);
        node.Add("planarize", planarize);
        node.Add("bounciness", 0.0f);
        node.Add("radius", sphere.Radius);
        node.Add("center", ToKVArray(sphere.Center));
        return node;
    }

    // The 4-bit collision mask maps to four boolean layer flags. An all-zero mask (no mask recorded) is
    // treated as "all layers" to match the tools' default fully-colliding capsule.
    static void AddClothCollisionLayers(KVObject node, int collisionMask)
    {
        var mask = collisionMask == 0 ? 0xF : collisionMask;
        node.Add("cloth_collision_layer0", (mask & 1) != 0);
        node.Add("cloth_collision_layer1", (mask & 2) != 0);
        node.Add("cloth_collision_layer2", (mask & 4) != 0);
        node.Add("cloth_collision_layer3", (mask & 8) != 0);
    }

    /// <summary>
    /// Whether a lone real bone must be re-authored as a single-joint <c>ClothChain</c> instead of a
    /// merged <c>ClothNode</c>: the original records it as an <c>m_SkelParents</c> ROOT, which a chain
    /// root compiles back to while a merged ClothNode is re-parented onto its nearest control-node
    /// ancestor. The single-joint chain compiles an otherwise identical node.
    /// </summary>
    static bool LoneClothNodeIsOriginalRoot(FeModel feModel, int node)
        => feModel.HasCompiledSkelParents
            && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0
            && node < feModel.NodeInvMasses.Length && feModel.NodeInvMasses[node] != 0f;

    static KVObject MakeLoneJointChain(FeModel feModel, string name, int node, bool hasOtherChains)
    {
        var chain = new FeModel.BoneChain { RootBone = name };
        chain.Joints.Add(new FeModel.BoneChainJoint
        {
            Node = node,
            Name = name,
            ParentNode = -1,
            InvMass = node < feModel.NodeInvMasses.Length ? feModel.NodeInvMasses[node] : 0f,
        });
        return MakeClothChainNode(feModel, chain, hasOtherChains);
    }

    static KVObject MakeClothChainNode(FeModel feModel, FeModel.BoneChain chain, bool hasOtherChains)
    {
        // A rigid hinge takes the chain's rod network over, so a hinged chain that still carries rods was
        // authored with a soft link instead.
        var softHinge = feModel.HasChainRods(chain);

        var joints = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            var jointNode = MakeClothJoint(feModel, joint, chainExtrudes: chain.ExtrudeSides >= 1, softHinge);
            if (feModel.SpringsHingeChildren(chain, joint.Node))
            {
                jointNode.Add("child_sibling_spring", 1.0f);
            }

            joints.Add(jointNode);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs(chain.ExtrudeSides, chain.ExtrudeRadius, chain.ExtrudeTwist));
        chainData.Add("selection", KVObject.Array());

        // The two chain formats are not interchangeable: format 1 registers a non-simulated joint that has
        // no parent to be offset from into m_LockToGoal, format 2 leaves it out. Both are in live use, so
        // the original's own m_LockToGoal membership is what says which one a chain was authored in.
        // A rotation-locked root carries a second, sharper signal: format 1 suppresses that root's
        // m_NodeBases entry and format 2 keeps it, so the original's own m_NodeBases decides those chains.
        // The node-base signal reads an ABSENT root entry as format 1, which is equally what an anchor bone
        // several sub-chains were merged under looks like: it roots no chain in the original, so nothing
        // ever gave it a base. Format 1 also locks every non-simulated, rotation-free joint of an extruding
        // chain to its goal, so an original that locks none of them rules format 1 out directly.
        var root = chain.Joints.Count > 0 ? chain.Joints[0] : null;
        var lockedInOriginal = chain.Joints.Exists(joint => feModel.IsLockedToGoal(joint.Node));
        var locksJoints = chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node));

        // A chain of one joint compiles only at version 0, but the access violation it avoids only
        // happens when the model carries a second chain; a model whose only chain has one joint
        // compiles fine at version >= 1 and keeps the node-base-driven choice below.
        chainData.Add("version", chain.Joints.Count == 1 && hasOtherChains
            ? 0
            : root is not null && !feModel.AllowsRotation(root.Node)
                && (lockedInOriginal || !locksJoints)
            ? (feModel.NodeBases.ContainsKey(root.Node) ? 2 : 1)
            : (lockedInOriginal ? 1 : 2));

        var chainNode = MakeNode("ClothChain",
            ("name", chain.RootBone + chain.DeclarationSuffix),
            ("root_bone", chain.RootBone),
            ("chain", chainData));

        // A rigid ClothChainHinge is a child node of the chain, constraining one joint by name.
        var hinges = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            if (feModel.RigidHingeJoints.TryGetValue(joint.Node, out var hingeVector))
            {
                hinges.Add(MakeNode("ClothChainHinge",
                    ("constrained_bone", joint.Name),
                    ("hinge_vector", ToKVArray(hingeVector)),
                    ("soft_hinge_link", false),
                    ("limits_enabled", false)));
            }
        }

        if (hinges.Count > 0)
        {
            chainNode.Add("children", hinges);
        }

        return chainNode;
    }

    /// <summary>
    /// The plain second declaration of the joints <c>BuildBoneChains</c> marked restated, or null when
    /// the chain has none. Emitted right after the extruding chain, so the compiler re-registers those
    /// joint nodes with these values and adds the plain parent rod, as the source's own second
    /// declaration did. Every value is read from the joint node itself, which is where the second
    /// declaration left it.
    /// </summary>
    static KVObject? MakeClothChainRestatement(FeModel feModel, FeModel.BoneChain chain)
    {
        var restated = chain.Joints.FindAll(static joint => joint.Restated);
        if (restated.Count == 0)
        {
            return null;
        }

        var members = restated.Select(static joint => joint.Node).ToHashSet();
        var joints = KVObject.Array();
        string? rootBone = null;
        foreach (var joint in restated)
        {
            var kv = KVObject.Collection();
            kv.Add("joint_name", joint.Name);

            var parented = members.Contains(joint.ParentNode);
            if (parented && joint.ParentName is { } parentName)
            {
                kv.Add("joint_parent", parentName);
            }
            else
            {
                rootBone ??= joint.Name;
            }

            kv.Add("simulate", joint.Simulated);

            var integrator = feModel.GetIntegrator(joint.Node);
            kv.Add("goal_strength", FeModel.GoalStrengthFromAttraction(integrator.ForceAttraction));
            kv.Add("goal_damping", FeModel.GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction));
            kv.Add("gravity_z", integrator.Gravity / ClothSourceBaseGravity);

            if (joint.Simulated)
            {
                kv.Add("collision_radius", feModel.GetCollisionRadius(joint.Node));
            }

            if (parented)
            {
                foreach (var rod in feModel.Rods)
                {
                    if ((rod.NodeA == joint.Node && rod.NodeB == joint.ParentNode)
                        || (rod.NodeA == joint.ParentNode && rod.NodeB == joint.Node))
                    {
                        if (rod.RelaxationFactor != 1f)
                        {
                            kv.Add("stretch_spring", rod.RelaxationFactor);
                        }

                        break;
                    }
                }
            }

            joints.Add(kv);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs());
        chainData.Add("selection", KVObject.Array());

        rootBone ??= restated[0].Name;
        return MakeNode("ClothChain",
            ("name", rootBone + "_restated"),
            ("root_bone", rootBone),
            ("chain", chainData));
    }

    static KVObject MakeClothJoint(FeModel feModel, FeModel.BoneChainJoint joint, bool chainExtrudes = false,
        bool softHinge = false)
    {
        var kv = KVObject.Collection();
        kv.Add("joint_name", joint.Name);

        if (joint.ParentName is not null)
        {
            kv.Add("joint_parent", joint.ParentName);
        }

        // The compiler CUBES the joint goal_strength into flAnimationForceAttraction, the same way it
        // treats the painted cloth_goal_strength_v2 on a proxy mesh, so the emitted value is the cube root
        // of the recovered attraction.
        //
        // It is recovered regardless of joint.Simulated: a chain ROOT is routinely authored
        // `simulate = false` with a nonzero goal_strength, so gating on the flag would zero goal_strength
        // on every chain root.
        //
        // A joint the source declared twice keeps the second declaration's values on its own node;
        // the first declaration's values survive on the ring it extruded (MakeClothChainRestatement
        // emits the second declaration from the node). Where the two declarations each extruded a ring
        // of their own, BoneChainJoint.ValueNode names this declaration's.
        var valueNode = joint.ValueNode >= 0 ? joint.ValueNode
            : joint.Restated && joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        var integrator = feModel.GetIntegrator(valueNode);
        var goalStrength = FeModel.GoalStrengthFromAttraction(integrator.ForceAttraction);

        kv.Add("simulate", joint.Simulated);

        // Only a static node carries a rotation lock.
        if (joint.Node < feModel.StaticNodeCount)
        {
            kv.Add("allow_rotation", feModel.AllowsRotation(joint.Node));
        }

        if (feModel.IsLockedToParent(joint.Node))
        {
            kv.Add("lock_translation", true);
        }

        kv.Add("goal_strength", goalStrength);
        kv.Add("goal_damping", FeModel.GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction));

        // The same flPointDamping channel the proxy sheets carry as cloth_drag.
        var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);
        if (drag != 0f)
        {
            kv.Add("drag", drag);
        }

        var gravityNode = joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        kv.Add("gravity_z", feModel.GetIntegrator(gravityNode).Gravity / ClothSourceBaseGravity);

        // A non-zero twist_relax, stiff_hinge or motion_bias makes the compiler build a Twist or
        // KelagerBend constraint network in place of the plain ropes a chain otherwise compiles to, so
        // each is recovered per joint, magnitude included, from the original's own m_Twists participation
        // (FeModel.GetAuthoredTwistRelax) rather than defaulted.
        kv.Add("twist_relax", feModel.GetAuthoredTwistRelax(joint.Node, joint.ParentNode, joint.ProxyNode));

        // World collision membership and radius (m_WorldCollisionNodes / m_NodeCollisionRadii).
        kv.Add("world_collision", feModel.IsWorldCollisionNode(joint.Node));

        var (worldFriction, groundFriction) = feModel.GetWorldFriction(joint.Node);
        kv.Add("world_friction", worldFriction);
        kv.Add("ground_friction", groundFriction);
        kv.Add("collision_radius", feModel.GetCollisionRadius(valueNode));

        // Stray radius (m_AnimStrayRadii): the max distance the node may stray from its animated position.
        // A joint whose own node is pinned records it on its ring alone, which is also the only place a
        // shared joint's second declaration keeps its own.
        var strayNode = joint.ValueNode >= 0 ? joint.ValueNode : joint.Node;
        kv.Add("stray_radius", feModel.GetStrayRadius(strayNode));
        kv.Add("stray_radius_stretchiness", feModel.GetStrayStretchiness(strayNode));
        kv.Add("friction", feModel.GetNodeFriction(joint.Node));

        if (feModel.RecoverJointMassMultiplier(joint.Node) is { } massMultiplier)
        {
            kv.Add("mass", massMultiplier);
        }

        // The named vertex selections this joint belongs to, comma separated. Naming them here is what
        // puts the joint and the proxies extruded from it back into the selections cloth effects target.
        // A joint that does not simulate stays out of the selection itself while its proxies join it, so
        // when the joint's own node belongs to none the proxies it extruded carry the membership.
        if ((feModel.GetVertexMapNames(joint.Node)
            ?? (joint.ProxyNode >= 0 ? feModel.GetVertexMapNames(joint.ProxyNode) : null))
            is { } vertexMaps)
        {
            kv.Add("vertex_map", vertexMaps);
        }

        // The hinge constraint the ClothChainHinge node writes onto the joint it constrains. It both
        // orients that joint's proxy ring and adds the compiler's own static anchor node, so a joint that
        // shipped one loses a control node without it - and a joint that did not gains one.
        var hinge = feModel.GetChainHinge(joint.Name, joint.Node);

        // Per-joint extrude width. The chain-level extrude_sides (MakeClothChainAttrs) is one uniform
        // value, so it cannot reproduce a ribbon whose END-CAP joint fans wider than its body; overriding
        // it per joint recovers that fan. A chain that extrudes at all emits every joint's own width,
        // including an explicit 0 for a joint that carries no proxies, which would otherwise inherit the
        // chain-level default. A chain that does not extrude emits nothing.
        if (chainExtrudes)
        {
            kv.Add("extrude_sides", joint.ExtrudeSides);

            // Ring geometry varies along a chain, so the chain-level defaults only fit one joint. Emit each
            // joint's own measured ring instead.
            if (joint.ExtrudeSides > 0)
            {
                kv.Add("extrude_radius", joint.ExtrudeRadius);
                kv.Add("extrude_twist", ClothExtrudeTwistBase - joint.ExtrudeTwist + joint.ExtrudeTwistTieNudge);

                // 'x' is the compiler's own default and needs no explicit key.
                if (joint.ForwardAxis != 'x')
                {
                    kv.Add("extrude_forward_axis", joint.ForwardAxis.ToString());
                }
            }

        }

        // A tip that fans into two rows is a second ring this far along the joint's forward axis, not
        // one ring of twice the width - the wider ring puts every proxy somewhere else entirely. A
        // hinged joint that carries only the hinge's own two proxies has no second ring to recover:
        // that pair straddles the hinge axis, which reads as two rings a ring apart. Emitted outside the
        // extrude block: a joint whose only generated node is the "$cc<bone>_Ctr" centre has an
        // end_effector but no ring at all, so its chain never extrudes.
        if (joint.EndEffector != 0f && (hinge is null || feModel.ProxyCountOf(joint.Node) > 2))
        {
            kv.Add("end_effector", joint.EndEffector);
        }

        // Each of the three sliders lands verbatim on the flRelaxationFactor of the rod it generates, so
        // they carry the recovered per-joint stiffness rather than a 1.0/0.0 on-off (see
        // FeModel.BuildBoneChains). Zero still means "no rod at all" on the bend and torsion spans.
        // 1.0 is stretch_spring's own attr default and needs no explicit key.
        if (joint.StretchStiffness != 1.0f)
        {
            kv.Add("stretch_spring", joint.StretchStiffness);
        }

        kv.Add("bend_spring", joint.BendStiffness);
        kv.Add("torsion_spring", joint.TorsionStiffness);
        kv.Add("extra_iterations", joint.ExtraIterations);
        kv.Add("suspender", joint.Suspender);

        // A stiff hinge compiles to a three-node bend rather than a rod, so it is recovered from the bend
        // centred on this joint (see FeModel.GetStiffHinge).
        if (feModel.GetStiffHinge(joint.Node) is { } stiffHinge)
        {
            kv.Add("stiff_hinge", stiffHinge.Stiffness);
            kv.Add("stiff_hinge_angle", stiffHinge.Angle);

            if (stiffHinge.MotionBias != 0f)
            {
                kv.Add("motion_bias", stiffHinge.MotionBias);
            }
        }

        if (hinge is { } chainHinge)
        {
            kv.Add("hinge_constraint_vector_worldspace", ToKVArray(chainHinge.Vector));
            kv.Add("hinge_constraint_soft", softHinge);
            kv.Add("hinge_constraint_limit_cw", chainHinge.LimitCw);
            kv.Add("hinge_constraint_limit_ccw", chainHinge.LimitCcw);
        }

        return kv;
    }

    // Emits a standalone ClothNode for a simulated real bone that is NOT part of any multi-joint
    // BoneChain and NOT back-solved by a proxy mesh: individual tie points connected only by explicit
    // ClothSpring, since a real bone with no real-bone descendants of its own never forms a BoneChain
    // (see BuildBoneChains). Mirrors MakeClothJoint's integrator recovery, which is what keeps the bone's
    // per-node cloth paint off the compiler defaults; its rods round-trip through AddClothProxySprings
    // either way, a plain skeleton bone name being a valid ClothSpring endpoint on its own.
    //
    // node_base_x0/x1/y0/y1 are read straight out of feModel.NodeBases and re-declared by NAME. A node
    // left without them registers as position-driven and is driven through a synthesized m_Ropes fallback
    // rather than simulated.
    static KVObject MakeClothNode(FeModel feModel, string boneName, int node, bool isStaticNode = false,
        string? elementName = null, Vector3 origin = default,
        IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        var integrator = feModel.GetIntegrator(node);
        var goalStrength = FeModel.GoalStrengthFromAttraction(integrator.ForceAttraction);
        var goalDamping = FeModel.GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction);
        var strayRadius = feModel.GetStrayRadius(node);

        // A basis reference names a node in the AUTHORED namespace, which is not the ctrl namespace: a
        // proxy vertex takes the name our own proxy split gives it and a free cloth node is declared under
        // its element name with the "$cloth_node_" prefix stripped, so echoing the ctrl name leaves a
        // reference that resolves to nothing and the compiler recomputes the basis instead.
        var hasBasis = feModel.NodeBases.TryGetValue(node, out var basis);
        string BasisName(int basisNode)
        {
            if (!hasBasis || basisNode < 0 || basisNode >= feModel.CtrlNames.Length)
            {
                return string.Empty;
            }

            return ResolveAntiTunnelNodeName(feModel, basisNode, proxyNodeNames) ?? string.Empty;
        }

        return MakeNode("ClothNode",
            ("name", elementName ?? boneName),
            ("origin", ToKVArray(origin)),
            ("angles", ToKVArray(Vector3.Zero)),
            ("cloth_node_root_bone", boneName),
            ("has_stray_radius", strayRadius > 0f),
            ("has_world_collision", feModel.IsWorldCollisionNode(node)),
            ("cloth_collision_layer0", true),
            ("cloth_collision_layer1", true),
            ("cloth_collision_layer2", true),
            ("cloth_collision_layer3", true),
            // The default alignment leaves a free cloth node with no basis at all - the neighbour scan
            // that would build one finds nothing. Alignment 4 both restores the basis and reproduces the
            // reference quadruple the original carries; on a node the scan can already serve it changes
            // the frame instead, so it is written only where the original has a basis the default drops.
            ("transform_alignment", hasBasis && elementName is not null ? 4 : 0),
            ("node_base_y1", BasisName(basis.NodeY1)),
            ("node_base_x1", BasisName(basis.NodeX1)),
            ("node_base_y0", BasisName(basis.NodeY0)),
            ("node_base_x0", BasisName(basis.NodeX0)),
            ("lock_translation", feModel.IsLockedToParent(node)),
            ("gravity_z", integrator.Gravity / ClothSourceBaseGravity),
            ("goal_strength", goalStrength),
            ("goal_damping", goalDamping),
            ("mass", feModel.RecoverMassMultiplier(node) ?? 1.0f),
            ("friction", feModel.GetNodeFriction(node)),
            ("stray_radius", strayRadius),
            ("stray_radius_relaxation_factor", 1.0f),
            ("collision_radius", feModel.GetCollisionRadius(node)),
            ("is_static_node", isStaticNode),
            ("allow_rotation", feModel.AllowsRotation(node)),
            ("super_damping", Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f)));
    }

    // The cloth-chain joint datatable schema: per-column UI metadata and defaults, matching the editable
    // ModelDoc source the tools produce. The compiler takes the "default" value of any joint field the
    // joint rows above do not write.
    static KVObject MakeClothChainAttrs(int extrudeSides = 0, float extrudeRadius = 0f, float extrudeTwist = 0f)
    {
        var attrs = KVObject.Collection();

        KVObject AddAttr(string key, string display, bool show, int uiOrder)
        {
            var attr = KVObject.Collection();
            attr.Add("display", display);
            attr.Add("show", show);
            attr.Add("ui_order", uiOrder);
            attrs.Add(key, attr);
            return attr;
        }

        KVObject FloatAttr(string key, string display, bool show, int uiOrder, float def, float? min = null, float? max = null)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            if (min.HasValue) { attr.Add("min", min.Value); }
            if (max.HasValue) { attr.Add("max", max.Value); }
            return attr;
        }

        KVObject IntAttr(string key, string display, bool show, int uiOrder, int def, int? min = null, int? max = null)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            if (min.HasValue) { attr.Add("min", min.Value); }
            if (max.HasValue) { attr.Add("max", max.Value); }
            return attr;
        }

        KVObject BoolAttr(string key, string display, bool show, int uiOrder, bool def)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            return attr;
        }

        KVObject StringAttr(string key, string display, bool show, int uiOrder)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", "");
            return attr;
        }

        // The complete version-2 attr set. An incomplete v1-era key list makes the v2 joint grid ignore
        // the table and fall back to default columns. Attrs with values recovered from the compiled
        // FeModel are shown; the rest keep stock visibility.
        StringAttr("joint_name", "Joint Name", true, 1).Add("lock", true);
        StringAttr("joint_parent", "Parent Joint", false, 2);
        BoolAttr("simulate", "Simulate", true, 3, true);
        BoolAttr("allow_rotation", "Allow Rotation", false, 4, true);
        // The display names match the ClothChainAttrEditor schema and are ModelDoc UI labels only.
        FloatAttr("stretch_spring", "Stretch Stiffness", false, 5, 1.0f, 0.0f, 1.0f);
        FloatAttr("child_sibling_spring", "Spring Between Children", false, 6, 0.0f, 0.0f, 1.0f);
        FloatAttr("bend_spring", "Bend Stiffness", false, 7, 1.0f, 0.0f, 1.0f);
        FloatAttr("torsion_spring", "Torsion Stiffness", false, 8, 0.0f, 0.0f, 1.0f);
        FloatAttr("explicit_length", "Explicit Length", false, 9, 0.0f, 0.0f);
        BoolAttr("world_collision", "World Ground Collision", true, 10, false);
        BoolAttr("animated_length", "Animated Length", false, 11, false);
        FloatAttr("goal_strength", "Goal Strength", true, 12, 0.0f, 0.0f, 1.0f);
        FloatAttr("goal_damping", "Goal Damping", true, 13, 0.0f, 0.0f, 1.0f);
        FloatAttr("drag", "Extra Drag", false, 14, 0.0f, 0.0f, 1.0f);
        FloatAttr("mass", "Mass", false, 15, 1.0f, 0.0f);
        FloatAttr("gravity_z", "Gravity", true, 16, 1.0f);
        FloatAttr("collision_radius", "Collision Radius", true, 17, 0.0f, 0.0f);
        BoolAttr("lock_translation", "Lock Translation", false, 18, false);
        FloatAttr("suspender", "Suspender Spring", false, 19, 0.0f);
        FloatAttr("antishrink", "Antishrink Strength", false, 20, 1.0f, 0.0f, 1.0f);
        FloatAttr("stray_radius", "Stray Radius", true, 21, 0.0f, 0.0f);
        FloatAttr("stray_radius_stretchiness", "Stray Radius Stretchiness", false, 22, 0.0f, 0.0f);
        FloatAttr("friction", "Friction", false, 23, 0.0f, 0.0f, 1.0f);
        StringAttr("vertex_map", "Vertex Map", false, 24).Add("verify", "vertex_map");
        FloatAttr("end_effector", "End Effector", false, 25, 0.0f).Add("lock_default_value", true);
        FloatAttr("stiff_hinge", "Stiff Hinge", true, 26, 0.0f, 0.0f, 1.0f).Add("lock_root2", true);
        FloatAttr("stiff_hinge_angle", "Stiff Hinge Angle", true, 27, 0.0f, 0.0f, 180.0f).Add("lock_root2", true);
        FloatAttr("motion_bias", "Motion Bias", true, 28, 0.0f, -1.0f, 1.0f).Add("lock_root", true);
        IntAttr("extra_iterations", "Extra Iterations", true, 29, 0, 0, 1000);
        FloatAttr("twist_relax", "Twist Relax", true, 30, 0.0f, 0.0f, 1.0f);
        // Recovered per chain from the compiled $cc proxy width (see FeModel.BuildBoneChains): a 2-wide
        // strip or N-sided tube regenerates its proxies only when the ClothChain re-declares the extrude.
        // extrudeSides 0 keeps the stock default, a plain rope.
        IntAttr("extrude_sides", "Extrude Sides", false, 31, extrudeSides, 0, 4);
        FloatAttr("extrude_radius", "Extrude Radius", false, 32, extrudeSides >= 1 ? extrudeRadius : 5.0f, 0.0f);
        FloatAttr("extrude_twist", "Extrude Twist", false, 33, extrudeSides >= 1 ? extrudeTwist : 0.0f);
        StringAttr("extrude_forward_axis", "Extrude Forward Axis", false, 34).Add("verify", "extrude_forward_axis");
        FloatAttr("world_friction", "Ground Softness (\"world friction\" in Source1)", false, 35, 0.0f, 0.0f, 1.0f);
        FloatAttr("ground_friction", "Ground Friction", false, 36, 0.0f, 0.0f, 1.0f);
        StringAttr("stray_box", "Stray Box", false, 37).Add("verify", "stray_box");
        BoolAttr("collision_layer_0", "Collision Layer 0", false, 38, true);
        BoolAttr("collision_layer_1", "Collision Layer 1", false, 39, true);
        BoolAttr("collision_layer_2", "Collision Layer 2", false, 40, true);
        BoolAttr("collision_layer_3", "Collision Layer 3", false, 41, true);

        return attrs;
    }

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
