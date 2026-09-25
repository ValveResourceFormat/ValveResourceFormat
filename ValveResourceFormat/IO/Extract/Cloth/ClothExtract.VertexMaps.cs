using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // A selection solved as a volume carries its strength and the node it takes its scale from. Both are
    // authored on the container, and the volumetric strength also decides the covered nodes' masses.
    private static void AddClothVertexMapAttributes(KVObject mapNode, FeModel feModel, string mapName,
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

    /// <summary>
    /// Declares a <c>ClothVertexMap</c> for every selection solved as a volume over chain joints. The compiler
    /// reads a container's <c>volumetric_solve</c> and <c>scale_source_node</c> only through the <c>data.nodes</c>
    /// table naming its members, never through a joint's own <c>vertex_map</c>, so the table lists every covered
    /// joint at its membership weight. A selection that also covers a sheet vertex, a free cloth node or any
    /// other named node is left to the containers those phases declare.
    /// </summary>
    internal static void AddClothChainVolumetricMaps(KVObject softbodyChildren, FeModel feModel,
        IEnumerable<FeModel.BoneChain> chains)
    {
        var joints = chains.SelectMany(static chain => chain.Joints).ToList();
        var jointNames = joints.Select(static joint => joint.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var map in feModel.VertexMaps)
        {
            if (map.VolumetricSolveStrength <= 0f || CoversNodeOutsideChains(feModel, map, jointNames))
            {
                continue;
            }

            var members = KVObject.Collection();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var joint in joints)
            {
                var weight = map.WeightOf(joint.Node);
                if (weight <= 0f || !listed.Add(joint.Name))
                {
                    continue;
                }

                if (weight >= 1f)
                {
                    members.Add(joint.Name, true);
                }
                else
                {
                    var member = KVObject.Collection();
                    member.Add("weight", weight);
                    members.Add(joint.Name, member);
                }
            }

            if (listed.Count == 0)
            {
                continue;
            }

            var (mapNode, _) = MakeListNode("ClothVertexMap");
            mapNode.Add("name", map.Name);
            AddClothVertexMapAttributes(mapNode, feModel, map.Name, proxyNodeNames: null);
            var data = KVObject.Collection();
            data.Add("nodes", members);
            mapNode.Add("data", data);
            softbodyChildren.Add(mapNode);
        }
    }

    private static bool CoversNodeOutsideChains(FeModel feModel, FeModel.VertexMap map, HashSet<string> jointNames)
    {
        for (var node = map.VertexBase; node < map.VertexBase + map.VertexCount && node < feModel.CtrlNames.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (map.WeightOf(node) > 0f && (name.StartsWith("$cloth_", StringComparison.Ordinal)
                || (!name.StartsWith('$') && !jointNames.Contains(name))))
            {
                return true;
            }
        }

        return false;
    }

    // Puts a free cloth node into the ClothVertexMap containers of every selection covering it, and
    // returns where the node itself goes. Each container lists its members in the data.nodes table the
    // ClothNodeListEditor keeps, which is membership on its own (with a partial weight where the
    // selection has one) and the only route on which the compiler reads the container's
    // volumetric_solve and scale_source_node. A node covered by exactly one selection is also parented
    // under that container, the grouping the "Add Cloth Vertex Map" wizard builds, unless the caller
    // keeps it flat; a node in several selections stays flat, a child having one parent.
    private static Func<int, bool, KVObject> ClothVertexMapFolders(FeModel feModel, KVObject clothFolderChildren)
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
}
