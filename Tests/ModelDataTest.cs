using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;

namespace Tests
{
    public class ModelDataTest
    {
        // Truck-like: one mesh per level, switch values per level.
        private static readonly long[] TruckMasks = [1, 2, 4];
        private static readonly float[] TruckSwitches = [0f, 35f, 50f];

        // Truck with a LODGroupAll mesh (mask 7, present in every level) plus a second LOD0-only mesh.
        private static readonly long[] AllGroupMasks = [1, 2, 4, 1, 7];
        private static readonly float[] AllGroupSwitches = [0f, 35f, 50f];

        // Mesh 0 is in LOD0 only, mesh 1 is in LODs 1-7, with 8 switch values.
        private static readonly long[] AlchemistMasks = [0x01, 0xFE];
        private static readonly float[] AlchemistSwitches = [0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];

        // No mesh in LOD0, so the lowest populated level is 1.
        private static readonly long[] EmptyLod0Masks = [2, 4];

        private static readonly int[] TruckLevels = [0, 1, 2];
        private static readonly int[] AlchemistLevels = [0, 1, 2, 3, 4, 5, 6, 7];
        private static readonly int[] EmptyLod0Levels = [1, 2];

        // lod_test.vmdl_c fixture: 5 embedded meshes, one per LOD level.
        private static readonly int[] FixtureLevels = [0, 1, 2, 3, 4];
        private static readonly float[] FixtureSwitches = [0f, 5f, 10f, 15f, 20f];

        [Test]
        public async Task ContiguousLods()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData(TruckMasks, TruckSwitches));

