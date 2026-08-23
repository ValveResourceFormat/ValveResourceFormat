using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class NmSkeletonExtractTest
    {
        // chicken.vnmskel_c is the only CS2 skeleton whose compiled bone order differs from a
        // hierarchy walk in bone index order.
        private static (Datamodel.Element Skeleton, string[] BoneIds, int LowLodBoneCount) ExtractSkeletonDmx()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "chicken.vnmskel_c"));
            ValveKeyValue.KVObject kv = ((BinaryKV3)resource.DataBlock!).Data;
            var boneIds = kv.GetArray<string>("m_boneIDs")!;
            var lowLodBoneCount = kv.GetInt32Property("m_numBonesToSampleAtLowLOD");

            using var contentFile = new NmSkeletonExtract(resource).ToContentFile();
            var dmxBytes = contentFile.SubFiles.Single().Extract!.Invoke();
            using var ms = new MemoryStream(dmxBytes);
            var dm = Datamodel.Datamodel.Load(ms, Datamodel.Codecs.DeferredMode.Disabled);

            return ((Datamodel.Element)dm.Root!["skeleton"]!, boneIds, lowLodBoneCount);
        }

        private static void Visit(Datamodel.Element joint, List<string> walk)
        {
            walk.Add(joint.Name);
            foreach (var child in ((Datamodel.ElementArray)joint["children"]!).Cast<Datamodel.Element>())
            {
                Visit(child, walk);
            }
        }

        [Test]
        public async Task ExportedDagReproducesCompiledBoneOrder()
        {
            var (skeleton, boneIds, lowLodBoneCount) = ExtractSkeletonDmx();

            var walk = new List<string>();
            foreach (var root in ((Datamodel.ElementArray)skeleton["children"]!).Cast<Datamodel.Element>())
            {
                Visit(root, walk);
            }

            // CompileNmSkeleton emits the hierarchy walk filtered to the low-LOD bones, then the
            // same walk filtered to the rest.
            var lowLod = boneIds.Take(lowLodBoneCount).ToHashSet();
            var compilerOrder = walk.Where(lowLod.Contains).Concat(walk.Where(name => !lowLod.Contains(name)));

            await Assert.That(compilerOrder).IsEquivalentTo(boneIds, CollectionOrdering.Matching);
        }

        [Test]
        public async Task RootMotionIsSoleRootAndCarriesAxisFixup()
        {
            var (skeleton, _, _) = ExtractSkeletonDmx();
            var roots = ((Datamodel.ElementArray)skeleton["children"]!).Cast<Datamodel.Element>().ToArray();

            await Assert.That(roots).Count().IsEqualTo(1);
            await Assert.That(roots[0].Name).IsEqualTo("root_motion");

            var orientation = (Quaternion)((Datamodel.Element)roots[0]["transform"]!)["orientation"]!;
            var expected = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
            var dot = Quaternion.Dot(orientation, expected);

            // The NM resource stores root_motion at identity; the export re-frames it into the
            // model axis convention.
            await Assert.That(MathF.Abs(dot)).IsEqualTo(1f).Within(0.0001f);
        }
    }
}
