using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
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

    // The collision layers a node's mask carries its own bit for; the rest of the mask is not per layer.
    private const int ClothCollisionLayers = 4;

    // The only leader_type that compiles to an m_DynKinLinks entry; 2 and 3 are rejected outright.
    private const int ClothFollowBoneLeaderTypeBone = 0;

    // Compiles to an m_BoneMergeLinks entry naming the leader by the hash of its bone name.
    private const int ClothFollowBoneLeaderTypeBoneMerge = 1;

    // Wind speeds are authored in mph and compiled to units per second.
    private const float ClothWindSpeedToUnits = 17.6f;

    private const int ClothEffectTypeWind = 1;

    private const int ClothEffectTypeStiffen = 3;

    private const int ClothEffectTypeAddGravity = 4;

    private const int ClothEffectTypeDampenVelocity = 6;

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
        var declaredMaps = new HashSet<string>(availableMaps, StringComparer.OrdinalIgnoreCase);
        CollectDeclaredVertexMaps(softbodyChildren, declaredMaps);
        availableMaps = declaredMaps;

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

    /// <summary>The <c>goal_strength</c> a <c>ClothNode</c> declared without one compiles.</summary>
    private const float ClothNodeDefaultGoalStrength = 0.6f;

    /// <summary>The <c>goal_damping</c> a <c>ClothNode</c> declared without one compiles.</summary>
    private const float ClothNodeDefaultGoalDamping = 0.3f;

    /// <summary>How far a recovered goal attribute or gravity may sit from a <c>ClothNode</c> default and still read as it.</summary>
    private const float ClothNodeDefaultTolerance = 1e-3f;

    /// <summary>
    /// Declares a bare static <c>ClothNode</c> on every collision-shape parent bone whose compiled goal pair and gravity are
    /// the defaults such a node compiles, where nothing the document already declares carries them: no static
    /// <c>ClothNode</c> and no <c>ClothChain</c> joint names the bone. A shape registers its parent bone with no goal
    /// attraction, and so does a sheet anchor, so those defaults on a shape parent come from a declaration of its own. One
    /// declared after the bone is registered compiles the same integrator and leaves every other key, node order included,
    /// as it was. It runs after <see cref="AddClothEffects"/>, whose own static nodes it leaves alone.
    /// </summary>
    internal static void AddShapeParentDefaultClothNodes(KVObject softbodyChildren, FeModel feModel)
    {
        var chainJoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectChainJointNames(softbodyChildren, chainJoints);

        var bones = new List<(int Node, string Bone)>();
        foreach (var bone in CollisionShapeParentBones(feModel))
        {
            if (bone is null || chainJoints.Contains(bone) || FindStaticClothNode(softbodyChildren, bone) is not null)
            {
                continue;
            }

            var node = Array.IndexOf(feModel.CtrlNames, bone);
            if (node >= 0 && feModel.IsStatic(node) && CompilesClothNodeDefaults(feModel, node))
            {
                bones.Add((node, bone));
            }
        }

        foreach (var (_, bone) in bones.OrderBy(static entry => entry.Node))
        {
            softbodyChildren.Add(MakeNode("ClothNode", ("name", bone), ("cloth_node_root_bone", bone),
                ("is_static_node", true)));
        }
    }

    private static bool CompilesClothNodeDefaults(FeModel feModel, int node)
    {
        var integrator = feModel.GetIntegrator(node);
        return integrator.PointDamping == 0f
            && MathF.Abs(integrator.Gravity - ClothSourceBaseGravity) <= ClothNodeDefaultTolerance
            && MathF.Abs(feModel.GoalStrengthPaint(integrator.ForceAttraction) - ClothNodeDefaultGoalStrength)
                <= ClothNodeDefaultTolerance
            && MathF.Abs(feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction)
                - ClothNodeDefaultGoalDamping) <= ClothNodeDefaultTolerance;
    }

    private static void CollectChainJointNames(KVObject children, HashSet<string> joints)
    {
        foreach (var (_, child) in children)
        {
            if (child.GetStringProperty("_class") == "ClothChain" && child.TryGetValue("chain", out var chain)
                && chain.TryGetValue("joints", out var list))
            {
                foreach (var (_, joint) in list)
                {
                    if (joint.GetStringProperty("joint_name") is { Length: > 0 } name)
                    {
                        joints.Add(name);
                    }
                }
            }

            if (child.TryGetValue("children", out var nested))
            {
                CollectChainJointNames(nested, joints);
            }
        }
    }

    /// <summary>
    /// Adds every selection the document already declares: a <c>ClothVertexMap</c> container by its name, and each
    /// <c>ClothChain</c> joint's <c>vertex_map</c> entries by their bare names.
    /// </summary>
    private static void CollectDeclaredVertexMaps(KVObject children, HashSet<string> maps)
    {
        foreach (var (_, child) in children)
        {
            var kind = child.GetStringProperty("_class");
            if (kind == "ClothVertexMap" && child.GetStringProperty("name") is { Length: > 0 } name)
            {
                maps.Add(name);
            }
            else if (kind == "ClothChain" && child.TryGetValue("chain", out var chain) && chain.TryGetValue("joints", out var joints))
            {
                foreach (var (_, joint) in joints)
                {
                    if (joint.GetStringProperty("vertex_map") is { Length: > 0 } entries)
                    {
                        foreach (var entry in entries.Split(','))
                        {
                            maps.Add(FeModel.VertexMapName(entry.Trim()));
                        }
                    }
                }
            }

            if (child.TryGetValue("children", out var nested))
            {
                CollectDeclaredVertexMaps(nested, maps);
            }
        }
    }

    private static KVObject? FindStaticClothNode(KVObject children, string rootBone)
    {
        var matches = new List<KVObject>();
        CollectStaticClothNodes(children, rootBone, matches);
        return matches.Find(node => !string.Equals(node.GetStringProperty("name"), rootBone, StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault();
    }

    private static void CollectStaticClothNodes(KVObject children, string rootBone, List<KVObject> matches)
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
    private HashSet<string> AvailableVertexMaps(FeModel feModel, List<FeModel.BoneChain> chains)
    {
        var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, _, proxy) in ProxyMeshes)
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
    private static Vector3 ClothEffectStrength(KVObject parameters)
        => parameters.GetSubCollection("Strength") is { } s ? s.ToVector3() : default;

    private static void AddClothEffectAngles(KVObject node, Vector3 strength, Quaternion frame)
    {
        if (strength != Vector3.Zero)
        {
            node.Add("angles", ToKVArray(EntityTransformHelper.ForwardDirectionToEulerAngles(
                Vector3.Transform(strength, Quaternion.Conjugate(frame)))));
        }
    }

    private static void AddClothAddGravityParams(KVObject node, KVObject parameters, Quaternion frame)
    {
        var strength = ClothEffectStrength(parameters);
        node.Add("strength", strength.Length());
        AddClothEffectAngles(node, strength, frame);
    }

    private static void AddClothWindParams(KVObject node, KVObject parameters, Quaternion frame)
    {
        var strength = ClothEffectStrength(parameters);
        var vortices = parameters.GetArray("Vortices") ?? [];

        // Time multiplier scales every compiled speed, and vortices are only written for a positive max speed,
        // so vortices compiled at zero speed were authored with a zero time multiplier.
        var stilled = vortices.Count > 0 && vortices.All(vortex => vortex.GetFloatProperty("MaxSpeed") == 0f);

        node.Add("wind_speed_mph", strength.Length() / ClothWindSpeedToUnits);
        node.Add("time_multiplier", stilled ? 0f : 1f);
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

        node.Add("vortex_count", vortices.Count);

        if (vortices.Count > 0)
        {
            node.Add("vortex_max_speed_mph", stilled ? 1f : vortices[0].GetFloatProperty("MaxSpeed") / ClothWindSpeedToUnits);
            node.Add("vortex_cell_size", vortices[0].GetFloatProperty("MaxCell"));
        }
    }
}
