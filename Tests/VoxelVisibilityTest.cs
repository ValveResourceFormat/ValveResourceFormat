using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;

namespace Tests
{
    public class VoxelVisibilityTest
    {
        private static VoxelVisibility LoadVis(Resource resource)
        {
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "world_visibility.vvis_c"));
            return (VoxelVisibility)resource.GetBlockByType(BlockType.VXVS)!;
        }

        [Test]
        public async Task ParsesOctreeStructure()
        {
            using var resource = new Resource();
            var vis = LoadVis(resource);

            using (Assert.Multiple())
            {
                await Assert.That((int)vis.BaseClusterCount).IsEqualTo(2);
                await Assert.That(vis.Nodes.Length).IsEqualTo(1);
                await Assert.That(vis.Regions.Length).IsEqualTo(1);
                await Assert.That(vis.MinBounds.X).IsLessThan(vis.MaxBounds.X);
                await Assert.That(vis.GridSize).IsGreaterThan(0f);
            }
        }

        [Test]
        public async Task ResolvesClustersAndPvsForPoints()
        {
            using var resource = new Resource();
            var vis = LoadVis(resource);

            var center = (vis.MinBounds + vis.MaxBounds) / 2f;
            var outside = vis.MaxBounds + new Vector3(10000f);

            var centerCluster = vis.GetClusterForPosition(center);
            var outsideCluster = vis.GetClusterForPosition(outside);

            var centerPvs = vis.GetPVSForPoint(center);
            var outsidePvs = vis.GetPVSForPoint(outside);

            using (Assert.Multiple())
            {
                await Assert.That(centerCluster).IsEqualTo(0);
                await Assert.That(outsideCluster).IsEqualTo(0);

                await Assert.That(centerPvs).IsNotNull();
                await Assert.That(centerPvs!.Length).IsEqualTo(4);
                await Assert.That(centerPvs.Count(b => b != 0)).IsEqualTo(4);
                await Assert.That(outsidePvs).IsNull();
            }
        }

        [Test]
        public async Task ClusterBoundsStayInsideTheOctree()
        {
            using var resource = new Resource();
            var vis = LoadVis(resource);

            var boundsByCluster = vis.BuildClusterChildBounds();

            using (Assert.Multiple())
            {
                await Assert.That(boundsByCluster.Count).IsEqualTo(1);

                foreach (var bounds in boundsByCluster.Values.SelectMany(v => v))
                {
                    await Assert.That(bounds.Min.X).IsGreaterThanOrEqualTo(vis.MinBounds.X);
                    await Assert.That(bounds.Min.Y).IsGreaterThanOrEqualTo(vis.MinBounds.Y);
                    await Assert.That(bounds.Min.Z).IsGreaterThanOrEqualTo(vis.MinBounds.Z);
                    await Assert.That(bounds.Max.X).IsLessThanOrEqualTo(vis.MaxBounds.X);
                    await Assert.That(bounds.Max.Y).IsLessThanOrEqualTo(vis.MaxBounds.Y);
                    await Assert.That(bounds.Max.Z).IsLessThanOrEqualTo(vis.MaxBounds.Z);
                }
            }
        }
    }
}
