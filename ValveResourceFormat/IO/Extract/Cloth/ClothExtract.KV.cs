using System.Globalization;
using ValveKeyValue;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>The collision layers a node's mask carries its own bit for.</summary>
    private const int ClothCollisionLayers = 4;

    /// <summary>Adds a column to a datatable's <c>attrs</c> schema and returns it, for its default.</summary>
    private static KVObject AddColumn(KVObject attrs, string key, string display, bool show, int uiOrder)
    {
        var attr = KVObject.Collection();
        attr.Add("display", display);
        attr.Add("show", show);
        attr.Add("ui_order", uiOrder);
        attrs.Add(key, attr);
        return attr;
    }

    /// <summary>A datatable's <c>chain</c> value: its rows, its schema, an empty selection and the version where given.</summary>
    private static KVObject MakeChainData(KVObject joints, KVObject attrs, int? version = null)
    {
        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", attrs);
        chainData.Add("selection", KVObject.Array());
        if (version is { } value)
        {
            chainData.Add("version", value);
        }

        return chainData;
    }

    /// <summary>A node list's <c>data</c> value naming <paramref name="members"/>.</summary>
    private static KVObject MakeNodeTable(KVObject members)
    {
        var data = KVObject.Collection();
        data.Add("nodes", members);
        return data;
    }

    /// <summary>Lists <paramref name="name"/> in a node table, with its weight where that is below one.</summary>
    private static void AddNodeTableMember(KVObject members, string name, float weight)
    {
        if (weight >= 1f)
        {
            members.Add(name, true);
            return;
        }

        var member = KVObject.Collection();
        member.Add("weight", weight);
        members.Add(name, member);
    }

    /// <summary>Adds the four collision-layer booleans of <paramref name="mask"/>, keyed by layer after <paramref name="keyPrefix"/>.</summary>
    private static void AddCollisionLayerFlags(KVObject node, string keyPrefix, int mask)
    {
        for (var layer = 0; layer < ClothCollisionLayers; layer++)
        {
            node.Add(keyPrefix + layer.ToString(CultureInfo.InvariantCulture), (mask & (1 << layer)) != 0);
        }
    }

    /// <summary>A bare static <c>ClothNode</c> on <paramref name="rootBone"/>.</summary>
    private static KVObject MakeStaticClothNode(string name, string rootBone)
        => MakeNode("ClothNode", ("name", name), ("cloth_node_root_bone", rootBone), ("is_static_node", true));
}
