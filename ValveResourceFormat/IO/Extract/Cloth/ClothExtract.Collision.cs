using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>Declares the cloth collision shapes and returns the names it gave them, in declaration order.</summary>
    internal static List<string> AddClothCollisionShapes(KVObject softbodyChildren, ClothReconstruction cloth)
    {
        var names = new List<string>();
        var shapes = cloth.CollisionShapes;
        var kinds = new[]
        {
            shapes.Capsules
                .Select(c => (c.Priority, Node: ParentBoneNode(cloth, c.ParentBone), Shape: MakeClothShapeCapsule(c))),
            shapes.Spheres
                .Select(s => (s.Priority, Node: ParentBoneNode(cloth, s.ParentBone), Shape: MakeClothShapeSphere(s))),
            shapes.Boxes
                .Select(b => (b.Priority, Node: ParentBoneNode(cloth, b.ParentBone), Shape: MakeClothShapeBox(b))),
        }
        .SelectMany(static entries => entries.GroupBy(static entry => entry.Priority)
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

        foreach (var shape in PlanarizedShapesInClaimOrder(cloth))
        {
            names.Add(shape.GetStringProperty("name"));
            softbodyChildren.Add(shape);
        }

        return names;
    }

    /// <summary>
    /// The planarized collision shapes ordered so each keeps its original <c>m_CollisionPlanes</c> entries. The first shape
    /// claims every node it reaches and later shapes win the rest, so the smallest leads and the others follow largest first.
    /// </summary>
    internal static List<KVObject> PlanarizedShapesInClaimOrder(ClothReconstruction cloth)
    {
        var shapes = cloth.CollisionShapes.PlanarizedCapsules
            .Select(c => (c.PlanarizePlanes, c.PlanarizeOwnPlanes, Shape: MakeClothShapeCapsule(c)))
            .Concat(cloth.CollisionShapes.PlanarizedBoxes
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
    /// Declares the anti-tunnel collider group behind <c>m_AntiTunnelBytecode</c>; it needs both shapes and cloth, as a
    /// group missing either compiles to nothing.
    /// </summary>
    internal static void AddClothAntiTunnelGroup(KVObject softbodyChildren, ClothReconstruction cloth,
        List<string> shapeNames, List<string> clothNames)
    {
        if (cloth.Fe.AntiTunnelBytecode.Length == 0 || shapeNames.Count == 0 || clothNames.Count == 0)
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

    /// <summary>A shape parent bone's control node, or int.MaxValue so shapes without one sort last.</summary>
    private static int ParentBoneNode(ClothReconstruction cloth, string? parentBone)
        => parentBone is not null && LookupsOf(cloth).NodeByName.TryGetValue(parentBone, out var node) ? node : int.MaxValue;

    /// <summary>Declares every anti-tunnel probe as a top-level <c>ClothAntiTunnelProbe</c>, in <c>Begin</c> order.</summary>
    internal static void AddClothAntiTunnelProbes(KVObject rootChildren, ClothReconstruction cloth, IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        foreach (var i in Enumerable.Range(0, cloth.Fe.AntiTunnelProbes.Length)
            .OrderBy(i => cloth.Fe.AntiTunnelProbes[i].Begin))
        {
            var probe = cloth.Fe.AntiTunnelProbes[i];
            var sourceName = AuthoredNodeName(cloth, probe.ProbeNode, proxyNodeNames);
            if (sourceName is null)
            {
                continue;
            }

            var targetNames = new List<string>();
            for (var t = probe.Begin; t < probe.Begin + probe.Count && t < cloth.Fe.AntiTunnelTargetNodes.Length; t++)
            {
                if (AuthoredNodeName(cloth, cloth.Fe.AntiTunnelTargetNodes[t], proxyNodeNames) is { } targetName)
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

    private static KVObject MakeClothShapeBox(CollisionBox box)
    {
        var node = MakeClothShape("ClothShapeBox", box.Planarize ? "_clothPlanarizedBox" : "_clothBox", box.ParentBone,
            box.CollisionMask, box.Priority, box.VertexMap, box.Inverted, box.Planarize);
        node.Add("recenter_on_parent_bone", false);
        node.Add("origin", ToKVArray(box.Origin));
        node.Add("angles", ToKVArray(EntityTransformHelper.ToEulerAngles(box.Rotation)));
        node.Add("dimensions", ToKVArray(box.Size * 2f));
        return node;
    }

    private static KVObject MakeClothShapeCapsule(CollisionCapsule capsule)
    {
        var node = MakeClothShape("ClothShapeCapsule", capsule.Planarize ? "_clothPlanarizedCapsule" : "_clothCapsule",
            capsule.ParentBone, capsule.CollisionMask, capsule.Priority, capsule.VertexMap, capsule.Inverted, capsule.Planarize);
        node.Add("radius0", capsule.Radius0);
        node.Add("radius1", capsule.Radius1);
        node.Add("point0", ToKVArray(capsule.Point0));
        node.Add("point1", ToKVArray(capsule.Point1));
        return node;
    }

    private static KVObject MakeClothShapeSphere(CollisionSphere sphere)
    {
        var node = MakeClothShape("ClothShapeSphere", "_clothSphere", sphere.ParentBone, sphere.CollisionMask, sphere.Priority,
            sphere.VertexMap, sphere.Inverted, planarize: false);
        node.Add("radius", sphere.Radius);
        node.Add("center", ToKVArray(sphere.Center));
        return node;
    }

    /// <summary>The keys every cloth collision shape carries ahead of its geometry. A zero mask means all layers.</summary>
    private static KVObject MakeClothShape(string className, string nameSuffix, string? parentBone, int collisionMask,
        int priority, string? vertexMap, bool inverted, bool planarize)
    {
        var node = MakeNode(className, ("name", (parentBone ?? "cloth") + nameSuffix), ("parent_bone", parentBone ?? string.Empty));
        AddCollisionLayerFlags(node, "cloth_collision_layer", collisionMask == 0 ? ClothAllCollisionLayers : collisionMask);
        node.Add("cloth_collision_priority", priority);
        node.Add("vertex_map", vertexMap ?? string.Empty);
        node.Add("inverted_collision", inverted);
        node.Add("planarize", planarize);
        node.Add("bounciness", 0.0f);
        return node;
    }
}
