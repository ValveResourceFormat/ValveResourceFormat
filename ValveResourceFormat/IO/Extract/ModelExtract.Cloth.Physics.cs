using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
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
}
