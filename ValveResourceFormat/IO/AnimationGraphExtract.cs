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

    KVObject ConvertToUncompiled(KVObject compiledNode)
    {
        var className = compiledNode.GetProperty<string>("_class");
        className = className.Replace("UpdateNode", string.Empty, StringComparison.Ordinal);

        var newClass = className + "AnimNode";
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

            if (className is "CSequence")
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
            else if (className is "CChoice")
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

        if (className is "CChoice")
        {
            var weights = compiledNode.GetFloatArray("m_weights");
            var blendTimes = compiledNode.GetFloatArray("m_blendTimes");

            var choiceNodeIds = compiledNode.GetArray("m_children")
                .Select(child => child.GetIntegerProperty("m_nodeIndex"));

            var children = weights.Zip(blendTimes, choiceNodeIds).Select((choice) =>
            {
                var (weight, blendTime, nodeId) = choice;

                var choiceNode = new KVObject(null, 3);
                var nodeIdObject = new KVObject("fakeIndentKey", new Dictionary<string, KVValue>()
                {
                    { "m_id", ModelExtract.MakeValue(nodeId) },
                });

                var inputConnection = new KVObject("fakeIndentKey", 2);
                inputConnection.AddProperty("m_nodeID", ModelExtract.MakeValue(nodeIdObject));
                inputConnection.AddProperty("m_outputID", ModelExtract.MakeValue(nodeIdObject));

                choiceNode.AddProperty("m_inputConnection", ModelExtract.MakeValue(inputConnection));
                choiceNode.AddProperty("m_weight", ModelExtract.MakeValue(weight));
                choiceNode.AddProperty("m_blendTime", ModelExtract.MakeValue(blendTime));

                return choiceNode;
            });

            node.AddProperty("m_children", ModelExtract.MakeArrayValue(children));
        }

        return node;
    }

    public string ToEditableAnimGraphVersion19()
    {
        var data = graph.GetSubCollection("m_pSharedData");
        var compiledNodes = data.GetArray("m_nodes");
        var compiledNodeIndexMap = data.GetArray("m_nodeIndexMap").Select(kv => kv.GetIntegerProperty("value")).ToArray();

        var nodeManager = ModelExtract.MakeListNode("CAnimNodeManager", "m_nodes");

        var i = 0;
        foreach (var compiledNode in compiledNodes)
        {
            var nodeId = i++; // compiledNodeIndexMap[i++];
            var nodeData = ConvertToUncompiled(compiledNode);

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
