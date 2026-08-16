using System.IO;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class ModelLodTest
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
            var lod = new ModelLodInfo(TruckMasks, TruckSwitches);

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
            var lod = new ModelLodInfo(TruckMasks, TruckSwitches);

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
                await Assert.That(new ModelLodInfo([], []).HasDistinctLevels).IsFalse();
                await Assert.That(new ModelLodInfo([0x01], []).HasDistinctLevels).IsFalse();

                // A mesh present in every level (mask 0xFF, no switch distances) is "always shown", not a
                // LOD. This is the chess king: m_refLODGroupMasks [255], no switch distances.
                await Assert.That(new ModelLodInfo([0xFF], []).HasDistinctLevels).IsFalse();
                // Multiple meshes that share the same all-levels mask also render identically everywhere.
                await Assert.That(new ModelLodInfo([0x03, 0x03], []).HasDistinctLevels).IsFalse();

                // Distinct geometry per level: real LODs.
                await Assert.That(new ModelLodInfo(TruckMasks, TruckSwitches).HasDistinctLevels).IsTrue();
                await Assert.That(new ModelLodInfo(AlchemistMasks, AlchemistSwitches).HasDistinctLevels).IsTrue();

                // Empty LOD0 with meshes only in LOD1 (ctm_sas): the empty level is a distinct state, so the
                // model has a real LOD. m_refLODGroupMasks [2,2,2,2,2], switch distances [0, 2].
                await Assert.That(new ModelLodInfo([2, 2, 2, 2, 2], [0f, 2f]).HasDistinctLevels).IsTrue();
            }
        }

        [Test]
        public async Task MetricRangePerLevel()
        {
            var lod = new ModelLodInfo(TruckMasks, TruckSwitches);
            var noSwitches = new ModelLodInfo(TruckMasks, []);

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
            var lod = new ModelLodInfo(AlchemistMasks, AlchemistSwitches);

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
            var lod = new ModelLodInfo(AllGroupMasks, AllGroupSwitches);

            using (Assert.Multiple())
            {
                // Only the mask-7 mesh spans every level, so only it is a LODGroupAll member.
                await Assert.That(lod.IsMeshInAllLevels(4)).IsTrue();
                await Assert.That(lod.IsMeshInAllLevels(0)).IsFalse();
                await Assert.That(lod.IsMeshInAllLevels(1)).IsFalse();
                await Assert.That(lod.IsMeshInAllLevels(2)).IsFalse();
                await Assert.That(lod.IsMeshInAllLevels(3)).IsFalse();

                // A single populated level is not treated as "all levels", so nothing is pulled out.
                await Assert.That(new ModelLodInfo([1, 1], [0f]).IsMeshInAllLevels(0)).IsFalse();

            }
        }

        [Test]
        public async Task EmptyLod0FallsBackToLowestPopulated()
        {
            var lod = new ModelLodInfo(EmptyLod0Masks, []);

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
            var lod = new ModelLodInfo([], []);

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

        // Decompiler round-trip: the extracted .vmdl must carry the LOD structure back as a
        // LODGroupList, one LODGroup per level with the right switch_threshold. Recompiling this
        // reproduces the original masks/distances (verified separately with resourcecompiler).
        [Test]
        public async Task DecompiledModelEmitsLodGroupList()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "lod_test.vmdl_c"));

            var vmdl = new ModelExtract(resource, new NullFileLoader()).ToValveModel();

            using (Assert.Multiple())
            {
                await Assert.That(vmdl).Contains("LODGroupList");
                await Assert.That(vmdl).Contains("_class = \"LODGroup\"");
                await Assert.That(vmdl).Contains("mesh_references");
                await Assert.That(vmdl).Contains("switch_threshold = 0");
                await Assert.That(vmdl).Contains("switch_threshold = 5");
                await Assert.That(vmdl).Contains("switch_threshold = 20");

            }
        }
    }
}
