using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamDatabase.ValvePak;
using TUnit.Core.Interfaces;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using DMElement = Datamodel.Element;

namespace Tests
{
    /// <summary>
    /// Tests vmap export of point_template entities, whose child entities are compiled into
    /// separate entity lumps with transforms stored relative to the template.
    ///
    /// The fixture vpk was compiled with resourcecompiler from a map containing only
    /// prop_dynamic crates and the point_templates referencing them:
    /// <code>
    /// template_offset   at (100, 0, 0)             -> crate_offset  at (50, 50, 50), 30 yaw
    /// template_rotated  at (0, 100, 0), 90 yaw     -> crate_rotated at (-50, 60, 70), 30 yaw
    /// template_sheared  at (100, 0, 0), (1, 2, 1)  -> crate_sheared at (10, 20, 30), 45 yaw
    /// template_shared_a at (200, 0, 0)             -> crate_shared  at (25, 25, 25), 15 yaw
    /// template_shared_b at (0, 200, 0), 90 yaw     -> crate_shared  (same entity as above)
    /// template_multi    at (300, 0, 0)             -> crate_multi_a at (310, 10, 0)
    ///                                                 crate_multi_b at (310, -10, 0)
    /// </code>
    /// </summary>
    [ClassDataSource<PointTemplateMap>(Shared = SharedType.PerClass)]
    [NotInParallel(nameof(PointTemplateExtractTest))]
    public class PointTemplateExtractTest(PointTemplateMap map)
    {
        private const float Tolerance = 0.01f;
        private const string CrateModel = "models/cs_italy/crate/italy_wood_crate_1.vmdl";

        [Test]
        public async Task CompiledMapResourceParses()
        {
            var entry = map.Package.FindEntry("maps/point_template_test.vmap_c");
            await Assert.That(entry).IsNotNull();

            map.Package.ReadEntry(entry!, out var bytes);

            using var resource = new Resource { FileName = "point_template_test.vmap_c" };
            resource.Read(new MemoryStream(bytes));

            await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Map);

            foreach (var block in resource.Blocks)
            {
                await Assert.That(block.ToString()).IsNotNull();
            }

