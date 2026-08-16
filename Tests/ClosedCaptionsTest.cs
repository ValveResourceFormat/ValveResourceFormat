using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.ClosedCaptions;

namespace Tests
{
    public class ClosedCaptionsTest
    {
        [Test]
        public async Task ParseClosedCaptions()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "subtitles_announcer_killing_spree_english.dat");

            var captions = new ClosedCaptions();
            captions.Read(file);

            using (Assert.Multiple())
            {
                await Assert.That(captions.Captions).Count().IsEqualTo(840);
                await Assert.That(captions.ToString().Length).IsGreaterThan(1000);

                var caption = captions.Captions[839];
                await Assert.That(caption.Blocknum).IsEqualTo(4);
                await Assert.That(caption.Hash).IsEqualTo(3873743860);
                await Assert.That(caption.HashText).IsEqualTo(3502107501);
                await Assert.That(caption.Length).IsEqualTo((ushort)16);
                await Assert.That(caption.Offset).IsEqualTo((ushort)2086);
                await Assert.That(caption.Text).IsEqualTo("Ownage!");

                var captionByCrc = captions["announcer_killing_spree_announcer_ownage_01"];
                await Assert.That(captionByCrc).IsEqualTo(caption);

                var i = 0;
                var found = false;

                foreach (var captionInLoop in captions)
                {
                    i++;

                    if (captionInLoop.Hash == caption.Hash)
                    {
                        found = true;
                        break;
                    }
                }

                await Assert.That(found).IsTrue();
                await Assert.That(i).IsEqualTo(840);
            }
        }
    }
}
