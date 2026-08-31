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
        private static EntityLump LoadLump(Resource resource, string name)
        {
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", name));
            return (EntityLump)resource.DataBlock!;
        }

        [Test]
        public async Task ResolvesConnectionTargets()
        {
            using var resource = new Resource();
            var lump = LoadLump(resource, "ascent_speedup_switch_template_ents.vents_c");
            var entities = lump.GetEntities().ToList();

            var named = entities.First(e => !string.IsNullOrEmpty(e.TargetName));
            var resolver = new EntityIOTargetResolver(entities);
            var results = new List<EntityLump.Entity>();

            using (Assert.Multiple())
            {
                await Assert.That(resolver.Resolve(named.TargetName, EntityIOTargetType.EntityName, results)).IsEqualTo(EntityIOTargetOutcome.Matched);
                await Assert.That(results).Contains(named);

                var byClass = named.GetStringProperty("classname");
                await Assert.That(resolver.Resolve(byClass, EntityIOTargetType.ClassName, results)).IsEqualTo(EntityIOTargetOutcome.Matched);
                await Assert.That(resolver.Resolve(byClass, EntityIOTargetType.EntityNameOrClassName, results)).IsEqualTo(EntityIOTargetOutcome.Matched);

                // Wildcards match by prefix the way the engine does.
                await Assert.That(resolver.Resolve(named.TargetName![..2] + "*", EntityIOTargetType.EntityName, results)).IsEqualTo(EntityIOTargetOutcome.Matched);

                await Assert.That(resolver.Resolve("!activator", EntityIOTargetType.EntityName, results)).IsEqualTo(EntityIOTargetOutcome.Special);
                await Assert.That(resolver.Resolve("nothing", EntityIOTargetType.SpecialCaller, results)).IsEqualTo(EntityIOTargetOutcome.Special);
                await Assert.That(resolver.Resolve(null, EntityIOTargetType.EntityName, results)).IsEqualTo(EntityIOTargetOutcome.Empty);
                await Assert.That(resolver.Resolve("does_not_exist_anywhere", EntityIOTargetType.EntityName, results)).IsEqualTo(EntityIOTargetOutcome.NotFound);
                await Assert.That(resolver.Resolve("anything", EntityIOTargetType.EHandle, results)).IsEqualTo(EntityIOTargetOutcome.Unsupported);
            }
        }

        [Test]
        public async Task FindsInputConnectionsAndRenderTint()
        {
            using var resource = new Resource();
            var lump = LoadLump(resource, "graphics_settings_ents.vents_c");
            var entities = lump.GetEntities().ToList();

            var inputs = entities
                .Select(e => (Entity: e, Inputs: e.GetInputConnections(entities)))
                .Where(pair => pair.Inputs.Count > 0)
                .ToList();

            using (Assert.Multiple())
            {
                await Assert.That(inputs.Count).IsEqualTo(1);
                await Assert.That(inputs[0].Inputs[0].InputName).IsEqualTo("SetOn");

                foreach (var entity in entities)
                {
                    var tint = entity.GetRenderTint();
                    await Assert.That(tint.X).IsBetween(0f, 1f);
                    await Assert.That(tint.W).IsBetween(0f, 1f);
                    await Assert.That(entity.GetVector2Property("no_such_vector2", Vector2.One)).IsEqualTo(Vector2.One);
                }
            }
        }

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