            var references = resource.ExternalReferences!.ResourceRefInfoList.Select(r => r.Name).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(references).Contains("maps/point_template_test/world.vwrld");
                await Assert.That(references).Contains("maps/point_template_test/entities/default_ents.vents");
                await Assert.That(references.Count(r => r.EndsWith("#entitylumpname.vents", StringComparison.Ordinal))).IsEqualTo(6);

            }
        }

        [Test]
        public async Task WorldResourceReferencesEntityLumps()
        {
            using var worldResource = map.Loader.LoadFile("maps/point_template_test/world.vwrld_c");
            var world = (World)worldResource!.DataBlock!;

            using (Assert.Multiple())
            {
                await Assert.That(world.GetEntityLumpNames()).Contains(name => name.Contains("default_ents", StringComparison.Ordinal));
                await Assert.That(world.GetWorldNodeNames()).IsNotEmpty();

            }
        }

        [Test]
        public async Task EntityLumpHierarchyParses()
        {
            using var lumpResource = map.Loader.LoadFile("maps/point_template_test/entities/default_ents.vents_c");
            var lump = (EntityLump)lumpResource!.DataBlock!;

            var childNames = lump.GetChildEntityNames();
            await Assert.That(childNames).Count().IsEqualTo(6);

            var compiledEntities = lump.GetEntities();
            var templates = compiledEntities.Where(e => e.GetStringProperty("classname") == "point_template").ToList();

            using (Assert.Multiple())
            {
                await Assert.That(compiledEntities.Single(e => e.GetStringProperty("classname") == "worldspawn")).IsNotNull();
                await Assert.That(templates).Count().IsEqualTo(6);

            }

            // every template's entitylumpname must resolve to one of the child lumps
            var childLumpNames = childNames.Select(name =>
            {
                using var childResource = map.Loader.LoadFileCompiled(name);
                return ((EntityLump)childResource!.DataBlock!).Name;
            }).ToList();

            foreach (var template in templates)
            {
                await Assert.That(childLumpNames).Contains(template.GetStringProperty("entitylumpname"));
            }

            await Assert.That(lump.ToString()).IsNotEmpty();
        }

        // The compiler stores child lump entities inverse-transformed by the template's origin and
        // rotation (but not its scale), which is why these origins differ from the authored ones.
        [Test]
        [Arguments("3#entityLumpName", "crate_offset", -50f, 50f, 50f, 30f)]
        [Arguments("5#entityLumpName", "crate_rotated", -40f, 50f, 70f, -60f)]
        [Arguments("7#entityLumpName", "crate_sheared", -90f, 20f, 30f, 45f)]
        [Arguments("9#entityLumpName", "crate_shared", -175f, 25f, 25f, 15f)]
        [Arguments("10#entityLumpName", "crate_shared", -175f, -25f, 25f, -75f)]
        public async Task ChildLumpStoresTemplateRelativeTransform(string lumpName, string targetname, float x, float y, float z, float yaw)
        {
            var lump = await LoadChildLump(lumpName);
            var crate = lump.GetEntities().Single();

            using (Assert.Multiple())
            {
                await Assert.That(crate.GetStringProperty("classname")).IsEqualTo("prop_dynamic");
                await Assert.That(crate.TargetName).IsEqualTo($"[PR#]{targetname}");
                await Assert.That(crate.GetStringProperty("model")).IsEqualTo(CrateModel);

            }

            await AssertVector(crate.GetVector3Property("origin"), new Vector3(x, y, z));
            await Assert.That(crate.GetVector3Property("angles").Y).IsEqualTo(yaw).Within(Tolerance);
            await AssertVector(crate.GetVector3Property("scales", Vector3.One), Vector3.One);
        }

        [Test]
        public async Task ChildLumpKeepsMultipleEntitiesInTemplateOrder()
        {
            var lump = await LoadChildLump("13#entityLumpName");
            var crates = lump.GetEntities();

            await Assert.That(crates).Count().IsEqualTo(2);

            using (Assert.Multiple())
            {
                await Assert.That(crates[0].TargetName).IsEqualTo("[PR#]crate_multi_a");
                await Assert.That(crates[1].TargetName).IsEqualTo("[PR#]crate_multi_b");
                await Assert.That(crates[0].GetStringProperty("_template_lump_ent_index")).IsEqualTo("0");
                await Assert.That(crates[1].GetStringProperty("_template_lump_ent_index")).IsEqualTo("1");

            }

            await AssertVector(crates[0].GetVector3Property("origin"), new Vector3(10, 10, 0));
            await AssertVector(crates[1].GetVector3Property("origin"), new Vector3(10, -10, 0));
        }

        private async Task<EntityLump> LoadChildLump(string lumpName)
        {
            var path = $"maps/point_template_test/entities/{lumpName.ToLowerInvariant()}.vents_c";
            using var resource = map.Loader.LoadFile(path);
            var lump = (EntityLump)resource!.DataBlock!;

            await Assert.That(lump.Name).IsEqualTo(lumpName);
            return lump;
        }

        [Test]
        public async Task ChildPlacementIsRestored()
        {
            var crate = FindEntity("crate_offset");

            await AssertVector((Vector3)crate["origin"]!, new Vector3(50, 50, 50));
            await AssertAngles((Datamodel.QAngle)crate["angles"]!, 0, 30, 0);
            await AssertVector((Vector3)crate["scales"]!, Vector3.One);

            // the authored Template01 keyvalue survives compilation (lowercased on export)
            // and must remain the only template reference key
            var template = FindEntity("template_offset");
            var properties = Properties(template);
            await Assert.That(properties["template01"]).IsEqualTo("crate_offset");
            await Assert.That(properties.Select(p => p.Key)).DoesNotContain("Template01");
        }

        [Test]
        public async Task RotatedTemplateChildPlacementIsRestored()
        {
            // the child is stored inverse-rotated relative to the template, so composing it
            // with the template transform must restore the authored placement
            var crate = FindEntity("crate_rotated");

            await AssertVector((Vector3)crate["origin"]!, new Vector3(-50, 60, 70));
            await AssertAngles((Datamodel.QAngle)crate["angles"]!, 0, 30, 0);

            var template = FindEntity("template_rotated");
            await AssertVector((Vector3)template["origin"]!, new Vector3(0, 100, 0));
            await AssertAngles((Datamodel.QAngle)template["angles"]!, 0, 90, 0);
        }

        [Test]
        public async Task NonUniformlyScaledTemplateChildPlacementIsRestored()
        {
            // the compiler stores child transforms relative by template origin and rotation only,
            // never by its scale, so composing must ignore the parent scale to restore the
            // authored placement (a full TRS product would also shear, which Decompose rejects)
            var crate = FindEntity("crate_sheared");

            await AssertVector((Vector3)crate["origin"]!, new Vector3(10, 20, 30));
            await AssertAngles((Datamodel.QAngle)crate["angles"]!, 0, 45, 0);
            await AssertVector((Vector3)crate["scales"]!, Vector3.One);
        }

        [Test]
        public async Task EntitySharedByTwoTemplatesIsDeduplicated()
        {
            // the compiler clones an entity referenced by two templates into both child lumps;
            // the clones must collapse back into one entity, or each recompile would make
            // every template capture all the same-named copies and multiply the spawns
            var crates = map.Entities.Where(e => Properties(e).TryGetValue("targetname", out var name) && (string?)name == "crate_shared").ToList();

            await Assert.That(crates).Count().IsEqualTo(1);
            await AssertVector((Vector3)crates[0]["origin"]!, new Vector3(25, 25, 25));
            await AssertAngles((Datamodel.QAngle)crates[0]["angles"]!, 0, 15, 0);

            await Assert.That(Properties(FindEntity("template_shared_a"))["template01"]).IsEqualTo("crate_shared");
            await Assert.That(Properties(FindEntity("template_shared_b"))["template01"]).IsEqualTo("crate_shared");
        }

        [Test]
        public async Task TemplateWithMultipleChildrenNumbersItsReferences()
        {
            await AssertVector((Vector3)FindEntity("crate_multi_a")["origin"]!, new Vector3(310, 10, 0));
            await AssertVector((Vector3)FindEntity("crate_multi_b")["origin"]!, new Vector3(310, -10, 0));

            var properties = Properties(FindEntity("template_multi"));
            await Assert.That(properties["template01"]).IsEqualTo("crate_multi_a");
            await Assert.That(properties["template02"]).IsEqualTo("crate_multi_b");
        }

        private static DMElement Properties(DMElement entity)
            => (DMElement)entity["entity_properties"]!;

        private DMElement FindEntity(string targetname)
            => map.Entities.Single(entity =>
                entity.TryGetValue("entity_properties", out var propsObject)
                && propsObject is DMElement properties
                && properties.TryGetValue("targetname", out var name)
                && (string?)name == targetname);

        private static async Task AssertVector(Vector3 actual, Vector3 expected)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.X).IsEqualTo(expected.X).Within(Tolerance);
                await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(Tolerance);
                await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(Tolerance);
            }
        }

        private static async Task AssertAngles(Datamodel.QAngle actual, float pitch, float yaw, float roll)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.Pitch).IsEqualTo(pitch).Within(Tolerance);
                await Assert.That(actual.Yaw).IsEqualTo(yaw).Within(Tolerance);
                await Assert.That(actual.Roll).IsEqualTo(roll).Within(Tolerance);
            }
        }
    }

    /// <summary>
    /// Extracts the point_template test map once and shares it across the fixture's tests.
    /// </summary>
    public sealed class PointTemplateMap : IAsyncInitializer, IAsyncDisposable
    {
        public Package Package { get; private set; } = null!;
        public GameFileLoader Loader { get; private set; } = null!;
        public Datamodel.Datamodel Vmap { get; private set; } = null!;
        public List<DMElement> Entities { get; private set; } = null!;

        public Task InitializeAsync()
        {
            var vpkPath = Path.Combine(TestContext.TestDirectory!, "Files", "point_template_test.vpk");

            Package = new Package();
            Package.Read(vpkPath);

            Loader = new GameFileLoader(Package, vpkPath);
            using var vmapResource = Loader.LoadFile("maps/point_template_test.vmap_c");

            var extract = new MapExtract(vmapResource!, Loader);
            var vmapData = extract.ToValveMap();

            using var stream = new MemoryStream(vmapData);
            Vmap = Datamodel.Datamodel.Load(stream, Datamodel.Codecs.DeferredMode.Disabled);

            var world = (DMElement)Vmap.Root!["world"]!;
            Entities = ((Datamodel.ElementArray)world["children"]!).OfType<DMElement>().ToList();

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Vmap?.Dispose();
            Loader?.Dispose();
            Package?.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
