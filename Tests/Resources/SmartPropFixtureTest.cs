using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.Resources
{
    public class SmartPropFixtureTest
    {
        private const string DumpName = "Evaluation.out";

        // Set VRF_REGEN_FIXTURES=1 to write mismatching or missing evaluation dumps in the source tree
        private static readonly bool RegenerateFixtures = Environment.GetEnvironmentVariable("VRF_REGEN_FIXTURES") == "1";

        [Test]
        [Arguments("street_curb_01.vsmart_c")]
        public async Task EvaluationMatchesTheStoredDump(string name)
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path(name));

            var definition = SmartPropDefinition.Load((SmartProp)resource.DataBlock!);
            var output = new SmartPropEvaluator(null).Evaluate(definition, new SmartPropEvaluationInput { MasterSeed = 1 });
            var actual = output.ToString().ReplaceLineEndings("\n");

            var expectedFile = Path.Combine(TestFixtures.ValidOutputDirectory, name, DumpName);
            var expected = File.Exists(expectedFile) ? (await File.ReadAllTextAsync(expectedFile)).ReplaceLineEndings("\n") : null;

            if (expected != actual && RegenerateFixtures)
            {
                var sourceFile = Path.Combine(TestFixtures.SourceValidOutputDirectory, name, DumpName);
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                await File.WriteAllTextAsync(sourceFile, actual);
                await Console.Error.WriteLineAsync($"Regenerated '{sourceFile}'");
                return;
            }

            await Assert.That(actual).IsEqualTo(expected);
        }
    }
}
