using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    [TestFixture]
    public class EntityLumpTest
    {
        [Test]
        public void TestEntityLump()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "default_ents.vents_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var entityLump = (EntityLump?)resource.DataBlock;

            Debug.Assert(entityLump != null);

            var entities = entityLump.GetEntities().ToList();

            Assert.That(entities, Has.Count.EqualTo(23));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entities[0], Has.Count.EqualTo(26));
                Assert.That(entities[22], Has.Count.EqualTo(56));
            }

            Assert.That(entities[0].TryGetValue("classname", out var classname), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(classname!.ValueType, Is.EqualTo(KVValueType.String));
                Assert.That((string)classname!, Is.EqualTo("worldspawn"));
            }

            var classnameString = entities[0].GetStringProperty("classname");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(classnameString, Is.EqualTo("worldspawn"));

                Assert.That(entities[0].TryGetValue("worldname", out var worldname), Is.True);
                Assert.That((string)worldname!, Is.EqualTo("blackmap"));
            }

            var entityString = entityLump.ToEntityDumpString();

            Assert.That(entityString, Is.Not.Empty);

            var fgdString = entityLump.ToForgeGameData();

            Assert.That(fgdString, Is.Not.Empty);
        }
    }
}
