using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using DMElement = Datamodel.Element;

namespace Tests.IO
{
    /// <summary>
    /// Tests entity I/O connections carrying input parameter maps, parsed from the entity lump and
    /// written back to vmap. The fixture was compiled from a Hammer map whose logic_relay io_relay and
    /// prop_dynamic io_breaker fire typed inputs on io_child and io_text with literal, routed, stale and
    /// empty parameter mappings.
    /// </summary>
    public class EntityIOParamMapTest
    {
        private static readonly string VpkPath = TestFixtures.Path("entity_io_param_map_test.vpk");

        private static List<EntityLump.Connection> LoadConnections()
        {
            using var package = new Package();
            package.Read(VpkPath);
            package.ReadEntry(package.FindEntry("maps/wtf/entities/default_ents.vents_c")!, out var bytes);

            using var resource = new Resource();
            resource.Read(new MemoryStream(bytes));

            return ((EntityLump)resource.DataBlock!).GetEntities()
                .SelectMany(static entity => entity.Connections ?? [])
                .ToList();
        }

        [Test]
        public async Task ParsesParamMapsFromLump()
        {
            var connections = LoadConnections();

            var maintainOffset = connections.Single(static c => c.InputName == "SetParentMaintainOffset" && c.OutputName == "OnTrigger");
            var parent = maintainOffset.ParamMap!["pNewParent"]["value"];

            var routed = connections.Single(static c => c.OutputName == "OnDestructibleHitGroupDamageLevelChanged" && c.InputName == "SetTextString");
            var empty = connections.Single(static c => c.InputName == "string_string_string");
            var legacy = connections.Single(static c => c.TargetName == "!activator");

            using (Assert.Multiple())
            {
                await Assert.That((string)parent).IsEqualTo("[PR#]io_breaker");
                await Assert.That(parent.Flag).IsEqualTo(KVFlag.EntityName);
                await Assert.That((string)routed.ParamMap!["pText"]["src"]).IsEqualTo("nDamageLevel");
                await Assert.That((bool)routed.ParamMap!["bLocalize"]["value"]).IsFalse();
                await Assert.That(empty.ParamMap).IsNotNull();
                await Assert.That(empty.ParamMap!.Count).IsEqualTo(0);
                await Assert.That(legacy.ParamMap).IsNull();
                await Assert.That(legacy.OverrideParam).IsEqualTo("tesfasdfas");
            }
        }

        [Test]
        public async Task WritesParamMapsToVmap()
        {
            using var package = new Package();
            package.Read(VpkPath);
            using var loader = new GameFileLoader(package, VpkPath);
            using var vmapResource = loader.LoadFile("maps/wtf.vmap_c");

            var vmapData = new MapExtract(vmapResource!, loader).ToValveMap();
            using var vmap = Datamodel.Datamodel.Load(new MemoryStream(vmapData), Datamodel.Codecs.DeferredMode.Disabled);

            var connections = vmap.AllElements
                .Where(static element => element.ClassName == "DmeConnectionData")
                .ToList();

            DMElement ParamMapOf(string outputName, string inputName)
                => (DMElement)connections.Single(c => (string)c["outputName"]! == outputName && (string)c["inputName"]! == inputName)["paramMap"]!;

            var maintainOffset = ParamMapOf("OnTrigger", "SetParentMaintainOffset");
            var parent = (DMElement)((DMElement)maintainOffset["pNewParent"]!)["value"]!;
            var routed = ParamMapOf("OnDestructibleHitGroupDamageLevelChanged", "SetTextString");
            var empty = ParamMapOf("OnTrigger", "string_string_string");

            using (Assert.Multiple())
            {
                await Assert.That(maintainOffset.ClassName).IsEqualTo("DmElement");
                await Assert.That(maintainOffset.Name).IsEqualTo("paramMap");
                await Assert.That((string)parent["specific_type"]!).IsEqualTo("entity_name");
                await Assert.That((string)parent["value"]!).IsEqualTo("io_breaker");
                await Assert.That((string)((DMElement)routed["pText"]!)["src"]!).IsEqualTo("nDamageLevel");
                await Assert.That((bool)((DMElement)routed["bLocalize"]!)["value"]!).IsFalse();
                await Assert.That(empty.Count).IsEqualTo(0);
                await Assert.That(connections.Single(static c => (string)c["targetName"]! == "!activator").ContainsKey("paramMap")).IsFalse();
            }
        }
    }
}
