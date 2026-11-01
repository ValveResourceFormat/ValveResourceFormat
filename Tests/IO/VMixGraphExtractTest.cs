using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.IO
{
    /// <summary>
    /// Checks the mix graph decompiler on a vmix compiled before the heap layout.
    /// </summary>
    public class VMixGraphExtractTest
    {
        private static string[] Edges(KVObject graph)
            => [.. graph.GetArray("m_EditorEdges").Select(static edge => $"{edge.GetStringProperty("plug0")} -> {edge.GetStringProperty("plug1")}")];

        [Test]
        public async Task DecompilesVMix()
        {
            using var resource = TestFixtures.Load("mixgraph.vmix_c");
            var document = TestFixtures.ParseKV3(new VMixGraphExtract(resource).ToEditorDocument());
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
    }
}
