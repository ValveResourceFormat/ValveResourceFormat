using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class ParticleUpgradeTraceTest
    {
        private static ParticleSystem Load(Resource resource, string name)
        {
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", name));
            return (ParticleSystem)resource.DataBlock!;
        }

        [Test]
        public async Task TracesRenamedFunctionsThroughTheChain()
        {
            using var resource = new Resource();
            var particle = Load(resource, "vent_impact_dust_02.vpcf_c");

            var trace = particle.GetUpgradeTrace();
            var all = trace.Values.SelectMany(list => list).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(trace.Keys.ToList()).Contains("m_Operators");
                await Assert.That(all.Count).IsEqualTo(16);
                await Assert.That(all.Count(f => f.OriginalClass != null)).IsEqualTo(5);
                await Assert.That(all.Count(f => f.RemovedByUpgrade)).IsEqualTo(0);

                var sphere = all.Single(f => f.OriginalClass == "C_INIT_CreateWithinSphere");
                await Assert.That(sphere.Class).IsEqualTo("C_INIT_CreateWithinSphereTransform");

                var lifetime = all.Single(f => f.OriginalClass == "C_INIT_RandomLifeTime");
                await Assert.That(lifetime.Class).IsEqualTo("C_INIT_InitFloat");
            }
        }

        [Test]
        public async Task TraceMatchesUpgradedDocument()
        {
            using var resource = new Resource();
            var particle = Load(resource, "plaster_dropseeds.vpcf_c");

            var trace = particle.GetUpgradeTrace();
            var upgraded = particle.GetUpgradedData();

            using (Assert.Multiple())
            {
                // Every surviving traced entry appears in the upgraded document under its final class name.
                foreach (var (listName, functions) in trace)
                {
                    var upgradedClasses = (upgraded.GetArray(listName) ?? [])
                        .Select(f => f.GetStringProperty("_class"))
                        .ToList();

                    foreach (var traced in functions.Where(f => !f.RemovedByUpgrade))
                    {
                        await Assert.That(upgradedClasses).Contains(traced.Class);
                    }
                }
            }
        }
    }
}
