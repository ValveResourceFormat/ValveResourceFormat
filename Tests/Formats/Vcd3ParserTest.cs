using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.ResourceTypes.Choreo.Parser;

namespace Tests.Formats
{
    public class Vcd3ParserTest
    {
        [Test]
        public async Task ReadsActorsChannelsAndFlexTracks()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("lash_test.vcd_c"));

            var scene = Vcd3Parser.Parse(((BinaryKV3)resource.DataBlock!).Data.Root);
            var flexEvents = scene.Actors[0].Channels[0].Events;

            using (Assert.Multiple())
            {
                await Assert.That(scene.Actors.Length).IsGreaterThan(0);
                await Assert.That(scene.Actors[0].IsActive).IsTrue();
                await Assert.That(flexEvents.Length).IsGreaterThan(0);
                await Assert.That(flexEvents[0].Flags.HasFlag(ChoreoFlags.IsActive)).IsTrue();
            }
        }
    }
}
