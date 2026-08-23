using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Datamodel;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.SmartProp;

public class SmartPropMapDeserializationTest
{
    [Test]
    public async Task ReadsPlacedSmartPropParameters()
    {
        var map = LoadMap("FitOnLine01");
        var smartProp = GetSmartProp(map);

        var parameters = SmartPropMapParameters.Read(smartProp);

        await Assert.That(parameters).IsNotNull();
        await Assert.That(parameters!.SmartPropFilename).IsEqualTo("models/test2.vsmart");
        await Assert.That(parameters.Values).ContainsKey("PickMode");
        await Assert.That(parameters.Values["PickMode"]).IsEqualTo("LARGEST_FIRST");
        await Assert.That(parameters.RandomSeed).IsEqualTo(1517519369);
        await Assert.That(parameters.ChoiceElementIds[4]).IsEqualTo(6);
        var smartPropPath = Path.Combine(TestContext.TestDirectory!, "SmartProp", "SmartPropMapDeserializationTest", "FitOnLine01", "test2.vsmart");
        var context = parameters.CreateEvaluationContext(KVDocumentExtensions.ParseKV3(smartPropPath).Root);
        await Assert.That(context.GetVariable("PickMode")).IsEqualTo("LARGEST_FIRST");
        await Assert.That(context.TryGetWidgetOutputValue("sizer_x", out var sizerValue)).IsTrue();
        await Assert.That(sizerValue).IsEqualTo(-200f);

        var smartPropRoot = KVDocumentExtensions.ParseKV3(smartPropPath).Root;
        var rootElement = smartPropRoot["m_Children"].AsArraySpan()[0];
        var fitOnLine = rootElement["m_Children"].AsArraySpan()[0];
        await Assert.That(context.ResolveVector3(fitOnLine["m_vEnd"]).X).IsEqualTo(-200f);
        var pickOne = fitOnLine["m_Children"].AsArraySpan()[0];
        var modelElement = pickOne["m_Children"].AsArraySpan()[0];
        await Assert.That(SmartPropEvaluator.Evaluate(modelElement, context).Models).IsNotEmpty();
        await Assert.That(SmartPropEvaluator.Evaluate(pickOne, context).Models).IsNotEmpty();
        await Assert.That(SmartPropEvaluator.Evaluate(fitOnLine, context).Models).IsNotEmpty();

        var evaluation = SmartPropEvaluator.Evaluate(smartPropRoot, context);
        await Assert.That(evaluation.Models).IsNotEmpty();
    }

    [Test]
    public async Task ReadsPlacedSmartPropWithoutUserParameters()
    {
        var map = LoadMap("Sample01");

        var parameters = SmartPropMapParameters.Read(GetSmartProp(map));

        await Assert.That(parameters).IsNotNull();
        await Assert.That(parameters!.SmartPropFilename).IsEqualTo("models/guide_meshes/guide_fitonline.vsmart");
        await Assert.That(parameters.Values).IsEmpty();
    }

    [Test]
    public async Task FindsPlacedSmartPropsFromMapRoot()
    {
        var map = LoadMap("FitOnLine01");

        var smartProps = SmartPropMapParameters.ReadAll(map);

        await Assert.That(smartProps).HasSingleItem();
        await Assert.That(smartProps[0].SmartPropFilename).IsEqualTo("models/test2.vsmart");
    }

    [Test]
    public async Task ReadsVmapEntitiesForViewerTabs()
    {
        var entities = ValveMapEntityReader.ReadAll(LoadMap("FitOnLine01"));

        await Assert.That(entities).IsNotEmpty();
        await Assert.That(entities.Any(item => item.Entity.GetStringProperty("classname") == "worldspawn")).IsTrue();
        var smartProp = entities.Single(item => item.Entity.GetStringProperty("classname") == "CMapSmartProp").Entity;
        await Assert.That(smartProp.GetStringProperty("smartpropfilename")).IsEqualTo("models/test2.vsmart");
        await Assert.That(smartProp.GetStringProperty("parameter.PickMode")).IsEqualTo("LARGEST_FIRST");
    }

    [Test]
    public async Task ReadsSavedSmartPropEvaluationParts()
    {
        var partsByNode = SmartPropMapPartSet.ReadAll(LoadMap("FitOnLine01"));

        await Assert.That(partsByNode).ContainsKey(2);
        var parts = partsByNode[2];
        await Assert.That(parts).Count().IsEqualTo(2);
        await Assert.That(parts[0].ModelName).IsEqualTo("models/props/de_nuke/hr_nuke/web_joist_001/web_joist_support_002_horizontal_128.vmdl");
        await Assert.That(parts[0].Transform).IsEqualTo(Matrix4x4.Identity);
        await Assert.That(parts[1].Transform.Translation.X).IsEqualTo(-128f);
        await Assert.That(parts[1].Transform.M11).IsEqualTo(0.5625f);
    }

    private static Datamodel.Element LoadMap(string sampleName)
    {
        var path = Path.Combine(TestContext.TestDirectory!, "SmartProp", "SmartPropMapDeserializationTest", sampleName, "sample.vmap");
        using var stream = File.OpenRead(path);
        using var map = Datamodel.Datamodel.Load(stream, Datamodel.Codecs.DeferredMode.Disabled);
        return (Datamodel.Element)map.Root!;
    }

    private static Datamodel.Element GetSmartProp(Datamodel.Element root)
    {
        var world = (Datamodel.Element)root["world"]!;
        var children = (Datamodel.ElementArray)world["children"]!;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Datamodel.Element child && child.TryGetValue("smartPropFilename", out _))
            {
                return child;
            }
        }

        throw new InvalidOperationException("The VMAP contains no CMapSmartProp element.");
    }
}
