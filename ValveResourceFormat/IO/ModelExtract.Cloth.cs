using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
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
        foreach (var (_, culledName) in CulledClothBones)
        {
            skeletonBoneNames.Add(culledName);
        }

        var boneParents = model.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().Parent?.Name, StringComparer.OrdinalIgnoreCase);
        feModel.SkeletonBoneParents = boneParents;
        feModel.SetSkeletonParents(boneParents);

        BuildClothRestBonePositions(feModel);

        // An imported PhysAuthFx cloth ships its own node/rod tables and is emitted as a single
        // ImportedCloth element, so neither a synthesised proxy sheet nor a chain grid has anything to
        // attach to and their DMX files would be written for nothing.
        if (feModel.IsImportedCloth)
        {
            return;
        }

        // The compiler assigns the $cloth_m<N> mesh index by ORDINAL STRING SORT of the proxy names, not
        // declaration order, so an unpadded suffix breaks every spring/basis reference on models with 11+
        // proxies ("cloth_proxy10" sorts before "cloth_proxy2" - spectre's 13 islands). Zero-padding the
        // suffix to the model's own digit count keeps declaration order and sort order identical; models
        // with up to 10 proxies keep their old single-digit names.
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
    // still be read as the same pose at better precision. The widest disagreement measured across the
    // Deadlock and Dota corpora is 0.087 units (invoker_wings_of_knowledge_shoulder_alt2, on a 227-unit
    // model) and the next value up is 269; anything past a whole unit is a node the compiler placed
    // somewhere else entirely, and that bone keeps its compiled transform.
    const float ClothRestBoneTolerance = 1.0f;

    // And how far it has to sit before the disagreement is worth acting on. Below the absolute floor the
    // round trip holds two floats equal at, the two poses ARE the same pose, and re-deriving the bone from
    // the cloth data buys nothing measurable while still re-rolling every tie the compiler breaks off that
    // bone's geometry - measured on digger_default, whose node-base scan turns on a 1e-6 margin.
    const float ClothRestBoneFloor = 1e-3f;

    // The correction runs per MODEL only when some bone disagrees by more than twice the floor. A model
    // whose disagreements all end inside the floor's own noise band gains nothing measurable but the
    // correction can push offsets measured against those bones just past the floor the other way
    // (snapfire_customgame, three bones at 1.0-1.6e-3). Once enabled, every bone past the per-bone floor
    // moves together - derived rest shapes span bones on both sides of any per-bone cut, so a partial
    // correction leaves them mixed (mh_palico's quads).
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
    // back_solve_joints_drive_meshes must track backSolveJoints, NOT be a blanket true/false: verified on
    // meepo_naruto_set's own hand-authored source (back_solve_joints=false AND
    // back_solve_joints_drive_meshes=false together on its one real jaket proxy) that leaving it true
    // while back_solve_joints is false makes the compiler back-solve fit matrices for UNRELATED bones
    // elsewhere in the model (5 standalone ClothNode neck bones + 1 real ancestor bone all wrongly turned
    // "position driven", m_FitMatrices 0->6) - i.e. this flag is not scoped to just this proxy's own
    // nodes when it disagrees with back_solve_joints. The disabled ready-made grid is a deliberate
    // exception: it always passes backSolveJoints=false itself but wants driveMeshes=true regardless (a
    // future re-author enables it to drive the mesh directly, per its own call sites below) - verified
    // harmless since disabling the grid node entirely changes nothing in the compiled FeModel.
    //
    // back_solve_influence_threshold's real display name (extracted via Ghidra schema analysis of
    // physicsbuilder.dll) is "Back-solve Min Weight" - a minimum skin-weight cutoff for which vertices
    // contribute to a joint's back-solved fit. The value is now DERIVED per model from the original's own
    // compiled fit data (FeModel.RecoveredBackSolveThreshold - the compiled fit ranges omit weights below
    // the original's own threshold while m_CtrlSoftOffsets still carries them, bounding the value from
    // both sides), passed in by the caller; 0.0 remains the default for models with no such signal.
    // Do not substitute a guessed constant here: a nonzero threshold computes each fit from a smaller,
    // filtered subset of vertices, which shifts the solved rigid transform (bone bind pose) even while the
    // fit-matrix COUNT stays 8/8 - so validate any value against the resulting per-bone transform, not just
    // that a fit matrix still exists.
    static KVObject MakeClothProxyMeshFile(string name, string fileName, bool backSolveJoints, bool driveMeshes, bool addBonesToRenderMesh = false, float backSolveInfluenceThreshold = 0.0f)
    {
        var node = MakeNode("ClothProxyMeshFile",
            ("name", name),
            ("filename", fileName),
            ("import_scale", 1.0f),
            ("back_solve_joints", backSolveJoints),
            ("back_solve_joints_drive_meshes", driveMeshes),
            ("flex_cloth_borders", false),
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

        // envelope_inches (how far the sheet reaches when DRIVING render meshes) is deliberately NOT
        // emitted - the compiler default matches how hand-authored proxies ship (they omit the field), and
        // it keeps dark_willow's fit matrices byte-exact. A large value (e.g. 1000in) drive-binds
        // essentially the entire render mesh to the sheet - a real stiffness source, and the drive bindings
        // live in the compiled vmesh, invisible to any PHYS comparison.

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

    // Global cloth solver parameters, populated from the FeModel scalars. Field names match the compiled
    // ClothParams source node (e.g. siren_legs.vmdl); the compiler re-derives everything not emitted here.
    static KVObject MakeClothParams(FeModel fe, bool generatesBendRods = false, bool generatesBendOnlyRods = false,
        float addCurvature = 0f, bool explicitMasses = false)
    {
        var flags = fe.DynamicNodeFlags;
        bool Flag(uint bits) => (flags & bits) != 0;

        return MakeNode("ClothParams",
            ("default_stretch", fe.DefaultSurfaceStretch),
            // Recovered from the rod relaxation factors, NOT from m_flDefaultThreadStretch: that field
            // tracks m_flDefaultSurfaceStretch whatever the shear is (measured across a 0..0.5 sweep), so
            // reading it emitted the surface stretch a second time and slackened every face diagonal.
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

            // m_SkelParents is the compiled image of this exact field, so a model whose original still
            // carries one hands the authored parenting back directly. Older compiles ship none and leave
            // only the m_Ropes runs, which record the same chain a rope's worth at a time.
            if (feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length && feModel.SkelParents[node] >= 0)
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

    // An unrolled proxy ring sits on the joint frame's +Y, so an authored twist counts down from 90 degrees.
    const float ClothExtrudeTwistBase = 90f;

    // Explicitly declares a two-node distance constraint (a "rod") by NODE NAME. CModelDocClothSpring in
    // physicsbuilder.dll - a real, direct analogue of ClothQuad for edges instead of faces.
    // is_length_explicit=false (the compiler's own default) pins min_length=max_length=rest distance
    // EXACTLY - a fully rigid edge. BOTH is_length_explicit=true AND enable_advanced_parameters=true are
    // required together for min_length/max_length to take effect at all (neither alone works).
    //
    // weight0/relaxation_factor are NOT re-authorable ClothSpring inputs: CModelDocClothSpring's complete
    // registered attribute set (Ghidra GetStaticAttributes extraction from physicsbuilder.dll - the same
    // method that found min_length/max_length/stiffness) is exactly m_Node0, m_Node1, m_flStiffness,
    // m_bEnableAdvancedParameters, m_bIsLengthExplicit, m_flMinLength, m_flMaxLength, m_nExtraIterations,
    // m_Color - no m_flWeight0/m_flRelaxationFactor. An authored weight0 compiles clean but the resulting
    // m_Rods entry reads back the compiler's default (0.5), while min_length/max_length on the same rod stay
    // exact. See FeModel.Rod.Weight0.
    static KVObject MakeClothSpring(string name, string n0, string n1, float minLength, float maxLength,
        float stiffness = 1.0f)
        => MakeNode("ClothSpring",
            ("name", name),
            ("cloth_node_0", n0),
            ("cloth_node_1", n1),
            ("stiffness", stiffness),
            ("enable_advanced_parameters", true),
            ("is_length_explicit", true),
            ("min_length", minLength),
            ("max_length", maxLength));

    // m_Rods is NOT derivable from the surface: verified on dark_willow that all 61 original rods match
    // neither a Quads/Tris edge nor a quad diagonal (0/61 either way) - guessing a geometric placement rule
    // (every edge, then quad diagonals) both measurably missed real vs. added wrong structure. m_Rods is
    // just another compiled array like m_Quads/m_NodeIntegrator: read it directly off the FeModel and
    // re-declare it as explicit ClothSpring nodes by NAME.
    //
    // Resolve every "$cloth_*" endpoint through our OWN global-node-index -> "$cloth_m{proxy}p{local}" map
    // (built from proxy.NodeIndices, the same one MakeClothQuad's proxy.Faces uses), NOT the original's
    // literal CtrlNames string: our re-exported proxy DMX re-sorts vertices (FeModel.BuildProxyMesh sorts
    // referenced nodes ascending), so the original's local index would compile fine but silently resolve to
    // the wrong vertex in our export - rods crossing to unrelated points. Real bone names need no
    // translation (skeleton bone names are not proxy-mesh-local).
    /// <summary>
    /// The rods the compiler rebuilds from the exported surface on its own, which must therefore not also
    /// be declared as explicit springs. Every face edge and diagonal is one. When the sheet's compiled rods
    /// reach further than that, the extra bend network was authored on (see <c>add_stiffness_rods</c> in
    /// <see cref="MakeClothParams"/>) and regenerates the remaining pairs of that sheet too.
    /// </summary>
    static HashSet<(int, int)> ClothRodsFromSurface(FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, out bool generatesBendRods,
        out bool generatesBendOnlyRods, out float addCurvature)
    {
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
        var boundedBeyondSurface = false;
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (surfaceNodes.Contains(edge.Item1) && surfaceNodes.Contains(edge.Item2) && !derived.Contains(edge))
            {
                beyondSurface.Add(edge);
                boundedBeyondSurface |= rod.MaxDist < ClothBendOnlyRodMaxDistance;
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
            // OWN joints automatically (verified: meepo_naruto_set's m_Rods includes every pairing of
            // e.g. hed_cloth_l_1/2/3/3_end, not just parent-child) - re-declaring one of these via an
            // explicit ClothSpring is both redundant AND rejected outright by the compiler ("Cannot find
            // Fx Bone"/"Cannot find node": a bone that is ONLY a ClothChain joint_name, with no fit-matrix
            // back-solve or ClothNode registration of its own, is not a valid ClothSpring endpoint).
            if (chainJointNodes.Contains(edge.Item1) || chainJointNodes.Contains(edge.Item2))
            {
                continue;
            }

            var name0 = ResolveName(edge.Item1);
            var name1 = ResolveName(edge.Item2);
            if (name0 is null || name1 is null)
            {
                // A rod-only proxy node dropped by BuildProxyMeshesFromRodsOnly's 3-member minimum (see
                // its own remarks) has no corresponding exported vertex to reference at all - skip rather
                // than author a dangling reference the compiler would reject outright.
                continue;
            }

            // Re-declare every rod directly. (No rod-skip heuristic: dark_willow/meepo_naruto_set/
            // legion_commander all compile every rod, so any static "skip risky pair" filter only risks
            // wrongly excluding a valid rod.)
            softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1, rod.MinDist, rod.MaxDist));
        }
    }

    // Emits cloth collision shapes (capsules/spheres) recovered from the FeModel rigids into a Softbody.
    // Most Dota cloth (including dark_willow) has none - then this is a no-op. Shapes are how the engine
    // keeps the cloth off the body for models that use them (e.g. primal_beast).
    // Rods the chains do not rebuild themselves (extra copies of a parent span) have to be re-declared,
    // or every node they touch compiles lighter than the original.
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

            softbodyChildren.Add(MakeClothSpring($"rod_{name0}_{name1}", name0, name1, rod.MinDist, rod.MaxDist));
        }
    }

    /// <summary>
    /// Re-declares the authored two-corner source elements (<see cref="FeModel.SourceSprings"/>) as
    /// explicit springs. Neither the surface nor a chain regenerates these, and the compiler records one
    /// source element per spring, so a model exported without them comes back short both a rod and a
    /// source element per pair. Endpoints are named verbatim, <c>$cc</c> proxies included - those are
    /// valid ClothSpring endpoints even though they are not chain joints.
    /// </summary>
    static void AddClothSourceSprings(KVObject softbodyChildren, FeModel feModel, List<FeModel.BoneChain> chains)
    {
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

            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[a], names[b], rod.MinDist, rod.MaxDist,
                rod.RelaxationFactor));
        }
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

    // Bone-local distance under which the compiler merges a free ClothNode into its root bone's control
    // node instead of giving it one of its own. Measured by bisection against the Dota resourcecompiler:
    // euclidean, merging below 0.001 and keeping both nodes at exactly 0.001.
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
        FeModel feModel, HashSet<int> coveredNodes, bool emitBareStatics,
        out HashSet<string> recreatedVertexMaps)
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

        recreatedVertexMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                clothChildren.Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin));
                springName[node] = elementName;
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
                if (!isStatic || rodTouched.Contains(node) || emitBareStatics)
                {
                    clothChildren.Add(MakeClothNode(feModel, name, node, isStaticNode: isStatic));
                    springName[node] = name;
                    emitted++;
                }
            }
        }

        if (springName.Count == 0)
        {
            return emitted;
        }

        // One spring per rod OCCURRENCE, not per distinct pair: node mass accumulates per rod, and models
        // ship genuine duplicate rods (am_debut_temple_lantern2 carries the same span three times).
        var occurrence = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA == rod.NodeB)
            {
                continue;
            }

            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!springName.TryGetValue(edge.Item1, out var name0)
                || !springName.TryGetValue(edge.Item2, out var name1))
            {
                continue;
            }

            var copy = occurrence.GetValueOrDefault(edge);
            occurrence[edge] = copy + 1;
            var springLabel = copy == 0 ? $"rod_{edge.Item1}_{edge.Item2}" : $"rod_{edge.Item1}_{edge.Item2}_{copy}";
            softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist));
        }

        return emitted;
    }

    // Every bone a cloth collision shape hangs off. The compiler walks such a bone's ancestor chain and
    // registers them itself, so an explicit ClothNode on one is redundant - and not merely redundant:
    // the node it declares is parented onto its nearest control-node ancestor, which the shape's own
    // registration does not do (hornet's weapon_bone, a BOX rigid, is a root in the original and a child
    // of hand_R with the extra node).
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
            softbodyChildren.Add(MakeClothShapeCapsule(capsule));
        }
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
        foreach (var targetName in targetNames)
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
                    maps.Add(name.Trim());
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
            var pitch = MathF.Atan2(-strength.Z, MathF.Sqrt((strength.X * strength.X) + (strength.Y * strength.Y)));
            var yaw = MathF.Atan2(strength.Y, strength.X);
            node.Add("angles", ToKVArray(Vector3.RadiansToDegrees(new Vector3(pitch, yaw, 0f))));
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
        node.Add("cloth_collision_priority", 0);
        node.Add("vertex_map", "");
        node.Add("inverted_collision", false);
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
            ("name", (capsule.ParentBone ?? "cloth") + "_clothCapsule"),
            ("parent_bone", capsule.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, capsule.CollisionMask);
        node.Add("cloth_collision_priority", 0);
        node.Add("vertex_map", capsule.VertexMap ?? "");
        node.Add("inverted_collision", false);
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
        node.Add("cloth_collision_priority", 0);
        node.Add("vertex_map", "");
        node.Add("inverted_collision", false);
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

    static KVObject MakeClothChainNode(FeModel feModel, FeModel.BoneChain chain)
    {
        // A rigid hinge takes the chain's rod network over, so a hinged chain that still carries rods was
        // authored with a soft link instead.
        var softHinge = feModel.HasChainRods(chain);

        var joints = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            joints.Add(MakeClothJoint(feModel, joint, chainExtrudes: chain.ExtrudeSides >= 1, softHinge));
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

        chainData.Add("version", root is not null && !feModel.AllowsRotation(root.Node)
                && (lockedInOriginal || !locksJoints)
            ? (feModel.NodeBases.ContainsKey(root.Node) ? 2 : 1)
            : (lockedInOriginal ? 1 : 2));

        return MakeNode("ClothChain",
            ("name", chain.RootBone),
            ("root_bone", chain.RootBone),
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

        // Recover the per-node attraction/damping/gravity so a bone-chain joint follows the body as tightly
        // as the original (anti-clip). The compiler CUBES the joint goal_strength into
        // flAnimationForceAttraction (same as the painted cloth_goal_strength_v2 on proxy meshes -
        // measured on dark_carnival_legion_commander: goal_strength 0.125 compiled to fa=0.001953=0.125^3),
        // so emit the cube ROOT of the recovered attraction; anything else leaves the cloth ~64x looser
        // than the original and the skirt hangs like a stiff pendulum.
        //
        // Recover it regardless of joint.Simulated: a chain ROOT is routinely authored `simulate = false`
        // with a real nonzero goal_strength anyway (meepo_naruto_set's `hed_cloth_l_1` ships
        // `simulate = false, goal_strength = 0.6`, compiling to flAnimationForceAttraction 0.216 = 0.6^3,
        // not 0). Gating this to 0 for non-simulated joints would zero goal_strength on every chain root.
        var integrator = feModel.GetIntegrator(joint.Node);
        var goalStrength = MathF.Cbrt(Math.Clamp(integrator.ForceAttraction, 0f, 1f));

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
        // A joint's own node is position-driven and compiles with flGravity 0; the authored gravity_z
        // lands on the $cc proxies the compiler extrudes from it.
        var gravityNode = joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        kv.Add("gravity_z", feModel.GetIntegrator(gravityNode).Gravity / ClothSourceBaseGravity);

        // Everything below is recovered per node; NO invented stiffness defaults. Emitting non-zero
        // twist_relax / stiff_hinge / motion_bias makes the compiler build a Twist/KelagerBend constraint
        // network instead of the plain ropes some chains compile to (dark_carnival original: 5 ropes,
        // 0 twists - twist_relax must stay 0 there). But this is NOT a universal default: meepo_naruto_set's
        // own source authors twist_relax=1.0 uniformly on every joint of its 4 real ClothChains, and its
        // compiled FeModel has a real m_Twists network (24 entries, 0 ropes) to match - hardcoding 0 there
        // instead produces a bogus 4-node "Rope" fallback constraint per chain (m_Ropes, entirely absent
        // from the original). Recover per-joint from the ORIGINAL's own m_Twists participation
        // (FeModel.HasTwistToParent) rather than guessing a single constant for every model. The pair is
        // owned by the CHILD of the link, so a joint whose node merely appears in some other joint's pair
        // must stay at 0 or the compiler builds a second twist on the link above it as well.
        kv.Add("twist_relax", feModel.HasAuthoredTwist(joint.Node, joint.ParentNode) ? 1.0f : 0.0f);


        // World collision membership + radius (m_WorldCollisionNodes / m_NodeCollisionRadii); without
        // them the recompiled tail/leg chains clip into the ground.
        kv.Add("world_collision", feModel.IsWorldCollisionNode(joint.Node));

        var (worldFriction, groundFriction) = feModel.GetWorldFriction(joint.Node);
        kv.Add("world_friction", worldFriction);
        kv.Add("ground_friction", groundFriction);
        kv.Add("collision_radius", feModel.GetCollisionRadius(joint.Node));

        // Stray radius (m_AnimStrayRadii): the max distance the node may stray from its animated position.
        kv.Add("stray_radius", feModel.GetStrayRadius(joint.Node));
        kv.Add("stray_radius_stretchiness", feModel.GetStrayStretchiness(joint.Node));
        kv.Add("friction", feModel.GetNodeFriction(joint.Node));

        // The named vertex selections this joint belongs to, comma separated. Naming them here is what
        // puts the joint and the proxies extruded from it back into the selections cloth effects target.
        if (feModel.GetVertexMapNames(joint.Node) is { } vertexMaps)
        {
            kv.Add("vertex_map", vertexMaps);
        }

        // The hinge constraint the ClothChainHinge node writes onto the joint it constrains. It both
        // orients that joint's proxy ring and adds the compiler's own static anchor node, so a joint that
        // shipped one loses a control node without it - and a joint that did not gains one.
        var hinge = feModel.GetChainHinge(joint.Name, joint.Node);

        // Per-joint extrude width. The chain-level extrude_sides (MakeClothChainAttrs) is one uniform value,
        // so it cannot reproduce a ribbon whose END-CAP joint fans wider than its body (primal_beast
        // back_chain body 2 / tip 4, hoodwink ear/tail/cape_front tips). Overriding extrude_sides PER JOINT
        // recovers that fan. For a chain that extrudes at all, emit EVERY joint's own width - INCLUDING an
        // explicit 0 for a joint that carries no proxies (hoodwink pendant's anchor is [0,1]; without the
        // explicit 0 it would inherit the chain-level default and gain a phantom proxy). A chain that does
        // not extrude (chainExtrudes false: meepo/dark_willow's genuine 0-proxy ropes) emits nothing and
        // stays byte-identical.
        if (chainExtrudes)
        {
            kv.Add("extrude_sides", joint.ExtrudeSides);

            // Ring geometry varies along a chain, so the chain-level defaults only fit one joint. Emit each
            // joint's own measured ring instead.
            if (joint.ExtrudeSides > 0)
            {
                kv.Add("extrude_radius", joint.ExtrudeRadius);
                kv.Add("extrude_twist", ClothExtrudeTwistBase - joint.ExtrudeTwist + joint.ExtrudeTwistTieNudge);

                // 'x' is the compiler's own default and needs no explicit key; only 5 of 120 models in
                // the reference corpus author anything else (roshan, gyro_sidegunner, hoodwink,
                // baron_of_the_minotaur_head_refit, ancient_exile_back_arcana).
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

        kv.Add("bend_spring", joint.BendSpring ? 1.0f : 0.0f);
        kv.Add("torsion_spring", joint.TorsionSpring ? 1.0f : 0.0f);
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
    // BoneChain and NOT back-solved by a proxy mesh (e.g. meepo_naruto_set's source "neck_nodes" folder:
    // individual ribbon-tie points connected only by explicit ClothSpring, not a parent-child chain - a
    // real bone with no real-bone descendants of its own never forms a BoneChain, see BuildBoneChains).
    // Mirrors MakeClothJoint's integrator recovery (goal_strength/damping/gravity/stray_radius/collision
    // radius) - without this, the bone's rods still round-trip via AddClothProxySprings (a plain
    // skeleton bone name not claimed by any ClothChain is a valid ClothSpring endpoint on its own, see
    // its remarks), but its per-node cloth paint silently reverts to compiler defaults instead of the
    // recovered original values.
    //
    // node_base_x0/x1/y0/y1: read feModel.NodeBases directly and re-declare it by NAME, the same
    // "read the compiled array, don't guess a geometric rule" fix as m_Rods/ClothSpring. Verified this
    // matters, not cosmetic: omitting it (leaving these empty, the previous behaviour) left
    // meepo_naruto_set's "neck_middle" bone (decoration_neck_r_m, the one source node with an explicit
    // node_base_x1/y1) mis-registering as a "position-driven" node (m_nFirstPositionDrivenNode moved
    // from 106/none in the original to 101, exactly this bone's block of 5) driven via a synthesized
    // m_Ropes fallback (12 ropes vs the original's 0) instead of a normal simulated node - i.e. a real
    // dynamical difference, not just a missing cosmetic orientation hint.
    static KVObject MakeClothNode(FeModel feModel, string boneName, int node, bool isStaticNode = false,
        string? elementName = null, Vector3 origin = default,
        IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        var integrator = feModel.GetIntegrator(node);
        var goalStrength = MathF.Cbrt(Math.Clamp(integrator.ForceAttraction, 0f, 1f));
        var goalDamping = FeModel.GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction);
        var strayRadius = feModel.GetStrayRadius(node);

        // A "$cloth_m{N}p{Y}" basis node must be referenced by the name OUR export's proxy split gives it,
        // not the original's own numbering (the two proxy layouts have no defined relationship); an
        // unresolvable one is omitted rather than echoed as a name the compile may reject or mis-bind.
        // Every other generated name ("$cc..." rings, "$cloth_node_..." elements) is regenerated verbatim
        // by our own chains/nodes, so the original's name stays correct as-is.
        var hasBasis = feModel.NodeBases.TryGetValue(node, out var basis);
        string BasisName(int basisNode)
        {
            if (!hasBasis || basisNode < 0 || basisNode >= feModel.CtrlNames.Length)
            {
                return string.Empty;
            }

            var basisName = feModel.CtrlNames[basisNode];
            return basisName.StartsWith("$cloth_m", StringComparison.Ordinal)
                ? proxyNodeNames?.GetValueOrDefault(basisNode) ?? string.Empty
                : basisName;
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
            ("transform_alignment", 0),
            ("node_base_y1", BasisName(basis.NodeY1)),
            ("node_base_x1", BasisName(basis.NodeX1)),
            ("node_base_y0", BasisName(basis.NodeY0)),
            ("node_base_x0", BasisName(basis.NodeX0)),
            ("lock_translation", feModel.IsLockedToParent(node)),
            ("gravity_z", integrator.Gravity / ClothSourceBaseGravity),
            ("goal_strength", goalStrength),
            ("goal_damping", goalDamping),
            ("mass", 1.0f),
            ("friction", feModel.GetNodeFriction(node)),
            ("stray_radius", strayRadius),
            ("stray_radius_relaxation_factor", 1.0f),
            ("collision_radius", feModel.GetCollisionRadius(node)),
            ("is_static_node", isStaticNode),
            ("allow_rotation", feModel.AllowsRotation(node)),
            ("super_damping", 0.0f));
    }

    // The cloth-chain joint datatable schema (per-column UI metadata + defaults), matched to the editable
    // ModelDoc source produced by the tools. The compiler uses the "default" values for any joint field not
    // explicitly written above, so this block is included verbatim to keep cloth defaults correct.
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

        // The COMPLETE version-2 attr set, captured from a vmdl saved by current ModelDoc (an incomplete
        // v1-era key list makes the v2 joint grid ignore the table and fall back to default columns).
        // Attrs with values recovered from the compiled FeModel are shown; the rest keep stock visibility.
        StringAttr("joint_name", "Joint Name", true, 1).Add("lock", true);
        StringAttr("joint_parent", "Parent Joint", false, 2);
        BoolAttr("simulate", "Simulate", true, 3, true);
        BoolAttr("allow_rotation", "Allow Rotation", false, 4, true);
        // Display names corrected against the real ClothChainAttrEditor schema (extracted via Ghidra from
        // physicsbuilder.dll's own registration code) - "Stiffness" not "Spring", and world_collision's
        // real display is "World Ground Collision". Cosmetic only (ModelDoc re-editing UI labels), no
        // effect on compiled physics - defaults/ranges already matched the real schema exactly.
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
        // Recovered per-chain from the compiled $cc proxy width (see FeModel.BuildBoneChains): a 2-wide
        // strip / N-sided tube regenerates its proxies only if the ClothChain re-declares the extrude.
        // extrudeSides 0 keeps the stock default (plain rope) so genuine 1-wide chains are byte-identical.
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
    // control nodes, the FeModel's m_CtrlName. Both are authored - nano_shadow_form's source declares the
    // Bone as "arm_upper_L" and the ClothShapeCapsule that anchors to it as parent_bone "arm_upper_l", and
    // the compiler records each verbatim because every bone lookup it does is case-insensitive. Our export
    // has one name per bone, so whichever spelling the skeleton carries is the one every DMX joint list
    // gets - and a bone the compiler registers as a control node through a blend INDEX rather than through
    // a KV name string then comes back under the skeleton's spelling instead of the cloth data's.
    //
    // Re-spelling the joints of THIS sheet only fixes that without disturbing anything else: the compiler
    // still binds each joint to the same bone (case-insensitively), the model skeleton and every other DMX
    // keep the spelling they were compiled with, and the control node lands under the name the cloth data
    // actually uses.
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
        var dmeModel = BuildDmeDagSkeleton(skeleton, out _, bonePositions: ClothRestBonePositions);
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

        // Per-vertex cloth paint layers. The names + the full layer set match a current authored cloth
        // proxy (meepo_scream qop_body_proxy.dmx): cloth_goal_strength_v2 is the modern attribute the
        // ModelDoc cloth editor actually paints (the legacy cloth_goal_strength reads as 0 in the current
        // editor), and friction/drag/ground_collision are the paint layers the editor shows - omitting them
        // is what made other cloth items "disappear". All are recovered 0..1 paint values (NOT raw compiled
        // solver numbers): the old code wrote cloth_goal_damping = flPointDamping (~6.0), 60x outside the
        // slider's 0..1 range, which is why the editor showed 0 in the text while the slider sat pegged.
        vertexData.AddIndexedStream("cloth_enable$0", proxy.ClothEnable, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", proxy.GoalStrength, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_damping$0", proxy.GoalDamping, vertexIndices);
        vertexData.AddIndexedStream("cloth_collision_radius$0", proxy.CollisionRadius, vertexIndices);
        vertexData.AddIndexedStream("cloth_ground_collision$0", proxy.GroundCollision, vertexIndices);
        vertexData.AddIndexedStream("cloth_drag$0", proxy.Drag, vertexIndices);

        // World-collision ground friction paint (compiler attribute "cloth_ground_friction", confirmed
        // against the resourcecompiler DMX importer's own attribute table). There is no
        // "cloth_world_friction" counterpart there because world friction rides the ground-collision
        // paint instead - see ProxyVertexData's GroundCollision.
        if (Array.Exists(proxy.GroundFriction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_ground_friction$0", proxy.GroundFriction, vertexIndices);
        }

        // Friction is painted only where the cloth carries any: no authored proxy in the reference corpus
        // ships the stream, and an all-zero stream is not the same input as no stream at all.
        if (Array.Exists(proxy.Friction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_friction$0", proxy.Friction, vertexIndices);
        }

        // Per-vertex gravity, painted VERBATIM: cloth_gravity$0 compiles into flGravity with no scaling
        // (measured: 0.002778 lands 0.002778, 1.0 lands 1). Without this stream the compiler defaults
        // every vertex to 360, silently discarding authored variation - dark_willow paints its hair
        // strands and paper lantern nearly weightless (flGravity=1) while the coattail is full weight
        // (360). A cloth_animation_attract$0 stream for the same integrator's out-of-range legacy
        // flAnimationVertexAttraction (15/10.5/6/5.25 on dark_willow) is IGNORED by the proxy importer (the
        // name belongs to ClothMapFilter's map list) - that field stays a legacy platform ceiling, do not
        // re-emit it.
        vertexData.AddIndexedStream("cloth_gravity$0", proxy.Gravity, vertexIndices);

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

        // cloth_drag_v2 and cloth_mass have no measurable effect on the compiled flPointDamping/
        // m_NodeInvMasses - cloth_drag (no suffix, unlike goal_strength) is already the attribute the
        // compiler reads, so they are intentionally omitted.

        // cloth_make_rods is the per-face paint gating whether the mesh importer turns a face into rods or
        // keeps it as a solve element (cloth_use_rods does not: probed on yamato's authored proxy, every
        // combination of the two, only make_rods moves the split). Painted under the ~0.5 threshold the
        // whole sheet stays faces, which is only ever right for cloth that ships a surface of its own: a
        // rod-network cloth then compiles to invented m_Tris and loses every rod (measured on a synthesized
        // sheet: 669 rods and 0 tris become 0 rods and 172 tris). So the paints go on only when the original
        // itself carries faces, and the sheet is otherwise left for the compiler to rebuild rods from.
        //
        // A sheet that ships BOTH kinds paints the split itself: 1 over the rod region, 0 over the surface,
        // which is how the original's own gradient paint compiled (see ProxyMesh.RodsDriven).
        //
        // A sheet exported with its AUTHORED faces and no rod region skips them entirely, as hand-authored
        // proxies do.
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
        // Match bone names case-INSENSITIVELY: Source treats bone names case-insensitively (the model
        // compiler matched them that way originally), and a model's compiled FeModel m_CtrlName array does
        // NOT always agree in case with its skeleton. kez ships skeleton bones CapeLeafB_0/CapeLeafC_0 but
        // stores the cloth control nodes as capeLeafB_0/capeLeafC_0 - an Ordinal lookup drops every one of
        // those influences, the affected simulated vertices end up with all-zero blend weights, and with
        // back_solve_joints on the compiler then hits "Cannot find most-bound-joint for position N in mesh
        // cloth_proxyK_shape" and ACCESS-VIOLATION crashes (its CapeLeafA_* chain, which happens to agree in
        // case, back-solved fine - which is exactly how the bug hid). OrdinalIgnoreCase resolves them.
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            boneIndexByName.TryAdd(bone.Name, bone.Index);
            boneIndexByName.TryAdd(GetExportBoneName(bone), bone.Index);
        }

        // A sheet no real bone drives ships UNSKINNED, like its hand-authored counterpart: the compiler
        // then anchors the whole sheet to a static root node it generates itself and records every vertex
        // as an m_CtrlOffsets entry hanging off that root. Skinning it to the synthetic per-vertex bones
        // binds each node directly instead, which costs both the root node and the entire offsets array.
        if (!proxy.IsFreeFloating)
        {
            // Four slots cover everything BuildChainSkinInfluences synthesises, but weights recovered
            // verbatim from a model's own offset network run to eight (abrams), and a truncated
            // influence takes its m_CtrlSoftOffsets entry with it - familiar lost exactly the 25 tail
            // entries of its 17 five-influence and 4 six-influence vertices. Widened only for the
            // vertices those recovered weights cover, and only where no fit is taken over the sheet:
            // giving a back-solving sheet more slots than its own fits need re-classifies its nodes
            // (mars gains five node-count/mass keys).
            var jointCount = 4;
            if (physAggregateData?.FeModel is { ProxyFitMatrixNodes.Count: 0 } feModel)
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
                foreach (var (boneName, weight) in proxy.SkinInfluences[v])
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

        var dmeModel = BuildDmeDagSkeleton(skeleton, out _, bonePositions: ClothRestBonePositions);
        dmeModel.Name = name;

        var (dag, vertexData) = CreateDmxDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = grid.Positions.Length;
        var identity = Enumerable.Range(0, vertexCount).ToArray();

        vertexData.AddIndexedStream("position$0", grid.Positions, identity);
        vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("texcoord$0", grid.Texcoords, identity);

        // Full paint set, matching BuildClothProxyMeshDmx - a grid missing friction/drag has nothing
        // damping its fall once goal_strength lets go, which is why an isolated cloth_grid (no
        // cloth_proxy back-solve driving it) looked like it "just falls".
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
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            boneIndexByName.TryAdd(bone.Name, bone.Index);
            boneIndexByName.TryAdd(GetExportBoneName(bone), bone.Index);
        }

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
            softbody.Add("motion_smooth_cdt", feModel.MotionSmoothCdt);
            softbodyChildren.Add(MakeClothParams(feModel, explicitMasses: true));

            var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
            clothFolder.Add("name", "cloth");
            softbodyChildren.Add(clothFolder);
            clothFolderChildren.Add(MakeImportedCloth(feModel));

            AddClothCollisionShapes(softbodyChildren, feModel);
            AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
            rootChildren.Add(softbody);
            AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
            return true;
    }

    bool EmitProxySheetClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
            // Phase 2 (preferred): the cloth sheet ships as a proxy mesh. With back_solve_joints the
            // compiler regenerates the $cloth_* sheet nodes AND back-solves the bone-chain follower
            // nodes that the sheet is skinned to - i.e. the full FeModel - so a chain whose joints
            // appear in the FeModel's own m_FitMatrices IS driven by the proxy and must not also get
            // an explicit ClothChain (double-driving). But a proxy mesh does not by itself mean EVERY
            // bone chain in the model is back-solved: a model can ship independent cloth (e.g. a
            // decorative ClothChain simulated on its own) alongside an unrelated proxy-mesh panel with
            // back_solve_joints=false and zero m_FitMatrices entries (verified on meepo_naruto_set: 4
            // ClothChains + a jaket cloth panel, m_FitMatrices empty) - such chains still need the
            // same explicit ClothChain emission Phase 1 uses below, or their joints never get
            // registered as cloth nodes at all (breaks compile: rods between them read as "Cannot find
            // Fx Bone"/"Cannot find node" since the bones were never wired into ANY cloth construct).
            // back_solve_joints must be on whenever the cloth drives real bones - not only the
            // fit-matrix case (dark_willow) but also the CtrlOffsets-only case with m_FitMatrices empty
            // (primal_beast's leg/back/neck chains), or those bones' render mesh stays frozen while the
            // proxy sim moves. DrivesRealBones is a superset of FitMatrixNodes; verified it leaves
            // willow/meepo/legion/snapfire's flag unchanged and only flips primal_beast on.
            var backSolveJoints = feModel.FitMatrixNodes.Count > 0 || feModel.DrivesRealBones;
            // A fit matrix only drives a bone THROUGH a proxy sheet when the fit is taken over that
            // sheet's vertices (see FeModel.ProxyFitMatrixNodes). A chain whose fit matrices are taken
            // over its own $cc extrude ring is the compiler's chain-internal orientation solve, so the
            // chain still has to be emitted or its whole ring goes with it (hornet's hat, dynamo's
            // coat_e, ivy's tail, wraith's coat, inferno's muddler).
            var independentChains = boneChains
                .Where(chain => !chain.Joints.Any(joint => feModel.ProxyFitMatrixNodes.Contains(joint.Node)))
                .ToList();

            // The bones an independent ClothChain already simulates and drives on its own. The compiler
            // regenerates those bones' proxy nodes from the chain, so a reconstructed proxy mesh that
            // ONLY re-drives such bones is redundant - and back-solving a tiny one crashes the compiler:
            // kez's capeLeafA/B/C are 3-vertex leaf strips, each skinned across a 3-joint chain (one
            // vertex "most-bound" per joint), which is degenerate for a back-solved fit and makes
            // resourcecompiler AV in its most-bound-joint search ("Cannot find most-bound-joint for
            // position N in mesh cloth_proxyK_shape" -> accessviolation). back_solve is therefore
            // decided PER PROXY below: on only when the proxy drives a real bone no ClothChain covers
            // (primal_beast's leg/back/neck proxies drive chain-less bones, so they keep it; dark_willow's
            // proxy drives fit-matrix Coattail/HairStrand bones, which are excluded from independentChains
            // above and so are NOT treated as chain-driven - its back_solve stays on and byte-exact).
            // Names compared case-insensitively (compiled control-node vs skeleton casing can disagree -
            // the same kez quirk that also broke the proxy skin resolution, see BuildClothProxyMeshDmx).
            var chainDrivenBones = new HashSet<string>(
                independentChains.SelectMany(static chain => chain.Joints).Select(joint => feModel.CtrlNames[joint.Node]),
                StringComparer.OrdinalIgnoreCase);

            // A sheet is also skinned to the body bones it merely hangs off (warden's skirt to
            // pelvis/leg_upper_*, which the cloth uses only as collision anchors). The bones a proxy
            // back-solves are the ones the original compile left position-driven, so only those count
            // as evidence below.
            var positionDrivenBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = feModel.FirstPositionDrivenNode; i < feModel.CtrlNames.Length; i++)
            {
                if (!FeModel.IsProxyNodeName(feModel.CtrlNames[i]))
                {
                    positionDrivenBones.Add(feModel.CtrlNames[i]);
                }
            }

            // The bone a vertex hangs off in the compiled hierarchy, which is what its exported
            // influences were derived from before the authored weights were recovered. Recovering them
            // widens the list to every bone the author painted, and a body bone carrying a few percent
            // of a vertex is not the sheet driving it - reading the wider list turns back_solve_joints
            // on for sheets the original never back-solved, which then compile fit matrices the
            // original has none of (frostivus2018_dp_mistress_of_the_blizzard_armor: 4 against 0).
            // Only a model with no fit matrix at all reads the anchor; everywhere else the influence
            // list stands, because that is the set the kez/meepo/primal_beast tuning was measured
            // against. Widening the test to ProxyFitMatrixNodes looks like the matching scope but is
            // not: it takes back_solve away from a chain-fit model that legitimately has it
            // (spacefrog_hunter_armor 10 -> 12, gaining m_NodeInvMasses and m_nStaticNodes).
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

            // add_bones_to_render_mesh is recoverable, not a guess: the compiler adds a model-space
            // skeleton "Bone" per cloth PROXY vertex, named with the RAW "$cloth_m{proxy}p{vertex}"
            // control-node name (GetExportBoneName only sanitizes '$'->'_' for OUR OWN vmdl text
            // output - the compiled skeleton itself still carries the literal '$' name, verified
            // directly in meepo's own MDAT dump), when the render mesh is actually skinned directly to
            // individual proxy nodes - which only happens when the original was compiled with this
            // flag on.
            //
            // `Bone.IsProceduralCloth` ALONE is NOT sufficient to detect this - it's `Cloth |
            // Procedural`, a broad combination set on ANY procedurally-driven cloth bone, including
            // REAL back-solved bones with real names (dark_willow's own Coattail/HairStrand bones -
            // simulated real bones, not synthetic proxy vertices - carry this same flag combination).
            // Checking the flag alone makes `addBonesToRenderMesh` wrongly true for dark_willow,
            // regressing its m_NodeBases from an exact 1/1 to 50. The correct, narrow signal is a bone
            // that is BOTH flagged procedural-cloth AND named like the synthetic proxy convention
            // ("$cloth_m{proxy}p{vertex}", matching FeModel.IsProxyNodeName's own '$' check).
            var addBonesToRenderMesh = model?.Skeleton.Bones.Any(static b =>
                b.IsProceduralCloth && FeModel.IsProxyNodeName(b.Name)) ?? false;

            var (clothProxyList, clothProxyChildren) = MakeListNode("ClothProxyMeshList");
            foreach (var proxyFile in ClothProxyMeshesToExtract)
            {
                // The threshold is derived from the ORIGINAL's own compiled fit data (see
                // FeModel.RecoveredBackSolveThreshold): with the authored weights recovered verbatim,
                // the sub-threshold weights the original's compile dropped from its fit ranges must be
                // dropped by ours too, or they show up as extra m_FitWeights entries and shift the fits.
                // Per-vertex gravity rides the cloth_gravity$0 paint in the proxy DMX instead of any
                // KV field here (the cloth_gravity_scale KV was tested and does not reach flGravity).
                var proxyBackSolve = backSolveJoints && ProxyDrivesUnchainedBone(proxyFile.Proxy);
                var proxyNode = MakeClothProxyMeshFile(proxyFile.Name, proxyFile.FileName, proxyBackSolve, driveMeshes: proxyBackSolve, addBonesToRenderMesh,
                    backSolveInfluenceThreshold: feModel.RecoveredBackSolveThreshold ?? 0.0f);

                // A selection covering exactly the sheet's simulated nodes is the m_VertexMaps entry a
                // ClothVertexMap around the sheet compiles to - the grouping the "Add Cloth Vertex Map"
                // wizard builds. Only a PROXY MESH may be wrapped this way: the same container around
                // free ClothNodes that a ClothSpring references makes the compiler access-violate.
                if (feModel.GetProxyVertexMapName(proxyFile.Proxy) is { } proxyVertexMap)
                {
                    var (mapNode, mapChildren) = MakeListNode("ClothVertexMap");
                    mapNode.Add("name", proxyVertexMap);
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
            softbody.Add("motion_smooth_cdt", feModel.MotionSmoothCdt);
            var surfaceRods = ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract,
                out var generatesBendRods, out var generatesBendOnlyRods, out var addCurvature);
            softbodyChildren.Add(MakeClothParams(feModel, generatesBendRods, generatesBendOnlyRods,
                addCurvature));

            // Simulated real bones that are NEITHER back-solved NOR part of any multi-joint BoneChain -
            // standalone goal-attraction points wired together only by ClothSpring (see MakeClothNode
            // remarks). A real bone with no real-bone descendants of its own never forms a BoneChain
            // (BuildBoneChains skips it), so these would otherwise carry correct rods/connectivity
            // (a plain bone name is a valid ClothSpring endpoint) but compiler-default paint instead
            // of the recovered original goal_strength/damping/gravity/stray_radius.
            var chainNodes = boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();
            var independentChainNodes = independentChains
                .SelectMany(static chain => chain.Joints)
                .Select(static joint => joint.Node)
                .ToHashSet();
            var loneClothNodes = new List<(string Name, int Node)>();

            // Real, STATIC (invMass == 0) bones the compiler still registers as FeModel control nodes
            // purely for orientation bookkeeping - no rods, no integrator role, no fit-matrix, no
            // ClothChain/capsule/sphere authoring of their own (e.g. meepo_naruto_set's "head_0": a
            // plain real ancestor of a ClothChain root two hops up, not referenced by anything cloth
            // explicitly authors). Verified this is exactly what the compiled Skeleton's plain `Cloth`
            // bone flag (Bone.IsClothControlNode, NOT the stricter IsProceduralCloth) predicts, with
            // zero false positives/negatives across meepo_naruto_set/dark_willow/legion_commander -
            // emit one as a static ClothNode (mirrors MakeClothNode, is_static_node=true) for every
            // Cloth-flagged real bone not already covered by a chain/fit-matrix/shape/lone-node above.
            // A capsule/sphere parent bone is excluded (direct-parent-only): the compiler auto-walks a
            // collision bone's ancestor chain and registers them itself, so an explicit ClothNode there
            // is redundant.
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
            foreach (var (_, _, proxy) in ClothProxyMeshesToExtract)
            {
                foreach (var vertex in SurvivingProxyVertices(proxy))
                {
                    proxyRegisteredNodes.Add(proxy.NodeIndices[vertex]);
                    foreach (var (bone, _) in proxy.SkinInfluences[vertex])
                    {
                        proxySkinnedBones.Add(bone);
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

                if (feModel.NodeInvMasses[node] != 0f)
                {
                    loneClothNodes.Add((name, node));
                }
                // A bone an emitted proxy vertex is skinned to is registered by the sheet, whose
                // hierarchy spans only the skinned bones; an extra ClothNode on it instead parents it
                // onto its nearest control-node ancestor whatever registered that (familiar's and
                // magician's spine_0 are roots in the original and children of the collision bone
                // pelvis with the extra node). Skipped only where the original records the bone as a
                // hierarchy ROOT, which is the whole of the damage the extra declaration does, and
                // only on a model with no fit matrix at all: elsewhere the exported influences are
                // synthesised rather than recovered, so they are no evidence that the compiler
                // registers the same bone the same way (abaddon and monkey_king_arcana keep their
                // node counts but lose the static split without the declaration).
                else if (!shapeParentBones.Contains(name)
                    && !(feModel.FitMatrixNodes.Count == 0 && proxySkinnedBones.Contains(name)
                        && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0)
                    && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
                {
                    leftoverStaticNodes.Add((name, node));
                }
            }

            var proxyNodeNameMap = BuildProxyNodeNameMap(ClothProxyMeshesToExtract);

            if (independentChains.Count > 0 || loneClothNodes.Count > 0 || leftoverStaticNodes.Count > 0
                || unregisteredNodes.Count > 0 || unregisteredFreeNodes.Count > 0)
            {
                var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
                clothFolder.Add("name", "cloth");
                softbodyChildren.Add(clothFolder);

                foreach (var boneChain in independentChains)
                {
                    clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain));
                }

                // A ClothNode carries no vertex_map of its own, so the selections covering one are
                // restored by parenting it under a ClothVertexMap instead - the same grouping the
                // "Add Cloth Vertex Map" wizard builds. Only a node belonging to exactly one
                // selection can be grouped this way, a node being a child of one parent.
                var vertexMapGroups = new Dictionary<string, KVObject>(StringComparer.Ordinal);

                KVObject FolderFor(int node)
                {
                    var maps = feModel.GetVertexMapNames(node);
                    if (maps is null || maps.Contains(',', StringComparison.Ordinal))
                    {
                        return clothFolderChildren;
                    }

                    if (!vertexMapGroups.TryGetValue(maps, out var group))
                    {
                        var (mapNode, mapChildren) = MakeListNode("ClothVertexMap");
                        mapNode.Add("name", maps);
                        clothFolderChildren.Add(mapNode);
                        vertexMapGroups[maps] = group = mapChildren;
                    }

                    return group;
                }

                foreach (var (name, node) in loneClothNodes)
                {
                    FolderFor(node).Add(MakeClothNode(feModel, name, node, proxyNodeNames: proxyNodeNameMap));
                }

                foreach (var (name, node) in leftoverStaticNodes)
                {
                    FolderFor(node).Add(MakeClothNode(feModel, name, node, isStaticNode: true,
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
            }

            var authoredClothNodes = loneClothNodes.Concat(leftoverStaticNodes).Concat(unregisteredNodes)
                .Select(static entry => entry.Node)
                .ToHashSet();
            AddClothProxySprings(softbodyChildren, feModel, ClothProxyMeshesToExtract, independentChainNodes,
                authoredClothNodes, freeClothNodeNames, surfaceRods, proxyNodeNameMap);
            AddClothCollisionShapes(softbodyChildren, feModel);
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
            softbody.Add("motion_smooth_cdt", feModel.MotionSmoothCdt);
            softbodyChildren.Add(MakeClothParams(feModel,
                generatesBendRods: feModel.HasChainStiffnessRods(boneChains),
                generatesBendOnlyRods: feModel.HasChainBendOnlyRods(boneChains)));
            var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
            clothFolder.Add("name", "cloth");
            softbodyChildren.Add(clothFolder);

            foreach (var boneChain in boneChains)
            {
                clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain));
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

            AddClothSourceSprings(softbodyChildren, feModel, boneChains);
            AddClothChainSurplusRods(softbodyChildren, feModel, boneChains);

            var chainCoveredNodes = boneChains.SelectMany(static chain => chain.Joints)
                .Select(static joint => joint.Node)
                .ToHashSet();
            AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel,
                chainCoveredNodes, emitBareStatics: false, out var freeNodeMaps);

            AddClothCollisionShapes(softbodyChildren, feModel);
            var availableMaps = AvailableVertexMaps(feModel, boneChains);
            availableMaps.UnionWith(freeNodeMaps);
            AddClothEffects(softbodyChildren, feModel, availableMaps);
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
            softbody.Add("motion_smooth_cdt", feModel.MotionSmoothCdt);
            softbodyChildren.Add(MakeClothParams(feModel));
            var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
            clothFolder.Add("name", "cloth");
            softbodyChildren.Add(clothFolder);

            if (AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel,
                [], emitBareStatics: true, out var freeNodeMaps) > 0)
            {
                AddClothCollisionShapes(softbodyChildren, feModel);
                var availableMaps = AvailableVertexMaps(feModel, boneChains);
                availableMaps.UnionWith(freeNodeMaps);
                AddClothEffects(softbodyChildren, feModel, availableMaps);
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
