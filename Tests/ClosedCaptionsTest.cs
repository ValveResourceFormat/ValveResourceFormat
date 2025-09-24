using System.IO;
using NUnit.Framework;
using ValveResourceFormat.ClosedCaptions;

namespace Tests
{
    public class ClosedCaptionsTest
    {
        [Test]
        public void ParseClosedCaptions()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "subtitles_announcer_killing_spree_english.dat");

            var captions = new ClosedCaptions();
            captions.Read(file);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(captions.Captions, Has.Count.EqualTo(840));
                Assert.That(captions.ToString(), Has.Length.GreaterThan(1000));

                var caption = captions.Captions[839];
                Assert.That(caption.Blocknum, Is.EqualTo(4));
                Assert.That(caption.Hash, Is.EqualTo(3873743860));
                Assert.That(caption.HashText, Is.EqualTo(3502107501));
                Assert.That(caption.Length, Is.EqualTo(16));
                Assert.That(caption.Offset, Is.EqualTo(2086));
                Assert.That(caption.Text, Is.EqualTo("Ownage!"));

                var captionByCrc = captions["announcer_killing_spree_announcer_ownage_01"];
                Assert.That(captionByCrc, Is.EqualTo(caption));

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

                Assert.That(found, Is.True);
                Assert.That(i, Is.EqualTo(840));
            }
        }
    }
}
