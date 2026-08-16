using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class EntityLumpTest
    {
        [Test]
        public async Task TestEntityLump()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "default_ents.vents_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var entityLump = (EntityLump?)resource.DataBlock;

            Debug.Assert(entityLump != null);

            var entities = entityLump.GetEntities().ToList();

            await Assert.That(entities).Count().IsEqualTo(23);
            using (Assert.Multiple())
            {
                await Assert.That(entities[0]).Count().IsEqualTo(26);
                await Assert.That(entities[22]).Count().IsEqualTo(56);
            }

            await Assert.That(entities[0].TryGetValue("classname", out var classname)).IsTrue();
            using (Assert.Multiple())
            {
                await Assert.That(classname!.ValueType).IsEqualTo(KVValueType.String);
                await Assert.That((string)classname!).IsEqualTo("worldspawn");
            }

            var classnameString = entities[0].GetStringProperty("classname");
            using (Assert.Multiple())
            {
                await Assert.That(classnameString).IsEqualTo("worldspawn");

                await Assert.That(entities[0].TryGetValue("worldname", out var worldname)).IsTrue();
                await Assert.That((string)worldname!).IsEqualTo("blackmap");
            }

            var entityString = entityLump.ToEntityDumpString();

            await Assert.That(entityString).IsNotEmpty();

            var fgdString = entityLump.ToForgeGameData();

            await Assert.That(fgdString).IsNotEmpty();
        }
    }
}
