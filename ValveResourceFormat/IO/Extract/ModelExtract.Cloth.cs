using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
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
        feModel.DropModelNameVertexSet(System.IO.Path.GetFileNameWithoutExtension(fileName));
        feModel.DropUnnamedVertexSet();

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

        BuildClothChainBoneOrigins(feModel);
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
    void AddSoftbodyAttributes(KVObject softbody, FeModel fe)
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

        AddSoftbodyModelKeyValues(softbody, model?.KeyValues);
    }

    /// <summary>
    /// Restores the two Softbody keys the compiler writes into the model's key values instead of the
    /// FeModel: <c>stiffness_on_ragdoll</c> as <c>cloth_stiffness_on_ragdoll</c> (only when above zero) and
    /// <c>cloth_sleep_enabled</c> (only when set).
    /// </summary>
    internal static void AddSoftbodyModelKeyValues(KVObject softbody, KVObject? keyValues)
    {
        if (keyValues is null)
        {
            return;
        }

        if (keyValues.ContainsKey("cloth_stiffness_on_ragdoll"))
        {
            softbody.Add("stiffness_on_ragdoll", keyValues.GetFloatProperty("cloth_stiffness_on_ragdoll"));
        }

        if (keyValues.ContainsKey("cloth_sleep_enabled"))
        {
            softbody.Add("cloth_sleep_enabled", keyValues.GetBooleanProperty("cloth_sleep_enabled"));
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
            // The compiler adds this to every node's goal strength before cubing it into the force
            // attraction while the vertex attraction keeps the unbiased cube, so a model that ships the
            // two a constant cube root apart was authored with it (see FeModel.GoalStrengthBias).
            ("goal_strength_bias", fe.GoalStrengthBias),
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
            // A model with rigid edge hinges states its curvature only through the ring bends that switch
            // turns on: its rods are all built rigid, so the readings taken off them saturate whatever it
            // was authored with (see FeModel.RigidHingeCurvature). Where its hubs fold apart, the per-hub
            // paint carries every fold and the model-wide value stays zero (see FeModel.RigidHingeBendPaint).
            ("add_curvature", fe.HasAxialEdges ? (fe.RigidHingeBendPaint is null ? fe.RigidHingeCurvature : 0f) : addCurvature),
            ("quad_bend_tolerance", fe.QuadBendTolerance),
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

    /// <summary>
    /// Re-declares the dynamic-to-kinematic links (<see cref="FeModel.DynKinLinks"/>) as
    /// <c>ClothFollowBone</c> nodes. One node per compiled entry, naming the link's parent node as
    /// <c>leader_bone</c> and its child node as <c>follower_bone</c>. Both endpoints must be bones this
    /// export declares in cloth (<paramref name="clothBones"/>) - the compiler rejects the whole compile
    /// over one naming a bone no cloth construct claims. Emitted in compiled order, which the compiler's
    /// own parent-before-child sort reproduces wherever the export's node order matches the original's.
    /// </summary>
    internal static void AddClothFollowBones(KVObject softbodyChildren, FeModel feModel, HashSet<string> clothBones)
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

        foreach (var link in feModel.BoneMergeLinks)
        {
            if (link.ChildNode < 0 || link.ChildNode >= names.Length)
            {
                continue;
            }

            var follower = names[link.ChildNode];
            var leader = (feModel.SkeletonBoneNames ?? Enumerable.Empty<string>()).Concat(names)
                .FirstOrDefault(name => Utils.StringToken.Get(name) == link.ParentHash);
            if (leader is null || !clothBones.Contains(follower) || feModel.IsGeneratedNodeName(follower))
            {
                continue;
            }

            softbodyChildren.Add(MakeNode("ClothFollowBone",
                ("name", $"merge_{link.ChildNode}"),
                ("leader_type", ClothFollowBoneLeaderTypeBoneMerge),
                ("leader_bone", leader),
                ("follower_bone", follower)));
        }
    }

    /// <summary>
    /// Declares a <c>ClothJointLock</c> on every locked skeleton joint <paramref name="needsLock"/> accepts,
    /// which is a joint no chain or cloth node of the export declares. The importer takes the feeder by its
    /// registered node name and sets the parent-link byte a joint's <c>lock_translation</c> sets, so the joint
    /// compiles into <c>m_LockToParent</c>, or into <c>m_LockToGoal</c> where it has no parent; a joint in
    /// <c>m_LockToGoal</c> that keeps a parent was locked by something else. Whether the lock is hard is not
    /// compiled.
    /// </summary>
    internal static void AddClothJointLocks(KVObject softbodyChildren, FeModel feModel, Func<int, string, bool> needsLock)
    {
        for (var node = 0; node < feModel.CtrlNames.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            var lockedWithoutParent = feModel.IsLockedToGoal(node) && node < feModel.SkelParents.Length
                && feModel.SkelParents[node] < 0;
            if (name.StartsWith('$') || !(lockedWithoutParent || feModel.IsLockedToParent(node))
                || !needsLock(node, name))
            {
                continue;
            }

            softbodyChildren.Add(MakeNode("ClothJointLock", ("feeder_bone", name), ("hard_lock", true)));
        }
    }

    // The only leader_type that compiles to an m_DynKinLinks entry; 2 and 3 are rejected outright.
    const int ClothFollowBoneLeaderTypeBone = 0;

    // Compiles to an m_BoneMergeLinks entry naming the leader by the hash of its bone name.
    const int ClothFollowBoneLeaderTypeBoneMerge = 1;

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

    // Wind speeds are authored in mph and compiled to units per second.
    const float ClothWindSpeedToUnits = 17.6f;

    const int ClothEffectTypeWind = 1;

    const int ClothEffectTypeStiffen = 3;

    const int ClothEffectTypeAddGravity = 4;

    const int ClothEffectTypeDampenVelocity = 6;

    /// <summary>
    /// Declares every effect the export can recreate. An effect whose parameters record a <c>Node</c> was authored
    /// under a static <c>ClothNode</c> rooted on that control bone, and is declared under the static node the export
    /// emits for that bone. Where it emits none, because a chain joint or another construct already claims the bone,
    /// a bare static <c>ClothNode</c> is declared for it: the compiler lands it on the node the bone already
    /// registers, without changing that node, and records it as the effect's parent.
    /// <para>
    /// Among the static nodes on that bone, one declared under a name of its own is preferred: a generated
    /// <c>$cloth_node_</c> element is where the effect was authored. The compiler rotates Strength by its parent
    /// node's own angles, so the effect's angles are expressed in that node's frame.
    /// </para>
    /// </summary>
    internal static void AddClothEffects(KVObject softbodyChildren, FeModel feModel, IReadOnlySet<string> availableMaps)
    {
        foreach (var effect in feModel.Effects)
        {
            var bone = effect.Params is not null && effect.Params.ContainsKey("Node")
                && effect.Params.GetInt32Property("Node") is var ctrl && ctrl >= 0 && ctrl < feModel.CtrlNames.Length
                && !feModel.CtrlNames[ctrl].StartsWith('$')
                    ? feModel.CtrlNames[ctrl]
                    : null;
            var parent = bone is null ? null : FindStaticClothNode(softbodyChildren, bone);
            var frame = parent?.GetSubCollection("angles") is { } angles
                ? EntityTransformHelper.EulerAnglesToQuaternion(angles.ToVector3())
                : Quaternion.Identity;

            if (MakeClothEffect(feModel, effect, availableMaps, frame) is not { } node)
            {
                continue;
            }

            if (bone is null)
            {
                softbodyChildren.Add(node);
                continue;
            }

            if (parent is null)
            {
                parent = MakeNode("ClothNode", ("name", bone + "_effects"), ("cloth_node_root_bone", bone),
                    ("is_static_node", true));
                softbodyChildren.Add(parent);
            }

            if (!parent.TryGetValue("children", out var children))
            {
                children = KVObject.Array();
                parent.Add("children", children);
            }

            children.Add(node);
        }
    }

    static KVObject? FindStaticClothNode(KVObject children, string rootBone)
    {
        var matches = new List<KVObject>();
        CollectStaticClothNodes(children, rootBone, matches);
        return matches.Find(node => !string.Equals(node.GetStringProperty("name"), rootBone, StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault();
    }

    static void CollectStaticClothNodes(KVObject children, string rootBone, List<KVObject> matches)
    {
        foreach (var (_, child) in children)
        {
            if (child.GetStringProperty("_class") == "ClothNode" && child.GetBooleanProperty("is_static_node")
                && string.Equals(child.GetStringProperty("cloth_node_root_bone"), rootBone, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(child);
            }

            if (child.TryGetValue("children", out var nested))
            {
                CollectStaticClothNodes(nested, rootBone, matches);
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

    /// <summary>
    /// Declares one compiled effect, its direction expressed in <paramref name="frame"/>, the rotation of the node
    /// it is declared under (identity at the top level).
    /// </summary>
    internal static KVObject? MakeClothEffect(FeModel feModel, FeModel.Effect effect, IReadOnlySet<string> availableMaps,
        Quaternion? frame = null)
    {
        var className = effect.Type switch
        {
            ClothEffectTypeWind => "ClothEffectWind",
            ClothEffectTypeStiffen => "ClothEffectStiffen",
            ClothEffectTypeAddGravity => "ClothEffectAddGravity",
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

        if (effect.Params.ContainsKey("Version"))
        {
            node.Add("cloth_effect_version", effect.Params.GetInt32Property("Version"));
        }

        switch (effect.Type)
        {
            case ClothEffectTypeWind:
                AddClothWindParams(node, effect.Params, frame ?? Quaternion.Identity);
                break;

            case ClothEffectTypeStiffen:
                node.Add("Stiffness", effect.Params.GetFloatProperty("Stiffness"));
                if (effect.Params.ContainsKey("BoneOverlay"))
                {
                    node.Add("BoneOverlay", effect.Params.GetFloatProperty("BoneOverlay"));
                }

                break;

            case ClothEffectTypeAddGravity:
                AddClothAddGravityParams(node, effect.Params, frame ?? Quaternion.Identity);
                break;

            default:
                node.Add("drag", effect.Params.GetFloatProperty("Drag"));
                break;
        }

        return node;
    }

    // Strength is the authored magnitude along the forward direction of the effect's angles, turned by its parent
    // node's own angles.
    static Vector3 ClothEffectStrength(KVObject parameters)
        => parameters.GetSubCollection("Strength") is { } s ? s.ToVector3() : default;

    static void AddClothEffectAngles(KVObject node, Vector3 strength, Quaternion frame)
    {
        if (strength != Vector3.Zero)
        {
            node.Add("angles", ToKVArray(EntityTransformHelper.ForwardDirectionToEulerAngles(
                Vector3.Transform(strength, Quaternion.Conjugate(frame)))));
        }
    }

    static void AddClothAddGravityParams(KVObject node, KVObject parameters, Quaternion frame)
    {
        var strength = ClothEffectStrength(parameters);
        node.Add("strength", strength.Length());
        AddClothEffectAngles(node, strength, frame);
    }

    static void AddClothWindParams(KVObject node, KVObject parameters, Quaternion frame)
    {
        var strength = ClothEffectStrength(parameters);
        node.Add("wind_speed_mph", strength.Length() / ClothWindSpeedToUnits);
        node.Add("time_multiplier", 1.0f);
        AddClothEffectAngles(node, strength, frame);

        var airToCloth = parameters.GetFloatProperty("AirToCloth");
        if (airToCloth > 0f)
        {
            node.Add("cloth_air_density", 1f / airToCloth);
        }

        var localSpace = parameters.ContainsKey("LocalSpace") ? parameters.GetFloatProperty("LocalSpace") : 0f;
        if (localSpace != 0f)
        {
            node.Add("local_space", localSpace);
        }

        node.Add("vortex_choppiness", parameters.GetFloatProperty("Choppiness"));

        if (parameters.ContainsKey("Algo"))
        {
            node.Add("underwater", parameters.GetInt32Property("Algo") == 1);
        }

        var vortices = parameters.GetArray("Vortices") ?? [];
        node.Add("vortex_count", vortices.Count);

        if (vortices.Count > 0)
        {
            node.Add("vortex_max_speed_mph", vortices[0].GetFloatProperty("MaxSpeed") / ClothWindSpeedToUnits);
            node.Add("vortex_cell_size", vortices[0].GetFloatProperty("MaxCell"));
        }
    }

    // Soft-body / cloth physics (m_pFeModel): reconstruct editable ModelDoc cloth source so the model
    // recompiles into a working FeModel PHYS block AND opens in ModelDoc (no binary transplant).
    // Phase 1 recovers bone-chain cloth as ClothChain nodes. Phase 2 recovers the cloth SHEET as a
    // ClothProxyMeshFile + proxy DMX.
    bool EmitCloth(FeModel feModel, KVObject rootChildren)
    {
        var boneChains = feModel.BuildBoneChains((chain, hasOtherChains) => ClothChainVersion(feModel, chain, hasOtherChains));

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
