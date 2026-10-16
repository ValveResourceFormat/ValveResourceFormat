using System.Globalization;
using ValveKeyValue;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    private const int ClothCollisionLayers = 4;
    private const int ClothAllCollisionLayers = 0xF;

    /// <summary>Adds a column to a datatable's <c>attrs</c> schema and returns it so the caller can add its default.</summary>
    private static KVObject AddColumn(KVObject attrs, string key, string display, bool show, int uiOrder)
    {
        var attr = KVObject.Collection();
        attr.Add("display", display);
        attr.Add("show", show);
        attr.Add("ui_order", uiOrder);
        attrs.Add(key, attr);
        return attr;
    }

    /// <summary>A datatable's <c>chain</c> value: its rows, its schema, an empty selection and an optional version.</summary>
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

    private static KVObject MakeNodeTable(KVObject members)
    {
        var data = KVObject.Collection();
        data.Add("nodes", members);
        return data;
    }

    /// <summary>Adds <paramref name="name"/> to a node table, with an explicit weight only when below one.</summary>
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

    /// <summary>Adds one boolean per collision layer of <paramref name="mask"/>, keyed as <paramref name="keyPrefix"/> plus the layer index.</summary>
    private static void AddCollisionLayerFlags(KVObject node, string keyPrefix, int mask)
    {
        for (var layer = 0; layer < ClothCollisionLayers; layer++)
        {
            node.Add(keyPrefix + layer.ToString(CultureInfo.InvariantCulture), (mask & (1 << layer)) != 0);
        }
    }

    private static KVObject MakeStaticClothNode(string name, string rootBone)
        => MakeNode("ClothNode", ("name", name), ("cloth_node_root_bone", rootBone), ("is_static_node", true));
}
