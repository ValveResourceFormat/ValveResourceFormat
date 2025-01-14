using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.IO;

public class AnimationGraphExtract
{
    private readonly KVObject graph;
    private readonly string outputFileName;

    public AnimationGraphExtract(Resource resource)
    {
        graph = ((BinaryKV3)resource.DataBlock).Data;

        if (resource.FileName != null)
        {
            outputFileName = resource.FileName;
            outputFileName = outputFileName.EndsWith("_c", StringComparison.Ordinal)
                ? outputFileName[..^2]
                : outputFileName;
        }
    }

    public ContentFile ToContentFile()
    {
        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToEditableAnimGraphVersion19()),
            FileName = outputFileName,
        };

        return contentFile;
    }

    static readonly Dictionary<string, string> ClassRemap = new()
    {
        { "CSequenceUpdateNode", "CSequenceAnimNode" },
        { "CChoiceUpdateNode", "CChoiceAnimNode" },
    };

    KVObject ConvertToUncompiled(KVObject compiledNode)
    {
        var className = compiledNode.GetProperty<string>("_class");
        var newClass = ClassRemap.GetValueOrDefault(className, className);

        var node = ModelExtract.MakeNode(newClass);

        foreach (var (key, value) in compiledNode.Properties)
        {
            if (key is "_class"
                    or "m_nodePath"
                    or "m_paramSpans" // todo
                    or "m_tags"       // todo
                    or "m_children"   // todo
            )
            {
                continue;
            }

            var newKey = key;
            var isChildren = key is "m_children";

            if (className is "CSequenceUpdateNode")
            {
                // skip
                if (key is "m_hSequence" or "m_duration")
                {
                    continue;
                }

                // remap
                if (key is "m_name")
                {
                    newKey = "m_sequenceName";
                }
            }
            else if (className is "CChoiceUpdateNode")
            {
                // skip
                if (key is "m_weights" or "m_blendTimes")
                {
                    continue;
                }

                // remap
                if (key is "m_name")
                {
                    newKey = "m_sName";
                }
            }

            node.AddProperty(newKey, value);
        }

        if (className is "CChoiceUpdateNode")
        {
            var weights = compiledNode.GetArray("m_weights");
            var blendTimes = compiledNode.GetArray("m_blendTimes");
            var choiceNodeIds = compiledNode.GetArray("m_children")
                .Select(child => child.GetIntegerProperty("m_nodeIndex"));

            // node.AddProperty("m_children"
        }

        return node;
    }

    public string ToEditableAnimGraphVersion19()
    {
        var data = graph.GetSubCollection("m_pSharedData");

        var nodeManager = ModelExtract.MakeListNode("CAnimNodeManager", "m_nodes");
        var compiledNodes = data.GetArray("m_nodes");

        var i = 1337;
        foreach (var compiledNode in compiledNodes)
        {
            var nodeData = ConvertToUncompiled(compiledNode);

            var nodeId = i++;
            var nodeIdObject = new KVObject("fakeIndentKey", 1);
            nodeIdObject.AddProperty("m_id", ModelExtract.MakeValue(nodeId));

            nodeData.AddProperty("m_nNodeID", ModelExtract.MakeValue(nodeIdObject));

            var nodeManagerItem = new KVObject(null, 2);
            nodeManagerItem.AddProperty("key", ModelExtract.MakeValue(nodeIdObject));
            nodeManagerItem.AddProperty("value", ModelExtract.MakeValue(nodeData));

            ModelExtract.AddItem(nodeManager.Children, nodeManagerItem);
        }

        var kv = ModelExtract.MakeNode(
            "CAnimationGraph",
            ("m_nodeManager", nodeManager.Node),
            // ("m_componentManager", componentManager.Node),
            // ("m_localParameters", localParameters),
            // ("m_localTags", localTags),
            // ("m_referencedParamGroups", referencedParamGroups),
            // ("m_referencedTagGroups", referencedTagGroups),
            // ("m_referencedAnimGraphs", referencedAnimGraphs),
            // ("m_pSettingsManager", settingsManager),
            // ("m_clipDataManager", clipDataManager),
            ("m_modelName", graph.GetProperty<string>("m_modelName"))
        );

        return new KV3File(kv, format: "animgraph19:version{0adb35b7-2585-4302-8d05-e2825b4518ac}").ToString();
    }
}
