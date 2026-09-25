using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>Every bone a cloth collision shape hangs off, which the shape registers as a control node itself.</summary>
    private static HashSet<string?> CollisionShapeParentBones(FeModel feModel)
        => feModel.BuildCollisionCapsules().Select(static c => c.ParentBone)
            .Concat(feModel.BuildPlanarizeCapsules().Select(static c => c.ParentBone))
            .Concat(feModel.BuildPlanarizeBoxes().Select(static b => b.ParentBone))
            .Concat(feModel.BuildCollisionSpheres().Select(static s => s.ParentBone))
            .Concat(feModel.BuildCollisionBoxes().Select(static b => b.ParentBone))
            .Where(static n => n is not null)
            .ToHashSet();

    /// <summary>Declares the cloth collision shapes and returns the names it gave them, in declaration order.</summary>
    internal static List<string> AddClothCollisionShapes(KVObject softbodyChildren, FeModel feModel)
    {
        var names = new List<string>();
        var kinds = new[]
        {
            feModel.BuildCollisionCapsules()
                .Select(c => (c.Priority, Node: ParentBoneNode(feModel, c.ParentBone), Shape: MakeClothShapeCapsule(c))),
            feModel.BuildCollisionSpheres()
                .Select(s => (s.Priority, Node: ParentBoneNode(feModel, s.ParentBone), Shape: MakeClothShapeSphere(s))),
            feModel.BuildCollisionBoxes()
                .Select(b => (b.Priority, Node: ParentBoneNode(feModel, b.ParentBone), Shape: MakeClothShapeBox(b))),
        }
        .SelectMany(static shapes => shapes.GroupBy(static entry => entry.Priority)
            .Select(static group => group.Select(static entry => (entry.Node, entry.Shape)).ToList()))
        .ToArray();

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

        foreach (var shape in PlanarizedShapesInClaimOrder(feModel))
        {
            names.Add(shape.GetStringProperty("name"));
            softbodyChildren.Add(shape);
        }

        return names;
    }

    /// <summary>
    /// The model's planarized collision shapes in the order that leaves each one its original <c>m_CollisionPlanes</c>
    /// entries: the first shape claims every node it reaches and later shapes win the rest, so the smallest shape leads
    /// and the others follow largest first.
    /// </summary>
    internal static List<KVObject> PlanarizedShapesInClaimOrder(FeModel feModel)
    {
        var shapes = feModel.BuildPlanarizeCapsules()
            .Select(c => (c.PlanarizePlanes, c.PlanarizeOwnPlanes, Shape: MakeClothShapeCapsule(c)))
            .Concat(feModel.BuildPlanarizeBoxes()
                .Select(b => (b.PlanarizePlanes, b.PlanarizeOwnPlanes, Shape: MakeClothShapeBox(b))))
            .OrderByDescending(static entry => entry.PlanarizePlanes)
            .ThenBy(static entry => entry.PlanarizeOwnPlanes)
            .Select(static entry => entry.Shape)
            .ToList();

        if (shapes.Count > 1)
        {
            var smallest = shapes[^1];
            shapes.RemoveAt(shapes.Count - 1);
            shapes.Insert(0, smallest);
        }

        return shapes;
    }

    /// <summary>
    /// Declares the anti-tunnel collider group that compiles to <c>m_AntiTunnelBytecode</c>, naming both the shapes and
    /// the cloth; a group missing either compiles to nothing.
    /// </summary>
    internal static void AddClothAntiTunnelGroup(KVObject softbodyChildren, FeModel feModel,
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

        softbodyChildren.Add(MakeNode("ClothAntiTunnelColliderGroup",
            ("name", "cloth_antitunnel_group0"),
            ("vertex_map", string.Empty),
            ("import_cloth_collision_layer0", false),
            ("import_cloth_collision_layer1", false),
            ("import_cloth_collision_layer2", false),
            ("import_cloth_collision_layer3", false),
            ("data", MakeNodeTable(nodes))));
    }

    /// <summary>A shape parent bone's control node, or int.MaxValue where it is not one.</summary>
    private static int ParentBoneNode(FeModel feModel, string? parentBone)
    {
        var node = parentBone is null ? -1 : Array.IndexOf(feModel.CtrlNames, parentBone);
        return node < 0 ? int.MaxValue : node;
    }

    /// <summary>
    /// Declares every anti-tunnel probe as a top-level <c>ClothAntiTunnelProbe</c>, in <c>Begin</c> order, with its targets
    /// in compiled order.
    /// </summary>
    internal static void AddClothAntiTunnelProbes(KVObject rootChildren, FeModel feModel, IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        foreach (var i in Enumerable.Range(0, feModel.AntiTunnelProbes.Length)
            .OrderBy(i => feModel.AntiTunnelProbes[i].Begin))
        {
            var probe = feModel.AntiTunnelProbes[i];
            var sourceName = AuthoredNodeName(feModel, probe.ProbeNode, proxyNodeNames);
            if (sourceName is null)
            {
                continue;
            }

            var targetNames = new List<string>();
            for (var t = probe.Begin; t < probe.Begin + probe.Count && t < feModel.AntiTunnelTargetNodes.Length; t++)
            {
                if (AuthoredNodeName(feModel, feModel.AntiTunnelTargetNodes[t], proxyNodeNames) is { } targetName)
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

    /// <summary>A <c>ClothAntiTunnelProbe</c> from <paramref name="sourceNode"/> to each distinct target.</summary>
    private static KVObject MakeClothAntiTunnelProbe(string name, string sourceNode, bool animSource, float weight,
        float activationDistance, IReadOnlyList<string> targetNames)
    {
        var nodes = KVObject.Collection();
        foreach (var targetName in targetNames.Distinct())
        {
            nodes.Add(targetName, true);
        }

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
            ("data", MakeNodeTable(nodes)));
    }

    /// <summary>The <c>ClothShapeBox</c> declaring a compiled collision box.</summary>
    private static KVObject MakeClothShapeBox(FeModel.CollisionBox box)
    {
        var node = MakeNode("ClothShapeBox",
            ("name", (box.ParentBone ?? "cloth") + (box.Planarize ? "_clothPlanarizedBox" : "_clothBox")),
            ("parent_bone", box.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, box.CollisionMask);
        node.Add("cloth_collision_priority", box.Priority);
        node.Add("vertex_map", box.VertexMap ?? string.Empty);
        node.Add("inverted_collision", box.Inverted);
        node.Add("planarize", box.Planarize);
        node.Add("bounciness", 0.0f);
        node.Add("recenter_on_parent_bone", false);
        node.Add("origin", ToKVArray(box.Origin));
        node.Add("angles", ToKVArray(EntityTransformHelper.ToEulerAngles(box.Rotation)));
        node.Add("dimensions", ToKVArray(box.Size * 2f));
        return node;
    }

    /// <summary>The <c>ClothShapeCapsule</c> declaring a compiled collision capsule.</summary>
    private static KVObject MakeClothShapeCapsule(FeModel.CollisionCapsule capsule)
    {
        var node = MakeNode("ClothShapeCapsule",
            ("name", (capsule.ParentBone ?? "cloth") + (capsule.Planarize ? "_clothPlanarizedCapsule" : "_clothCapsule")),
            ("parent_bone", capsule.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, capsule.CollisionMask);
        node.Add("cloth_collision_priority", capsule.Priority);
        node.Add("vertex_map", capsule.VertexMap ?? string.Empty);
        node.Add("inverted_collision", capsule.Inverted);
        node.Add("planarize", capsule.Planarize);
        node.Add("bounciness", 0.0f);
        node.Add("radius0", capsule.Radius0);
        node.Add("radius1", capsule.Radius1);
        node.Add("point0", ToKVArray(capsule.Point0));
        node.Add("point1", ToKVArray(capsule.Point1));
        return node;
    }

    /// <summary>The <c>ClothShapeSphere</c> declaring a compiled collision sphere.</summary>
    private static KVObject MakeClothShapeSphere(FeModel.CollisionSphere sphere)
    {
        var node = MakeNode("ClothShapeSphere",
            ("name", (sphere.ParentBone ?? "cloth") + "_clothSphere"),
            ("parent_bone", sphere.ParentBone ?? string.Empty));
        AddClothCollisionLayers(node, sphere.CollisionMask);
        node.Add("cloth_collision_priority", sphere.Priority);
        node.Add("vertex_map", sphere.VertexMap ?? string.Empty);
        node.Add("inverted_collision", sphere.Inverted);
        node.Add("planarize", false);
        node.Add("bounciness", 0.0f);
        node.Add("radius", sphere.Radius);
        node.Add("center", ToKVArray(sphere.Center));
        return node;
    }

    /// <summary>Adds the collision layer keys of a shape mask, where zero means all layers.</summary>
    private static void AddClothCollisionLayers(KVObject node, int collisionMask)
        => AddCollisionLayerFlags(node, "cloth_collision_layer", collisionMask == 0 ? 0xF : collisionMask);
}
