using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // Leader type 0 compiles to an m_DynKinLinks entry, 1 to an m_BoneMergeLinks entry naming the leader by its name hash.
    private const int ClothFollowBoneLeaderTypeBone = 0;
    private const int ClothFollowBoneLeaderTypeBoneMerge = 1;

    // Wind speeds are authored in mph and compiled to units per second.
    private const float ClothWindSpeedToUnits = 17.6f;

    private const int ClothEffectTypeWind = 1;
    private const int ClothEffectTypeStiffen = 3;
    private const int ClothEffectTypeAddGravity = 4;
    private const int ClothEffectTypeDampenVelocity = 6;

    // The goal pair a ClothNode declared without one compiles to.
    private const float ClothNodeDefaultGoalStrength = 0.6f;
    private const float ClothNodeDefaultGoalDamping = 0.3f;
    private const float ClothNodeDefaultTolerance = 1e-3f;

    /// <summary>
    /// Re-declares <see cref="FeModel.DynKinLinks"/> and <see cref="FeModel.BoneMergeLinks"/> as <c>ClothFollowBone</c>
    /// nodes in compiled order, skipping a link whose cloth bones are not in <paramref name="clothBones"/>.
    /// </summary>
    internal static void AddClothFollowBones(KVObject softbodyChildren, ClothReconstruction cloth, HashSet<string> clothBones)
    {
        var names = cloth.Fe.CtrlName;
        foreach (var link in cloth.Fe.DynKinLinks)
        {
            if (link.Parent < 0 || link.Parent >= names.Length || link.Child < 0 || link.Child >= names.Length)
            {
                continue;
            }

            var leader = names[link.Parent];
            var follower = names[link.Child];
            if (!clothBones.Contains(leader) || !clothBones.Contains(follower)
                || cloth.IsGeneratedNodeName(leader) || cloth.IsGeneratedNodeName(follower))
            {
                continue;
            }

            softbodyChildren.Add(MakeNode("ClothFollowBone",
                ("name", $"follow_{link.Parent}_{link.Child}"),
                ("leader_type", ClothFollowBoneLeaderTypeBone),
                ("leader_bone", leader),
                ("follower_bone", follower)));
        }

        foreach (var link in cloth.Fe.BoneMergeLinks)
        {
            if (link.ChildNode < 0 || link.ChildNode >= names.Length)
            {
                continue;
            }

            var follower = names[link.ChildNode];
            if (!LookupsOf(cloth).BoneByHash.TryGetValue(link.ParentHash, out var leader)
                || !clothBones.Contains(follower) || cloth.IsGeneratedNodeName(follower))
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
    /// Declares a hard <c>ClothJointLock</c> on every joint locked to its parent, or to its goal without a parent, that
    /// <paramref name="needsLock"/> accepts.
    /// </summary>
    internal static void AddClothJointLocks(KVObject softbodyChildren, ClothReconstruction cloth, Func<int, string, bool> needsLock)
    {
        for (var node = 0; node < cloth.Fe.CtrlName.Length; node++)
        {
            var name = cloth.Fe.CtrlName[node];
            var lockedWithoutParent = cloth.Index.IsLockedToGoal(node) && node < cloth.SkelParents.Length
                && cloth.SkelParents[node] < 0;
            if (name.StartsWith('$') || !(lockedWithoutParent || cloth.Index.IsLockedToParent(node))
                || !needsLock(node, name))
            {
                continue;
            }

            softbodyChildren.Add(MakeNode("ClothJointLock", ("feeder_bone", name), ("hard_lock", true)));
        }
    }

    /// <summary>
    /// Declares every effect the export can recreate. An effect whose <c>Node</c> parameter names a control bone is
    /// declared under a static <c>ClothNode</c> on that bone, and its angles are expressed in that node's frame.
    /// </summary>
    internal static void AddClothEffects(KVObject softbodyChildren, ClothReconstruction cloth, IReadOnlySet<string> availableMaps)
    {
        var declaredMaps = new HashSet<string>(availableMaps, StringComparer.OrdinalIgnoreCase);
        CollectDeclaredVertexMaps(softbodyChildren, declaredMaps);

        foreach (var effect in cloth.Fe.Effects)
        {
            var bone = effect.Params is not null && effect.Params.ContainsKey("Node")
                && effect.Params.GetInt32Property("Node") is var ctrl && ctrl >= 0 && ctrl < cloth.Fe.CtrlName.Length
                && !cloth.Fe.CtrlName[ctrl].StartsWith('$')
                    ? cloth.Fe.CtrlName[ctrl]
                    : null;
            var parent = bone is null ? null : FindStaticClothNode(softbodyChildren, bone);
            var frame = parent?.GetSubCollection("angles") is { } angles
                ? EntityTransformHelper.EulerAnglesToQuaternion(angles.ToVector3())
                : Quaternion.Identity;

            if (MakeClothEffect(cloth, effect, declaredMaps, frame) is not { } node)
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
                parent = MakeStaticClothNode(bone + "_effects", bone);
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

    /// <summary>
    /// Declares a bare static <c>ClothNode</c> on every collision-shape parent bone that compiles the <c>ClothNode</c>
    /// defaults and is not already declared by a static <c>ClothNode</c> or a <c>ClothChain</c> joint.
    /// </summary>
    internal static void AddShapeParentDefaultClothNodes(KVObject softbodyChildren, ClothReconstruction cloth)
    {
        var chainJoints = new HashSet<string>(
            ChainJointRows(softbodyChildren).Select(static joint => joint.GetStringProperty("joint_name"))
                .Where(static name => !string.IsNullOrEmpty(name)),
            StringComparer.OrdinalIgnoreCase);

        var bones = new List<(int Node, string Bone)>();
        foreach (var bone in cloth.CollisionShapes.ParentBones)
        {
            if (chainJoints.Contains(bone) || FindStaticClothNode(softbodyChildren, bone) is not null)
            {
                continue;
            }

            if (LookupsOf(cloth).NodeByName.TryGetValue(bone, out var node) && cloth.Index.IsStatic(node)
                && CompilesClothNodeDefaults(cloth, node))
            {
                bones.Add((node, bone));
            }
        }

        foreach (var (_, bone) in bones.OrderBy(static entry => entry.Node))
        {
            softbodyChildren.Add(MakeStaticClothNode(bone, bone));
        }
    }

    private static bool CompilesClothNodeDefaults(ClothReconstruction cloth, int node)
    {
        var integrator = cloth.Index.GetIntegrator(node);
        var paint = NodePaint.Of(cloth, node);
        return integrator.PointDamping == 0f
            && MathF.Abs(integrator.Gravity - ClothReconstruction.ClothSourceBaseGravity) <= ClothNodeDefaultTolerance
            && MathF.Abs(paint.GoalStrength - ClothNodeDefaultGoalStrength) <= ClothNodeDefaultTolerance
            && MathF.Abs(paint.GoalDamping - ClothNodeDefaultGoalDamping) <= ClothNodeDefaultTolerance;
    }

    /// <summary>Every node of a KV node tree, each before its own children.</summary>
    private static IEnumerable<KVObject> EnumerateTree(KVObject children)
    {
        foreach (var (_, child) in children)
        {
            yield return child;

            if (child.TryGetValue("children", out var nested))
            {
                foreach (var descendant in EnumerateTree(nested))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>The joint rows of every <c>ClothChain</c> in a KV node tree.</summary>
    private static IEnumerable<KVObject> ChainJointRows(KVObject children)
    {
        foreach (var child in EnumerateTree(children))
        {
            if (child.GetStringProperty("_class") == "ClothChain" && child.TryGetValue("chain", out var chain)
                && chain.TryGetValue("joints", out var joints))
            {
                foreach (var (_, joint) in joints)
                {
                    yield return joint;
                }
            }
        }
    }

    /// <summary>Adds every <c>ClothVertexMap</c> name and every bare name a chain joint's <c>vertex_map</c> lists.</summary>
    private static void CollectDeclaredVertexMaps(KVObject children, HashSet<string> maps)
    {
        foreach (var child in EnumerateTree(children))
        {
            if (child.GetStringProperty("_class") == "ClothVertexMap" && child.GetStringProperty("name") is { Length: > 0 } name)
            {
                maps.Add(name);
            }
        }

        foreach (var joint in ChainJointRows(children))
        {
            if (joint.GetStringProperty("vertex_map") is { Length: > 0 } entries)
            {
                foreach (var entry in entries.Split(','))
                {
                    maps.Add(ClothReconstruction.VertexMapName(entry.Trim()));
                }
            }
        }
    }

    /// <summary>The static <c>ClothNode</c> on <paramref name="rootBone"/>, preferring one named otherwise than the bone.</summary>
    private static KVObject? FindStaticClothNode(KVObject children, string rootBone)
    {
        var matches = EnumerateTree(children)
            .Where(child => child.GetStringProperty("_class") == "ClothNode" && child.GetBooleanProperty("is_static_node")
                && string.Equals(child.GetStringProperty("cloth_node_root_bone"), rootBone, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Find(node => !string.Equals(node.GetStringProperty("name"), rootBone, StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault();
    }

    /// <summary>The bare names of the vertex maps painted into a proxy mesh or named by a chain joint.</summary>
    private HashSet<string> AvailableVertexMaps(ClothReconstruction cloth, List<BoneChain> chains)
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
            if (cloth.GetVertexMapNames(joint.Node) is { } names)
            {
                foreach (var name in names.Split(','))
                {
                    maps.Add(ClothReconstruction.VertexMapName(name.Trim()));
                }
            }
        }

        return maps;
    }

    /// <summary>
    /// Declares one compiled effect, with its direction expressed in <paramref name="frame"/>, the rotation of the node it
    /// is declared under.
    /// </summary>
    internal static KVObject? MakeClothEffect(ClothReconstruction cloth, FeModel.FeEffectDesc effect, IReadOnlySet<string> availableMaps,
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
        foreach (var map in cloth.VertexMaps)
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
