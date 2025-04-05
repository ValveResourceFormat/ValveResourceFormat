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

            var entityLump = (EntityLump)resource.DataBlock;

            var entities = entityLump.GetEntities().ToList();

            Assert.That(entities, Has.Count.EqualTo(23));
            Assert.Multiple(() =>
            {
                Assert.That(entities[0].Properties.Properties, Has.Count.EqualTo(26));
                Assert.That(entities[22].Properties.Properties, Has.Count.EqualTo(56));
            });

            var classname = entities[0].GetProperty("classname");
            Assert.Multiple(() =>
            {
                Assert.That(classname.Type, Is.EqualTo(KVValueType.String));
                Assert.That(classname.Value, Is.EqualTo("worldspawn"));
            });

            var classnameString = entities[0].GetProperty<string>("classname");
            Assert.That(classnameString, Is.EqualTo("worldspawn"));

            var worldname = entities[0].GetProperty("worldname");
            Assert.That(worldname.Value, Is.EqualTo("blackmap"));

            var entityString = entityLump.ToEntityDumpString();

            Assert.That(entityString, Is.Not.Empty);

            var fgdString = entityLump.ToForgeGameData();

            Assert.That(fgdString, Is.Not.Empty);
        }
    }
}
