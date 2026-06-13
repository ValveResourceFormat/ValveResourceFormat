using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
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
        public void ContiguousLods()
        {
            var lod = new ModelLodInfo(TruckMasks, TruckSwitches);

            Assert.Multiple(() =>
            {
                Assert.That(lod.LowestLevel, Is.EqualTo(0));
                Assert.That(lod.AvailableLevels, Is.EqualTo(TruckLevels));
                Assert.That(lod.LevelCount, Is.EqualTo(3));

                Assert.That(lod.IsMeshInLevel(0, 0), Is.True);
                Assert.That(lod.IsMeshInLevel(1, 1), Is.True);
                Assert.That(lod.IsMeshInLevel(2, 2), Is.True);
                Assert.That(lod.IsMeshInLevel(0, 1), Is.False);
                Assert.That(lod.IsMeshInLevel(1, 0), Is.False);
            });
        }

        [Test]
        public void SelectLevelFollowsMetric()
        {
            var lod = new ModelLodInfo(TruckMasks, TruckSwitches);

            Assert.Multiple(() =>
            {
                // Metric grows as the model gets smaller on screen, so higher metric => higher (lower-detail) level.
                Assert.That(lod.SelectLevel(0f), Is.EqualTo(0));
                Assert.That(lod.SelectLevel(34f), Is.EqualTo(0));
                Assert.That(lod.SelectLevel(35f), Is.EqualTo(1));
                Assert.That(lod.SelectLevel(49f), Is.EqualTo(1));
                Assert.That(lod.SelectLevel(50f), Is.EqualTo(2));
                Assert.That(lod.SelectLevel(1000f), Is.EqualTo(2));
            });
        }

        [Test]
        public void HasDistinctLevelsDetectsRealLods()
        {
            Assert.Multiple(() =>
            {
                // No LOD data, or a single level: nothing to switch between.
                Assert.That(new ModelLodInfo([], []).HasDistinctLevels, Is.False);
                Assert.That(new ModelLodInfo([0x01], []).HasDistinctLevels, Is.False);

                // A mesh present in every level (mask 0xFF, no switch distances) is "always shown", not a
                // LOD. This is the chess king: m_refLODGroupMasks [255], no switch distances.
                Assert.That(new ModelLodInfo([0xFF], []).HasDistinctLevels, Is.False);
                // Multiple meshes that share the same all-levels mask also render identically everywhere.
                Assert.That(new ModelLodInfo([0x03, 0x03], []).HasDistinctLevels, Is.False);

                // Distinct geometry per level: real LODs.
                Assert.That(new ModelLodInfo(TruckMasks, TruckSwitches).HasDistinctLevels, Is.True);
                Assert.That(new ModelLodInfo(AlchemistMasks, AlchemistSwitches).HasDistinctLevels, Is.True);

                // Empty LOD0 with meshes only in LOD1 (ctm_sas): the empty level is a distinct state, so the
                // model has a real LOD. m_refLODGroupMasks [2,2,2,2,2], switch distances [0, 2].
                Assert.That(new ModelLodInfo([2, 2, 2, 2, 2], [0f, 2f]).HasDistinctLevels, Is.True);
            });
        }

        [Test]
        public void MetricRangePerLevel()
        {
            var lod = new ModelLodInfo(TruckMasks, TruckSwitches);
            var noSwitches = new ModelLodInfo(TruckMasks, []);

            Assert.Multiple(() =>
            {
                // Each level is active from its own switch value up to the next level's. The top one is open-ended.
                Assert.That(lod.GetMetricRange(0), Is.EqualTo((0f, (float?)35f)));
                Assert.That(lod.GetMetricRange(1), Is.EqualTo((35f, (float?)50f)));
                Assert.That(lod.GetMetricRange(2), Is.EqualTo((50f, (float?)null)));

                // No switch data: everything collapses to an open range from 0.
                Assert.That(noSwitches.GetMetricRange(0), Is.EqualTo((0f, (float?)null)));
            });
        }

        [Test]
        public void MultipleLevelsPerMesh()
        {
            var lod = new ModelLodInfo(AlchemistMasks, AlchemistSwitches);

            Assert.Multiple(() =>
            {
                Assert.That(lod.CombinedMask, Is.EqualTo(0xFF));
                Assert.That(lod.LowestLevel, Is.EqualTo(0));
                Assert.That(lod.AvailableLevels, Is.EqualTo(AlchemistLevels));
                Assert.That(lod.LevelCount, Is.EqualTo(8));

                Assert.That(lod.IsMeshInLevel(0, 0), Is.True);
                Assert.That(lod.IsMeshInLevel(0, 1), Is.False);
                Assert.That(lod.IsMeshInLevel(1, 0), Is.False);
                Assert.That(lod.IsMeshInLevel(1, 1), Is.True);
                Assert.That(lod.IsMeshInLevel(1, 7), Is.True);
            });
        }

        [Test]
        public void MeshInAllLevelsIsLodGroupAll()
        {
            var lod = new ModelLodInfo(AllGroupMasks, AllGroupSwitches);

            Assert.Multiple(() =>
            {
                // Only the mask-7 mesh spans every level, so only it is a LODGroupAll member.
                Assert.That(lod.IsMeshInAllLevels(4), Is.True);
                Assert.That(lod.IsMeshInAllLevels(0), Is.False);
                Assert.That(lod.IsMeshInAllLevels(1), Is.False);
                Assert.That(lod.IsMeshInAllLevels(2), Is.False);
                Assert.That(lod.IsMeshInAllLevels(3), Is.False);

                // A single populated level is not treated as "all levels", so nothing is pulled out.
                Assert.That(new ModelLodInfo([1, 1], [0f]).IsMeshInAllLevels(0), Is.False);
            });
        }

        [Test]
        public void EmptyLod0FallsBackToLowestPopulated()
        {
            var lod = new ModelLodInfo(EmptyLod0Masks, []);

            Assert.Multiple(() =>
            {
                Assert.That(lod.LowestLevel, Is.EqualTo(1));
                Assert.That(lod.AvailableLevels, Is.EqualTo(EmptyLod0Levels));
                Assert.That(lod.LevelCount, Is.EqualTo(3));
                // With no switch values, automatic selection stays at the lowest populated level.
                Assert.That(lod.SelectLevel(1000f), Is.EqualTo(1));
            });
        }

        [Test]
        public void NoLodData()
        {
            var lod = new ModelLodInfo([], []);

            Assert.Multiple(() =>
            {
                Assert.That(lod.CombinedMask, Is.EqualTo(0));
                Assert.That(lod.LowestLevel, Is.EqualTo(0));
                Assert.That(lod.AvailableLevels, Is.Empty);
                Assert.That(lod.LevelCount, Is.EqualTo(0));
                // A mesh with no mask entry is treated as always present.
                Assert.That(lod.IsMeshInLevel(0, 0), Is.True);
            });
        }

        // The lod_test fixture is a synthetic 5-LOD model with embedded meshes (no external
        // references), m_refLODGroupMasks [1,2,4,8,16] and m_lodGroupSwitchDistances [0,5,10,15,20].
        [Test]
        public void FixtureLodInfoMatchesData()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "lod_test.vmdl_c"));

            var lod = ((Model)resource.DataBlock!).LodInfo;

            Assert.Multiple(() =>
            {
                Assert.That(lod.LowestLevel, Is.EqualTo(0));
                Assert.That(lod.AvailableLevels, Is.EqualTo(FixtureLevels));
                Assert.That(lod.LevelCount, Is.EqualTo(5));
                Assert.That(lod.SwitchDistances, Is.EqualTo(FixtureSwitches));
            });
        }

        // Decompiler round-trip: the extracted .vmdl must carry the LOD structure back as a
        // LODGroupList, one LODGroup per level with the right switch_threshold. Recompiling this
        // reproduces the original masks/distances (verified separately with resourcecompiler).
        [Test]
        public void DecompiledModelEmitsLodGroupList()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "lod_test.vmdl_c"));

            var vmdl = new ModelExtract(resource, new NullFileLoader()).ToValveModel();

            Assert.Multiple(() =>
            {
                Assert.That(vmdl, Does.Contain("LODGroupList"));
                Assert.That(vmdl, Does.Contain("_class = \"LODGroup\""));
                Assert.That(vmdl, Does.Contain("mesh_references"));
                Assert.That(vmdl, Does.Contain("switch_threshold = 0"));
                Assert.That(vmdl, Does.Contain("switch_threshold = 5"));
                Assert.That(vmdl, Does.Contain("switch_threshold = 20"));
            });
        }
    }
}
