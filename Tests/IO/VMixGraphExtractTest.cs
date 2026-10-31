using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.IO
{
    /// <summary>
    /// Checks the mix graph decompiler on a vmix compiled before the heap layout, and on a CS2 sound
    /// that a graph was compiled into.
    /// </summary>
    public class VMixGraphExtractTest
    {
        private static (KVObject Document, VMixGraphExtract Extract) Decompile(string fileName)
        {
            using var resource = TestFixtures.Load(fileName);
            var extract = new VMixGraphExtract(resource);

            return (TestFixtures.ParseKV3(extract.ToEditorDocument()), extract);
        }

        private static string[] Edges(KVObject graph)
            => [.. graph.GetArray("m_EditorEdges").Select(static edge => $"{edge.GetStringProperty("plug0")} -> {edge.GetStringProperty("plug1")}")];

        [Test]
        public async Task DecompilesVMix()
        {
            var (document, _) = Decompile("mixgraph.vmix_c");
            var graph = document.GetArray("Graphs").Single();
            var nodes = graph.GetArray("m_EditorNodes").ToDictionary(static node => node.GetStringProperty("name"), static node => node.GetStringProperty("type"));

            using (Assert.Multiple())
            {
                await Assert.That(document.ContainsKey("dsp_presets")).IsTrue();
                await Assert.That(graph.GetStringProperty("Name")).IsEqualTo("main");
                await Assert.That(nodes.Count).IsEqualTo(20);
                await Assert.That(nodes["audio.Processor1"]).IsEqualTo("audio.Processor");
                await Assert.That(nodes["audio.Output1"]).IsEqualTo("audio.Output");
                await Assert.That(Edges(graph)).Contains("audio.Track3/audio:Source -> audio.Mix3/audio:In1");
                await Assert.That(Edges(graph)).Contains("control.Input3/value -> audio.Processor1/mix");
                await Assert.That(Edges(graph)).Contains("effect.Input1/Effect Name -> audio.Processor1/effect");
            }
        }

        [Test]
        public async Task DecompilesGraphSound()
        {
            using var resource = TestFixtures.Load("csgo_dsp_bypass.vsnd_c");
            await Assert.That(VMixGraphExtract.GetGraphSound(resource)).IsNotNull();

            var (document, extract) = Decompile("csgo_dsp_bypass.vsnd_c");
            var graph = document.GetArray("Graphs").Single();
            var track = graph.GetArray("m_EditorNodes").Single(static node => node.GetStringProperty("type") == "audio.Track");

            using (Assert.Multiple())
            {
                await Assert.That(extract.Warnings).IsEmpty();
                await Assert.That(document.ContainsKey("dsp_presets")).IsFalse();
                await Assert.That(graph.GetStringProperty("Name")).IsEqualTo("csgo_dsp_bypass");
                await Assert.That(track.GetSubCollection("properties").GetStringProperty("m_nSendOperator")).IsEqualTo("ALL_VOICES");
                await Assert.That(Edges(graph)).IsEquivalentTo(["audio.Track1/audio:Source -> audio.Output1/audio:In1"]);
            }
        }

        [Test]
        public async Task ExtractsGraphSoundAsVMix()
        {
            using var resource = TestFixtures.Load("csgo_dsp_bypass.vsnd_c");
            using var content = FileExtract.Extract(resource, new NullFileLoader());

            await Assert.That(content.FileName).EndsWith(".vmix");
            await Assert.That(Encoding.UTF8.GetString(content.Data!)).Contains("m_EditorNodes");
        }
    }
}
