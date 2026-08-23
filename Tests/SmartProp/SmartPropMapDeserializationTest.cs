using Datamodel;
using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Serialization.KeyValues;
using ValveKeyValue;

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
        await Assert.That(parameters!.SmartPropFilename).IsEqualTo("models/guide_meshes/test2.vsmart");
        await Assert.That(parameters.Values).ContainsKey("PickMode");
        await Assert.That(parameters.Values["PickMode"]).IsEqualTo("LARGEST_FIRST");
        var smartPropPath = Path.Combine(TestContext.TestDirectory!, "SmartProp", "SmartPropMapDeserializationTest", "FitOnLine01", "test2.vsmart");
        var context = parameters.CreateEvaluationContext(KVDocumentExtensions.ParseKV3(smartPropPath).Root);
        await Assert.That(context.GetVariable("PickMode")).IsEqualTo("LARGEST_FIRST");
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
