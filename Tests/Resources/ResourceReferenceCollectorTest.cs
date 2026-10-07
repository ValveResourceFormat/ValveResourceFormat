using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;

namespace Tests.Resources
{
    public class ResourceReferenceCollectorTest
    {
        [Test]
        public async Task CollectsSourceFileListAsContentRelativeInputDependencies()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("deadlock_tracked_stats_player_staging_kv3_v5.vdata_c"));

            var references = ResourceReferenceCollector.Collect(resource);

            using (Assert.Multiple())
            {
                await Assert.That(references).Contains(reference => reference.Name == "stats/tracked_stats_player_staging.vdata" && reference.Kinds == ResourceReferenceKind.InputDependency);
                await Assert.That(references).DoesNotContain(reference => reference.Name.StartsWith("content/", StringComparison.Ordinal));
            }
        }

        [Test]
        public async Task CollectsBinaryEntityKeyValues()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("default_ents.vents_c"));

            var references = ResourceReferenceCollector.Collect(resource);

            await Assert.That(references).Contains(reference => reference.Name == "models/props_structures/radiant_tower002.vmdl" && reference.Source == "model");
        }

        [Test]
        public async Task CollectsUrlsInPanoramaText()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("multiteam_flyout_scoreboard.vjs_c"));

            var references = ResourceReferenceCollector.Collect(resource);

            await Assert.That(references).Contains(reference => reference.Name == "panorama/layout/custom_game/multiteam_flyout_scoreboard_team.vxml");
        }

        [Test]
        public async Task CollectsNamesInEmbeddedText()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("soundevents_dota.vsndevts_c"));

            var references = ResourceReferenceCollector.Collect(resource);

            await Assert.That(references).Contains(reference => reference.Name == "sounds/diagnostics/bell.vsnd" && reference.Source == "m_OperatorsKV");
        }

        [Test]
        public async Task SkipsOwnSourceFile()
        {
            using var stream = File.OpenRead(TestFixtures.Path("deadlock_tracked_stats_player_staging_kv3_v5.vdata_c"));
            using var resource = new Resource { FileName = "stats/tracked_stats_player_staging.vdata_c" };
            resource.Read(stream);

            var references = ResourceReferenceCollector.Collect(resource);

            await Assert.That(references.Select(static reference => reference.Name)).DoesNotContain("stats/tracked_stats_player_staging.vdata");
        }
    }
}
