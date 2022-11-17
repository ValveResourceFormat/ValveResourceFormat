using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

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

            Assert.That(entities.Count, Is.EqualTo(23));
            Assert.That(entities[0].Properties.Count, Is.EqualTo(26));
            Assert.That(entities[22].Properties.Count, Is.EqualTo(56));

            var classname = entities[0].GetProperty("classname");
            Assert.That(classname, Is.Not.Null);
            Assert.That(classname.Type, Is.EqualTo(0x1e));
            Assert.That(classname.Data, Is.EqualTo("worldspawn"));

            var classnameString = entities[0].GetProperty<string>("classname");
            Assert.That(classnameString, Is.EqualTo("worldspawn"));

            var worldname = entities[0].GetProperty("worldname");
            Assert.That(worldname, Is.Not.Null);
            Assert.That(worldname.Data, Is.EqualTo("blackmap"));

            var entityString = entityLump.ToEntityDumpString();

            Assert.That(entityString, Is.Not.Empty);
        }
    }
}
