using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tests.Resources
{
    public class ValidOutputTest
    {
        [Test]
        public async Task ValidOutputHasNoOrphanedFolders()
        {
            var fixtures = TestFixtures.CompiledFiles().Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

            using (Assert.Multiple())
            {
                foreach (var directory in Directory.GetDirectories(TestFixtures.ValidOutputDirectory))
                {
                    var name = Path.GetFileName(directory);

                    await Assert.That(fixtures).Contains(name).Because($"{name}: no such resource");
                }
            }
        }
    }
}