            using (Assert.Multiple())
            {
                await Assert.That(lod.LowestLevel).IsEqualTo(0);
                await Assert.That(lod.AvailableLevels).IsEquivalentTo(TruckLevels, CollectionOrdering.Matching);
                await Assert.That(lod.LevelCount).IsEqualTo(3);

                await Assert.That(lod.IsMeshInLevel(0, 0)).IsTrue();
                await Assert.That(lod.IsMeshInLevel(1, 1)).IsTrue();
                await Assert.That(lod.IsMeshInLevel(2, 2)).IsTrue();
                await Assert.That(lod.IsMeshInLevel(0, 1)).IsFalse();
                await Assert.That(lod.IsMeshInLevel(1, 0)).IsFalse();

            }
        }

        [Test]
        public async Task SelectLevelFollowsMetric()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData(TruckMasks, TruckSwitches));

            using (Assert.Multiple())
            {
                // Metric grows as the model gets smaller on screen, so higher metric => higher (lower-detail) level.
                await Assert.That(lod.SelectLevel(0f)).IsEqualTo(0);
                await Assert.That(lod.SelectLevel(34f)).IsEqualTo(0);
                await Assert.That(lod.SelectLevel(35f)).IsEqualTo(1);
                await Assert.That(lod.SelectLevel(49f)).IsEqualTo(1);
                await Assert.That(lod.SelectLevel(50f)).IsEqualTo(2);
                await Assert.That(lod.SelectLevel(1000f)).IsEqualTo(2);

            }
        }

        [Test]
        public async Task HasDistinctLevelsDetectsRealLods()
        {
            using (Assert.Multiple())
            {
                // No LOD data, or a single level: nothing to switch between.
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData([], [])).HasDistinctLevels).IsFalse();
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData([0x01], [])).HasDistinctLevels).IsFalse();

                // A mesh present in every level (mask 0xFF, no switch distances) is "always shown", not a
                // LOD. This is the chess king: m_refLODGroupMasks [255], no switch distances.
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData([0xFF], [])).HasDistinctLevels).IsFalse();
                // Multiple meshes that share the same all-levels mask also render identically everywhere.
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData([0x03, 0x03], [])).HasDistinctLevels).IsFalse();

                // Distinct geometry per level: real LODs.
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData(TruckMasks, TruckSwitches)).HasDistinctLevels).IsTrue();
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData(AlchemistMasks, AlchemistSwitches)).HasDistinctLevels).IsTrue();

                // Empty LOD0 with meshes only in LOD1 (ctm_sas): the empty level is a distinct state, so the
                // model has a real LOD. m_refLODGroupMasks [2,2,2,2,2], switch distances [0, 2].
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData([2, 2, 2, 2, 2], [0f, 2f])).HasDistinctLevels).IsTrue();
            }
        }

        [Test]
        public async Task MetricRangePerLevel()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData(TruckMasks, TruckSwitches));
            var noSwitches = new ModelLodInfo(TestFixtures.ModelLodData(TruckMasks, []));

            using (Assert.Multiple())
            {
                // Each level is active from its own switch value up to the next level's. The top one is open-ended.
                await Assert.That(lod.GetMetricRange(0)).IsEqualTo((0f, (float?)35f));
                await Assert.That(lod.GetMetricRange(1)).IsEqualTo((35f, (float?)50f));
                await Assert.That(lod.GetMetricRange(2)).IsEqualTo((50f, (float?)null));

                // No switch data: everything collapses to an open range from 0.
                await Assert.That(noSwitches.GetMetricRange(0)).IsEqualTo((0f, (float?)null));

            }
        }

        [Test]
        public async Task MultipleLevelsPerMesh()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData(AlchemistMasks, AlchemistSwitches));

            using (Assert.Multiple())
            {
                await Assert.That(lod.CombinedMask).IsEqualTo(0xFF);
                await Assert.That(lod.LowestLevel).IsEqualTo(0);
                await Assert.That(lod.AvailableLevels).IsEquivalentTo(AlchemistLevels, CollectionOrdering.Matching);
                await Assert.That(lod.LevelCount).IsEqualTo(8);

                await Assert.That(lod.IsMeshInLevel(0, 0)).IsTrue();
                await Assert.That(lod.IsMeshInLevel(0, 1)).IsFalse();
                await Assert.That(lod.IsMeshInLevel(1, 0)).IsFalse();
                await Assert.That(lod.IsMeshInLevel(1, 1)).IsTrue();
                await Assert.That(lod.IsMeshInLevel(1, 7)).IsTrue();

            }
        }

        [Test]
        public async Task MeshInAllLevelsIsLodGroupAll()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData(AllGroupMasks, AllGroupSwitches));

            using (Assert.Multiple())
            {
                // Only the mask-7 mesh spans every level, so only it is a LODGroupAll member.
                await Assert.That(lod.IsMeshInAllLevels(4)).IsTrue();
                await Assert.That(lod.IsMeshInAllLevels(0)).IsFalse();
                await Assert.That(lod.IsMeshInAllLevels(1)).IsFalse();
                await Assert.That(lod.IsMeshInAllLevels(2)).IsFalse();
                await Assert.That(lod.IsMeshInAllLevels(3)).IsFalse();

                // A single populated level is not treated as "all levels", so nothing is pulled out.
                await Assert.That(new ModelLodInfo(TestFixtures.ModelLodData([1, 1], [0f])).IsMeshInAllLevels(0)).IsFalse();

            }
        }

        [Test]
        public async Task EmptyLod0FallsBackToLowestPopulated()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData(EmptyLod0Masks, []));

            using (Assert.Multiple())
            {
                await Assert.That(lod.LowestLevel).IsEqualTo(1);
                await Assert.That(lod.AvailableLevels).IsEquivalentTo(EmptyLod0Levels, CollectionOrdering.Matching);
                await Assert.That(lod.LevelCount).IsEqualTo(3);
                // With no switch values, automatic selection stays at the lowest populated level.
                await Assert.That(lod.SelectLevel(1000f)).IsEqualTo(1);

            }
        }

        [Test]
        public async Task NoLodData()
        {
            var lod = new ModelLodInfo(TestFixtures.ModelLodData([], []));

            using (Assert.Multiple())
            {
                await Assert.That(lod.CombinedMask).IsEqualTo(0);
                await Assert.That(lod.LowestLevel).IsEqualTo(0);
                await Assert.That(lod.AvailableLevels).IsEmpty();
                await Assert.That(lod.LevelCount).IsEqualTo(0);
                // A mesh with no mask entry is treated as always present.
                await Assert.That(lod.IsMeshInLevel(0, 0)).IsTrue();

            }
        }

        // The lod_test fixture is a synthetic 5-LOD model with embedded meshes (no external
        // references), m_refLODGroupMasks [1,2,4,8,16] and m_lodGroupSwitchDistances [0,5,10,15,20].
        [Test]
        public async Task FixtureLodInfoMatchesData()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "lod_test.vmdl_c"));

            var lod = ((Model)resource.DataBlock!).LodInfo;

            using (Assert.Multiple())
            {
                await Assert.That(lod.LowestLevel).IsEqualTo(0);
                await Assert.That(lod.AvailableLevels).IsEquivalentTo(FixtureLevels, CollectionOrdering.Matching);
                await Assert.That(lod.LevelCount).IsEqualTo(5);
                await Assert.That(lod.SwitchDistances).IsEquivalentTo(FixtureSwitches, CollectionOrdering.Matching);

            }
        }

        /// <summary>
        /// Mesh group names encode body groups: a name of the form <c>group_@choice</c> declares one
        /// choice of a body group, and newer models bury the authored choice name behind a marker. A name
        /// without the separator belongs to no body group at all.
        /// </summary>
        [Test]
        public async Task MeshGroupNamesDecodeIntoBodyGroups()
        {
            using var resource = TestFixtures.Load("necro_archer.vmdl_c");
            var groups = ((Model)resource.DataBlock!).MeshGroups;

            var lone = groups.BodyGroups[0];
            var gear = groups.BodyGroups[1];

            using (Assert.Multiple())
            {
                await Assert.That(groups.Names).IsEquivalentTo([
                    "default_#&necro_archer_model",
                    "necro_gear1_@0_#&necro_archer_gear1",
                    "necro_gear1_@1_#&necro_archer_gear2",
                    "necro_gear1_@2_#&necro_archer_gear3",
                ], CollectionOrdering.Matching);

                // A group holding one choice is compiled without the choice index, so "default" is a
                // real body group whose one choice is named after the mesh it shows.
                await Assert.That(groups.BodyGroups).Count().IsEqualTo(2);
                await Assert.That(lone.Name).IsEqualTo("default");
                await Assert.That(lone.Choices.Select(choice => choice.Name))
                    .IsEquivalentTo(["necro_archer_model"], CollectionOrdering.Matching);
                await Assert.That(lone.Choices[0].Indexed).IsFalse();

                await Assert.That(gear.Name).IsEqualTo("necro_gear1");
                await Assert.That(gear.Choices.Select(choice => choice.Indexed))
                    .IsEquivalentTo([true, true, true], CollectionOrdering.Matching);
                await Assert.That(gear.Choices.Select(choice => choice.Name))
                    .IsEquivalentTo(["necro_archer_gear1", "necro_archer_gear2", "necro_archer_gear3"], CollectionOrdering.Matching);

                // A choice's index is the bit it occupies in a mesh's group mask, so it indexes Names.
                await Assert.That(gear.Choices.Select(choice => choice.GroupIndex))
                    .IsEquivalentTo([1, 2, 3], CollectionOrdering.Matching);
                await Assert.That(gear.Choices.Select(choice => groups.Names[choice.GroupIndex]))
                    .IsEquivalentTo(gear.Choices.Select(choice => choice.FullName), CollectionOrdering.Matching);

                // The default mask picks the base model plus the first gear choice.
                await Assert.That(groups.Defaults)
                    .IsEquivalentTo(["default_#&necro_archer_model", "necro_gear1_@0_#&necro_archer_gear1"], CollectionOrdering.Matching);

                // A model that declares no groups draws everything, whatever is asked for.
                using var noGroups = TestFixtures.Load("box_creature_ik_model.vmdl_c");
                var empty = ((Model)noGroups.DataBlock!).MeshGroups;

                await Assert.That(empty.Names).IsEmpty();
                await Assert.That(empty.IsMeshInAnyGroup(0, ["anything"])).IsTrue();
                await Assert.That(empty.IndexOf("anything")).IsEqualTo(-1);
            }
        }

        /// <summary>
        /// Every mesh owns a contiguous slice of one bone remap table, so a mesh's slice starts where the
        /// previous one ended and the last runs to the end of the table.
        /// </summary>
        [Test]
        public async Task BoneRemapSlicesTileTheTable()
        {
            using var resource = TestFixtures.Load("necro_archer.vmdl_c");
            var model = (Model)resource.DataBlock!;
            var remap = model.BoneRemapTable;

            var boneCount = model.Skeleton.Bones.Length;

            using (Assert.Multiple())
            {
                await Assert.That(remap.MeshCount).IsEqualTo(4);
                await Assert.That(remap.Table.Length).IsEqualTo(244);
                await Assert.That(remap.GetMeshStart(0)).IsZero();

                var expectedStart = 0;
                for (var meshIndex = 0; meshIndex < remap.MeshCount; meshIndex++)
                {
                    await Assert.That(remap.GetMeshStart(meshIndex)).IsEqualTo(expectedStart);
                    await Assert.That(remap.GetMeshTable(meshIndex)!.Length).IsEqualTo(remap.GetMeshBoneCount(meshIndex));
                    expectedStart += remap.GetMeshBoneCount(meshIndex);
                }

                await Assert.That(expectedStart).IsEqualTo(remap.Table.Length);

                // Every entry addresses a real skeleton bone.
                await Assert.That(remap.Table.ToArray()).All(index => index >= 0 && index < boneCount);

                // GetRemapTable is the same slice, and a mesh with no slice has none.
                await Assert.That(model.GetRemapTable(1)).IsEquivalentTo(remap.GetMeshTable(1)!, CollectionOrdering.Matching);
                await Assert.That(model.GetRemapTable(remap.MeshCount)).IsNull();
            }
        }

        /// <summary>
        /// A mesh's own index addresses the model's LOD mask table, which covers embedded and referenced
        /// meshes alike, so an embedded mesh is not necessarily the nth entry of it. Reading the mask by
        /// position instead of by index is the bug this pins.
        /// </summary>
        [Test]
        public async Task MeshesCarryTheMaskTheirOwnIndexAddresses()
        {
            using var resource = TestFixtures.Load("lod_test.vmdl_c");
            var model = (Model)resource.DataBlock!;

            var meshes = model.GetEmbeddedMeshes().ToList();
            var lod = model.LodInfo;

            using (Assert.Multiple())
            {
                await Assert.That(meshes).Count().IsEqualTo(5);

                foreach (var mesh in meshes)
                {
                    // The mask on the mesh is the one its index addresses, not the one at its position.
                    await Assert.That(mesh.LodMask).IsEqualTo(lod.GetMeshMask(mesh.MeshIndex));
                    await Assert.That(mesh.Mesh).IsNotNull();
                    await Assert.That(mesh.Name).IsNotEmpty();
                }

                // This fixture puts one mesh in each of its five levels.
                await Assert.That(meshes.Select(mesh => mesh.LodMask).Order())
                    .IsEquivalentTo([1L, 2L, 4L, 8L, 16L], CollectionOrdering.Matching);

                // Filtering by level agrees with the mask each mesh carries.
                for (var level = 0; level < 5; level++)
                {
                    var atLevel = model.GetEmbeddedMeshesForLod(level).Select(mesh => mesh.MeshIndex).ToList();
                    var expected = meshes.Where(mesh => lod.IsMeshInLevel(mesh.MeshIndex, level)).Select(mesh => mesh.MeshIndex).ToList();

                    await Assert.That(atLevel).IsEquivalentTo(expected, CollectionOrdering.Matching);
                }
            }
        }

        /// <summary>
        /// A model whose meshes live in their own vmesh files reports them as references instead, keyed by
        /// the same mesh index, so the two kinds address the model's tables the same way.
        /// </summary>
        [Test]
        public async Task ReferencedMeshesUseTheSameIndexSpaceAsEmbeddedOnes()
        {
            using var resource = TestFixtures.Load("alchemist.vmdl_c");
            var model = (Model)resource.DataBlock!;

            var references = model.GetReferenceMeshNamesAndLoD().ToList();
            var embedded = model.GetEmbeddedMeshes().ToList();

            using (Assert.Multiple())
            {
                await Assert.That(references).IsNotEmpty();

                // Every reference names a vmesh and carries the mask its own index addresses.
                foreach (var reference in references)
                {
                    await Assert.That(reference.MeshName).EndsWith(".vmesh");
                    await Assert.That(reference.LodMask).IsEqualTo(model.LodInfo.GetMeshMask(reference.MeshIndex));
                }

                // A slot is filled by one or the other, never both.
                await Assert.That(references.Select(reference => reference.MeshIndex)
                    .Intersect(embedded.Select(mesh => mesh.MeshIndex))).IsEmpty();
            }
        }
    }
}
