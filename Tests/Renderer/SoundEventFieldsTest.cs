using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Audio;

namespace Tests.Renderer
{
    public class SoundEventFieldsTest
    {
        private static SoundEventDefinition Define(KVObject data)
        {
            var bank = new SoundEventBank();
            bank.AddSoundEvent("test", data);
            return bank.GetSoundEvent("test")!;
        }

        [Test]
        public async Task StringFloatsParseLeniently()
        {
            var data = KVObject.Collection();
            data.Add("volume", "0.3");
            data.Add("pitch", "1.5,0.5");
            data.Add("delay", "[0.25]");
            data.Add("block_duration", "2.5abc");
            data.Add("volume_fade_in", "");
            data.Add("volume_fade_out", 0.75f);

            var definition = Define(data);

            using (Assert.Multiple())
            {
                await Assert.That(definition.Volume).IsEqualTo(0.3f);
                await Assert.That(definition.Pitch).IsEqualTo(1.5f);
                await Assert.That(definition.Delay).IsEqualTo(0.25f);
                await Assert.That(definition.BlockDuration).IsEqualTo(2.5f);
                await Assert.That(definition.FadeIn).IsEqualTo(0f);
                await Assert.That(definition.FadeOut).IsEqualTo(0.75f);
            }
        }

        [Test]
        public async Task StringBooleansParseLeniently()
        {
            var data = KVObject.Collection();
            data.Add("set_child_position", "true");
            data.Add("enable_retrigger", "1.0");
            data.Add("block_matching_events", "yes");
            data.Add("block_match_this_event", -1f);

            var definition = Define(data);

            using (Assert.Multiple())
            {
                await Assert.That(definition.SetChildPosition).IsTrue();
                await Assert.That(definition.EnableRetrigger).IsTrue();
                await Assert.That(definition.BlockMatchingEvents).IsFalse();
            }
        }

        [Test]
        public async Task VectorsParseFromStringsAndArrays()
        {
            var data = KVObject.Collection();
            data.Add("position", "[1, 2]");
            data.Add("position_offset", KVObject.Array([0f, 0f, 20f]));

            var definition = Define(data);

            using (Assert.Multiple())
            {
                await Assert.That(definition.Position).IsEqualTo(new Vector3(1f, 2f, 2f));
                await Assert.That(definition.PositionOffset).IsEqualTo(new Vector3(0f, 0f, 20f));
            }
        }
    }
}
