using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValveResourceFormat;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests;

public class NmGraphExtractTest
{
    [Test]
    public void ExtractNmGraphDocument()
    {
        using var resource = new Resource();
        resource.Read("Files/viewmodel_inspects.vnmgraph+ak47.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocument\""));
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocStateMachineNode\""));
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocParameterizedClipSelectorNode\""));
            Assert.That(text, Does.Contain("lookat01_ak.vnmclip"));
            Assert.That(text, Does.Contain("m_ID = \"ak47\""));
        });
    }

    [Test]
    public void ExtractAllAnimgraph2Documents()
    {
        var files = Directory.GetFiles("Files/Animgraph2", "*.vnmgraph_c", SearchOption.AllDirectories)
            .OrderBy(path => path)
            .ToArray();

        Assert.That(files, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var file in files)
            {
                using var resource = new Resource();
                resource.Read(file);

                Assert.DoesNotThrow(() => FileExtract.Extract(resource, new NullFileLoader()), file);
            }
        });
    }

    [Test]
    public void ExtractNmGraphTransitionTimeMatchModes()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_gun.vnmgraph+p90.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("m_timeMatchMode = \"MatchSyncEventID\""));
            Assert.That(text, Does.Contain("m_name = \"ID\""));
            Assert.That(text, Does.Contain("m_value = \"WPN_RELOAD_LOOP\""));
        });
    }

    [Test]
    public void ExtractNmGraphCreatesSeparateTransitionConduitsPerStatePair()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_gun.vnmgraph+p90.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);
        var transitionConduitCount = Regex.Count(text, "_class = \"CNmGraphDocTransitionConduitNode\"");

        Assert.That(transitionConduitCount, Is.EqualTo(5));
    }

    [Test]
    public void ExtractNmGraphPutsTimedStateEventsInLegacyArray()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_gun.vnmgraph+p90.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("m_timedStateEvents = "));
            Assert.That(text, Does.Contain("m_comparisonOperator = \"LessThanEqual\""));
            Assert.That(text, Does.Contain("m_timeElapsedEvents = [  ]"));
        });
    }

    [Test]
    public void ExtractNmGraphPutsStateEventsInStateEventsArray()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/ui/uimodel.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("m_stateEvents = "));
            Assert.That(text, Does.Contain("m_ID = \"WPN_DISABLE_LEFT_HAND_IK\""));
            Assert.That(text, Does.Contain("m_bIsEntry = true"));
            Assert.That(text, Does.Contain("m_bIsFullyInState = true"));
            Assert.That(text, Does.Contain("m_exitEvents = [  ]"));
        });
    }

    [Test]
    public void ExtractNmGraphKeepsUimodelOffStateCount()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/ui/uimodel.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);
        var offStateCount = Regex.Count(text, "m_type = \"OffState\"");

        Assert.That(offStateCount, Is.EqualTo(1));
    }

    [Test]
    public void ExtractNmGraphAddsEnabledPinToTwoBoneIk()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/viewmodel/viewmodel.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("_class = \"CnmGraphDocTwoBoneIKNode\""));
            Assert.That(text, Does.Contain("m_name = \"Enabled\""));
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocIDEventConditionNode\""));
            Assert.That(text, Does.Contain("WPN_DISABLE_LEFT_HAND_IK"));
        });
    }

    [Test]
    public void ExtractNmGraphKeepsTwoBoneIkBlendTime()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/viewmodel/viewmodel.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("_class = \"CnmGraphDocTwoBoneIKNode::CData\""));
            Assert.That(text, Does.Contain("m_effectorBoneName = \"hand_L\""));
            Assert.That(text, Does.Contain("m_flBlendTimeSeconds = 0.2"));
        });
    }

    [Test]
    public void ExtractNmGraphBlend2DExportsPoints()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_locomotion.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("_class = \"CNmGraphDocBlend2DNode\""));
            Assert.That(text, Does.Contain("m_pointNames = "));
            Assert.That(text, Does.Contain("m_points = "));
            Assert.That(text, Does.Contain("[ 225.0, 0.0 ]"));
            Assert.That(text, Does.Contain("[ 0.0, 225.0 ]"));
        });
    }

    [Test]
    public void ExtractNmGraphPreservesConstBoneTargetNodes()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/viewmodel/viewmodel.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("_class = \"CnmGraphDocConstBoneTargetNode\""));
            Assert.That(text, Does.Contain("m_boneName = \"wpnHand_L\""));
        });
    }

    [Test]
    public void ExtractNmGraphUsesUniqueTransitionConduitIds()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel.vnmgraph_c");

        var content = FileExtract.Extract(resource, new NullFileLoader());

        Assert.That(content.Data, Is.Not.Null);

        var text = Encoding.UTF8.GetString(content.Data!);
        var conduitMatches = Regex.Matches(text, "_class = \"CNmGraphDocTransitionConduitNode\"\\s+m_ID = \"([^\"]+)\"", RegexOptions.Multiline);
        var graphMatches = Regex.Matches(text, "_class = \"CNmGraphDocFlowGraph\"\\s+m_ID = \"([^\"]+)\"\\s+m_nodes =", RegexOptions.Multiline);

        var conduitIds = conduitMatches.Select(match => match.Groups[1].Value).ToArray();
        var transitionGraphIds = graphMatches.Select(match => match.Groups[1].Value).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(conduitIds, Is.Not.Empty);
            Assert.That(conduitIds.Distinct().Count(), Is.EqualTo(conduitIds.Length));
            Assert.That(transitionGraphIds.Distinct().Count(), Is.EqualTo(transitionGraphIds.Length));
        });
    }

    [Test]
    public void ExtractNmGraphLeavesBodyAdditivesBreathingGlobalNodeUnwired()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel.vnmgraph_c");

        var extractor = new NmGraphExtract(resource);
        var method = typeof(NmGraphExtract).GetMethod("BuildStateMachineGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stateMachineGraph = (KVObject)method.Invoke(extractor, [1074])!;
        var nodes = stateMachineGraph.GetArray("m_nodes");

        var globalConduit = nodes.First(node => node.GetStringProperty("_class") == "CNmGraphDocGlobalTransitionConduitNode");
        var globalGraph = globalConduit.GetSubCollection("m_pSecondaryGraph")!;
        var globalNodes = globalGraph.GetArray("m_nodes")
            .Where(node => node.GetStringProperty("_class") == "CNmGraphDocGlobalTransitionNode")
            .ToArray();
        var connections = globalGraph.GetArray("m_connections");
        var breathingNode = globalNodes.First(node => node.GetStringProperty("m_name") == "Breathing");
        var breathingNodeId = breathingNode.GetStringProperty("m_ID");
        var hasBreathingInput = connections.Any(connection => connection.GetStringProperty("m_toNodeID") == breathingNodeId);

        Assert.Multiple(() =>
        {
            Assert.That(globalNodes.Select(node => node.GetStringProperty("m_name")), Does.Contain("Breathing"));
            Assert.That(globalNodes.Select(node => node.GetStringProperty("m_name")), Does.Contain("Off"));
            Assert.That(globalNodes.Select(node => node.GetStringProperty("m_name")), Does.Contain("JumpAdditiveStart"));
            Assert.That(globalNodes.Select(node => node.GetStringProperty("m_name")), Does.Contain("JumpAdditiveLand"));
            Assert.That(hasBreathingInput, Is.False);
        });
    }

    [Test]
    public void DebugNmGraphBlend2DCompiledValues()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_locomotion.vnmgraph_c");

        var data = ((BinaryKV3)resource.DataBlock!).Data.Root;
        var nodes = data.GetArray("m_nodes");
        var blendNode = nodes.First(node => node.GetStringProperty("_class") == "CNmBlend2DNode::CDefinition");
        var values = blendNode.GetArray("m_values");
        var lines = new List<string> { $"values-count={values.Count}" };

        for (var i = 0; i < Math.Min(values.Count, 3); i++)
        {
            var value = values[i];
            lines.Add($"value[{i}] type={value.ValueType} isArray={value.IsArray} valuesType={value.Values?.GetType().FullName}");
            lines.Add($"value[{i}] string={value}");

            if (value.IsArray)
            {
                var span = value.AsArraySpan();
                var vector2 = value.ToVector2();
                lines.Add($"value[{i}] vector2={vector2.X},{vector2.Y}");
                lines.Add($"value[{i}] span-length={span.Length}");
                for (var j = 0; j < span.Length; j++)
                {
                    lines.Add($"value[{i}][{j}] type={span[j].ValueType} value={span[j]}");
                }
            }
        }

        Assert.Fail(string.Join(Environment.NewLine, lines));
    }

    [Test]
    public void DebugKvNestedArraySerialization()
    {
        var inner = KVObject.Array();
        inner.Add(225.0f);
        inner.Add(0.0f);

        var outer = KVObject.Array();
        outer.Add(inner);

        var root = KVObject.Collection();
        root.Add("m_points", outer);

        Assert.Fail(root.ToKV3String());
    }

    [Test]
    public void DebugBlend2DNodeSerialization()
    {
        var points = KVObject.Array();
        points.Add(KVHelpers.MakeArray((KVObject)225.0f, (KVObject)0.0f));
        points.Add(KVHelpers.MakeArray((KVObject)0.0f, (KVObject)225.0f));

        var blendSpace = KVObject.Collection();
        blendSpace.Add("m_pointNames", KVHelpers.MakeArray((KVObject)"A", (KVObject)"B"));
        blendSpace.Add("m_points", points);

        var node = KVObject.Collection();
        node.Add("_class", "CNmGraphDocBlend2DNode");
        node.Add("m_blendSpace", blendSpace);

        Assert.Fail(node.ToKV3String());
    }

    [Test]
    public void DebugExtractedBlend2DNode()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_locomotion.vnmgraph_c");

        var extractor = new NmGraphExtract(resource);
        var compiledNodesField = typeof(NmGraphExtract).GetField("compiledNodes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var compiledNodes = (IReadOnlyList<KVObject?>)compiledNodesField.GetValue(extractor)!;
        var nodeIndex = Enumerable.Range(0, compiledNodes.Count)
            .First(i => compiledNodes[i]?.GetStringProperty("_class") == "CNmBlend2DNode::CDefinition");

        var method = typeof(NmGraphExtract).GetMethod("CreateBlend2DNode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var node = (KVObject)method.Invoke(extractor, [nodeIndex, compiledNodes[nodeIndex]!])!;

        Assert.Fail(node.ToKV3String());
    }

    [Test]
    public void DebugExtractedBlend2DNodeInGraph()
    {
        using var resource = new Resource();
        resource.Read("Files/Animgraph2/animation/graphs/worldmodel/worldmodel_locomotion.vnmgraph_c");

        var extractor = new NmGraphExtract(resource);
        var compiledNodesField = typeof(NmGraphExtract).GetField("compiledNodes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var compiledNodes = (IReadOnlyList<KVObject?>)compiledNodesField.GetValue(extractor)!;
        var nodeIndex = Enumerable.Range(0, compiledNodes.Count)
            .First(i => compiledNodes[i]?.GetStringProperty("_class") == "CNmBlend2DNode::CDefinition");

        var method = typeof(NmGraphExtract).GetMethod("CreateBlend2DNode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var node = (KVObject)method.Invoke(extractor, [nodeIndex, compiledNodes[nodeIndex]!])!;

        var graph = KVObject.Collection();
        graph.Add("_class", "CNmGraphDocFlowGraph");
        graph.Add("m_nodes", KVHelpers.MakeArray(node));

        Assert.Fail(graph.ToKV3String());
    }

    [Test]
    public void ExtractNmGraphDecodesIdEventConditionFlags()
    {
        static object InvokeRules(long flags)
        {
            var rules = KVObject.Collection();
            rules.Add("m_flags", flags);

            var node = KVObject.Collection();
            node.Add("m_eventConditionRules", rules);

            var method = typeof(NmGraphExtract).GetMethod("GetEventConditionRules", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return method!.Invoke(null, [node])!;
        }

        static object? GetProperty(object value, string propertyName)
            => value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);

        var searchAll = InvokeRules(272);
        var searchAnim = InvokeRules(144);
        var searchAnimAndLimit = InvokeRules(145);
        var searchAnimAndOperatorAnd = InvokeRules(160);

        Assert.Multiple(() =>
        {
            Assert.That(GetProperty(searchAll, "SearchRule"), Is.EqualTo("SearchAll"));
            Assert.That(GetProperty(searchAll, "Operator"), Is.EqualTo("Or"));
            Assert.That(GetProperty(searchAll, "LimitSearchToSourceState"), Is.EqualTo(false));
            Assert.That(GetProperty(searchAll, "IgnoreInactiveBranchEvents"), Is.EqualTo(false));

            Assert.That(GetProperty(searchAnim, "SearchRule"), Is.EqualTo("OnlySearchAnimEvents"));
            Assert.That(GetProperty(searchAnim, "Operator"), Is.EqualTo("Or"));
            Assert.That(GetProperty(searchAnim, "LimitSearchToSourceState"), Is.EqualTo(false));

            Assert.That(GetProperty(searchAnimAndLimit, "SearchRule"), Is.EqualTo("OnlySearchAnimEvents"));
            Assert.That(GetProperty(searchAnimAndLimit, "LimitSearchToSourceState"), Is.EqualTo(true));

            Assert.That(GetProperty(searchAnimAndOperatorAnd, "SearchRule"), Is.EqualTo("OnlySearchAnimEvents"));
            Assert.That(GetProperty(searchAnimAndOperatorAnd, "Operator"), Is.EqualTo("And"));
        });
    }

}
