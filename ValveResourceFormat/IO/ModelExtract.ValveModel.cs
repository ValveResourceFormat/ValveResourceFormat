using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    #region Bone Constraints
    static string? RemapBoneConstraintClassname(string className)
    {
        return className switch
        {
            "CTiltTwistConstraint" => "AnimConstraintTiltTwist",
            "CTwistConstraint" => "AnimConstraintTwist",
            "CAimConstraint" => "AnimConstraintAim",
            "COrientConstraint" => "AnimConstraintOrient",
            "CPointConstraint" => "AnimConstraintPoint",
            "CParentConstraint" => "AnimConstraintParent",
            "CMorphConstraint" => "AnimConstraintMorph",
            "CBoneConstraintPoseSpaceBone" => "AnimConstraintPoseSpaceBone",
            "CBoneConstraintPoseSpaceMorph" => "AnimConstraintPoseSpaceMorph",
            "CBoneConstraintDotToMorph" => "AnimConstraintDotToMorph",
            _ => null
        };
    }

    static void AddBoneConstraintProperty<T>(KVObject sourceObject, KVObject targetObject, string sourceName, string targetName)
    {
        if (sourceObject.ContainsKey(sourceName))
        {
            if (typeof(T) == typeof(Quaternion))
            {
                var value = sourceObject.GetFloatArray(sourceName);
                var rot = new Quaternion(value[0], value[1], value[2], value[3]);
                var angles = ToEulerAngles(rot);
                targetObject.Add(targetName, ToKVArray(angles));
            }
            else if (typeof(T) == typeof(Vector3))
            {
                var value = sourceObject.GetFloatArray(sourceName);
                var pos = new Vector3(value[0], value[1], value[2]);
                targetObject.Add(targetName, ToKVArray(pos));
            }
            else
            {
                targetObject.Add(targetName, sourceObject[sourceName]);
            }
        }
    }

    static KVObject? ProcessBoneConstraintTarget(KVObject target)
    {
        var isAttachment = target.GetBooleanProperty("m_bIsAttachment");
        var targetHash = target.GetUInt32Property("m_nBoneHash");
        if (!StringToken.InvertedTable.TryGetValue(targetHash, out var targetName))
        {
#if DEBUG
            Console.WriteLine($"Couldn't find name of {(isAttachment ? "attachment" : "bone")} for bone constraint: {targetHash}");
#endif
            return null;
        }

        KVObject node;
        if (isAttachment)
        {
            node = MakeNode("AnimConstraintAttachmentInput", ("parent_attachment", targetName));
        }
        else
        {
            node = MakeNode("AnimConstraintBoneInput", ("parent_bone", targetName));
        }

        AddBoneConstraintProperty<double>(target, node, "m_flWeight", "weight");
        AddBoneConstraintProperty<Vector3>(target, node, "m_vOffset", "relative_origin");
        AddBoneConstraintProperty<Quaternion>(target, node, "m_qOffset", "relative_angles");
        return node;
    }

    static KVObject? ProcessBoneConstraintSlave(KVObject slave)
    {
        var boneHash = slave.GetUInt32Property("m_nBoneHash");
        if (!StringToken.InvertedTable.TryGetValue(boneHash, out var boneName))
        {
#if DEBUG
            Console.WriteLine($"Couldn't find name of bone for bone constraint: {boneHash}");
#endif
            return null;
        }

        var node = MakeNode("AnimConstraintSlave", ("parent_bone", boneName));
        AddBoneConstraintProperty<double>(slave, node, "m_flWeight", "weight");
        AddBoneConstraintProperty<Vector3>(slave, node, "m_vBasePosition", "relative_origin");
        AddBoneConstraintProperty<Quaternion>(slave, node, "m_qBaseOrientation", "relative_angles");
        return node;
    }

    static void ProcessBoneConstraintChildren(KVObject boneConstraint, KVObject node)
    {
        var targets = boneConstraint.GetArray("m_targets")
                                    .Select(p => ProcessBoneConstraintTarget(p))
                                    .OfType<KVObject>();

        IEnumerable<KVObject> children;
        if (node.GetStringProperty("_class") == "AnimConstraintParent")
        {
            //Parent constraints only have a single slave and it's not a child node in the .vmdl
            children = targets;

            var constrainedBoneData = boneConstraint.GetArray("m_slaves")[0];
            AddBoneConstraintProperty<double>(constrainedBoneData, node, "m_flWeight", "weight");
            AddBoneConstraintProperty<Vector3>(constrainedBoneData, node, "m_vBasePosition", "translation_offset");

            //Order of angles is different for some reason
            var rotArray = constrainedBoneData.GetFloatArray("m_qBaseOrientation");
            var rot = new Quaternion(rotArray[0], rotArray[1], rotArray[2], rotArray[3]);
            var angles = ToEulerAngles(rot);
            angles = new Vector3(angles.Z, angles.X, angles.Y);
            node.Add("rotation_offset_xyz", ToKVArray(angles));
        }
        else
        {
            var slaves = boneConstraint.GetArray("m_slaves")
                                        .Select(p => ProcessBoneConstraintSlave(p))
                                        .OfType<KVObject>();

            children = slaves.Concat(targets);
        }

        var childrenKV = KVObject.Array();
        foreach (var child in children)
        {
            childrenKV.Add(child);
        }
        node.Add("children", childrenKV);
    }

    static KVObject? ProcessBoneConstraint(KVObject? boneConstraint)
    {
        if (boneConstraint == null) //ModelDoc will compile constraints as null if it considers them invalid
        {
            return null;
        }

        var className = boneConstraint.GetStringProperty("_class");
        var targetClassName = RemapBoneConstraintClassname(className);
        if (targetClassName == null)
        {
#if DEBUG
            Console.WriteLine($"Skipping unknown bone constraint type: {className}");
#endif
            return null;
        }

        var node = MakeNode(targetClassName);

        // These constraints are stored the same way in the .vmdl and the compiled model
        if (targetClassName is "AnimConstraintPoseSpaceBone"
                            or "AnimConstraintPoseSpaceMorph"
                            or "AnimConstraintDotToMorph")
        {

            return MakeNode(targetClassName, boneConstraint);
        }

        ProcessBoneConstraintChildren(boneConstraint, node);

        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nTargetAxis", "input_axis");
        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nSlaveAxis", "slave_axis");
        AddBoneConstraintProperty<Quaternion>(boneConstraint, node, "m_qAimOffset", "aim_offset");
        AddBoneConstraintProperty<Vector3>(boneConstraint, node, "m_vUpVector", "up_vector");
        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nUpType", "up_type");
        AddBoneConstraintProperty<Quaternion>(boneConstraint, node, "m_qParentBindRotation", "parent_bind_rotation");
        AddBoneConstraintProperty<Quaternion>(boneConstraint, node, "m_qChildBindRotation", "child_bind_rotation");
        AddBoneConstraintProperty<bool>(boneConstraint, node, "m_bInverse", "inverse");
        AddBoneConstraintProperty<string>(boneConstraint, node, "m_sTargetMorph", "target_morph_control");
        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nSlaveChannel", "slave_channel");
        AddBoneConstraintProperty<double>(boneConstraint, node, "m_flMin", "min");
        AddBoneConstraintProperty<double>(boneConstraint, node, "m_flMax", "max");

        return node;
    }

    KVObject ExtractBoneConstraints(IReadOnlyList<KVObject> boneConstraintsList)
    {
        Debug.Assert(model is not null);

        var stringTokenKeys = model.Skeleton.Bones.Select(b => b.Name);
        if (RenderMeshesToExtract.Count > 0)
        {
            var mesh = RenderMeshesToExtract.First().Mesh;
            stringTokenKeys = stringTokenKeys.Concat(mesh.Attachments.Keys);
        }

        StringToken.Store(stringTokenKeys);

        var childrenKV = KVObject.Array();

        foreach (var boneConstraint in boneConstraintsList)
        {
            var constraint = ProcessBoneConstraint(boneConstraint);
            if (constraint != null)
            {
                childrenKV.Add(constraint);
            }
        }

        var constraintListNode = MakeNode("AnimConstraintList",
            ("children", childrenKV)
        );

        return constraintListNode;
    }
    #endregion

    /// <summary>
    /// Converts a quaternion to Euler angles in degrees.
    /// </summary>
    public static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // pitch / x
        var sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
        {
            angles.X = MathF.CopySign(MathF.PI / 2, sinp);
        }
        else
        {
            angles.X = MathF.Asin(sinp);
        }

        // yaw / y
        var siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        var cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Y = MathF.Atan2(siny_cosp, cosy_cosp);

        // roll / z
        var sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        var cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.Z = MathF.Atan2(sinr_cosp, cosr_cosp);

        return Vector3.RadiansToDegrees(angles);
    }

    static void AddBonesRecursive(IEnumerable<Bone> bones, KVObject parent)
    {
        foreach (var bone in bones)
        {
            var boneDefinitionNode = MakeNode(
                "Bone",
                ("name", GetExportBoneName(bone)),
                ("origin", ToKVArray(bone.Position)),
                ("angles", ToKVArray(ToEulerAngles(bone.Angle))),
                ("do_not_discard", true)
            );

            parent.Add(boneDefinitionNode);

            if (bone.Children.Count > 0)
            {
                var childBones = KVObject.Array();
                boneDefinitionNode.Add("children", childBones);
                AddBonesRecursive(bone.Children, childBones);
            }
        }
    }

    #region Cloth (ClothChain)

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

    // Global cloth solver parameters, populated from the FeModel scalars. Field names match the compiled
    // ClothParams source node (e.g. siren_legs.vmdl). Boolean/style fields use the modern Source 2 defaults
    // (the originals are not recoverable from the compiled FeModel); the compiler re-derives the rest.
    static KVObject MakeClothParams(FeModel fe)
    {
        return MakeNode("ClothParams",
            ("default_stretch", fe.DefaultSurfaceStretch),
            // Mirrors default_stretch's own recovery (fe.DefaultSurfaceStretch <- m_flDefaultSurfaceStretch):
            // additional_shear_stretch reads the already-parsed-but-previously-unused m_flDefaultThreadStretch
            // instead of a hardcoded 0 - both dark_willow and meepo_naruto_set ship 0.0 for this field so
            // there's no live signal to verify a nonzero case against yet, but the old hardcoded 0 silently
            // discarded a real value on any model that DOES ship a nonzero m_flDefaultThreadStretch.
            ("additional_shear_stretch", fe.DefaultThreadStretch),
            ("extra_iterations", fe.ExtraIterations),
            ("extra_goal_iterations", fe.ExtraGoalIterations),
            ("extra_pressure_iterations", fe.ExtraPressureIterations),
            ("goal_strength_bias", 0.0f),
            ("default_gravity_scale", fe.DefaultGravityScale),
            ("default_vel_air_drag", fe.DefaultVelAirDrag),
            ("default_exp_air_drag", fe.DefaultExpAirDrag),
            ("velocity_smooth_rate", 0.0f),
            ("internal_pressure", fe.InternalPressure),
            ("windage", fe.Windage),
            ("wind_drag", fe.WindDrag),
            ("velocity_smooth_iterations", 0),
            ("default_ground_friction", 0.0f),
            ("default_world_collision_penetration", 0.0f),
            ("add_world_collision_radius", fe.AddWorldCollisionRadius),
            ("local_force", fe.LocalForce),
            ("local_rotation", fe.LocalRotation),
            ("add_curvature", 0.0f),
            ("quad_bend_tolerance", 0.05f),
            ("local_drag1", fe.LocalDrag1),
            // follow_the_lead=false matches the original compiled node flags (bit 5 clear on shipped Dota cloth).
            ("follow_the_lead", false),
            ("use_per_node_local_force_and_rotation", false),
            ("uninertial_rods", false),
            ("explicit_masses", false),
            ("unitless_damping", true),
            ("force_world_collision_on_all_nodes", false),
            ("new_style", true),
            ("can_collide_with_world_hulls", false),
            ("can_collide_with_world_meshes", false),
            ("can_collide_with_world_capsule_and_spheres", false),
            ("add_stiffness_rods", false),
            ("rigid_edge_hinges", false),
            ("add_bend_only_rods", false),
            ("immovable", false));
    }

    const float ClothSourceBaseGravity = FeModel.ClothSourceBaseGravity;

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
    static KVObject MakeClothSpring(string name, string n0, string n1, float minLength, float maxLength)
        => MakeNode("ClothSpring",
            ("name", name),
            ("cloth_node_0", n0),
            ("cloth_node_1", n1),
            ("stiffness", 1.0f),
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
    static void AddClothProxySprings(KVObject softbodyChildren, FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, HashSet<int> chainJointNodes)
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

        var proxyNodeNames = new Dictionary<int, string>();
        for (var proxyIndex = 0; proxyIndex < proxies.Count; proxyIndex++)
        {
            var proxy = proxies[proxyIndex].Proxy;
            var nodeIndices = proxy.NodeIndices;

            // Only proxy vertices actually referenced by at least one face are registered as FeModel
            // control nodes by the compiler - an unfaced vertex is silently dropped (see
            // TriangulateDominantPlane remarks: "a vertex not referenced by ANY face is NOT registered as
            // a valid FeModel control node"). A rod-only island whose triangulation leaves some vertices
            // unfaced (e.g. snapfire's two proxy panels) would otherwise get ClothSprings pointing at
            // "$cloth_mXpY" bones the compile never creates ("Cannot find node") - a HARD compile failure.
            // Map only the faced vertices; the null-guard in the rod loop then drops any rod touching an
            // unfaced one. dark_willow/meepo/legion fully triangulate, so none of their rods are affected.
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

        string? ResolveName(int node)
            => FeModel.IsProxyNodeName(feModel.CtrlNames[node])
                ? proxyNodeNames.GetValueOrDefault(node)
                : feModel.CtrlNames[node];

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
    }

    static KVObject MakeClothShapeCapsule(FeModel.CollisionCapsule capsule)
    {
        var node = MakeNode("ClothShapeCapsule",
            ("name", (capsule.ParentBone ?? "cloth") + "_clothCapsule"),
            ("parent_bone", capsule.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, capsule.CollisionMask);
        node.Add("cloth_collision_priority", 0);
        node.Add("vertex_map", "");
        node.Add("inverted_collision", false);
        node.Add("planarize", false);
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
        var joints = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            joints.Add(MakeClothJoint(feModel, joint, chainExtrudes: chain.ExtrudeSides >= 1));
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs(chain.ExtrudeSides, chain.ExtrudeRadius));
        chainData.Add("selection", KVObject.Array());
        // Version 2 is the current ModelDoc chain format (v1 shows an "Update version 1->2" banner);
        // the compiled FeModel is identical for both (verified byte-level on the PHYS block).
        chainData.Add("version", 2);

        return MakeNode("ClothChain",
            ("name", chain.RootBone),
            ("root_bone", chain.RootBone),
            ("chain", chainData));
    }

    static KVObject MakeClothJoint(FeModel feModel, FeModel.BoneChainJoint joint, bool chainExtrudes = false)
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
        kv.Add("goal_strength", goalStrength);
        kv.Add("goal_damping", FeModel.GoalDampingFromAttraction(goalStrength, integrator.VertexAttraction - integrator.ForceAttraction));
        kv.Add("gravity_z", integrator.Gravity / ClothSourceBaseGravity);

        // Everything below is recovered per node; NO invented stiffness defaults. Emitting non-zero
        // twist_relax / stiff_hinge / motion_bias makes the compiler build a Twist/KelagerBend constraint
        // network instead of the plain ropes some chains compile to (dark_carnival original: 5 ropes,
        // 0 twists - twist_relax must stay 0 there). But this is NOT a universal default: meepo_naruto_set's
        // own source authors twist_relax=1.0 uniformly on every joint of its 4 real ClothChains, and its
        // compiled FeModel has a real m_Twists network (24 entries, 0 ropes) to match - hardcoding 0 there
        // instead produces a bogus 4-node "Rope" fallback constraint per chain (m_Ropes, entirely absent
        // from the original). Recover per-joint from the ORIGINAL's own m_Twists participation
        // (FeModel.TwistNodes) rather than guessing a single constant for every model.
        kv.Add("twist_relax", feModel.TwistNodes.Contains(joint.Node) ? 1.0f : 0.0f);

        // World collision membership + radius (m_WorldCollisionNodes / m_NodeCollisionRadii); without
        // them the recompiled tail/leg chains clip into the ground.
        kv.Add("world_collision", feModel.IsWorldCollisionNode(joint.Node));
        kv.Add("collision_radius", feModel.GetCollisionRadius(joint.Node));

        // Stray radius (m_AnimStrayRadii): the max distance the node may stray from its animated position.
        kv.Add("stray_radius", feModel.GetStrayRadius(joint.Node));

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
        }

        kv.Add("extra_iterations", 0);
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
    static KVObject MakeClothNode(FeModel feModel, string boneName, int node, bool isStaticNode = false)
    {
        var integrator = feModel.GetIntegrator(node);
        var goalStrength = MathF.Cbrt(Math.Clamp(integrator.ForceAttraction, 0f, 1f));
        var goalDamping = FeModel.GoalDampingFromAttraction(goalStrength, integrator.VertexAttraction - integrator.ForceAttraction);
        var strayRadius = feModel.GetStrayRadius(node);

        var hasBasis = feModel.NodeBases.TryGetValue(node, out var basis);
        string BasisName(int basisNode) => hasBasis ? feModel.CtrlNames[basisNode] : string.Empty;

        return MakeNode("ClothNode",
            ("name", boneName),
            ("origin", ToKVArray(Vector3.Zero)),
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
            ("lock_translation", false),
            ("gravity_z", integrator.Gravity / ClothSourceBaseGravity),
            ("goal_strength", goalStrength),
            ("goal_damping", goalDamping),
            ("mass", 1.0f),
            ("friction", 0.0f),
            ("stray_radius", strayRadius),
            ("stray_radius_relaxation_factor", 1.0f),
            ("collision_radius", feModel.GetCollisionRadius(node)),
            ("is_static_node", isStaticNode),
            ("allow_rotation", false),
            ("super_damping", 0.0f));
    }

    // The cloth-chain joint datatable schema (per-column UI metadata + defaults), matched to the editable
    // ModelDoc source produced by the tools. The compiler uses the "default" values for any joint field not
    // explicitly written above, so this block is included verbatim to keep cloth defaults correct.
    static KVObject MakeClothChainAttrs(int extrudeSides = 0, float extrudeRadius = 0f)
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
        FloatAttr("extrude_twist", "Extrude Twist", false, 33, 0.0f);
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

    #endregion

    // Decompiles a compiled flex rule (RPN op program) back into the expression string ModelDoc's
    // MorphRule node accepts (verified: the compiler parses "max( x, 0 ) * 0.5" back into the exact
    // FETCH/CONST/MAX/MUL ops). Returns null for programs using ops with no expression equivalent.
    static string? DecompileFlexRule(ValveResourceFormat.ResourceTypes.ModelFlex.FlexRule rule,
        ValveResourceFormat.ResourceTypes.ModelFlex.FlexController[] controllers)
    {
        var stack = new Stack<string>();

        bool Binary(string format)
        {
            if (stack.Count < 2)
            {
                return false;
            }

            var b = stack.Pop();
            var a = stack.Pop();
            stack.Push(string.Format(CultureInfo.InvariantCulture, format, a, b));
            return true;
        }

        foreach (var op in rule.FlexOps)
        {
            var handled = op switch
            {
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpFetch1 fetch
                    when fetch.ControllerId >= 0 && fetch.ControllerId < controllers.Length
                    => PushValue(controllers[fetch.ControllerId].Name),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpConst c
                    => PushValue(c.Data.ToString(CultureInfo.InvariantCulture)),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpAdd => Binary("( {0} + {1} )"),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpSub => Binary("( {0} - {1} )"),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpMul => Binary("( {0} * {1} )"),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpDiv => Binary("( {0} / {1} )"),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpMin => Binary("min( {0}, {1} )"),
                ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps.FlexOpMax => Binary("max( {0}, {1} )"),
                _ => false,
            };

            if (!handled)
            {
                return null;
            }
        }

        return stack.Count == 1 ? stack.Pop() : null;

        bool PushValue(string value)
        {
            stack.Push(value);
            return true;
        }
    }

    static KVObject ProcessAnimationAutoLayer(Animation animation, AnimationAutoLayer autoLayer, string[] localSequenceNameArray, string[] poseParamNames)
    {
        var animName = localSequenceNameArray[autoLayer.LocalReference];

        if (autoLayer.Pose == true)
        {
            var poseParam = poseParamNames[autoLayer.LocalPose];
            return MakeNode("AnimBlendLayerPoseParam", [
                ("anim_name", animName),
                ("spline", autoLayer.Spline),
                ("xfade", autoLayer.XFade),
                ("no_blend", autoLayer.NoBlend),
                ("local_space", autoLayer.Local),
                ("pose_param_name", poseParam),
                ("start_cycle", autoLayer.Start),
                ("peak_cycle", autoLayer.Peak),
                ("tail_cycle", autoLayer.Tail),
                ("end_cycle", autoLayer.End),
            ]);
        }
        else if (autoLayer.LocalPose != -1)
        {
            return MakeNode("AnimAddLayer", [
                ("anim_name", animName),
            ]);
        }
        else
        {
            return MakeNode("AnimBlendLayer", [
                ("anim_name", animName),
                ("spline", autoLayer.Spline),
                ("xfade", autoLayer.XFade),
                ("no_blend", autoLayer.NoBlend),
                ("local_space", autoLayer.Local),
                ("start_frame", (int)(autoLayer.Start * animation.FrameCount)),
                ("peak_frame", (int)(autoLayer.Peak * animation.FrameCount)),
                ("tail_frame", (int)(autoLayer.Tail * animation.FrameCount)),
                ("end_frame", (int)(autoLayer.End * animation.FrameCount)),
            ]);
        }
    }

    /// <summary>
    /// Converts the model to Valve model format as a string.
    /// </summary>
    public string ToValveModel()
    {
        Debug.Assert(model is not null, "model should not be null when converting to ValveModel");

        var kv = KVObject.Collection();

        var root = MakeListNode("RootNode");
        kv.Add("rootNode", root.Node);

        Lazy<KVObject> MakeLazyList(string className)
        {
            return new Lazy<KVObject>(() =>
            {
                var list = MakeListNode(className);
                root.Children.Add(list.Node);

                return list.Children;
            });
        }

        var materialGroupList = MakeLazyList("MaterialGroupList");
        var renderMeshList = MakeLazyList("RenderMeshList");
        var bodyGroupList = MakeLazyList("BodyGroupList");
        var lodGroupList = MakeLazyList("LODGroupList");
        var animationList = MakeLazyList("AnimationList");
        var physicsShapeList = MakeLazyList("PhysicsShapeList");
        var attachmentList = MakeLazyList("AttachmentList");
        var skeleton = MakeLazyList("Skeleton");
        var modelModifierList = MakeLazyList("ModelModifierList");
        var weightLists = MakeLazyList("WeightListList");
        var hitboxSetList = MakeLazyList("HitboxSetList");
        var poseParamList = MakeLazyList("PoseParamList");

        var nmskelList = MakeLazyList("NmSkeletonList");
        var animGraph2List = MakeLazyList("AnimGraph2List");

        var boneMarkupList = MakeListNode("BoneMarkupList");
        root.Children.Add(boneMarkupList.Node);
        boneMarkupList.Node.Add("bone_cull_type", "None");

        if (RenderMeshesToExtract.Count != 0)
        {
            foreach (var renderMesh in RenderMeshesToExtract)
            {
                var renderMeshFile = MakeNode(
                    "RenderMeshFile",
                    ("name", renderMesh.Name),
                    ("filename", renderMesh.FileName)
                );

                if (renderMesh.ImportFilter != default)
                {
                    var importFilter = KVObject.Collection();
                    {
                        importFilter.Add("exclude_by_default", renderMesh.ImportFilter.ExcludeByDefault);
                        importFilter.Add("exception_list", MakeArray([.. renderMesh.ImportFilter.Filter.Select(s => (KVObject)s)]));
                    }

                    renderMeshFile.Add("import_filter", importFilter);
                }

                renderMeshList.Value.Add(renderMeshFile);
            }

            {
                // Mesh/Body Groups
                var meshGroups = model.Data.GetArray<string>("m_meshGroups");
                var meshGroupMasks = model.Data.GetUnsignedIntegerArray("m_refMeshGroupMasks");
                var hideInTools = Array.Empty<string>();
                if (model.Data.GetArray<string>("m_BodyGroupsHiddenInTools") is string[] hideBodyGroups)
                {
                    hideInTools = hideBodyGroups;
                }

                var groupedChoices = new Dictionary<string, List<(int ChoiceIndex, string FullName, string ChoiceName)>>();

                for (var i = 0; i < meshGroups!.Length; i++)
                {
                    var fullName = meshGroups[i];
                    var split = fullName.Split("_@");

                    if (split.Length < 2)
                    {
                        continue;
                    }

                    var groupName = split[0];
                    var choiceName = split[1];

                    groupedChoices.TryAdd(groupName, []);
                    groupedChoices[groupName].Add((i, fullName, choiceName));
                }

                foreach (var (groupName, choices) in groupedChoices)
                {
                    var choiceList = KVObject.Array();
                    var bodyGroup = MakeNode("BodyGroup",
                        ("name", groupName),
                        ("children", choiceList)
                    );

                    if (hideInTools.Contains(groupName))
                    {
                        bodyGroup.Add("hidden_in_tools", true);
                    }

                    var i = 0;
                    foreach (var (index, key, name) in choices)
                    {
                        var meshGroupChoice = MakeNode("BodyGroupChoice");

                        if (name != i.ToString(CultureInfo.InvariantCulture))
                        {
                            var choiceName = name;

                            // Fix up weird substring added to newer models
                            const string indexMarker = "#&";
                            var markerIndex = name.IndexOf(indexMarker, StringComparison.Ordinal);
                            if (markerIndex >= 0)
                            {
                                var start = markerIndex + indexMarker.Length;
                                if (start < name.Length)
                                {
                                    choiceName = name[start..];
                                }
                            }

                            meshGroupChoice.Add("name", choiceName);
                        }

                        if (hideInTools.Contains(key))
                        {
                            meshGroupChoice.Add("hide_in_tools", true);
                        }

                        var meshes = KVObject.Array();
                        meshGroupChoice.Add("meshes", meshes);

                        foreach (var renderMesh in RenderMeshesToExtract)
                        {
                            // No mask will show up as 'Empty' in editor
                            var mask = renderMesh.Index < meshGroupMasks.Length ? meshGroupMasks[renderMesh.Index] : 0UL;

                            if ((mask & 1UL << index) == 0)
                            {
                                continue;
                            }

                            meshes.Add(renderMesh.Name);
                        }

                        choiceList.Add(meshGroupChoice);
                        i++;
                    }

                    bodyGroupList.Value.Add(bodyGroup);
                }
            }

            {
                // LOD groups. m_refLODGroupMasks says which level each mesh belongs to (bit N => level N) and
                // m_lodGroupSwitchDistances gives each level's switch value. Emit one LODGroup per populated
                // level so a recompile rebuilds the original LoD structure, and collect meshes that live in
                // every level into a single LODGroupAll rather than repeating them in each group.
                var lodInfo = model.LodInfo;

                for (var lodLevel = 0; lodLevel < lodInfo.SwitchDistances.Count; lodLevel++)
                {
                    var meshReferences = KVObject.Array();

                    foreach (var renderMesh in RenderMeshesToExtract)
                    {
                        if (!lodInfo.IsMeshInLevel(renderMesh.Index, lodLevel) || lodInfo.IsMeshInAllLevels(renderMesh.Index))
                        {
                            continue;
                        }

                        var meshReference = KVObject.Collection();
                        meshReference.Add("mesh_name", renderMesh.Name);
                        meshReferences.Add(meshReference);
                    }

                    // Skip levels with no meshes (e.g. a misconfigured empty LoD0).
                    if (meshReferences.Count == 0)
                    {
                        continue;
                    }

                    lodGroupList.Value.Add(MakeNode("LODGroup",
                        ("switch_threshold", lodInfo.SwitchDistances[lodLevel]),
                        ("mesh_references", meshReferences)
                    ));
                }

                if (lodInfo.SwitchDistances.Count > 0)
                {
                    var allLevelReferences = KVObject.Array();

                    foreach (var renderMesh in RenderMeshesToExtract)
                    {
                        if (!lodInfo.IsMeshInAllLevels(renderMesh.Index))
                        {
                            continue;
                        }

                        var meshReference = KVObject.Collection();
                        meshReference.Add("mesh_name", renderMesh.Name);
                        allLevelReferences.Add(meshReference);
                    }

                    if (allLevelReferences.Count > 0)
                    {
                        lodGroupList.Value.Add(MakeNode("LODGroupAll",
                            ("mesh_references", allLevelReferences)
                        ));
                    }
                }
            }

            var mesh = RenderMeshesToExtract.First();
            var attachments = mesh.Mesh.Attachments;

            foreach (var attachment in attachments.Values)
            {
                var mainInfluence = attachment[^1];

                var node = MakeNode("Attachment",
                    ("name", attachment.Name),
                    ("ignore_rotation", attachment.IgnoreRotation),
                    ("parent_bone", mainInfluence.Name),
                    ("relative_origin", ToKVArray(mainInfluence.Offset)),
                    ("relative_angles", ToKVArray(ToEulerAngles(mainInfluence.Rotation))),
                    ("weight", mainInfluence.Weight)
                );

                if (attachment.Length > 1)
                {
                    var children = KVObject.Array();
                    for (var i = 0; i < attachment.Length - 1; i++)
                    {
                        var influence = attachment[i];
                        var childNode = MakeNode("AttachmentInfluence",
                            ("parent_bone", influence.Name),
                            ("relative_origin", ToKVArray(influence.Offset)),
                            ("relative_angles", ToKVArray(ToEulerAngles(influence.Rotation))),
                            ("weight", influence.Weight)
                        );

                        children.Add(childNode);
                    }
                    node.Add("children", children);
                }

                attachmentList.Value.Add(node);
            }
        }

        // Material groups / skins.
        if (model.GetMaterialGroups().ToList() is { Count: > 0 } materialGroups)
        {
            var defaultMaterials = materialGroups[0].Materials;

            materialGroupList.Value.Add(MakeNode("DefaultMaterialGroup",
                ("name", materialGroups[0].Name ?? "default"),
                ("remaps", KVObject.Array())
            ));

            for (var groupIndex = 1; groupIndex < materialGroups.Count; groupIndex++)
            {
                var variantMaterials = materialGroups[groupIndex].Materials;
                if (variantMaterials.Length == 0)
                {
                    continue;
                }

                var remaps = KVObject.Array();
                var pairCount = Math.Min(defaultMaterials.Length, variantMaterials.Length);
                for (var i = 0; i < pairCount; i++)
                {
                    var fromMaterial = defaultMaterials[i];
                    var toMaterial = variantMaterials[i];
                    if (string.Equals(fromMaterial, toMaterial, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    remaps.Add(MakeNode("BaseMaterialRemap",
                        ("from", fromMaterial),
                        ("to", toMaterial)
                    ));
                }

                materialGroupList.Value.Add(MakeNode("MaterialGroup",
                    ("name", materialGroups[groupIndex].Name ?? groupIndex.ToString(CultureInfo.InvariantCulture)),
                    ("remaps", remaps)
                ));
            }
        }

        // Morph controls + rules: the compiled MRPH stores the flex controllers (with their ranges) and
        // the flex rules as RPN op programs. ModelDoc authors these as MorphControl nodes and MorphRule
        // nodes whose `expression` STRING the compiler parses back into the same ops - so decompiling
        // each rule program into an expression makes controllers, ranges AND formulas (split pairs,
        // phoneme dominators, ...) round-trip exactly. Explicit definitions override the implicit ones
        // generated from the mesh DMX combination operator.
        if (model?.Resource?.GetBlockByType(BlockType.MRPH) is Morph morphBlock
            && morphBlock.FlexControllers.Length > 0)
        {
            var morphControlList = MakeLazyList("MorphControlList");
            var morphRuleList = MakeLazyList("MorphRuleList");
            var flexDescriptorSet = morphBlock.GetFlexDescriptors().ToHashSet(StringComparer.Ordinal);

            foreach (var controller in morphBlock.FlexControllers)
            {
                // A plain 0..1 controller matching a raw flex of the same name is fully implicit (the
                // mesh's combination controls recreate it); an explicit node would only clutter ModelDoc
                // and stamp its name into the otherwise-empty rule display. Only controllers the
                // implicit path cannot produce (paired eyeDownAndUp-style, custom ranges) are emitted.
                var isTrivial = controller.Min == 0f && controller.Max == 1f && flexDescriptorSet.Contains(controller.Name);
                if (isTrivial)
                {
                    continue;
                }

                morphControlList.Value.Add(MakeNode("MorphControl",
                    ("name", controller.Name),
                    ("min_value", controller.Min),
                    ("max_value", controller.Max)
                ));
            }

            var flexDescriptors = morphBlock.GetFlexDescriptors();
            foreach (var rule in morphBlock.FlexRules)
            {
                if (rule.FlexID < 0 || rule.FlexID >= flexDescriptors.Count)
                {
                    continue;
                }

                var target = flexDescriptors[rule.FlexID];
                var expression = DecompileFlexRule(rule, morphBlock.FlexControllers);

                // A trivial identity rule (the flex just fetches the controller of its own name) stays
                // IMPLICIT: no explicit MorphRule node, so its rule shows empty in ModelDoc like
                // authored content, and the compiler regenerates the plain fetch on its own.
                if (expression is null || expression == target)
                {
                    continue;
                }

                morphRuleList.Value.Add(MakeNode("MorphRule",
                    ("name", target),
                    ("target", target),
                    ("expression", expression),
                    ("implicit", false)
                ));
            }
        }

        var modelSequenceData = model?.Resource?.GetBlockByType(BlockType.ASEQ) as KeyValuesOrNTRO;
        var additionalSequenceData = new Dictionary<string, KVObject>();
        string[]? sequenceLocalReferenceArray = null;
        string[]? poseParamNames = null;

        if (modelSequenceData?.Data is KVObject sequenceData)
        {
            ExtractSequenceData(modelSequenceData);

            foreach (var data in sequenceData.GetArray("m_localS1SeqDescArray"))
            {
                additionalSequenceData.Add(data.GetStringProperty("m_sName"), data);
            }

            var poseParams = sequenceData.GetArray("m_localPoseParamArray");
            ExtractPoseParams(poseParams);

            poseParamNames = [.. poseParams.Select(x => x.GetStringProperty("m_sName"))];
            sequenceLocalReferenceArray = sequenceData.GetArray<string>("m_localSequenceNameArray");
        }

        if (AnimationsToExtract.Count > 0)
        {
            var animationToFolder = new Dictionary<string, KVObject>(AnimationsToExtract.Count);
            if (modelSequenceData?.Data.GetSubCollection("m_keyValues") is KVObject sequenceKeyValues)
            {
                if (sequenceKeyValues.GetSubCollection("faceposer_folders") is KVObject faceposerFolders)
                {
                    foreach (var (folderName, _) in faceposerFolders)
                    {
                        var animationNames = faceposerFolders.GetArray<string>(folderName);

                        var (folderNode, children) = MakeListNode("Folder");
                        folderNode.Add("name", folderName);
                        animationList.Value.Add(folderNode);

                        foreach (var animationName in animationNames!)
                        {
                            animationToFolder.Add(animationName, children);
                        }
                    }
                }
            }

            void AddToFolderOrRoot(string name, KVObject node)
            {
                var folderOrRoot = animationToFolder.GetValueOrDefault(name, animationList.Value);
                folderOrRoot.Add(node);
            }

            foreach (var (name, aseq) in additionalSequenceData)
            {
                var sequenceKeys = aseq.GetSubCollection("m_SequenceKeys");
                if (sequenceKeys == null)
                {
                    continue;
                }

                // Animations that have this property do not appear in Animations list.
                if (sequenceKeys.GetBooleanProperty("bind_pose"))
                {
                    var animBindPose = MakeNode(
                        "AnimBindPose",
                        ("name", name)
                    );

                    AddToFolderOrRoot(name, animBindPose);
                }
            }

            // 1D blend sequences (m_bMulti + 1D fetch): VRF used to flatten these into a plain baked
            // AnimFile, losing the blend entirely (turn leaning stopped working on recompiled models).
            // Two reconstructions, best fidelity first:
            //  - Turn layers (ModelDoc "AnimTurn"): pose keys [-1, 0, 1] over lookFrame deltas that were
            //    generated from a local 3-frame animation @X. The AnimTurn node regenerates the implicit
            //    @X_lookFrame_0/1/2 anims from that file, so this path needs @X's data to exist.
            //  - Generic "1DBlend": a blendList of (anim name, pose key) pairs. Works even when the
            //    referenced anims have no local data (wearables whose lookFrames live in the hero model):
            //    the compiler emits the same dangling name references the original carries.
            var turnSequences = new Dictionary<string, (string SourceFileName, string PoseParamName)>();
            var turnGeneratedAnims = new HashSet<string>(StringComparer.Ordinal);
            var blend1DSequences = new Dictionary<string, KVObject>(StringComparer.Ordinal);

            if (sequenceLocalReferenceArray != null && poseParamNames != null)
            {
                foreach (var (seqName, seqDesc) in additionalSequenceData)
                {
                    var seqFlags = seqDesc.GetSubCollection("m_flags");
                    if (!(seqFlags?.GetBooleanProperty("m_bMulti") ?? false))
                    {
                        continue;
                    }

                    var fetch = seqDesc.GetSubCollection("m_fetch");
                    if (fetch is null || !(fetch.GetSubCollection("m_flags")?.GetBooleanProperty("m_b1D") ?? false))
                    {
                        continue;
                    }

                    var references = fetch.GetIntegerArray("m_localReferenceArray");
                    var poseKeys = fetch.GetFloatArray("m_poseKeyArray0");
                    var localPose = fetch.GetIntegerArray("m_nLocalPose");

                    if (references.Length < 2 || poseKeys.Length < references.Length || localPose.Length == 0)
                    {
                        continue;
                    }

                    var poseParamIndex = (int)localPose[0];
                    if (poseParamIndex < 0 || poseParamIndex >= poseParamNames.Length)
                    {
                        continue;
                    }

                    var referenceNames = new string[references.Length];
                    var referencesValid = true;
                    for (var i = 0; i < references.Length; i++)
                    {
                        var referenceIndex = (int)references[i];
                        if (referenceIndex < 0 || referenceIndex >= sequenceLocalReferenceArray.Length)
                        {
                            referencesValid = false;
                            break;
                        }

                        referenceNames[i] = sequenceLocalReferenceArray[referenceIndex];
                    }

                    if (!referencesValid)
                    {
                        continue;
                    }

                    var isLookFramePattern = references.Length == 3
                        && poseKeys[0] == -1f && poseKeys[1] == 0f && poseKeys[2] == 1f
                        && referenceNames[0] == $"@{seqName}_lookFrame_0"
                        && referenceNames[1] == $"@{seqName}_lookFrame_1"
                        && referenceNames[2] == $"@{seqName}_lookFrame_2";

                    var sourceAnimName = $"@{seqName}";
                    if (isLookFramePattern && AnimationsToExtract.Any(x => x.Anim.Name == sourceAnimName))
                    {
                        turnSequences[seqName] = (GetDmxFileName_ForAnimation(sourceAnimName), poseParamNames[poseParamIndex]);

                        // The lookFrame deltas are regenerated by the compiler and the baked blend X.dmx is
                        // unused. The hidden @X AnimFile is kept: originals ship the raw @X anim alongside
                        // the generated pieces (the AnimTurn node itself does not recreate it).
                        for (var i = 0; i < 3; i++)
                        {
                            turnGeneratedAnims.Add($"@{seqName}_lookFrame_{i}");
                            AnimationsExcludedFromDmxExport.Add($"@{seqName}_lookFrame_{i}");
                        }

                        AnimationsExcludedFromDmxExport.Add(seqName);
                        continue;
                    }

                    // Generic 1DBlend reconstruction. Field set matches authored content
                    // (e.g. anessix_geometry.vmdl); blendList weights are the fetch pose keys.
                    var blendList = KVObject.Array();
                    for (var i = 0; i < references.Length; i++)
                    {
                        var entry = KVObject.Collection();
                        entry.Add("name", referenceNames[i]);
                        entry.Add("weight", poseKeys[i]);
                        blendList.Add(entry);
                    }

                    var transition = seqDesc.GetSubCollection("m_transition");
                    var seqActivities = seqDesc.GetArray("m_activityArray");

                    var blendNode = MakeNode("1DBlend",
                        ("name", seqName),
                        ("activity_name", seqActivities is { Count: > 0 } ? seqActivities[0].GetStringProperty("m_name") : ""),
                        ("activity_weight", seqActivities is { Count: > 0 } ? seqActivities[0].GetInt32Property("m_nWeight") : 1),
                        ("weight_list_name", ""),
                        ("fade_in_time", transition?.GetFloatProperty("m_flFadeInTime") ?? 0.2f),
                        ("fade_out_time", transition?.GetFloatProperty("m_flFadeOutTime") ?? 0.2f),
                        ("looping", seqFlags.GetBooleanProperty("m_bLooping")),
                        ("delta", seqFlags.GetBooleanProperty("m_bLegacyDelta")),
                        ("worldSpace", seqFlags.GetBooleanProperty("m_bLegacyWorldspace")),
                        ("hidden", seqFlags.GetBooleanProperty("m_bHidden")),
                        ("anim_markup_ordered", false),
                        ("disable_compression", false),
                        ("animgraph_additive", false),
                        ("blend_anim_events", false),
                        ("poseParam", poseParamNames[poseParamIndex]),
                        ("blendList", blendList));

                    if (seqActivities is { Count: > 1 })
                    {
                        var modifiers = KVObject.Array();
                        for (var modifierIndex = 1; modifierIndex < seqActivities.Count; modifierIndex++)
                        {
                            modifiers.Add(MakeNode("ActivityModifier",
                                ("activity_name", seqActivities[modifierIndex].GetStringProperty("m_name")),
                                ("activity_weight", seqActivities[modifierIndex].GetInt32Property("m_nWeight"))
                            ));
                        }

                        blendNode.Add("children", modifiers);
                    }

                    blend1DSequences[seqName] = blendNode;

                    // A baked AnimFile of the blend would collide with the reconstructed sequence.
                    AnimationsExcludedFromDmxExport.Add(seqName);
                }

                foreach (var (name, node) in blend1DSequences)
                {
                    AddToFolderOrRoot(name, node);
                }
            }

            var sequences = AnimationsToExtract.Where(x => x.Anim.FromSequence);
            foreach (var animation in sequences)
            {
                // Implicit turn anims (@X, @X_lookFrame_N) are regenerated from the AnimTurn node's
                // 3-frame source file, and 1D blend sequences are reconstructed as 1DBlend nodes;
                // emitting their baked AnimFiles too would define them twice.
                if (turnGeneratedAnims.Contains(animation.Anim.Name) || blend1DSequences.ContainsKey(animation.Anim.Name))
                {
                    continue;
                }

                if (turnSequences.TryGetValue(animation.Anim.Name, out var turn))
                {
                    // pose_param_name must be explicit: without it the compiler leaves the generated
                    // 1D blend without a pose parameter ("1DBlend: X : Undefined pose parameter").
                    AddToFolderOrRoot(animation.Anim.Name, MakeNode("AnimTurn",
                        ("name", animation.Anim.Name),
                        ("source_filename", turn.SourceFileName),
                        ("pose_param_name", turn.PoseParamName),
                        ("fade_in_time", animation.Anim.SequenceParams.FadeInTime),
                        ("fade_out_time", animation.Anim.SequenceParams.FadeOutTime)
                    ));
                    continue;
                }

                var animationFile = MakeNode(
                    "AnimFile",
                    ("name", animation.Anim.Name),
                    ("source_filename", animation.FileName),
                    ("fade_in_time", animation.Anim.SequenceParams.FadeInTime),
                    ("fade_out_time", animation.Anim.SequenceParams.FadeOutTime),
                    ("looping", animation.Anim.IsLooping),
                    ("delta", animation.Anim.Delta),
                    ("worldSpace", animation.Anim.Worldspace),
                    ("hidden", animation.Anim.Hidden)
                );

                // The sequence's m_activityArray is the authoritative source for activity + modifiers:
                // entry [0] is the activity (ACT_*), entries [1..] are activity modifiers ("injured", "ether", ...).
                // VRF previously only read the activity from the anim block and dropped the modifiers entirely.
                IReadOnlyList<KVObject>? seqActivityArray = null;
                if (additionalSequenceData.TryGetValue(animation.Anim.Name, out var seqDescForActivity))
                {
                    seqActivityArray = seqDescForActivity.GetArray("m_activityArray");
                }

                if (seqActivityArray is { Count: > 0 })
                {
                    var activity = seqActivityArray[0];
                    animationFile.Add("activity_name", activity.GetStringProperty("m_name"));
                    animationFile.Add("activity_weight", activity.GetInt32Property("m_nWeight"));
                }
                else if (animation.Anim.Activities.Length > 0)
                {
                    var activity = animation.Anim.Activities[0];
                    animationFile.Add("activity_name", activity.Name);
                    animationFile.Add("activity_weight", activity.Weight);
                }

                var childrenKV = KVObject.Array();

                // Activity modifiers are entries [1..] of the sequence m_activityArray. ModelDoc represents
                // each as an "ActivityModifier" child node of the AnimFile (activity_name = the modifier tag);
                // the compiler merges them back into the ASEQ m_activityArray. A field-level
                // "activity_modifiers" array is silently ignored by the compiler, so it must be child nodes.
                if (seqActivityArray is { Count: > 1 })
                {
                    for (var modifierIndex = 1; modifierIndex < seqActivityArray.Count; modifierIndex++)
                    {
                        var modifier = seqActivityArray[modifierIndex];
                        childrenKV.Add(MakeNode("ActivityModifier",
                            ("activity_name", modifier.GetStringProperty("m_name")),
                            ("activity_weight", modifier.GetInt32Property("m_nWeight"))
                        ));
                    }
                }

                foreach (var localHierarchy in animation.Anim.LocalHierarchy)
                {
                    childrenKV.Add(MakeNode("LocalHierarchy",
                        ("bone_name", localHierarchy.Bone),
                        ("new_parent_bone_name", localHierarchy.NewParent),
                        ("start_frame", localHierarchy.StartFrame),
                        ("peak_frame", localHierarchy.PeakFrame),
                        ("tail_frame", localHierarchy.TailFrame),
                        ("end_frame", localHierarchy.EndFrame)
                    ));
                }

                if (animation.Anim.HasMovementData())
                {
                    var flags = animation.Anim.Movements[0].MotionFlags;
                    var extractMotion = MakeNode("ExtractMotion",
                        ("extract_tx", flags.HasFlag(ModelAnimationMotionFlags.TX)),
                        ("extract_ty", flags.HasFlag(ModelAnimationMotionFlags.TY)),
                        // never extract vertical. on recompile it makes the compiler counter-bake the root
                        // and float the whole model up. the engine doesn't apply vertical root motion.
                        ("extract_tz", false),
                        ("extract_rz", flags.HasFlag(ModelAnimationMotionFlags.RZ)),
                        ("linear", flags.HasFlag(ModelAnimationMotionFlags.Linear)),
                        ("quadratic", false),
                        ("motion_type", "uniform")
                    );

                    childrenKV.Add(extractMotion);
                }
                foreach (var animEvent in animation.Anim.Events)
                {
                    var animEventNode = MakeNode("AnimEvent",
                        ("event_class", animEvent.Name),
                        ("event_frame", animEvent.Frame)
                    );

                    if (animEvent.EventData != null)
                    {
                        animEventNode.Add("event_keys", animEvent.EventData);
                    }
                    childrenKV.Add(animEventNode);
                }

                if (sequenceLocalReferenceArray != null && poseParamNames != null)
                {
                    foreach (var autoLayer in animation.Anim.AutoLayers)
                    {
                        var layerNode = ProcessAnimationAutoLayer(animation.Anim, autoLayer, sequenceLocalReferenceArray, poseParamNames);
                        childrenKV.Add(layerNode);
                    }
                }

                if (animation.Anim.Autoplay)
                {
                    var autoLayer = MakeNode("AnimAutoLayer");
                    childrenKV.Add(autoLayer);
                }

                if (poseParamNames != null && animation.Anim.Fetch != null && animation.Anim.Fetch.Value.LocalCyclePoseParameter != -1)
                {
                    var poseParamIndex = animation.Anim.Fetch.Value.LocalCyclePoseParameter;
                    var poseParam = poseParamNames[poseParamIndex];

                    var autoLayer = MakeNode("AnimCycleOverride", [
                        ("cycle_type", "Pose To Cycle"),
                        ("pose_param_name", poseParam),
                    ]);
                    childrenKV.Add(autoLayer);
                }

                if (animation.Anim.Realtime)
                {
                    var autoLayer = MakeNode("AnimCycleOverride", [
                        ("cycle_type", "Auto Cycle"),
                        ("pose_param_name", ""),
                    ]);
                    childrenKV.Add(autoLayer);
                }

                if (additionalSequenceData.TryGetValue(animation.Anim.Name, out var animSequenceData))
                {
                    var sequenceKeys = animSequenceData.GetSubCollection("m_SequenceKeys");
                    if (sequenceKeys != null)
                    {
                        if (sequenceKeys.GetSubCollection("AnimGameplayTiming") is KVObject animGameplayTiming)
                        {
                            childrenKV.Add(MakeNode("AnimGameplayTiming", animGameplayTiming));
                        }
                    }
                }

                if (childrenKV.Count > 0)
                {
                    animationFile.Add("children", childrenKV);
                }

                AddToFolderOrRoot(animation.Anim.Name, animationFile);
            }
        }

        if (PhysHullsToExtract.Count > 0 || PhysMeshesToExtract.Count > 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                if (PhysicsToRenderMaterialNameProvider is null)
                {
                    RemapMaterials(null, globalReplace: true);
                }
                else
                {
                    var remapTable = SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );
                    RemapMaterials(remapTable, globalReplace: false);
                }
            }

            foreach (var (physHull, fileName, parentBone) in PhysHullsToExtract)
            {
                HandlePhysMeshNode(physHull, fileName, parentBone);
            }

            foreach (var (physMesh, fileName, parentBone) in PhysMeshesToExtract)
            {
                HandlePhysMeshNode(physMesh, fileName, parentBone);
            }
        }

        if (model != null)
        {
            ExtractModelKeyValues(root.Node);
            ExtractHitboxSets();

            if (model.Skeleton.Roots.Length > 0)
            {
                AddBonesRecursive(model.Skeleton.Roots, skeleton.Value);
            }
        }

        if (physAggregateData is not null)
        {
            for (var i = 0; i < physAggregateData.Parts.Length; i++)
            {
                var physicsPart = physAggregateData.Parts[i];
                var parentBone = physAggregateData.GetParentBoneName(i);

                foreach (var sphere in physicsPart.Shape.Spheres)
                {
                    var physicsShapeSphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[sphere.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[sphere.CollisionAttributeIndex])),
                        ("radius", sphere.Shape.Radius),
                        ("center", ToKVArray(sphere.Shape.Center)),
                        ("name", sphere.UserFriendlyName ?? string.Empty)
                    );

                    physicsShapeList.Value.Add(physicsShapeSphere);
                }

                foreach (var capsule in physicsPart.Shape.Capsules)
                {
                    var physicsShapeCapsule = MakeNode(
                        "PhysicsShapeCapsule",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[capsule.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[capsule.CollisionAttributeIndex])),
                        ("radius", capsule.Shape.Radius),
                        ("point0", ToKVArray(capsule.Shape.Center[0])),
                        ("point1", ToKVArray(capsule.Shape.Center[1])),
                        ("name", capsule.UserFriendlyName ?? string.Empty)
                    );

                    physicsShapeList.Value.Add(physicsShapeCapsule);
                }
            }
        }

        // Soft-body / cloth physics (m_pFeModel): reconstruct editable ModelDoc cloth source so the model
        // recompiles into a working FeModel PHYS block AND opens in ModelDoc (no binary transplant).
        // Phase 1 recovers bone-chain cloth as ClothChain nodes. Phase 2 recovers the cloth SHEET as a
        // ClothProxyMeshFile + proxy DMX.
        var clothEmitted = false;
        if (physAggregateData?.FeModel is { } feModel)
        {
            var boneChains = feModel.BuildBoneChains();

            if (ClothProxyMeshesToExtract.Count > 0)
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
                var independentChains = boneChains
                    .Where(chain => !chain.Joints.Any(joint => feModel.FitMatrixNodes.Contains(joint.Node)))
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

                bool ProxyDrivesUnchainedBone(FeModel.ProxyMesh proxy)
                {
                    for (var v = 0; v < proxy.ClothEnable.Length; v++)
                    {
                        // Only simulated vertices back-solve a bone; a pinned vertex just follows its anchor.
                        if (proxy.ClothEnable[v] == 0f)
                        {
                            continue;
                        }

                        foreach (var (bone, _) in proxy.SkinInfluences[v])
                        {
                            if (!chainDrivenBones.Contains(bone))
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
                    clothProxyChildren.Add(MakeClothProxyMeshFile(proxyFile.Name, proxyFile.FileName, proxyBackSolve, driveMeshes: proxyBackSolve, addBonesToRenderMesh,
                        backSolveInfluenceThreshold: feModel.RecoveredBackSolveThreshold ?? 0.0f));
                }

                // Clean regular grids generated over the bone chains, shipped DISABLED next to the
                // recovered surface: a ready-made editable sheet for re-authoring the cloth.
                foreach (var clothGrid in ClothChainGridsToExtract)
                {
                    var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
                    gridNode.Add("disabled", true);
                    clothProxyChildren.Add(gridNode);
                }

                root.Children.Add(clothProxyList);

                // The proxy mesh ships the global solver scalars + any collision shapes via a Softbody.
                // Independent (non-back-solved) chains, if any, are emitted alongside it - see above.
                var (softbody, softbodyChildren) = MakeListNode("Softbody");
                softbodyChildren.Add(MakeClothParams(feModel));

                // Simulated real bones that are NEITHER back-solved NOR part of any multi-joint BoneChain -
                // standalone goal-attraction points wired together only by ClothSpring (see MakeClothNode
                // remarks). A real bone with no real-bone descendants of its own never forms a BoneChain
                // (BuildBoneChains skips it), so these would otherwise carry correct rods/connectivity
                // (a plain bone name is a valid ClothSpring endpoint) but compiler-default paint instead
                // of the recovered original goal_strength/damping/gravity/stray_radius.
                var chainNodes = boneChains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();
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
                var shapeParentBones = feModel.BuildCollisionCapsules().Select(static c => c.ParentBone)
                    .Concat(feModel.BuildCollisionSpheres().Select(static s => s.ParentBone))
                    .Where(static n => n is not null)
                    .ToHashSet();
                var leftoverStaticNodes = new List<(string Name, int Node)>();

                for (var node = 0; node < feModel.CtrlNames.Length; node++)
                {
                    var name = feModel.CtrlNames[node];
                    if (FeModel.IsProxyNodeName(name) || feModel.FitMatrixNodes.Contains(node) || chainNodes.Contains(node))
                    {
                        continue;
                    }

                    if (feModel.NodeInvMasses[node] != 0f)
                    {
                        loneClothNodes.Add((name, node));
                    }
                    else if (!shapeParentBones.Contains(name)
                        && boneByName is not null && boneByName.TryGetValue(name, out var bone) && bone.IsClothControlNode)
                    {
                        leftoverStaticNodes.Add((name, node));
                    }
                }

                if (independentChains.Count > 0 || loneClothNodes.Count > 0 || leftoverStaticNodes.Count > 0)
                {
                    var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
                    clothFolder.Add("name", "cloth");
                    softbodyChildren.Add(clothFolder);

                    foreach (var boneChain in independentChains)
                    {
                        clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain));
                    }

                    foreach (var (name, node) in loneClothNodes)
                    {
                        clothFolderChildren.Add(MakeClothNode(feModel, name, node));
                    }

                    foreach (var (name, node) in leftoverStaticNodes)
                    {
                        clothFolderChildren.Add(MakeClothNode(feModel, name, node, isStaticNode: true));
                    }
                }

                var independentChainNodes = independentChains
                    .SelectMany(static chain => chain.Joints)
                    .Select(static joint => joint.Node)
                    .ToHashSet();
                AddClothProxySprings(softbodyChildren, feModel, ClothProxyMeshesToExtract, independentChainNodes);
                AddClothCollisionShapes(softbodyChildren, feModel);

                root.Children.Add(softbody);

                clothEmitted = true;
            }
            else if (boneChains.Count > 0)
            {
                // Phase 1 fallback (no recoverable sheet): bone-chain cloth, plus a GENERATED sheet grid
                // over each group of neighbouring chains (skirts/capes). The grid mirrors hand-authored
                // item proxies: with back_solve_joints=false the chains keep simulating the bones while
                // the sheet simulates the surface between them and drives the render mesh directly.
                var (softbody, softbodyChildren) = MakeListNode("Softbody");
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

                AddClothCollisionShapes(softbodyChildren, feModel);
                root.Children.Add(softbody);
                clothEmitted = true;
            }
        }

        // Other embedded physics that VRF cannot reconstruct from source (e.g. ragdoll-only aggregates) still
        // ship as a PHYS block in the compiled model. When the model has such a block but no rigid shapes or
        // cloth were emitted above, drop a minimal placeholder PhysicsShapeList so the compiler allocates a
        // PHYS block plus the CTRL embedded_physics reference. A post-compile step can then replace the PHYS
        // payload in-place with the original block, keeping the block index the CTRL ref points at.
        if (!clothEmitted
            && !physicsShapeList.IsValueCreated
            && model?.Resource?.GetBlockByType(BlockType.PHYS) is not null
            && model.Skeleton.Bones.Length > 0)
        {
            physicsShapeList.Value.Add(MakeNode("PhysicsShapeSphere",
                ("parent_bone", GetExportBoneName(model.Skeleton.Bones[0])),
                ("surface_prop", "default"),
                ("collision_tags", "solid"),
                ("radius", 1.0f),
                ("center", ToKVArray(Vector3.Zero)),
                ("name", "vrf_phys_transplant_placeholder")
            ));
        }

        if (Translation != Vector3.Zero)
        {
            modelModifierList.Value.Add(MakeNode("ModelModifier_Translate", ("translation", ToKVArray(Translation))));
        }


        return kv.ToKV3String(format: KV3IDLookup.Get("modeldoc28"));

        #region Local Functions
        void HandlePhysMeshNode<TShape>(ShapeDescriptor<TShape> shapeDesc, string fileName, string parentBone)
            where TShape : struct
        {
            var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
            var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                renderMeshList.Value.Add(MakeNode("RenderMeshFile", ("filename", fileName)));
                return;
            }

            var className = shapeDesc switch
            {
                HullDescriptor => "PhysicsHullFile",
                MeshDescriptor => "PhysicsMeshFile",
                _ => throw new NotImplementedException()
            };

            var shapeName = shapeDesc.UserFriendlyName ?? Path.GetFileNameWithoutExtension(fileName);

            // TODO: per faceSet surface_prop
            var physicsShapeFile = MakeNode(
                className,
                ("filename", fileName),
                ("parent_bone", parentBone),
                ("surface_prop", surfacePropName),
                ("collision_tags", string.Join(" ", collisionTags)),
                ("name", shapeName)
            );

            physicsShapeList.Value.Add(physicsShapeFile);
        }

        void RemapMaterials(
            IReadOnlyDictionary<string, string>? remapTable = null,
            bool globalReplace = false,
            string globalDefault = "materials/tools/toolsnodraw.vmat")
        {
            var remaps = KVObject.Array();
            materialGroupList.Value.Add(
                MakeNode(
                    "DefaultMaterialGroup",
                    ("remaps", remaps),
                    ("use_global_default", globalReplace),
                    ("global_default_material", globalDefault)
                )
            );

            if (globalReplace || remapTable == null)
            {
                return;
            }

            foreach (var (from, to) in remapTable)
            {
                var remap = KVObject.Collection();
                remap.Add("from", from);
                remap.Add("to", to);
                remaps.Add(remap);
            }
        }

        KVObject GetHitboxNode(Hitbox hitbox)
        {
            var node = hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Box => MakeNode("Hitbox",
                    ("hitbox_mins", ToKVArray(hitbox.MinBounds)),
                    ("hitbox_maxs", ToKVArray(hitbox.MaxBounds))
                ),
                Hitbox.HitboxShape.Capsule => MakeNode("HitboxCapsule",
                    ("radius", hitbox.ShapeRadius),
                    ("point0", ToKVArray(hitbox.MinBounds)),
                    ("point1", ToKVArray(hitbox.MaxBounds))
                ),
                Hitbox.HitboxShape.Sphere => MakeNode("HitboxSphere",
                    ("center", ToKVArray(hitbox.MinBounds)),
                    ("radius", hitbox.ShapeRadius)
                ),
                _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
            };

            node.Add("name", hitbox.Name);
            node.Add("parent_bone", hitbox.BoneName);
            node.Add("surface_property", hitbox.SurfaceProperty);
            node.Add("translation_only", hitbox.TranslationOnly);
            node.Add("group_id", hitbox.GroupId);

            return node;
        }

        void ExtractHitboxSets()
        {
            if (model.HitboxSets == null)
            {
                return;
            }

            foreach (var pair in model.HitboxSets)
            {
                var children = KVObject.Array();
                var hitboxSet = MakeNode("HitboxSet", ("name", pair.Key), ("children", children));

                foreach (var hitbox in pair.Value)
                {
                    var hitboxNode = GetHitboxNode(hitbox);
                    children.Add(hitboxNode);
                }

                hitboxSetList.Value.Add(hitboxSet);
            }
        }

        void ExtractSequenceData(KeyValuesOrNTRO sequenceData)
        {
            var boneMasks = sequenceData.Data.GetArray("m_localBoneMaskArray");
            var boneNames = sequenceData.Data.GetArray<string>("m_localBoneNameArray");

            foreach (var boneMask in boneMasks!)
            {
                var name = boneMask.GetStringProperty("m_sName");
                var boneArray = boneMask.GetIntegerArray("m_nLocalBoneArray");
                var boneWeights = boneMask.GetFloatArray("m_flBoneWeightArray");
                // master_morph_weight = m_flDefaultMorphCtrlWeight

                // skip default mask
                if (name == "default" && boneArray.Length == 0)
                {
                    continue;
                }

                var weights = KVObject.Array();
                var weightListNode = MakeNode("WeightList",
                    ("name", name),
                    ("weights", weights)
                );

                foreach (var (boneIndex, boneWeight) in boneArray.Zip(boneWeights))
                {
                    var weightDefinition = KVObject.Collection();
                    var boneName = boneNames![boneIndex];

                    weightDefinition.Add("bone", boneName);
                    weightDefinition.Add("weight", boneWeight);
                    weights.Add(weightDefinition);
                }

                weightLists.Value.Add(weightListNode);
            }
        }


        void ExtractPoseParams(IReadOnlyList<KVObject> poseParamsData)
        {
            foreach (var poseParam in poseParamsData)
            {
                var name = poseParam.GetStringProperty("m_sName");
                var start = poseParam.GetFloatProperty("m_flStart");
                var end = poseParam.GetFloatProperty("m_flEnd");
                var loop = poseParam.GetFloatProperty("m_flLoop");
                var looping = poseParam.GetBooleanProperty("m_bLooping");

                var poseParamNode = MakeNode("PoseParam",
                    ("name", name),
                    ("poseparam_min", start),
                    ("poseparam_max", end),
                    ("poseparam_looping", looping),
                    ("poseparam_loop", loop)
                );

                poseParamList.Value.Add(poseParamNode);
            }
        }

        void ExtractModelKeyValues(KVObject rootNode)
        {
            if (model.Data.ContainsKey("m_refAnimIncludeModels"))
            {
                foreach (var animIncludeModel in model.Data.GetArray<string>("m_refAnimIncludeModels")!)
                {
                    animationList.Value.Add(MakeNode("AnimIncludeModel", ("model", animIncludeModel)));
                }
            }

            if (model.Data.ContainsKey("m_vecNmSkeletonRefs"))
            {
                foreach (var skeletonRef in model.Data.GetArray<string>("m_vecNmSkeletonRefs"))
                {
                    nmskelList.Value.Add(MakeNode("NmSkeletonReference", ("filename", skeletonRef)));
                }
            }

            if (model.Data.ContainsKey("m_animGraph2Refs"))
            {
                var animGraph2Refs = model.Data.GetArray("m_animGraph2Refs");
                for (int i = 0; i < animGraph2Refs.Count; i++)
                {
                    var refObj = animGraph2Refs[i];
                    var identifier = refObj.GetStringProperty("m_sIdentifier");
                    var graphPath = refObj.GetStringProperty("m_hGraph");

                    if (i == 0)
                    {
                        animGraph2List.Value.Add(MakeNode("DefaultAnimGraph2", ("filename", graphPath)));
                    }
                    else
                    {
                        animGraph2List.Value.Add(MakeNode("AnimGraph2", ("name", identifier), ("filename", graphPath)));
                    }
                }
            }

            var breakPieceList = MakeLazyList("BreakPieceList");
            var gameDataList = MakeLazyList("GameDataList");

            var keyvalues = model.KeyValues;

            if (keyvalues.Count == 0)
            {
                return;
            }

            if (keyvalues.ContainsKey("anim_graph_resource"))
            {
                rootNode.Add("anim_graph_name", keyvalues.GetStringProperty("anim_graph_resource"));
            }

            if (keyvalues.ContainsKey("BoneConstraintList"))
            {
                var boneConstraintListData = keyvalues.GetArray("BoneConstraintList");
                var boneConstraintList = ExtractBoneConstraints(boneConstraintListData);
                root.Children.Add(boneConstraintList);
            }

            var genericDataClasses = new string[] {
                "prop_data",
                "character_arm_config",
                "vr_carry_type",
                "door_sounds",
                "nav_data",
                "npc_foot_sweep",
                "ai_model_info",
                "breakable_door_model",
                "dynamic_interactions",
                "explosion_behavior",
                "eye_occlusion_renderer",
                "fire_interactions",
                "gastank_markup",
                "hand_conform_data",
                "handpose_data",
                "physgun_interactions",
                "weapon_metadata",
                "glove_viewmodel_reference",
                "composite_material_order",
                "patch_camera_preset_list",
                "camera_settings",
                "scene_data_map",
                "particle_settings",
                "damage_number_settings",
                "CitadelCameraSettings_t",
                "CCitadelHeroModelGameData_t",
                "CCitadelNPCModelGameData_t",
                "CitadelUnitStatusSettings_t",
                "CitadelModelDamageNumberSettings_t",
                "CitadelModelParticleSettings_t",
                "CitadelTaggedSoundSettings_t",
                "CitadelModelSceneData_t",
                "CitadelMuzzleSettings_t",
                "CitadelTeamRelativeParticleSettings_t",
                "CitadelEventIDToBodyGroupMapping_t",
                //"AttachmentCameraData", - is autogenerated from AttachmentCameraPreview/ExporttoRuntimeModel modeldoc node/parameter
                "CDestructiblePart",
                "CDestructiblePartsSystemData",
                "DeformablePropModelGameData_t",
                "CPhysicsBodyGameMarkupData",
                "electrical_interactions",
                "world_interactions",
            };

            var genericDataClassesList = new (string ListKey, string Class)[] {
            ("ao_proxy_capsule_list", "ao_proxy_capsule"),
            ("ao_proxy_box_list", "ao_proxy_box"),
            ("particles_list", "particle"),
            ("hand_pose_list", "hand_pose_pair"),
            ("eye_data_list", "eye"),
            ("bodygroup_driven_morph_list", "bodygroup_driven_morph"),
            ("materialgroup_driven_morph_list", "materialgroup_driven_morph"),
            ("animating_breakable_stage_list", "animating_breakable_stage"),
            ("cables_list", "cable"),
            ("high_quality_shadows_region_list", "high_quality_shadows_region"),
            ("particle_cfg_list", "particle_cfg"),
            ("snapshot_weights_upperbody_list", "snapshot_weights_upperbody"),
            ("snapshot_weights_all_list", "snapshot_weights_all"),
            ("bodygroup_preset_list", "bodygroup_preset"),
            ("muzzle_desc_list", "muzzle_settings"),
            ("unit_status_settings_list", "unit_status_settings"),
            ("team_relative_particles_cfg_list", "team_relative_particle_settings"),
            ("CNPCPhysicsHull", "CNPCPhysicsHull"), // exports as list, needs m_sName changed to name near game_class
        };

            foreach (var genericDataClass in genericDataClasses)
            {
                if (keyvalues.ContainsKey(genericDataClass))
                {
                    var genericData = keyvalues.GetSubCollection(genericDataClass);
                    if (genericData != null)
                    {
                        AddGenericGameData(gameDataList.Value, genericDataClass, genericData);
                    }
                }
            }

            foreach (var genericDataClass in genericDataClassesList)
            {
                var dataKey = genericDataClass.ListKey;
                if (keyvalues.ContainsKey(dataKey))
                {
                    var genericDataList = keyvalues.GetArray(dataKey);
                    foreach (var genericData in genericDataList!)
                    {
                        AddGenericGameData(gameDataList.Value, genericDataClass.Class, genericData);
                    }
                }
            }

            if (keyvalues.ContainsKey("LookAtList"))
            {
                var lookAtList = keyvalues.GetSubCollection("LookAtList");
                foreach (var (_, item) in lookAtList)
                {
                    if (item.ValueType == KVValueType.Collection)
                    {
                        AddGenericGameData(gameDataList.Value, "LookAtChain", item, "lookat_chain");
                    }
                }
            }

            if (keyvalues.ContainsKey("MovementSettings"))
            {
                var movementSettings = keyvalues.GetSubCollection("MovementSettings");
                AddGenericGameData(gameDataList.Value, "MovementSettings", movementSettings, "movementsettings");
            }

            if (keyvalues.ContainsKey("FeetSettings"))
            {
                var feetSettings = keyvalues.GetSubCollection("FeetSettings");
                var feetNode = ConvertFeetSettings(feetSettings!);
                if (feetNode != null)
                {
                    gameDataList.Value.Add(feetNode);
                }
            }

            if (keyvalues.ContainsKey("break_list"))
            {
                foreach (var breakPiece in keyvalues.GetArray("break_list")!)
                {
                    var breakPieceFile = MakeNode("BreakPieceExternal", breakPiece);
                    breakPieceList.Value.Add(breakPieceFile);
                }
            }

            static KVObject? ConvertFeetSettings(KVObject feetSettings)
            {
                var children = KVObject.Array();

                // Field mappings from compiled to source names
                var footFieldMappings = new (string CompiledName, string SourceName)[]
                {
                    ("m_name", "name"),
                    ("m_ankleBoneName", "anklebone"),
                    ("m_toeBoneName", "toebone"),
                    ("m_vBallOffset", "balloffset"),
                    ("m_vHeelOffset", "heeloffset"),
                    ("m_flTraceHeight", "traceheight"),
                    ("m_flTraceRadius", "traceradius"),
                };

                // Convert each foot entry to a Foot child node
                foreach (var (_, footEntry) in feetSettings.Children)
                {
                    if (footEntry.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }

                    var footNode = MakeNode("Foot");

                    // Map compiled field names to source field names
                    foreach (var (compiledName, sourceName) in footFieldMappings)
                    {
                        if (footEntry.ContainsKey(compiledName))
                        {
                            footNode.Add(sourceName, footEntry[compiledName]);
                        }
                    }

                    // autolevel is typically true by default in source format
                    footNode.Add("autolevel", true);

                    children.Add(footNode);
                }

                if (children.Count == 0)
                {
                    return null;
                }

                // Create the Feet node
                var feetNode = MakeNode("Feet", ("children", children));

                // Parent-level field mappings
                var parentFieldMappings = new (string CompiledName, string SourceName)[]
                {
                    ("m_flLockTolerance", "locktolerance"),
                    ("m_flHeightTolerance", "heighttolerance"),
                    ("m_bSanitizeTrajectories", "sanitizetrajectories"),
                };

                // Add parent-level properties if they exist
                foreach (var (compiledName, sourceName) in parentFieldMappings)
                {
                    if (feetSettings.ContainsKey(compiledName))
                    {
                        feetNode.Add(sourceName, feetSettings[compiledName]);
                    }
                }

                return feetNode;
            }

            static void AddGenericGameData(KVObject gameDataList, string genericDataClass, KVObject? genericData, string? dataKey = null)
            {
                if (genericData is null)
                {
                    return;
                }

                // Remove quotes from keys by rebuilding the object
                var cleanedData = KVObject.Collection();
                foreach (var (key, value) in genericData.Children)
                {
                    var trimmed = key?.Trim('"') ?? string.Empty;
                    cleanedData.Add(trimmed, value);
                }

                var name = cleanedData.GetStringProperty("name", string.Empty);

                // The node name should not contain non identifier characters like / or .
                name = Path.GetFileNameWithoutExtension(name);

                KVObject genericGameData;
                if (dataKey == null)
                {
                    genericGameData = MakeNode("GenericGameData",
                        ("name", name),
                        ("game_class", genericDataClass),
                        ("game_keys", cleanedData)
                    );
                }
                else
                {
                    genericGameData = MakeNode(genericDataClass,
                        ("name", name),
                        (dataKey, cleanedData)
                    );
                }

                gameDataList.Add(genericGameData);
            }
        }
        #endregion
    }
}
