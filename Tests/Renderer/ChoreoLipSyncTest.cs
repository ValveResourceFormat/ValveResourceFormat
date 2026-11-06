using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.FlexSceneFile;
using ValveResourceFormat.Renderer.Choreo;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Renderer
{
    public class ChoreoLipSyncTest
    {
        private const float Tolerance = 1e-4f;

        private static (ChoreoLipSync LipSync, ushort Phoneme, int Controller, float Weight) Create()
        {
            var phonemes = new FlexSceneFile();
            phonemes.Read(TestFixtures.Path("phonemes.vfe"));

            var setting = phonemes.FlexSettings.First(setting => setting.Settings.Values.Any(weight => weight.Weight > 0f));
            var (key, flexWeight) = setting.Settings.First(entry => entry.Value.Weight > 0f);

            var controllers = new Dictionary<string, int> { [phonemes.KeyNames[key]] = 0 };

            return (new ChoreoLipSync(phonemes, controllers), (ushort)setting.Phoneme, 0, flexWeight.Weight);
        }

        private static Sentence CreateSentence(ushort phoneme, EmphasisSample[]? emphasis = null)
        {
            return new Sentence
            {
                RunTimePhonemes = [new PhonemeTag { StartTime = 1f, EndTime = 2f, PhonemeCode = phoneme }],
                EmphasisSamples = emphasis ?? [],
            };
        }

        [Test]
        public async Task PhonemeUnderTheWholeWindowHasItsFullValue()
        {
            var (lipSync, phoneme, controller, weight) = Create();
            var values = new float[1];

            lipSync.AddVisemes(CreateSentence(phoneme), 1.5f, 3f, values);

            await Assert.That(values[controller]).IsEqualTo(weight).Within(Tolerance);
        }

        [Test]
        public async Task PhonemeShowsBeforeItStartsAsItEntersTheWindow()
        {
            var (lipSync, phoneme, controller, weight) = Create();
            var values = new float[1];

            lipSync.AddVisemes(CreateSentence(phoneme), 0.96f, 3f, values);

            await Assert.That(values[controller]).IsEqualTo(weight * 0.5f).Within(Tolerance);
        }

        [Test]
        public async Task PhonemeOutsideOfTheWindowAddsNothing()
        {
            var (lipSync, phoneme, controller, _) = Create();
            var values = new float[1];

            lipSync.AddVisemes(CreateSentence(phoneme), 0.5f, 3f, values);
            lipSync.AddVisemes(CreateSentence(phoneme), 2.5f, 3f, values);

            await Assert.That(values[controller]).IsEqualTo(0f);
        }

        [Test]
        public async Task LongPhonemeWidensTheWindowOverTheNext()
        {
            var (lipSync, phoneme, controller, weight) = Create();
            var values = new float[1];

            var sentence = new Sentence
            {
                RunTimePhonemes =
                [
                    new PhonemeTag { StartTime = 0f, EndTime = 1f, PhonemeCode = phoneme },
                    new PhonemeTag { StartTime = 1f, EndTime = 2f, PhonemeCode = phoneme },
                ],
            };

            // Both phonemes share the window, so together they cover it
            lipSync.AddVisemes(sentence, 0.5f, 3f, values);

            await Assert.That(values[controller]).IsEqualTo(weight).Within(Tolerance);
        }

        [Test]
        public async Task EmphasisScalesBetweenItsLimits()
        {
            var (lipSync, phoneme, controller, weight) = Create();
            var strong = new float[1];
            var weak = new float[1];

            EmphasisSample[] high = [new EmphasisSample { Time = 0f, Value = 1f }, new EmphasisSample { Time = 3f, Value = 1f }];
            EmphasisSample[] low = [new EmphasisSample { Time = 0f, Value = 0f }, new EmphasisSample { Time = 3f, Value = 0f }];

            lipSync.AddVisemes(CreateSentence(phoneme, high), 1.5f, 3f, strong);
            lipSync.AddVisemes(CreateSentence(phoneme, low), 1.5f, 3f, weak);

            using (Assert.Multiple())
            {
                await Assert.That(strong[controller]).IsEqualTo(weight * 1.2f).Within(Tolerance);
                await Assert.That(weak[controller]).IsEqualTo(weight * 0.8f).Within(Tolerance);
            }
        }
    }
}
