using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>A <c>ClothVertexMap</c> container naming <paramref name="members"/> in its node table, and its children list.</summary>
    private static (KVObject Node, KVObject Children) MakeClothVertexMap(ClothReconstruction cloth, string mapName, KVObject members,
        IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        var (mapNode, children) = MakeListNode("ClothVertexMap");
        mapNode.Add("name", mapName);
        AddClothVertexMapAttributes(mapNode, cloth, mapName, proxyNodeNames);
        mapNode.Add("data", MakeNodeTable(members));
        return (mapNode, children);
    }

    /// <summary>Adds the <c>volumetric_solve</c> strength and <c>scale_source_node</c> of a volume-solved selection.</summary>
    private static void AddClothVertexMapAttributes(KVObject mapNode, ClothReconstruction cloth, string mapName,
        IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        if (!cloth.TryGetVertexMap(mapName, out var map) || map.VolumetricSolveStrength <= 0f)
        {
            return;
        }

        mapNode.Add("volumetric_solve", map.VolumetricSolveStrength);

        if (AuthoredNodeName(cloth, map.ScaleSourceNode, proxyNodeNames) is { } scaleSource)
        {
            mapNode.Add("scale_source_node", scaleSource);
        }
    }

    /// <summary>
    /// Declares a <c>ClothVertexMap</c> listing its members in <c>data.nodes</c> for every volume-solved selection that
    /// covers only chain joints, the only route the compiler reads <c>volumetric_solve</c> on.
    /// </summary>
    internal static void AddClothChainVolumetricMaps(KVObject softbodyChildren, ClothReconstruction cloth,
        IEnumerable<BoneChain> chains)
    {
        var joints = chains.SelectMany(static chain => chain.Joints).ToList();
        var jointNames = joints.Select(static joint => joint.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var map in cloth.VertexMaps)
        {
            if (map.VolumetricSolveStrength <= 0f || CoversNodeOutsideChains(cloth, map, jointNames))
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

            softbodyChildren.Add(MakeClothVertexMap(cloth, map.Name, members).Node);
        }
    }

    private static bool CoversNodeOutsideChains(ClothReconstruction cloth, FeModelIndex.VertexMap map, HashSet<string> jointNames)
    {
        for (var node = map.VertexBase; node < map.VertexBase + map.VertexCount && node < cloth.Fe.CtrlName.Length; node++)
        {
            var name = cloth.Fe.CtrlName[node];
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
    private static Func<int, bool, KVObject> ClothVertexMapFolders(ClothReconstruction cloth, KVObject clothFolderChildren)
    {
        var groups = new Dictionary<string, (KVObject Children, KVObject Members)>(StringComparer.Ordinal);

        (KVObject Children, KVObject Members) GroupFor(string mapName)
        {
            if (!groups.TryGetValue(mapName, out var group))
            {
                var members = KVObject.Collection();
                var (mapNode, mapChildren) = MakeClothVertexMap(cloth, mapName, members);
                clothFolderChildren.Add(mapNode);
                groups[mapName] = group = (mapChildren, members);
            }

            return group;
        }

        return (node, parentUnderMap) =>
        {
            var maps = cloth.GetVertexMapNames(node);
            if (maps is null)
            {
                return clothFolderChildren;
            }

            var memberName = AuthoredNodeName(cloth, node, proxyNodeNames: null);
            if (memberName is null || memberName.StartsWith('$'))
            {
                return parentUnderMap && !maps.Contains(',', StringComparison.Ordinal)
                    ? GroupFor(ClothReconstruction.VertexMapName(maps)).Children
                    : clothFolderChildren;
            }

            KVObject? home = null;
            foreach (var entry in maps.Split(','))
            {
                var mapName = ClothReconstruction.VertexMapName(entry);
                var group = GroupFor(mapName);
                if (!group.Members.ContainsKey(memberName))
                {
                    AddNodeTableMember(group.Members, memberName, cloth.VertexMapWeight(mapName, node));
                }

                home = home is null ? group.Children : clothFolderChildren;
            }

            return parentUnderMap && home is not null ? home : clothFolderChildren;
        };
    }
}
