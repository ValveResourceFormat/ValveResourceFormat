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
    // Sheets EmitProxySheetClothPhase re-emits with flex_cloth_borders on; their pinned vertices
    // get freed by the flag, every other sheet's freed pins ride the per-vertex
    // cloth_anchor_free_rotate paint instead (see BuildClothProxyMeshDmx).
    private readonly HashSet<FeModel.ProxyMesh> clothProxiesFlexed = [];

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

    // m_Rods is not derivable from the surface: a shipped rod matches neither a Quads/Tris edge nor a quad
    // diagonal. It is read directly off the FeModel and re-declared as explicit ClothSpring nodes by NAME.
    //
    // Every "$cloth_*" endpoint resolves through the export's own global-node-index to
    // "$cloth_m{proxy}p{local}" map (built from proxy.NodeIndices, the same one proxy.Faces uses) rather
    // than through the original's literal CtrlNames string: the re-exported proxy DMX re-sorts vertices
    // (FeModel.BuildProxyMesh sorts referenced nodes ascending), so the original's local index names a
    // different vertex here. Real bone names are not proxy-mesh-local and need no translation.
    /// <summary>
    /// The rods the compiler rebuilds from the exported surface on its own, which must therefore not also
    /// be declared as explicit springs. Every face edge and diagonal is one. When the sheet's compiled rods
    /// reach further than that, the extra bend network was authored on (see <c>add_stiffness_rods</c> in
    /// <see cref="MakeClothParams"/>) and regenerates the remaining pairs of that sheet too.
    /// </summary>
    static HashSet<(int, int)> ClothRodsFromSurface(FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, out bool generatesBendRods,
        out bool generatesBendOnlyRods, out float addCurvature, out HashSet<int> suspenderNodes)
    {
        suspenderNodes = [];
        var surfaceNodes = new HashSet<int>();
        var derived = new HashSet<(int, int)>();
        var surfaceFaces = new List<int[]>();
        foreach (var (_, _, proxyMesh) in proxies)
        {
            if (!proxyMesh.UsesAuthoredFaces)
            {
                continue;
            }

            var nodeOf = proxyMesh.NodeIndices;
            surfaceNodes.UnionWith(nodeOf);
            var globalFaces = proxyMesh.Faces.Select(face => face.Select(local => nodeOf[local]).ToArray()).ToList();
            surfaceFaces.AddRange(globalFaces);
            derived.UnionWith(FeModel.DeriveRodsFromFaces(globalFaces));
        }

        var beyondSurface = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (surfaceNodes.Contains(edge.Item1) && surfaceNodes.Contains(edge.Item2) && !derived.Contains(edge))
            {
                beyondSurface.Add(edge);
            }
        }

        // The bend network spans the pairs two steps apart across the surface. Only when every rod
        // reaching past the faces has that shape is the switch able to account for all of them - otherwise
        // enabling it would drop the rods it cannot reproduce, so those keep their explicit springs.
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var (a, b) in derived)
        {
            (neighbours.TryGetValue(a, out var na) ? na : neighbours[a] = []).Add(b);
            (neighbours.TryGetValue(b, out var nb) ? nb : neighbours[b] = []).Add(a);
        }

        var regenerable = beyondSurface.Count > 0 && beyondSurface.All(edge =>
            neighbours.TryGetValue(edge.Item1, out var near)
            && near.Any(step => neighbours.TryGetValue(step, out var beyond) && beyond.Contains(edge.Item2)));

        var boundedBeyondSurface = feModel.Rods.Any(rod => rod.MaxDist < ClothBendOnlyRodMaxDistance
            && beyondSurface.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)));

        // Both switches span the same pairs; only the bend-only network leaves their maximum length
        // unbounded, so the lengths are what tells the two apart.
        generatesBendOnlyRods = regenerable && !boundedBeyondSurface;
        generatesBendRods = regenerable && boundedBeyondSurface;

        // Only a regenerated network carries the curvature: where the rods are re-declared as explicit
        // springs instead they already ship their own minimum, and the compiler builds nothing to bend.
        addCurvature = regenerable ? ClothCurvatureFromSurface(feModel, surfaceFaces, beyondSurface) : 0f;

        if (regenerable)
        {
            derived.UnionWith(beyondSurface);
        }
        else if (ClothMixedSurfaceRods(feModel, surfaceFaces, beyondSurface) is
            { Bend.Count: > 0 } mixed)
        {
            generatesBendRods = mixed.Bounded;
            generatesBendOnlyRods = !mixed.Bounded;
            addCurvature = mixed.AddCurvature;
            suspenderNodes.UnionWith(mixed.Suspenders.SelectMany(static edge => new[] { edge.Item1, edge.Item2 }));
            derived.UnionWith(mixed.Bend);
            derived.UnionWith(mixed.Suspenders);
        }
        else if (ClothSuspenders(feModel, beyondSurface) is var (suspenders, suspenderCurvature)
            && suspenders.Count > 0)
        {
            addCurvature = suspenderCurvature;
            suspenderNodes.UnionWith(suspenders.SelectMany(static edge => new[] { edge.Item1, edge.Item2 }));
            derived.UnionWith(suspenders);
        }
        else
        {
            // Last resort, for a sheet none of the readings above accounts for: the compiler's own bend
            // network is one rod per edge two faces share, joining the far corners of the two, and where
            // every pair it would build is a rod the original carries it can be turned on to cover that
            // part of the sheet. What it does not name keeps its explicit springs. The subset test is what
            // keeps it from inventing a constraint, and it is only reached once the whole-surface, mixed
            // and suspender readings have each declined the sheet. The curvature is left alone: the rods
            // this network builds take a fixed bend angle, not the cloth's own curvature.
            var bend = FeModel.BendRodsFromSurface(surfaceFaces, feModel.IsStatic);
            bend.ExceptWith(derived);
            if (bend.Count > 0 && bend.IsSubsetOf(beyondSurface))
            {
                var boundedBend = feModel.Rods.Any(rod => rod.MaxDist < ClothBendOnlyRodMaxDistance
                    && bend.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)));
                generatesBendRods = boundedBend;
                generatesBendOnlyRods = !boundedBend;
                derived.UnionWith(bend);
            }
        }

        // Cloth that ships no surface of its own exports its synthesised sheets without the rod-suppressing
        // paint (see BuildClothProxyMeshDmx), so the compiler rebuilds rods from that triangulation as
        // well - declaring those same edges as explicit springs would ship each of them twice.
        if (!feModel.HasSurfaceElements)
        {
            foreach (var (_, _, proxyMesh) in proxies)
            {
                var nodeOf = proxyMesh.NodeIndices;
                derived.UnionWith(FeModel.DeriveRodsFromFaces(
                    proxyMesh.Faces.Select(face => face.Select(local => nodeOf[local]).ToArray())));
            }
        }

        return derived;
    }

    // The maximum length a bend-only rod is given, which is no limit at all.
    const float ClothBendOnlyRodMaxDistance = FeModel.UnboundedRodDistance;

    /// <summary>
    /// A sheet whose rods beyond its own faces are a MIXTURE of the <c>add_stiffness_rods</c> bend network
    /// and suspender rods, split into those two classes so each can be emitted through its own route
    /// rather than every rod of the sheet becoming an explicit <c>ClothSpring</c>.
    /// <para>
    /// The bend network is derived from the exported surface the way the compiler derives it
    /// (<see cref="FeModel.BendRodsFromSurface"/>), and taken only when no rod it would build is one the
    /// model has not got - a network reaching past the compiled data would add constraints the original
    /// lacks. The rods it does not account for all have to be suspender rods agreeing on the same
    /// <c>add_curvature</c> the network was folded by: the two passes share that one value, and a leftover
    /// is the signal that the surface being exported is not the one the network was built from, so such a
    /// sheet keeps every spring it has.
    /// </para>
    /// </summary>
    static (HashSet<(int, int)> Bend, HashSet<(int, int)> Suspenders, float AddCurvature, bool Bounded)?
        ClothMixedSurfaceRods(FeModel feModel, List<int[]> surfaceFaces, HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || feModel.HasAxialEdges)
        {
            return null;
        }

        var invMasses = feModel.NodeInvMasses;
        bool IsStatic(int node) => node >= 0 && node < invMasses.Length && invMasses[node] == 0f;

        var network = FeModel.BendRodsFromSurface(surfaceFaces, IsStatic);
        var shipped = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            shipped.Add(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
        }

        if (network.Count == 0 || !network.IsSubsetOf(shipped))
        {
            return null;
        }

        var bend = new HashSet<(int, int)>();
        var rest = new HashSet<(int, int)>();
        foreach (var edge in beyondSurface)
        {
            (network.Contains(edge) ? bend : rest).Add(edge);
        }

        var (suspenders, suspenderCurvature) = ClothSuspenders(feModel, rest);
        if (bend.Count == 0 || suspenders.Count != rest.Count)
        {
            return null;
        }

        var curvature = ClothCurvatureFromSurface(feModel, surfaceFaces, bend);
        if (suspenders.Count > 0)
        {
            if (curvature > 0f && MathF.Abs(curvature - suspenderCurvature)
                > FeModel.ChainRingCurvatureAgreement * MathF.Max(curvature, suspenderCurvature))
            {
                return null;
            }

            curvature = suspenderCurvature;
        }

        var bounded = false;
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (bend.Contains(edge) && rod.MaxDist < ClothBendOnlyRodMaxDistance)
            {
                bounded = true;
                break;
            }
        }

        return (bend, suspenders, curvature, bounded);
    }

    /// <summary>
    /// The SUSPENDER rods among the ones a sheet has beyond its own faces, and the
    /// <c>add_curvature</c> they were authored with. A suspender rod ties a static sheet vertex to a
    /// simulated one over their rest span, and the compiler gives it <c>flMaxDist</c> = that span and
    /// <c>flMinDist</c> = <c>flMaxDist * sin(add_curvature * pi)</c>, so one such rod pins the curvature
    /// down. The rest of the set keeps its explicit springs: a rod the paint does not rebuild has to,
    /// or the model comes back short of it.
    /// <para>
    /// The paint that builds them has to reach BOTH ends of each rod (see
    /// <see cref="ClothSuspenderPaint"/>), and the compiler pairs each painted simulated vertex with its
    /// nearest painted static one. <c>add_curvature</c> is one model-wide value with three readers, so
    /// the answer is taken only where the readings cannot contradict each other: every suspender rod has
    /// to agree with every other to
    /// <see cref="FeModel.ChainRingCurvatureAgreement"/>, a chain ring reading of its own has to agree
    /// too, and a sheet with axial edges is left alone entirely because <c>rigid_edge_hinges</c> gives
    /// the same value a second, independent job.
    /// </para>
    /// </summary>
    static (HashSet<(int, int)> Suspenders, float AddCurvature) ClothSuspenders(FeModel feModel,
        HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || feModel.HasAxialEdges)
        {
            return ([], 0f);
        }

        var positions = feModel.InitPosePositions;
        var invMasses = feModel.NodeInvMasses;
        var shaped = new List<((int, int) Edge, float Reading)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!beyondSurface.Contains(edge) || edge.Item1 < 0
                || edge.Item2 >= positions.Length || edge.Item2 >= invMasses.Length)
            {
                continue;
            }

            if ((invMasses[rod.NodeA] == 0f) == (invMasses[rod.NodeB] == 0f)
                || MathF.Abs(rod.RelaxationFactor - 1f) > 1e-4f || rod.MaxDist <= 0f)
            {
                continue;
            }

            var rest = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
            if (rest <= 0f || MathF.Abs(rod.MaxDist - rest) > 1e-3f * rest)
            {
                continue;
            }

            shaped.Add((edge, MathF.Asin(Math.Clamp(rod.MinDist / rod.MaxDist, 0f, 1f)) / MathF.PI));
        }

        // The answer is the value the largest set of them shares, as everywhere else a curvature is read
        // back. A curvature of zero is the one value the paint alone already reproduces, and taking the
        // branch for it would replace the sheet's own chain curvature with nothing.
        //
        // The whole set has to agree AND account for every rod reaching past the faces: the compiler's own
        // pass walks the authored proxy vertices while this recovers only the ones that became nodes, so
        // where the two differ the pass pairs the sheet up differently and rebuilds only part of what it
        // shipped. A set with leftovers is exactly that case, and it keeps every spring it has.
        var curvature = DominantReading(shaped.Select(static s => s.Reading), out var agreeing);
        if (curvature <= 0f || agreeing != shaped.Count || shaped.Count != beyondSurface.Count)
        {
            return ([], 0f);
        }

        var ring = feModel.ChainRingCurvature;
        if (ring > 0f && MathF.Abs(ring - curvature) > FeModel.ChainRingCurvatureAgreement * MathF.Max(ring, curvature))
        {
            return ([], 0f);
        }

        var suspenders = new HashSet<(int, int)>();
        foreach (var (edge, reading) in shaped)
        {
            if (MathF.Abs(reading - curvature) <= FeModel.ChainRingCurvatureAgreement * MathF.Max(reading, curvature))
            {
                suspenders.Add(edge);
            }
        }

        return (suspenders, curvature);
    }

    // The value the largest subset of `readings` agrees on to ChainRingCurvatureAgreement, taking the
    // largest such value on a tie, with the size of that subset. Zero when there are none.
    static float DominantReading(IEnumerable<float> readings, out int agreeing)
    {
        var sorted = readings.ToArray();
        Array.Sort(sorted);
        var best = 0f;
        agreeing = 0;
        var low = 0;
        for (var high = 0; high < sorted.Length; high++)
        {
            while (sorted[high] - sorted[low] > FeModel.ChainRingCurvatureAgreement * sorted[high])
            {
                low++;
            }

            if (high - low + 1 >= agreeing)
            {
                agreeing = high - low + 1;
                best = sorted[high];
            }
        }

        return best;
    }

    /// <summary>
    /// The <c>cloth_suspenders</c> paint of a proxy sheet, or null when the sheet has none. The compiler
    /// builds a suspender rod only when the paint reaches both of its ends, so both the static vertex and
    /// the simulated one it holds up carry it.
    /// </summary>
    float[]? ClothSuspenderPaint(FeModel.ProxyMesh proxy)
    {
        if (physAggregateData?.FeModel is not { } feModel)
        {
            return null;
        }

        ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract, out _, out _, out _, out var suspenderNodes);
        if (suspenderNodes.Count == 0)
        {
            return null;
        }

        var paint = new float[proxy.NodeIndices.Length];
        var painted = 0;
        for (var v = 0; v < paint.Length; v++)
        {
            if (suspenderNodes.Contains(proxy.NodeIndices[v]))
            {
                paint[v] = 1f;
                painted++;
            }
        }

        return painted > 0 ? paint : null;
    }

    /// <summary>
    /// The <c>add_curvature</c> the sheet was authored with, read back out of the bend network it
    /// generates. Such a rod joins the far corners of two faces that share an edge; the compiler gives it
    /// the span those corners have with the two faces coplanar as <c>flMaxDist</c>, and the span they have
    /// folded about that shared edge through a dihedral angle of <c>add_curvature * pi</c> as
    /// <c>flMinDist</c> - capped at the rod's own rest span, which a curved sheet reaches before the fold
    /// opens all the way. One uncapped rod plus the rest positions therefore pin the value down, and every
    /// rod of the network agrees on it to the print quantum, so the answer is the value the largest set of
    /// them shares - which also discards the pairs some other rule shaped. A capped rod only says the
    /// value is at least enough to have reached its rest span, so a network that is capped throughout
    /// yields the greatest of those bounds. Values at or above 1.0 all open the fold fully and compile
    /// identically, which is the one distinction the compiled data cannot make.
    /// </summary>
    static float ClothCurvatureFromSurface(FeModel feModel, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var positions = feModel.InitPosePositions;
        var hinges = new Dictionary<(int, int), List<int[]>>();
        var touching = new Dictionary<int, List<int[]>>();
        foreach (var face in faces)
        {
            for (var i = 0; i < face.Length; i++)
            {
                var a = face[i];
                var b = face[(i + 1) % face.Length];
                var hinge = a < b ? (a, b) : (b, a);
                (hinges.TryGetValue(hinge, out var sharing) ? sharing : hinges[hinge] = []).Add(face);
                (touching.TryGetValue(a, out var around) ? around : touching[a] = []).Add(face);
            }
        }

        var opened = new List<float>();
        var capped = new List<float>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!beyondSurface.Contains(edge) || edge.Item2 >= positions.Length)
            {
                continue;
            }

            // A bend-only rod has no length of its own to identify its hinge by, so its rest span stands in.
            var rest = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
            var coplanar = rod.MaxDist < ClothBendOnlyRodMaxDistance ? rod.MaxDist : rest;
            var closest = float.MaxValue;
            var flat = 0f;
            var folded = 0f;
            foreach (var hinge in HingesAround(touching, rod.NodeA))
            {
                if (hinge.Item1 == edge.Item1 || hinge.Item1 == edge.Item2
                    || hinge.Item2 == edge.Item1 || hinge.Item2 == edge.Item2
                    || !hinges[hinge].Any(face => face.Contains(rod.NodeB)))
                {
                    continue;
                }

                var axis = positions[hinge.Item2] - positions[hinge.Item1];
                var axisLength = axis.Length();
                if (axisLength < 1e-6f)
                {
                    continue;
                }

                axis /= axisLength;
                var toA = positions[rod.NodeA] - positions[hinge.Item1];
                var toB = positions[rod.NodeB] - positions[hinge.Item1];
                var alongA = Vector3.Dot(toA, axis);
                var alongB = Vector3.Dot(toB, axis);
                var riseA = (toA - alongA * axis).Length();
                var riseB = (toB - alongB * axis).Length();
                var slide = (alongA - alongB) * (alongA - alongB);
                var open = MathF.Sqrt(slide + ((riseA + riseB) * (riseA + riseB)));
                var shut = MathF.Sqrt(slide + ((riseA - riseB) * (riseA - riseB)));
                var error = MathF.Abs(open - coplanar);
                if (error < closest && open - shut >= 0.02f * open)
                {
                    closest = error;
                    flat = open;
                    folded = shut;
                }
            }

            if (closest > 0.005f * MathF.Max(1f, coplanar))
            {
                continue;
            }

            var reach = (flat * flat) - (folded * folded);
            var span = rod.MinDist >= rest - (2e-4f * MathF.Max(1f, rest)) ? rest : rod.MinDist;
            var fraction = Math.Clamp(((span * span) - (folded * folded)) / reach, 0f, 1f);
            (span == rest ? capped : opened).Add(fraction);
        }

        // The half-angle sine squared is what the minimum length is linear in, so the rods are clustered
        // in that before the value is read off - the angle itself is arbitrarily sensitive near either end.
        opened.Sort();
        var agreed = 0;
        var consensus = 0f;
        for (var i = 0; i < opened.Count; i++)
        {
            var j = i;
            while (j < opened.Count && opened[j] <= opened[i] + 1e-3f)
            {
                j++;
            }

            if (j - i > agreed)
            {
                agreed = j - i;
                consensus = opened[(i + j - 1) / 2];
            }
        }

        if (agreed < 3 || agreed * 4 < opened.Count)
        {
            if (capped.Count == 0)
            {
                return 0f;
            }

            consensus = capped.Max();
        }

        return 2f / MathF.PI * MathF.Asin(MathF.Sqrt(consensus));
    }

    static IEnumerable<(int, int)> HingesAround(Dictionary<int, List<int[]>> touching, int node)
    {
        if (!touching.TryGetValue(node, out var around))
        {
            yield break;
        }

        foreach (var face in around)
        {
            for (var i = 0; i < face.Length; i++)
            {
                var a = face[i];
                var b = face[(i + 1) % face.Length];
                yield return a < b ? (a, b) : (b, a);
            }
        }
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

    // TODO: some models re-export more rods than the original, from overlap between the springs emitted
    // here, the chains, and the proxy sheet all re-declaring the same span.
    static void AddClothProxySprings(KVObject softbodyChildren, FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, HashSet<int> chainJointNodes,
        HashSet<int> authoredClothNodes, Dictionary<int, string> freeClothNodeNames,
        HashSet<(int, int)> derivedRods, Dictionary<int, string> proxyNodeNames)
    {
        // Islands the cloth importer is expected to prune vertices from (see FeModel.ComputeDropRisk):
        // emitting explicit rods into them would orphan a ClothSpring on a vertex the compiler never creates
        // ("Cannot find node $cloth_mXpY", a hard failure). Skip their explicit rods entirely and let the
        // importer auto-derive the network from the surface instead - guaranteed to compile, at the cost of
        // exact rod topology for that one island. Clean islands keep their exact reconstructed rods.
        var riskyNodes = new HashSet<int>();

        foreach (var (_, _, proxyMesh) in proxies)
        {
            if (proxyMesh.IsDropRisk)
            {
                foreach (var node in proxyMesh.NodeIndices)
                {
                    riskyNodes.Add(node);
                }
            }
        }

        // A real bone anchors a spring only when this export also declares it as a ClothNode. A bone the
        // compile knows solely through a chain's joint list or a proxy back-solve is not a valid endpoint,
        // and naming one fails the whole compile with "Cannot find Fx Bone"/"Cannot find node". A
        // "$cloth_node_" ctrl re-authored as a free ClothNode is named by its element name instead.
        string? ResolveName(int node)
            => FeModel.IsProxyNodeName(feModel.CtrlNames[node])
                ? proxyNodeNames.GetValueOrDefault(node) ?? freeClothNodeNames.GetValueOrDefault(node)
                : authoredClothNodes.Contains(node) ? feModel.CtrlNames[node] : null;

        var seen = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!seen.Add(edge))
            {
                continue;
            }

            // A rod inside a drop-risk island is skipped (the whole island falls back to compiler-derived
            // rods) - see the riskyNodes remarks above.
            if (riskyNodes.Contains(edge.Item1) || riskyNodes.Contains(edge.Item2))
            {
                continue;
            }

            if (derivedRods.Contains(edge))
            {
                continue;
            }

            // A ClothChain's own joint hierarchy compiles to a fully-connected local rod mesh among ITS
            // OWN joints, not just parent-child pairs, so re-declaring one of these as an explicit
            // ClothSpring is redundant. It is also rejected: a bone that is only a ClothChain joint_name,
            // with no fit-matrix back-solve or ClothNode registration of its own, is not a valid
            // ClothSpring endpoint.
            if (chainJointNodes.Contains(edge.Item1) || chainJointNodes.Contains(edge.Item2))
            {
                continue;
            }

            var name0 = ResolveName(rod.NodeA);
            var name1 = ResolveName(rod.NodeB);
            if (name0 is null || name1 is null)
            {
                // A rod-only proxy node dropped by BuildProxyMeshesFromRodsOnly's 3-member minimum (see
                // its own remarks) has no corresponding exported vertex to reference at all - skip rather
                // than author a dangling reference the compiler would reject outright.
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1, rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor));
        }
    }

    // Rods the chains do not rebuild themselves (extra copies of a parent span) are re-declared here.
    static void AddClothChainSurplusRods(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var controlNames = feModel.CtrlNames;

        // Only a bone some emitted chain actually claims as a joint is registered as a cloth node, and so
        // only such a bone can anchor a spring. A cloth-flagged bone that no chain covers (a chain's own
        // parent one hop above its root, say) resolves to nothing and fails the whole compile with
        // "Cannot find Fx Bone".
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // One spring per surplus rod OCCURRENCE, numbered like AddFreeClothNodesAndSprings' copies.
        var occurrence = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.GetUngeneratedRods(chains))
        {
            if (rod.NodeA < 0 || rod.NodeA >= controlNames.Length
            || rod.NodeB < 0 || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            if (!chainJoints.Contains(rod.NodeA) || !chainJoints.Contains(rod.NodeB))
            {
                continue;
            }

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if (FeModel.IsProxyNodeName(name0) || FeModel.IsProxyNodeName(name1))
            {
                continue;
            }

            var copy = occurrence.GetValueOrDefault((rod.NodeA, rod.NodeB));
            occurrence[(rod.NodeA, rod.NodeB)] = copy + 1;
            var springLabel = copy == 0 ? $"rod_{name0}_{name1}" : $"rod_{name0}_{name1}_{copy}";
            softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                rod.RelaxationFactor));
        }
    }

    // The proxy-sheet phase's own AddClothProxySprings skips every rod touching an independent chain
    // joint (that pairing is a chain's job), but a chain's own generated spans (see
    // FeModel.ChainGeneratedSpans) only ever cover ITS OWN joints - a rod between two joints of two
    // DIFFERENT chains is never regenerated by anything in that phase and was dropped outright before
    // this. Unlike AddClothChainSurplusRods' plain ClothSpring, this emits a ClothSelfCollisionCluster
    // (see MakeClothSelfCollisionCluster), which adds no m_SourceElems entry.
    //
    // A cluster's compiled rod always carries the builder's own fixed relax and weight of 1.0 and 0.5,
    // neither an authorable cluster input (same as ClothSpring's, see MakeClothSpring). A rod without that
    // signature is left unemitted rather than re-declared as a ClothSpring, which would compile the
    // m_SourceElems entry a cluster-derived rod never has.
    static void AddClothChainSurplusClusters(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var controlNames = feModel.CtrlNames;
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // GetUngeneratedRods decides which of several same-pair rod entries counts as "generated" by
        // array order rather than by value, so a pair carrying both a chain-adjacent rod and a
        // separate cluster-pairwise rod can have the two attributed backwards. A pair with more than
        // one raw entry is that ambiguous case and is skipped.
        var rodCounts = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            rodCounts[key] = rodCounts.GetValueOrDefault(key) + 1;
        }

        foreach (var rod in feModel.GetUngeneratedRods(chains))
        {
            if (rod.NodeA < 0 || rod.NodeA >= controlNames.Length
            || rod.NodeB < 0 || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            if (!chainJoints.Contains(rod.NodeA) || !chainJoints.Contains(rod.NodeB))
            {
                continue;
            }

            var pairKey = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (rodCounts.GetValueOrDefault(pairKey) > 1)
            {
                continue;
            }

            if (rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f)
            {
                continue;
            }

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if (FeModel.IsProxyNodeName(name0) || FeModel.IsProxyNodeName(name1))
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSelfCollisionCluster($"cluster_{name0}_{name1}", name0, name1,
                rod.MinDist / 2f, rod.MaxDist / 2f));
        }
    }

    /// <summary>
    /// Re-declares the authored two-corner source elements (<see cref="FeModel.SourceSprings"/>) as
    /// explicit springs. Neither the surface nor a chain regenerates these, and the compiler records one
    /// source element per spring, so a model exported without them comes back short both a rod and a
    /// source element per pair. Endpoints are named verbatim, <c>$cc</c> proxies included - those are
    /// valid ClothSpring endpoints even though they are not chain joints.
    /// </summary>
    static HashSet<(int, int)> AddClothSourceSprings(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var emitted = new HashSet<(int, int)>();
        var names = feModel.CtrlNames;
        var rodByEdge = new Dictionary<(int, int), FeModel.Rod>();
        foreach (var rod in feModel.Rods)
        {
            rodByEdge.TryAdd(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA), rod);
        }

        foreach (var (a, b) in feModel.GetAuthoredSourceSprings(chains))
        {
            if (a < 0 || a >= names.Length || b < 0 || b >= names.Length)
            {
                continue;
            }

            if (!rodByEdge.TryGetValue(a < b ? (a, b) : (b, a), out var rod))
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[rod.NodeA], names[rod.NodeB], rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor));
            emitted.Add(a < b ? (a, b) : (b, a));
        }

        return emitted;
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

    /// <summary>
    /// Separates equal blend weights on one proxy vertex by the smallest amount that survives the
    /// float stream, keeping the recovered order. The cloth proxy importer sorts a vertex's
    /// influences by descending weight to choose its primary anchor bone, which becomes the
    /// vertex's <c>m_CtrlOffsets</c> parent while the rest become its <c>m_CtrlSoftOffsets</c>
    /// parents; the sort breaks a tie on something the emitted skinning does not control, so an
    /// exactly tied pair can promote the wrong bone. The nudge is several orders of magnitude below
    /// the weight resolution the compiled data carries.
    /// </summary>
    static (string Bone, float Weight)[] SeparateTiedInfluenceWeights((string Bone, float Weight)[] influences)
    {
        static bool IsTied(float a, float b)
            => MathF.Abs(a - b) <= TiedInfluenceSeparation * MathF.Max(MathF.Abs(a), MathF.Abs(b));

        var tied = false;
        for (var i = 1; i < influences.Length && !tied; i++)
        {
            tied = IsTied(influences[i].Weight, influences[i - 1].Weight);
        }

        if (!tied)
        {
            return influences;
        }

        var separated = new (string Bone, float Weight)[influences.Length];
        influences.CopyTo(separated, 0);
        for (var i = 1; i < separated.Length; i++)
        {
            if (IsTied(influences[i].Weight, influences[i - 1].Weight))
            {
                separated[i] = (separated[i].Bone, separated[i - 1].Weight * (1f - TiedInfluenceSeparation));
            }
        }

        return separated;
    }

    /// <summary>
    /// Relative gap forced between two proxy blend weights that would otherwise be a tie. It is
    /// three orders below the 1/255 quantum a painted weight is authored at and survives the
    /// per-vertex renormalization the importer applies before it sorts.
    /// </summary>
    const float TiedInfluenceSeparation = 1e-6f;

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

    // A "$cloth_node_<name>" control node is an authored free-standing ClothNode: the compiler names the
    // ctrl "$cloth_node_" + the element name, anchors it to cloth_node_root_bone via an m_CtrlOffsets
    // entry holding the authored bone-local origin, and registers the root bone as a second ctrl of its
    // own. A ClothNode whose name equals its root bone merges into ONE ctrl carrying the plain bone name
    // (static when is_static_node), which is how a plain cloth bone that no chain, proxy or shape claims
    // was authored. The rods among these nodes come from explicit ClothSprings, whose endpoints resolve
    // by ClothNode element name (or plain bone name for a merged/root ClothNode); a bone with no cloth
    // declaration of its own is not a valid endpoint ("Cannot find Fx Bone").
    static int AddFreeClothNodesAndSprings(KVObject clothChildren, KVObject softbodyChildren,
        FeModel feModel, HashSet<int> coveredNodes, Func<string, bool> emitBareStatic,
        HashSet<string> clothBones, Func<int, bool, KVObject>? folderFor = null, bool hasOtherChains = false,
        Func<string, bool>? bareStaticReparented = null, HashSet<(int, int)>? alreadyEmitted = null)
    {
        const string ClothNodePrefix = "$cloth_node_";
        var names = feModel.CtrlNames;

        var anchorOf = BuildCtrlAnchorMap(feModel);

        var nodeByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var node = 0; node < names.Length; node++)
        {
            nodeByName.TryAdd(names[node], node);
        }

        var jiggleNodes = feModel.JiggleBones.Select(static j => j.Node).ToHashSet();
        var shapeParentBones = CollisionShapeParentBones(feModel);

        var rodTouched = new HashSet<int>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA != rod.NodeB)
            {
                rodTouched.Add(rod.NodeA);
                rodTouched.Add(rod.NodeB);
            }
        }

        // node -> the name a ClothSpring endpoint references it by.
        var springName = new Dictionary<int, string>();
        var emitted = 0;

        // A ClothNode carries no vertex_map of its own, so a lone one joins its selections through the
        // ClothVertexMap containers. A node a ClothSpring names is listed by them but left flat.
        KVObject FolderOf(int node)
            => folderFor is not null ? folderFor(node, !rodTouched.Contains(node)) : clothChildren;

        for (var node = 0; node < names.Length; node++)
        {
            var name = names[node];
            if (coveredNodes.Contains(node) || jiggleNodes.Contains(node) || shapeParentBones.Contains(name))
            {
                continue;
            }

            if (name.StartsWith(ClothNodePrefix, StringComparison.Ordinal))
            {
                var elementName = name[ClothNodePrefix.Length..];
                if (!TryResolveClothNodeAnchor(feModel, anchorOf, node, out var rootBone, out var origin))
                {
                    continue;
                }

                FolderOf(node).Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin));
                springName[node] = elementName;
                clothBones.Add(rootBone);
                emitted++;

                // The root bone compiles into a registered ctrl of its own, referencable by plain name.
                if (nodeByName.TryGetValue(rootBone, out var rootNode))
                {
                    springName.TryAdd(rootNode, rootBone);
                }
            }
            else if (!feModel.IsGeneratedNodeName(name))
            {
                var isStatic = feModel.IsStatic(node);
                var bareStatic = isStatic && !rodTouched.Contains(node);
                if (!isStatic || !bareStatic || emitBareStatic(name))
                {
                    // A static node a rod names is an anchor the spring network already ties in, and a
                    // static node with no control-node ancestor compiles to a hierarchy root from a
                    // merged ClothNode already. Only a BARE, re-parented one needs the chain form.
                    var loneNode = LoneClothNodeIsOriginalRoot(feModel, node)
                        && (!isStatic || (bareStatic && (bareStaticReparented?.Invoke(name) ?? false)));
                    (loneNode ? clothChildren : FolderOf(node)).Add(loneNode
                        ? MakeLoneJointChain(feModel, name, node, hasOtherChains)
                        : MakeClothNode(feModel, name, node, isStaticNode: isStatic));
                    springName[node] = name;
                    clothBones.Add(name);
                    emitted++;
                }
            }
        }

        if (springName.Count == 0)
        {
            return emitted;
        }

        // One spring per rod OCCURRENCE, not per distinct pair: node mass accumulates per rod, and a model
        // can ship genuine duplicate rods. Where EVERY occurrence of a pair is an identical copy, one
        // ClothSpring's own extra_iterations reproduces them: the compiler duplicates a spring's rod once
        // per iteration, so N identical copies come from one authored spring declaration and leave one
        // m_SourceElems entry, where N separate springs would leave N. A pair whose copies are not all
        // identical keeps the per-occurrence numbering.
        var rodsByEdge = new Dictionary<(int, int), List<FeModel.Rod>>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA == rod.NodeB
                || !springName.ContainsKey(rod.NodeA) || !springName.ContainsKey(rod.NodeB))
            {
                continue;
            }

            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!rodsByEdge.TryGetValue(edge, out var list))
            {
                rodsByEdge[edge] = list = [];
            }

            list.Add(rod);
        }

        foreach (var (edge, rods) in rodsByEdge)
        {
            // A pair an explicit source spring already re-declared is spent: emitting it here as well
            // ships the same constraint twice and records a second source element for it.
            if (alreadyEmitted is not null && alreadyEmitted.Contains(edge))
            {
                continue;
            }

            var name0 = springName[edge.Item1];
            var name1 = springName[edge.Item2];
            var first = rods[0];
            var allIdentical = rods.TrueForAll(rod => rod.MinDist == first.MinDist
                && rod.MaxDist == first.MaxDist && rod.RelaxationFactor == first.RelaxationFactor);

            if (rods.Count > 1 && allIdentical)
            {
                softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1,
                    first.MinDist, first.MaxDist, first.RelaxationFactor, extraIterations: rods.Count - 1));
                continue;
            }

            for (var copy = 0; copy < rods.Count; copy++)
            {
                var rod = rods[copy];
                var springLabel = copy == 0 ? $"rod_{edge.Item1}_{edge.Item2}" : $"rod_{edge.Item1}_{edge.Item2}_{copy}";
                softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                    rod.RelaxationFactor));
            }
        }

        return emitted;
    }

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

    /// <summary>
    /// Declares the cloth collision shapes and returns the names it gave them, in declaration order.
    /// </summary>
    static List<string> AddClothCollisionShapes(KVObject softbodyChildren, FeModel feModel)
    {
        var names = new List<string>();
        // A shape declaration creates its parent bone as a control node where nothing has created it yet,
        // so the three rigid kinds are interleaved to introduce their parent bones in the order the
        // compiled model numbers them. Each kind keeps the relative order its own rigid array carries.
        var kinds = new List<(int Node, KVObject Shape)>[]
        {
            [.. feModel.BuildCollisionCapsules()
                .Select(c => (ParentBoneNode(feModel, c.ParentBone), MakeClothShapeCapsule(c)))],
            [.. feModel.BuildCollisionSpheres()
                .Select(s => (ParentBoneNode(feModel, s.ParentBone), MakeClothShapeSphere(s)))],
            [.. feModel.BuildCollisionBoxes()
                .Select(b => (ParentBoneNode(feModel, b.ParentBone), MakeClothShapeBox(b)))],
        };

        var taken = new int[kinds.Length];
        while (true)
        {
            var next = -1;
            for (var kind = 0; kind < kinds.Length; kind++)
            {
                if (taken[kind] < kinds[kind].Count
                    && (next < 0 || kinds[kind][taken[kind]].Node < kinds[next][taken[next]].Node))
                {
                    next = kind;
                }
            }

            if (next < 0)
            {
                break;
            }

            var shape = kinds[next][taken[next]++].Shape;
            names.Add(shape.GetStringProperty("name"));
            softbodyChildren.Add(shape);
        }

        // Last: a planarized capsule is excluded from m_TaperedCapsuleRigids, but declaring it ahead of the
        // real ones still rotates their order in that array. m_CollisionPlanes is sorted by the compiler,
        // so ordering these by parent bone costs the plane array nothing.
        foreach (var capsule in feModel.BuildPlanarizeCapsules()
            .OrderBy(c => ParentBoneNode(feModel, c.ParentBone)))
        {
            var shape = MakeClothShapeCapsule(capsule);
            names.Add(shape.GetStringProperty("name"));
            softbodyChildren.Add(shape);
        }

        return names;
    }

    /// <summary>
    /// Declares the anti-tunnel collider group the compiler turns into <c>m_AntiTunnelBytecode</c>: a node
    /// list naming both the cloth swept for tunnelling and the shapes it is swept against. The two kinds
    /// are each necessary - a group naming only colliders or only cloth compiles to no bytecode at all -
    /// and members are named, not parented, so the cloth keeps the declaration site that builds it.
    /// </summary>
    static void AddClothAntiTunnelGroup(KVObject softbodyChildren, FeModel feModel,
        List<string> shapeNames, List<string> clothNames)
    {
        if (feModel.AntiTunnelBytecode.Length == 0 || shapeNames.Count == 0 || clothNames.Count == 0)
        {
            return;
        }

        var nodes = KVObject.Collection();
        foreach (var name in shapeNames.Concat(clothNames).Distinct())
        {
            nodes.Add(name, true);
        }

        var data = KVObject.Collection();
        data.Add("nodes", nodes);

        softbodyChildren.Add(MakeNode("ClothAntiTunnelColliderGroup",
            ("name", "cloth_antitunnel_group0"),
            ("vertex_map", ""),
            ("import_cloth_collision_layer0", false),
            ("import_cloth_collision_layer1", false),
            ("import_cloth_collision_layer2", false),
            ("import_cloth_collision_layer3", false),
            ("data", data)));
    }

    // Where a collision shape's parent bone sits in the compiled control-node array, or last when the
    // compiled model does not carry it as a control node at all.
    static int ParentBoneNode(FeModel feModel, string? parentBone)
    {
        var node = parentBone is null ? -1 : Array.IndexOf(feModel.CtrlNames, parentBone);
        return node < 0 ? int.MaxValue : node;
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

    static KVObject MakeClothShapeSphere(FeModel.CollisionSphere sphere)
    {
        var node = MakeNode("ClothShapeSphere",
            ("name", (sphere.ParentBone ?? "cloth") + "_clothSphere"),
            ("parent_bone", sphere.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, sphere.CollisionMask);
        node.Add("cloth_collision_priority", sphere.Priority);
        node.Add("vertex_map", sphere.VertexMap ?? "");
        node.Add("inverted_collision", sphere.Inverted);
        node.Add("planarize", false);
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
    /// <summary>
    /// Builds the test for whether a bone has an ancestor that is itself a cloth control node. That
    /// ancestor is what the compiler re-parents a merged <c>ClothNode</c> declaration onto, so a bone
    /// with none already compiles to an <c>m_SkelParents</c> root without a chain declaration.
    /// </summary>
    Func<string, bool> ClothControlAncestorTest(FeModel feModel)
    {
        var controlNames = new HashSet<string>(feModel.CtrlNames, StringComparer.Ordinal);
        var boneByName = model?.Skeleton.Bones.ToDictionary(static b => b.Name, StringComparer.Ordinal);

        return name =>
        {
            if (boneByName is null || !boneByName.TryGetValue(name, out var bone))
            {
                return false;
            }

            for (var ancestor = bone.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (controlNames.Contains(ancestor.Name))
                {
                    return true;
                }
            }

            return false;
        };
    }

    static bool LoneClothNodeIsOriginalRoot(FeModel feModel, int node)
        => feModel.HasCompiledSkelParents
            && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0;

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

        var twistRelax = feModel.GetAuthoredTwistRelax(joint.Node, joint.ParentNode, joint.ProxyNode);

        // The compiler scales a twist entry by the ORIENT joint's own twist_relax only where that
        // joint simulates, and writes a flat 0.0 where it merely allows rotation. So a chain root
        // the original gives a non-zero entry of its own was authored as a SIMULATED joint, and it
        // is pinned into the static block by lock_translation rather than by simulate = false.
        var pinnedSimulatedRoot = joint.IsRoot && !joint.Simulated && twistRelax > 0f;
        kv.Add("simulate", joint.Simulated || pinnedSimulatedRoot);

        // Only a static node carries a rotation lock.
        if (joint.Node < feModel.StaticNodeCount)
        {
            kv.Add("allow_rotation", feModel.AllowsRotation(joint.Node));
        }

        if (feModel.LocksTranslation(joint.Node) || pinnedSimulatedRoot)
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
        kv.Add("twist_relax", twistRelax);

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
            ("lock_translation", feModel.LocksTranslation(node)),
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

    /// <summary>
    /// The name a <c>ClothTri</c> / <c>ClothQuad</c> corner references a control node by: the element
    /// name for a free <c>ClothNode</c> (the compiler prefixes <c>$cloth_node_</c> to it itself) and the
    /// plain bone name for everything else.
    /// </summary>
    static string ClothFaceCornerName(FeModel feModel, int node)
    {
        var name = feModel.CtrlNames[node];
        return name.StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal)
            ? name[FeModel.FreeClothNodePrefix.Length..]
            : name;
    }

    /// <summary>
    /// Declares one compiled surface face whose corners are all already-declared cloth nodes as the
    /// <c>ClothTri</c> or <c>ClothQuad</c> element the original was built from, instead of inventing a
    /// proxy sheet to carry it. Repeated corners collapse, so a triangle stored in a quad slot emits as
    /// a ClothTri.
    /// </summary>
    static KVObject? MakeClothFace(FeModel feModel, int[] face)
    {
        var corners = new List<int>(4);
        foreach (var corner in face)
        {
            if (!corners.Contains(corner))
            {
                corners.Add(corner);
            }
        }

        if (corners.Count is not (3 or 4))
        {
            return null;
        }

        var node = MakeNode(corners.Count == 4 ? "ClothQuad" : "ClothTri");
        for (var i = 0; i < corners.Count; i++)
        {
            node.Add("cloth_node_" + i.ToString(CultureInfo.InvariantCulture),
                ClothFaceCornerName(feModel, corners[i]));
        }

        return node;
    }

    /// <summary>
    /// Emits every face the original built from a ClothTri / ClothQuad over declared cloth nodes, and
    /// returns the control nodes those faces name so the caller can keep them declared.
    /// </summary>
    static HashSet<int> AddClothFaces(KVObject clothChildren, FeModel feModel)
    {
        var cornered = new HashSet<int>();
        foreach (var face in feModel.GetAuthoredElementFaces())
        {
            if (MakeClothFace(feModel, face) is not { } element)
            {
                continue;
            }

            clothChildren.Add(element);
            cornered.UnionWith(face);
        }

        return cornered;
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

    /// <summary>
    /// Builds the cloth proxy-mesh DMX (the cloth "sheet") from the soft-body <see cref="FeModel"/>.
    /// Vertices are the FeModel surface control nodes (positions = their rest pose), faces come from the
    /// quad/tri surface, each vertex carries a <c>cloth_enable$0</c> paint value (1 = simulated, 0 = pinned)
    /// and is skinned to the real skeleton bone it is anchored to. A recompile turns this back into the
    /// <c>$cloth_*</c> FeModel nodes (one per enabled vertex). The skeleton is emitted into the DMX joint
    /// list so the skinning resolves, exactly like a render mesh.
    /// </summary>
    internal byte[] BuildClothProxyMeshDmx(FeModel.ProxyMesh proxy, string name)
    {
        Debug.Assert(model is not null, "model required for cloth proxy mesh");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        // Joint list = the full skeleton, so BLENDINDICES resolve (mirrors ConvertMeshToDatamodelMesh).
        var dmeModel = BuildDmeDagSkeleton(skeleton, out _, bonePositions: ClothProxyRestBonePositions);
        dmeModel.Name = name;
        RespellJointsAsClothControlNodes(dmeModel, physAggregateData?.FeModel);

        var (dag, vertexData) = CreateDmxDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = proxy.Positions.Length;

        // Indexed one face corner at a time, the way authored proxies are: the face set names corner
        // ordinals and every stream's index array maps corner -> vertex. A sheet with no faces has no
        // corners and stays indexed per vertex.
        var cornerVertices = proxy.Faces.SelectMany(static face => face).ToArray();
        var identity = Enumerable.Range(0, vertexCount).ToArray();
        var vertexIndices = cornerVertices.Length > 0 ? cornerVertices : identity;

        vertexData.AddIndexedStream("position$0", proxy.Positions, vertexIndices);

        // The sheet's normals are its rest orientations (see FeModel.RecoverRestNormals). The importer
        // reads proxy vertex v's normal from the flattened per-corner stream at ordinal v, not at one of
        // v's own corners, so slot v carries vertex v's; the corners past the vertex count carry theirs.
        var restNormals = physAggregateData?.FeModel?.RecoverRestNormals(proxy)
            ?? [.. Enumerable.Repeat(Vector3.UnitZ, vertexCount)];
        var cornerNormals = new Vector3[vertexIndices.Length];
        for (var corner = 0; corner < cornerNormals.Length; corner++)
        {
            cornerNormals[corner] = restNormals[corner < vertexCount ? corner : vertexIndices[corner]];
        }

        vertexData.AddIndexedStream("normal$0", cornerNormals, Enumerable.Range(0, cornerNormals.Length).ToArray());

        // The cloth importer needs texcoords on the proxy (authored proxies always carry them; without
        // UVs the surface is not accepted as a sheet). A bounding-box projection along the two largest
        // extents is enough - the UVs only need to vary smoothly across the sheet.
        var boundsMin = proxy.Positions.Aggregate(Vector3.Min);
        var boundsMax = proxy.Positions.Aggregate(Vector3.Max);
        var extent = boundsMax - boundsMin;
        Span<int> axes = [0, 1, 2];
        axes.Sort((a, b) => extent[b].CompareTo(extent[a]));
        var (axisU, axisV) = (axes[0], axes[1]);
        var texcoords = new Vector2[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            texcoords[v] = new Vector2(
                extent[axisU] > 1e-6f ? (proxy.Positions[v][axisU] - boundsMin[axisU]) / extent[axisU] : 0f,
                extent[axisV] > 1e-6f ? (proxy.Positions[v][axisV] - boundsMin[axisV]) / extent[axisV] : 0f);
        }

        vertexData.AddIndexedStream("texcoord$0", texcoords, vertexIndices);

        // Per-vertex cloth paint layers, named and ordered as a current authored cloth proxy carries them.
        // cloth_goal_strength_v2 is the attribute the ModelDoc cloth editor paints; the legacy
        // cloth_goal_strength reads as 0 there. All values are 0..1 paint values rather than raw compiled
        // solver numbers.
        vertexData.AddIndexedStream("cloth_enable$0", proxy.ClothEnable, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", proxy.GoalStrength, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_damping$0", proxy.GoalDamping, vertexIndices);

        // The raw goal pair drives the same two integrator fields at 30x without the goal-damped solve,
        // and per vertex keeps that node on the raw integrator (see FeModel.RawGoalPaintNodes). A sheet
        // whose nodes all compiled goal-damped ships neither stream.
        if (Array.Exists(proxy.AnimationForceAttract, static value => value != 0f)
            || Array.Exists(proxy.AnimationAttract, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_animation_force_attract$0", proxy.AnimationForceAttract, vertexIndices);
            vertexData.AddIndexedStream("cloth_animation_attract$0", proxy.AnimationAttract, vertexIndices);
        }

        vertexData.AddIndexedStream("cloth_collision_radius$0", proxy.CollisionRadius, vertexIndices);
        vertexData.AddIndexedStream("cloth_ground_collision$0", proxy.GroundCollision, vertexIndices);
        vertexData.AddIndexedStream("cloth_drag$0", proxy.Drag, vertexIndices);

        // World-collision ground friction paint. The importer has no "cloth_world_friction" counterpart:
        // world friction rides the ground-collision paint instead, see ProxyVertexData's GroundCollision.
        if (Array.Exists(proxy.GroundFriction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_ground_friction$0", proxy.GroundFriction, vertexIndices);
        }

        // Friction is painted only where the cloth carries any: an all-zero stream is not the same input
        // as no stream at all.
        if (Array.Exists(proxy.Friction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_friction$0", proxy.Friction, vertexIndices);
        }

        // Per-vertex gravity, painted VERBATIM: cloth_gravity$0 compiles into flGravity with no scaling.
        // Without the stream the compiler gives every vertex 360.
        vertexData.AddIndexedStream("cloth_gravity$0", proxy.Gravity, vertexIndices);

        // The per-vertex rot-lock release: a pinned vertex compiles rotation-locked unless this
        // paint (or the sheet-level flex_cloth_borders, which frees every pin at once) releases
        // it, so each pin the original records as rotation-free is painted 1.0 on sheets the
        // flag is not re-emitted for.
        if (physAggregateData?.FeModel is { } feRotate && !clothProxiesFlexed.Contains(proxy))
        {
            var freeRotate = new float[vertexCount];
            var anyFreed = false;
            for (var v = 0; v < vertexCount && v < proxy.NodeIndices.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                if (proxy.ClothEnable[v] == 0f && node < feRotate.StaticNodeCount && feRotate.AllowsRotation(node))
                {
                    freeRotate[v] = 1f;
                    anyFreed = true;
                }
            }

            if (anyFreed)
            {
                vertexData.AddIndexedStream("cloth_anchor_free_rotate$0", freeRotate, vertexIndices);
            }
        }

        // Per-vertex mass paint. The compiler adds expf(cloth_mass * cloth_mass_scale) on top of the mass
        // it derives from the sheet's own geometry, and only when the mesh ships this stream - so a sheet
        // exported without it comes back lighter than the original wherever the mass was painted, while an
        // all-zero stream is a real authoring choice (e^0 = 1) and not the same as no stream at all.
        if (physAggregateData?.FeModel?.RecoverMassPaint(proxy) is { } mass)
        {
            vertexData.AddIndexedStream("cloth_mass$0", mass, vertexIndices);
        }

        // Named vertex selections are painted per vertex, one stream per selection. A cloth effect or a
        // chain joint then names the selection, and the compiler collects every vertex the paint reaches.
        // The one selection this sheet is parented under as a ClothVertexMap is left unpainted: the
        // container recreates the same m_VertexMaps entry without the dynamic vertex set the paint also
        // registers (and which then gives back_solve a sheet-sized set to fit against). Every other
        // selection keeps its paint - an effect naming one the compile cannot find is a hard failure
        // ("refers to non-existent vertex map/set").
        var containerMap = physAggregateData?.FeModel?.GetProxyVertexMapName(proxy);
        foreach (var (mapName, weights) in proxy.VertexMaps)
        {
            if (mapName != containerMap)
            {
                vertexData.AddIndexedStream("cloth_vertex_set_" + mapName + "$0", weights, vertexIndices);
            }
        }

        // Per-vertex stray radius: how far a simulated vertex may leave its animated position
        // (m_AnimStrayRadii). Without the stream the whole array compiles away.
        if (physAggregateData?.FeModel?.RecoverStrayRadiusPaint(proxy) is { } strayRadius)
        {
            vertexData.AddIndexedStream("cloth_stray_radius$0", strayRadius, vertexIndices);
        }

        // Suspender rods, which the compiler regenerates from this paint. Declaring them as explicit
        // springs instead costs a source element per pair, which leaves every vertex they touch heavier
        // than the original and re-picks its node basis (see ClothSuspenderCurvature).
        if (ClothSuspenderPaint(proxy) is { } suspenders)
        {
            vertexData.AddIndexedStream("cloth_suspenders$0", suspenders, vertexIndices);
        }

        // cloth_drag_v2 and cloth_mass have no measurable effect on the compiled flPointDamping/
        // m_NodeInvMasses - cloth_drag (no suffix, unlike goal_strength) is already the attribute the
        // compiler reads, so they are intentionally omitted.

        // cloth_make_rods is the per-face paint gating whether the mesh importer turns a face into rods or
        // keeps it as a solve element; cloth_use_rods does not move that split. Painted under the ~0.5
        // threshold the whole sheet stays faces, which is only right for cloth that ships a surface of its
        // own: a rod-network cloth then compiles to invented m_Tris and loses every rod. So the paints go
        // on only when the original itself carries faces, and the sheet is otherwise left for the compiler
        // to rebuild rods from.
        //
        // A sheet that ships BOTH kinds paints the split itself: 1 over the rod region, 0 over the surface
        // (see ProxyMesh.RodsDriven). A sheet exported with its AUTHORED faces and no rod region skips the
        // paints entirely, as hand-authored proxies do.
        if (proxy.RodsDriven.Length == vertexCount)
        {
            vertexData.AddIndexedStream("cloth_make_rods$0", proxy.RodsDriven, vertexIndices);
        }
        else if (!proxy.UsesAuthoredFaces && physAggregateData?.FeModel is { HasSurfaceElements: true })
        {
            vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), vertexIndices);
            vertexData.AddIndexedStream("cloth_make_rods$0", Enumerable.Repeat(0.4f, vertexCount).ToArray(), vertexIndices);
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), vertexIndices);
        }

        // Skin the proxy vertices. Pinned (cloth_enable 0) vertices follow their anchor bone with weight 1;
        // simulated vertices carry smooth two-joint chain weights (see FeModel.ProxyMesh.SkinInfluences) so
        // the compiler back-solves each chain joint with a proper fit matrix instead of a point rope.
        //
        // Bone names are matched case-INSENSITIVELY, the way Source itself matches them: a model's
        // compiled FeModel m_CtrlName array does not always agree in case with its skeleton, and an
        // Ordinal lookup drops every influence on a bone whose two spellings differ, leaving the affected
        // simulated vertices with all-zero blend weights.
        var clothCompaction = BuildClothBoneCompaction(skeleton);
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            if (IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            var emitted = clothCompaction[bone.Index];
            boneIndexByName.TryAdd(bone.Name, emitted);
            boneIndexByName.TryAdd(GetExportBoneName(bone), emitted);
        }

        AppendCulledClothBoneJoints(dmeModel, boneIndexByName);

        // A sheet no real bone drives ships UNSKINNED, like its hand-authored counterpart: the compiler
        // then anchors the whole sheet to a static root node it generates itself and records every vertex
        // as an m_CtrlOffsets entry hanging off that root. Skinning it to the synthetic per-vertex bones
        // binds each node directly instead, which costs both the root node and the entire offsets array.
        if (!proxy.IsFreeFloating)
        {
            // Four slots cover everything BuildChainSkinInfluences synthesises; weights recovered
            // verbatim from a model's own offset network run wider, and an influence with no slot is
            // dropped before the compiler sees it. The count is widened to hold every recovered
            // influence, whose bone is always a control node the original already carries.
            var jointCount = FeModel.ClothProxyInfluenceSlots;
            if (physAggregateData?.FeModel is { } feModel)
            {
                for (var v = 0; v < vertexCount; v++)
                {
                    if (v < proxy.NodeIndices.Length && feModel.RecoveredSkinWeights.ContainsKey(proxy.NodeIndices[v]))
                    {
                        jointCount = Math.Max(jointCount, proxy.SkinInfluences[v].Count(i => boneIndexByName.ContainsKey(i.Bone)));
                    }
                }
            }

            var blendIndices = new int[vertexCount * jointCount];
            var blendWeights = new float[vertexCount * jointCount];
            for (var v = 0; v < vertexCount; v++)
            {
                var slot = 0;
                foreach (var (boneName, weight) in SeparateTiedInfluenceWeights(proxy.SkinInfluences[v]))
                {
                    if (slot >= jointCount || !boneIndexByName.TryGetValue(boneName, out var bi))
                    {
                        continue;
                    }

                    blendIndices[v * jointCount + slot] = bi;
                    blendWeights[v * jointCount + slot] = weight;
                    slot++;
                }
            }

            vertexData.JointCount = jointCount;
            vertexData.AddStream("blendindices$0", blendIndices);
            vertexData.AddStream("blendweights$0", blendWeights);
        }

        var faceSet = new DmeFaceSet { Name = "cloth" };
        faceSet.Material.MaterialName = "cloth";
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        var cornerOrdinal = 0;
        foreach (var face in proxy.Faces)
        {
            foreach (var _ in face)
            {
                faceSet.Faces.Add(cornerOrdinal++);
            }

            faceSet.Faces.Add(-1);
        }

        if (dag.Shape is DmeMesh morphTarget)
        {
            AddClothProxyMorphLayers(morphTarget, proxy, physAggregateData?.FeModel);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);
        return stream.ToArray();
    }

    /// <summary>
    /// Re-emits a sheet's cloth morph layers (<c>m_MorphLayers</c>) as DMX delta states, sparse per
    /// vertex like any flex. The compiler reads them off the proxy mesh itself - no vmdl node carries
    /// the deltas, so a sheet exported without them loses the layer entirely.
    /// </summary>
    static void AddClothProxyMorphLayers(DmeMesh dmeMesh, FeModel.ProxyMesh proxy, FeModel? feModel)
    {
        if (feModel is null || feModel.MorphLayers.Length == 0)
        {
            return;
        }

        var localOfNode = new Dictionary<int, int>(proxy.NodeIndices.Length);
        for (var v = 0; v < proxy.NodeIndices.Length; v++)
        {
            localOfNode.TryAdd(proxy.NodeIndices[v], v);
        }

        foreach (var layer in feModel.MorphLayers)
        {
            var indices = new List<int>(layer.Nodes.Length);
            var values = new List<Vector3>(layer.Nodes.Length);
            for (var i = 0; i < layer.Nodes.Length && i < layer.InitPos.Length; i++)
            {
                if (localOfNode.TryGetValue(layer.Nodes[i], out var local))
                {
                    indices.Add(local);
                    values.Add(layer.InitPos[i]);
                }
            }

            if (values.Count == 0)
            {
                continue;
            }

            var deltaState = new DmeVertexDeltaData { Name = layer.Name };
            deltaState.AddIndexedStream("position$0", values.ToArray(), indices.ToArray());
            dmeMesh.DeltaStates.Add(deltaState);
            dmeMesh.DeltaStateWeights.Add(Vector2.Zero);
            dmeMesh.DeltaStateWeightsLagged.Add(Vector2.Zero);
        }
    }

    /// <summary>
    /// Builds a generated cloth sheet grid DMX over a group of bone chains (see
    /// <see cref="FeModel.BuildChainGrids"/>). Mirrors hand-authored item proxies: rows/columns of
    /// vertices spanning the chains, bilinear chain-joint skinning, recovered cloth paints, quad faces.
    /// </summary>
    internal byte[] BuildClothChainGridDmx(FeModel.ChainGrid grid, string name)
    {
        Debug.Assert(model is not null, "model required for cloth grid");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeModel = BuildDmeDagSkeleton(skeleton, out _, bonePositions: ClothProxyRestBonePositions);
        dmeModel.Name = name;

        var (dag, vertexData) = CreateDmxDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = grid.Positions.Length;
        var identity = Enumerable.Range(0, vertexCount).ToArray();

        vertexData.AddIndexedStream("position$0", grid.Positions, identity);
        vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("texcoord$0", grid.Texcoords, identity);

        // Full paint set, matching BuildClothProxyMeshDmx: friction and drag are what damp the grid's fall
        // once goal_strength lets go.
        vertexData.AddIndexedStream("cloth_enable$0", grid.ClothEnable, identity);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", grid.GoalStrength, identity);
        vertexData.AddIndexedStream("cloth_goal_damping$0", grid.GoalDamping, identity);
        vertexData.AddIndexedStream("cloth_collision_radius$0", grid.CollisionRadius, identity);
        vertexData.AddIndexedStream("cloth_ground_collision$0", Enumerable.Repeat(0f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_drag$0", grid.Drag, identity);

        if (Array.Exists(grid.Friction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_friction$0", grid.Friction, identity);
        }

        // See BuildClothProxyMeshDmx: keeping the sheet as faces is only right for cloth that ships faces.
        if (physAggregateData?.FeModel is { HasSurfaceElements: true })
        {
            vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), identity);
            vertexData.AddIndexedStream("cloth_make_rods$0", Enumerable.Repeat(0.4f, vertexCount).ToArray(), identity);
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), identity);
        }

        // Case-insensitive bone-name resolution - see BuildClothProxyMeshDmx for why (compiled cloth control
        // node names do not always agree in case with the skeleton; an Ordinal miss silently drops the skin).
        var clothCompaction = BuildClothBoneCompaction(skeleton);
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            if (IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            var emitted = clothCompaction[bone.Index];
            boneIndexByName.TryAdd(bone.Name, emitted);
            boneIndexByName.TryAdd(GetExportBoneName(bone), emitted);
        }

        AppendCulledClothBoneJoints(dmeModel, boneIndexByName);

        const int JointCount = 4;
        var blendIndices = new int[vertexCount * JointCount];
        var blendWeights = new float[vertexCount * JointCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var slot = 0;
            foreach (var (boneName, weight) in grid.SkinInfluences[v])
            {
                if (slot >= JointCount || !boneIndexByName.TryGetValue(boneName, out var bi))
                {
                    continue;
                }

                blendIndices[v * JointCount + slot] = bi;
                blendWeights[v * JointCount + slot] = weight;
                slot++;
            }
        }

        vertexData.JointCount = JointCount;
        vertexData.AddStream("blendindices$0", blendIndices);
        vertexData.AddStream("blendweights$0", blendWeights);

        var faceSet = new DmeFaceSet { Name = "cloth" };
        faceSet.Material.MaterialName = "cloth";
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        foreach (var face in grid.Faces)
        {
            foreach (var index in face)
            {
                faceSet.Faces.Add(index);
            }

            faceSet.Faces.Add(-1);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);
        return stream.ToArray();
    }

    // Soft-body / cloth physics (m_pFeModel): reconstruct editable ModelDoc cloth source so the model
    // recompiles into a working FeModel PHYS block AND opens in ModelDoc (no binary transplant).
    // Phase 1 recovers bone-chain cloth as ClothChain nodes. Phase 2 recovers the cloth SHEET as a
    // ClothProxyMeshFile + proxy DMX.
    bool EmitCloth(FeModel feModel, KVObject rootChildren)
    {
        var boneChains = feModel.BuildBoneChains();

        if (feModel.IsImportedCloth)
        {
            return EmitImportedClothPhase(feModel, boneChains, rootChildren);
        }

        if (ClothProxyMeshesToExtract.Count > 0)
        {
            return EmitProxySheetClothPhase(feModel, boneChains, rootChildren);
        }

        if (boneChains.Count > 0)
        {
            return EmitChainClothPhase(feModel, boneChains, rootChildren);
        }

        return feModel.HasData && EmitFreeNodeClothPhase(feModel, boneChains, rootChildren);
    }

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

    bool EmitProxySheetClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 2 (preferred): the cloth sheet ships as a proxy mesh. With back_solve_joints the
        // compiler regenerates the $cloth_* sheet nodes and back-solves the bone-chain follower
        // nodes the sheet is skinned to, so a chain whose joints appear in the FeModel's own
        // m_FitMatrices is driven by the proxy and must not also get an explicit ClothChain. A
        // proxy mesh does not by itself mean every bone chain is back-solved, though: a model can
        // ship independent cloth alongside an unrelated proxy-mesh panel with back_solve_joints
        // off and no m_FitMatrices entries, and such chains still need the explicit ClothChain
        // emission Phase 1 uses below, or their joints are never registered as cloth nodes at all.
        // back_solve_joints goes on whenever the cloth drives real bones, both the fit-matrix case
        // and the CtrlOffsets-only case with m_FitMatrices empty; DrivesRealBones is a superset of
        // FitMatrixNodes being non-empty.
        var backSolveJoints = feModel.FitMatrixNodes.Count > 0 || feModel.DrivesRealBones;
        // A fit matrix only drives a bone THROUGH a proxy sheet when the fit is taken over that
        // sheet's vertices (see FeModel.ProxyFitMatrixNodes). A chain whose fit matrices are taken
        // over its own $cc extrude ring is the compiler's chain-internal orientation solve, so that
        // chain still has to be emitted or its whole ring goes with it.
        var independentChains = boneChains
            .Where(chain => !chain.Joints.Any(joint => feModel.ProxyFitMatrixNodes.Contains(joint.Node))
                && !feModel.IsSheetDrivenChain(chain))
            .ToList();

        // The bones an independent ClothChain already simulates and drives on its own. The compiler
        // regenerates those bones' proxy nodes from the chain, so a reconstructed proxy mesh that
        // ONLY re-drives such bones is redundant, and a tiny one is degenerate for a back-solved fit:
        // a strip with one "most-bound" vertex per joint makes the compiler's most-bound-joint search
        // access-violate. back_solve is therefore decided PER PROXY below, on only when the proxy
        // drives a real bone no ClothChain covers. Names are compared case-insensitively, compiled
        // control-node and skeleton casing being free to disagree (see BuildClothProxyMeshDmx).
        var chainDrivenBones = new HashSet<string>(
            independentChains.SelectMany(static chain => chain.Joints).Select(joint => feModel.CtrlNames[joint.Node]),
            StringComparer.OrdinalIgnoreCase);

        // A sheet is also skinned to the body bones it merely hangs off, which the cloth uses only
        // as collision anchors. The bones a proxy back-solves are the ones the original compile left
        // position-driven, so only those count as evidence below.
        var positionDrivenBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = feModel.FirstPositionDrivenNode; i < feModel.CtrlNames.Length; i++)
        {
            if (!FeModel.IsProxyNodeName(feModel.CtrlNames[i]))
            {
                positionDrivenBones.Add(feModel.CtrlNames[i]);
            }
        }

        // The bone a vertex hangs off in the compiled hierarchy, which is what its exported
        // influences were derived from before the authored weights were recovered. The recovered
        // list is wider, covering every bone the author painted, and a body bone carrying a few
        // percent of a vertex is not the sheet driving it, so reading the wider list turns
        // back_solve_joints on for sheets the original never back-solved. Only a model with no fit
        // matrix at all reads the anchor; everywhere else the influence list stands.
        IEnumerable<string> DrivingBones(FeModel.ProxyMesh proxy, int vertex)
            => feModel.FitMatrixNodes.Count == 0
                ? [feModel.ResolveSkinBone(proxy.NodeIndices[vertex]) ?? string.Empty]
                : proxy.SkinInfluences[vertex].Select(static i => i.Bone);

        bool ProxyDrivesUnchainedBone(FeModel.ProxyMesh proxy)
        {
            for (var v = 0; v < proxy.ClothEnable.Length; v++)
            {
                // Only simulated vertices back-solve a bone; a pinned vertex just follows its anchor.
                if (proxy.ClothEnable[v] == 0f)
                {
                    continue;
                }

                foreach (var bone in DrivingBones(proxy, v))
                {
                    if (positionDrivenBones.Contains(bone) && !chainDrivenBones.Contains(bone))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // add_bones_to_render_mesh is recoverable from the skeleton: with the flag on, the compiler
        // adds a model-space skeleton "Bone" per cloth PROXY vertex, carrying the raw
        // "$cloth_m{proxy}p{vertex}" control-node name (GetExportBoneName sanitizes '$' to '_' for
        // this exporter's own vmdl text only; the compiled skeleton keeps the literal '$').
        //
        // Bone.IsProceduralCloth alone does not identify those bones. It is Cloth | Procedural, a
        // combination any procedurally-driven cloth bone carries, real back-solved bones with real
        // names included. The narrow signal is a bone that is both flagged procedural-cloth and
        // named by the synthetic proxy convention, which is FeModel.IsProxyNodeName's '$' check.
        var addBonesToRenderMesh = model?.Skeleton.Bones.Any(static b =>
            b.IsProceduralCloth && FeModel.IsProxyNodeName(b.Name)) ?? false;

        // A pinned border vertex keeps its rotation locked unless the sheet was imported with
        // flex_cloth_borders on, so the flag is re-emitted wherever that reproduces the
        // original. On a non-back-solving sheet the flag reaches exactly the pins a face joins
        // to two or more simulated corners: those it frees and gives a node base, which no
        // per-vertex paint does, while a pin every face leaves with fewer simulated corners
        // stays rotation-locked either way and carries no evidence. The flag is taken when every
        // reached pin is recorded rotation-free and every unreached one rotation-locked, so the
        // paint the flag replaces has nothing left to say. A back-solving sheet instead
        // frees exactly the pins with a skin influence on a registered control and its fit
        // machinery pulls anchor parent chains in, so each pin's influence registration has to
        // match its rot-lock class, no gap slot's influences may register it (a new node the
        // original does not have), and every freed pin's static anchor needs its skeleton
        // parent already position-driven (the compiler otherwise registers or reclassifies
        // the parent).
        var ctrlIndexByName = new Dictionary<string, int>(feModel.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < feModel.CtrlNames.Length; i++)
        {
            ctrlIndexByName.TryAdd(feModel.CtrlNames[i], i);
        }

        bool ProxyFlexesClothBorders(FeModel.ProxyMesh proxy, bool proxyBackSolves)
        {
            var faced = new HashSet<int>();
            var flexReaches = new HashSet<int>();
            foreach (var face in proxy.Faces)
            {
                faced.UnionWith(face);
                if (face.Distinct().Count(corner => proxy.ClothEnable[corner] != 0f) >= 2)
                {
                    flexReaches.UnionWith(face.Where(corner => proxy.ClothEnable[corner] == 0f));
                }
            }

            var freesAny = false;
            for (var v = 0; v < proxy.ClothEnable.Length; v++)
            {
                if (proxy.ClothEnable[v] != 0f)
                {
                    continue;
                }

                var node = proxy.NodeIndices[v];

                // A non-back-solving sheet never registers a padded gap slot, so only the
                // recorded rot-lock classes have to agree with the flag's reach.
                if (!proxyBackSolves)
                {
                    if (!faced.Contains(v) || node >= feModel.StaticNodeCount)
                    {
                        continue;
                    }

                    if (flexReaches.Contains(v) != feModel.AllowsRotation(node))
                    {
                        return false;
                    }

                    freesAny |= flexReaches.Contains(v);
                    continue;
                }

                (string Bone, float Weight)[] influences = feModel.RecoveredSkinWeights.TryGetValue(node, out var recovered) && recovered.Length > 0
                    ? [.. recovered]
                    : feModel.ResolveSkinBone(node) is { } skinBone ? [(skinBone, 1f)] : [];

                var registeredAnchors = influences
                    .Where(i => i.Weight > 0f && ctrlIndexByName.ContainsKey(i.Bone))
                    .Select(i => ctrlIndexByName[i.Bone])
                    .ToArray();

                if (!faced.Contains(v))
                {
                    if (registeredAnchors.Length > 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (node >= feModel.StaticNodeCount)
                {
                    continue;
                }

                if ((registeredAnchors.Length > 0) != feModel.AllowsRotation(node))
                {
                    return false;
                }

                if (!feModel.AllowsRotation(node))
                {
                    continue;
                }

                freesAny = true;

                foreach (var anchorNode in registeredAnchors)
                {
                    if (!feModel.IsStatic(anchorNode))
                    {
                        continue;
                    }

                    if (feModel.SkeletonBoneParents?.GetValueOrDefault(feModel.CtrlNames[anchorNode]) is { } parent
                        && (!ctrlIndexByName.TryGetValue(parent, out var parentNode)
                            || !feModel.IsPositionDriven(parentNode)))
                    {
                        return false;
                    }
                }
            }

            return freesAny;
        }

        var (clothProxyList, clothProxyChildren) = MakeListNode("ClothProxyMeshList");
        foreach (var proxyFile in ClothProxyMeshesToExtract)
        {
            // The threshold is derived from the ORIGINAL's own compiled fit data (see
            // FeModel.GetBackSolveInfluenceThreshold): the sub-threshold weights the original's
            // compile dropped from its fit ranges have to be dropped by ours too, or the extra
            // influences carry bones over the fit-matrix minimum the original left below it.
            // Per-vertex gravity rides the cloth_gravity$0 paint in the proxy DMX instead of any
            // KV field here (the cloth_gravity_scale KV was tested and does not reach flGravity).
            var proxyBackSolve = backSolveJoints && ProxyDrivesUnchainedBone(proxyFile.Proxy);
            var proxyFlexes = ProxyFlexesClothBorders(proxyFile.Proxy, proxyBackSolve);
            if (proxyFlexes)
            {
                clothProxiesFlexed.Add(proxyFile.Proxy);
            }

            var proxyNode = MakeClothProxyMeshFile(proxyFile.Name, proxyFile.FileName, proxyBackSolve, driveMeshes: proxyBackSolve, addBonesToRenderMesh,
                backSolveInfluenceThreshold: feModel.GetBackSolveInfluenceThreshold(proxyFile.Proxy),
                flexClothBorders: proxyFlexes);

            // A selection covering exactly the sheet's simulated nodes is the m_VertexMaps entry a
            // ClothVertexMap around the sheet compiles to - the grouping the "Add Cloth Vertex Map"
            // wizard builds. Only a PROXY MESH may be wrapped this way: the same container around
            // free ClothNodes that a ClothSpring references makes the compiler access-violate.
            if (feModel.GetProxyVertexMapName(proxyFile.Proxy) is { } proxyVertexMap)
            {
                var (mapNode, mapChildren) = MakeListNode("ClothVertexMap");
                mapNode.Add("name", proxyVertexMap);
                AddClothVertexMapAttributes(mapNode, feModel, proxyVertexMap,
                    BuildProxyNodeNameMap(ClothProxyMeshesToExtract));
                mapChildren.Add(proxyNode);
                clothProxyChildren.Add(mapNode);
                continue;
            }

            clothProxyChildren.Add(proxyNode);
        }

        // Clean regular grids generated over the bone chains, shipped DISABLED next to the
        // recovered surface: a ready-made editable sheet for re-authoring the cloth.
        foreach (var clothGrid in ClothChainGridsToExtract)
        {
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothProxyChildren.Add(gridNode);
        }

        rootChildren.Add(clothProxyList);

        // The proxy mesh ships the global solver scalars + any collision shapes via a Softbody.
        // Independent (non-back-solved) chains, if any, are emitted alongside it - see above.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        var surfaceRods = ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract,
            out var generatesBendRods, out var generatesBendOnlyRods, out var addCurvature, out _);
        softbodyChildren.Add(MakeClothParams(feModel, generatesBendRods, generatesBendOnlyRods,
            addCurvature > 0f ? addCurvature : feModel.ChainRingCurvature));

        // Simulated real bones that are NEITHER back-solved NOR part of any multi-joint BoneChain:
        // standalone goal-attraction points wired together only by ClothSpring (see MakeClothNode).
        // A real bone with no real-bone descendants of its own never forms a BoneChain, so without
        // this these carry correct rods and connectivity but compiler-default paint.
        var chainNodes = boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();
        var independentChainNodes = independentChains
            .SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();
        var loneClothNodes = new List<(string Name, int Node)>();

        // Real, STATIC (invMass == 0) bones the compiler still registers as FeModel control nodes
        // purely for orientation bookkeeping: no rods, no integrator role, no fit matrix, and no
        // ClothChain, capsule or sphere authoring of their own. The compiled skeleton's plain Cloth
        // bone flag (Bone.IsClothControlNode, not the stricter IsProceduralCloth) marks exactly
        // these, and each is emitted as a static ClothNode. A capsule or sphere parent bone is
        // excluded: the compiler walks a collision bone's ancestor chain and registers them itself.
        var boneByName = model?.Skeleton.Bones.ToDictionary(static b => b.Name, StringComparer.Ordinal);
        var shapeParentBones = CollisionShapeParentBones(feModel);
        var leftoverStaticNodes = new List<(string Name, int Node)>();

        // The three constructs skipped above each rely on something else recreating the node:
        // a generated name on the proxy sheet or chain that regenerates it, a fit-matrix or
        // back-solved chain joint on the sheet that drives it. What actually registers a bone as a
        // control node is a proxy VERTEX skinned to it - so a fit-matrix/chain bone no emitted
        // proxy is skinned to, and a generated node no emitted proxy contains, is recreated by
        // nothing and vanishes from the compiled node set along with its rods and its share of
        // every neighbour's mass. Reconstructed sheets are smaller than the originals whenever a
        // vertex ends up unfaced (BuildProxyMeshesFromRodsOnly's 3-member minimum drops the rest),
        // which is exactly when this bites.
        var anchorOf = BuildCtrlAnchorMap(feModel);
        var jiggleNodes = feModel.JiggleBones.Select(static j => j.Node).ToHashSet();
        var proxyRegisteredNodes = new HashSet<int>();
        var proxySkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recoveredSkinnedBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, proxy) in ClothProxyMeshesToExtract)
        {
            foreach (var vertex in SurvivingProxyVertices(proxy))
            {
                var vertexNode = proxy.NodeIndices[vertex];
                proxyRegisteredNodes.Add(vertexNode);
                var recoveredVertex = feModel.RecoveredSkinWeights.ContainsKey(vertexNode);
                foreach (var (bone, weight) in proxy.SkinInfluences[vertex])
                {
                    proxySkinnedBones.Add(bone);
                    if (recoveredVertex && weight > 0f)
                    {
                        recoveredSkinnedBones.Add(bone);
                    }
                }
            }
        }

        bool IsRecreated(int node, string name)
            => proxyRegisteredNodes.Contains(node) || proxySkinnedBones.Contains(name)
                || independentChainNodes.Contains(node) || jiggleNodes.Contains(node)
                || shapeParentBones.Contains(name);

        // Emitted flat, never under a ClothVertexMap: that container around a free ClothNode a
        // ClothSpring names makes the compiler access-violate.
        var unregisteredNodes = new List<(string Name, int Node)>();
        var unregisteredFreeNodes = new List<(string RootBone, int Node, string ElementName, Vector3 Origin)>();
        var freeClothNodeNames = new Dictionary<int, string>();

        for (var node = 0; node < feModel.CtrlNames.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (FeModel.IsProxyNodeName(name) || feModel.FitMatrixNodes.Contains(node) || chainNodes.Contains(node))
            {
                if (IsRecreated(node, name))
                {
                    continue;
                }

                const string ClothNodePrefix = "$cloth_node_";
                if (name.StartsWith(ClothNodePrefix, StringComparison.Ordinal))
                {
                    if (TryResolveClothNodeAnchor(feModel, anchorOf, node, out var rootBone, out var origin))
                    {
                        var elementName = name[ClothNodePrefix.Length..];
                        unregisteredFreeNodes.Add((rootBone, node, elementName, origin));
                        freeClothNodeNames[node] = elementName;
                    }
                }
                else if (!FeModel.IsProxyNodeName(name))
                {
                    unregisteredNodes.Add((name, node));
                }

                continue;
            }

            if (node < feModel.NodeInvMasses.Length && feModel.NodeInvMasses[node] != 0f)
            {
                loneClothNodes.Add((name, node));
            }
            // A bone an emitted proxy vertex is skinned to is registered by the sheet, whose
            // hierarchy spans only the skinned bones; an extra ClothNode on it instead parents it
            // onto its nearest control-node ancestor, whatever registered that. Skipped only where
            // the original records the bone as a hierarchy ROOT, and only where the skinning
            // evidence is real: on a no-fit model every exported influence is recovered, and on a
            // fit model a bone counts only when a RECOVERED influence names it, a synthesised one
            // being no evidence of how the compiler registers that bone.
            else if (!shapeParentBones.Contains(name)
                && !((recoveredSkinnedBones.Contains(name)
                        || (feModel.FitMatrixNodes.Count == 0 && proxySkinnedBones.Contains(name)))
                    && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0)
                && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
            {
                leftoverStaticNodes.Add((name, node));
            }
        }

        var proxyNodeNameMap = BuildProxyNodeNameMap(ClothProxyMeshesToExtract);

        var authoredFaces = feModel.GetAuthoredElementFaces();
        if (independentChains.Count > 0 || loneClothNodes.Count > 0 || leftoverStaticNodes.Count > 0
            || unregisteredNodes.Count > 0 || unregisteredFreeNodes.Count > 0 || authoredFaces.Count > 0)
        {
            var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
            clothFolder.Add("name", "cloth");
            softbodyChildren.Add(clothFolder);

            var loneJointChainCount = loneClothNodes.Count(n => LoneClothNodeIsOriginalRoot(feModel, n.Node));
            var hasOtherChains = independentChains.Count + loneJointChainCount > 1;

            foreach (var boneChain in independentChains)
            {
                clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains));
                if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
                {
                    clothFolderChildren.Add(restated);
                }
            }

            // A ClothNode carries no vertex_map of its own, so the selections covering one are
            // restored by parenting it under a ClothVertexMap instead.
            var folderFor = ClothVertexMapFolders(feModel, clothFolderChildren);

            foreach (var (name, node) in loneClothNodes)
            {
                if (LoneClothNodeIsOriginalRoot(feModel, node))
                {
                    clothFolderChildren.Add(MakeLoneJointChain(feModel, name, node, hasOtherChains));
                }
                else
                {
                    folderFor(node, true).Add(MakeClothNode(feModel, name, node, proxyNodeNames: proxyNodeNameMap));
                }
            }

            foreach (var (name, node) in leftoverStaticNodes)
            {
                folderFor(node, true).Add(MakeClothNode(feModel, name, node, isStaticNode: true,
                    proxyNodeNames: proxyNodeNameMap));
            }

            // is_static_node reproduces the node's own compiled class: a free node with no
            // coverage left compiles to invMass exactly 1.0 when dynamic and 0.0 when static.
            foreach (var (name, node) in unregisteredNodes)
            {
                clothFolderChildren.Add(MakeClothNode(feModel, name, node,
                    isStaticNode: feModel.IsStatic(node), proxyNodeNames: proxyNodeNameMap));
            }

            foreach (var (rootBone, node, elementName, origin) in unregisteredFreeNodes)
            {
                clothFolderChildren.Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin,
                    proxyNodeNames: proxyNodeNameMap));
            }

            AddClothFaces(clothFolderChildren, feModel);
        }

        var authoredClothNodes = loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
            .Select(static entry => entry.Node)
            .ToHashSet();
        AddClothProxySprings(softbodyChildren, feModel, ClothProxyMeshesToExtract, independentChainNodes,
            authoredClothNodes, freeClothNodeNames, surfaceRods, proxyNodeNameMap);
        AddClothChainSurplusClusters(softbodyChildren, feModel, independentChains);

        // A bone an emitted proxy vertex is skinned to is registered by the sheet itself; the rest
        // come from the chains and cloth nodes emitted above.
        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(proxySkinnedBones);
        clothBones.UnionWith(independentChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        clothBones.UnionWith(loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
            .Select(static entry => entry.Name));
        clothBones.UnionWith(unregisteredFreeNodes.Select(static entry => entry.RootBone));
        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        var shapeNames = AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothAntiTunnelGroup(softbodyChildren, feModel, shapeNames,
            [.. ClothProxyMeshesToExtract.Select(static proxy => proxy.Name)]);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, independentChains));

        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNameMap);

        return true;
    }

    bool EmitChainClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 1 fallback (no recoverable sheet): bone-chain cloth, plus a GENERATED sheet grid
        // over each group of neighbouring chains (skirts/capes). The grid mirrors hand-authored
        // item proxies: with back_solve_joints=false the chains keep simulating the bones while
        // the sheet simulates the surface between them and drives the render mesh directly.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel,
            generatesBendRods: feModel.HasChainStiffnessRods(boneChains),
            generatesBendOnlyRods: feModel.HasChainBendOnlyRods(boneChains),
            addCurvature: feModel.ChainRingCurvature));
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);

        var hasOtherChains = boneChains.Count > 1;
        foreach (var boneChain in boneChains)
        {
            clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains));
            if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
            {
                clothFolderChildren.Add(restated);
            }
        }

        foreach (var clothGrid in ClothChainGridsToExtract)
        {
            // The grid ships DISABLED: the chains alone reproduce the original physics, and with
            // drive_meshes the sheet would fight the chain-driven skinning of the same region.
            // It is a ready-made starting sheet the author can enable/retarget in ModelDoc
            // (like hand-authored cape proxies that drive otherwise boneless render regions).
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothFolderChildren.Add(gridNode);
        }

        AddClothFaces(clothFolderChildren, feModel);
        var sourceSprings = AddClothSourceSprings(softbodyChildren, feModel, boneChains);
        AddClothChainSurplusRods(softbodyChildren, feModel, boneChains);

        var chainCoveredNodes = boneChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();
        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(boneChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        // A static control node no chain, shape or jiggle bone claims is recreated by nothing else in
        // this phase, so it is declared as a bare ClothNode wherever the compiled skeleton records the
        // bone as a cloth control node - the same evidence the proxy-sheet phase reads.
        var clothControlBones = model?.Skeleton.Bones
            .Where(static b => b.IsClothControlNode)
            .Select(static b => b.Name)
            .ToHashSet(StringComparer.Ordinal);
        var chainSurface = feModel.Quads.Length > 0 || feModel.Tris.Length > 0;
        AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel, chainCoveredNodes,
            name => chainSurface || (clothControlBones?.Contains(name) ?? false),
            clothBones, ClothVertexMapFolders(feModel, clothFolderChildren), hasOtherChains: true,
            ClothControlAncestorTest(feModel), sourceSprings);

        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
        return true;
    }

    bool EmitFreeNodeClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // No sheet and no chains: cloth built purely from free-standing ClothNodes (and the
        // ClothSprings wiring them), e.g. the "$cloth_node_*" minimal rigs and lone goal-driven
        // bones. A FeModel that yields no authorable node here (jiggle-bone users, weapon-offset
        // rigs) emits nothing and falls through to the PHYS transplant placeholder below.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel));
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);

        var clothBones = ClothBoneNames(feModel);
        var freeNodes = AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel,
            [], static _ => true, clothBones,
            ClothVertexMapFolders(feModel, clothFolderChildren),
            bareStaticReparented: ClothControlAncestorTest(feModel));
        AddClothFaces(clothFolderChildren, feModel);

        // Every ctrl of a collision-shape-only model is a shape parent bone, which the loop above
        // skips, so gating on the node count alone drops the shapes with the rest of the Softbody.
        if (freeNodes > 0 || CollisionShapeParentBones(feModel).Count > 0)
        {
            AddClothFollowBones(softbodyChildren, feModel, clothBones);
            AddClothCollisionShapes(softbodyChildren, feModel);
            AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
            rootChildren.Add(softbody);
            AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
            return true;
        }

        return false;
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
