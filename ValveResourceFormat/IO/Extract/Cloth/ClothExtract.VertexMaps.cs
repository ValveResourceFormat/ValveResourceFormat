using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // A volume-solved selection states its strength and scale source node on its container.
    private static void AddClothVertexMapAttributes(KVObject mapNode, FeModel feModel, string mapName,
        IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        var map = feModel.VertexMaps.FirstOrDefault(m => m.Name == mapName);
        if (map.Name != mapName || map.VolumetricSolveStrength <= 0f)
        {
            return;
        }

        mapNode.Add("volumetric_solve", map.VolumetricSolveStrength);

        if (AuthoredNodeName(feModel, map.ScaleSourceNode, proxyNodeNames) is { } scaleSource)
        {
            mapNode.Add("scale_source_node", scaleSource);
        }
    }

    /// <summary>
    /// Declares a <c>ClothVertexMap</c> listing its members in <c>data.nodes</c> for every volume-solved selection that
    /// covers only chain joints, the only route the compiler reads <c>volumetric_solve</c> on.
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

                AddNodeTableMember(members, joint.Name, weight);
            }

            if (listed.Count == 0)
            {
                continue;
            }

            var (mapNode, _) = MakeListNode("ClothVertexMap");
            mapNode.Add("name", map.Name);
            AddClothVertexMapAttributes(mapNode, feModel, map.Name, proxyNodeNames: null);
            mapNode.Add("data", MakeNodeTable(members));
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

    /// <summary>
    /// Builds the function that lists a free cloth node in the <c>ClothVertexMap</c> of every selection covering it and
    /// returns the children list the node goes into: that container for a node in one selection when parenting is
    /// allowed, and the cloth folder otherwise.
    /// </summary>
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
                mapNode.Add("data", MakeNodeTable(members));
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

            var memberName = AuthoredNodeName(feModel, node, proxyNodeNames: null);
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
                AddNodeTableMember(group.Members, memberName, feModel.VertexMapWeight(mapName, node));
                home = home is null ? group.Children : clothFolderChildren;
            }

            return parentUnderMap && home is not null ? home : clothFolderChildren;
        };
    }
}
