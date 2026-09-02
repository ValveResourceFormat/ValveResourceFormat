using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class NmExtractTest
    {
        /// <summary>
        /// Neither a compiled clip nor a compiled skeleton stores the file it was authored from, but the
        /// compiler records it as an input dependency, so both recover it from there.
        /// </summary>
        [Test]
        public async Task RecoversSourceFilenamesFromInputDependencies()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ExtractClipDoc("idle_ak.vnmclip_c").GetStringProperty("m_sourceFilename"))
                    .IsEqualTo("phase2/animation/anims/viewmodel/rifle/rifle_ak/idles/dmx/idle_ak.dmx");

                await Assert.That(ExtractSkeletonDoc("chicken.vnmskel_c").GetStringProperty("m_sourceFilename"))
                    .IsEqualTo("models/chicken/dmx/chicken_mike.dmx");
            }
        }

        /// <summary>
        /// An additive clip names the frame that decodes to identity as its base, and a doc event keeps
        /// the authored id while dropping the sync id the compiler assigned.
        /// </summary>
        [Test]
        public async Task KeepsAdditiveBaseFrameAndAuthoredEventIds()
        {
            var additive = ExtractClipDoc("shoot_cz75.vnmclip_c");

            var events = ExtractClipDoc("shoot1_nova.vnmclip_c")
                .GetArray("m_eventTracks")!
                .SelectMany(track => track.GetArray("m_events")!)
                .ToArray();

            var idEvent = events.Single(ev => ev.GetStringProperty("_class") == "CNmClipDocEvent_ID");

            using (Assert.Multiple())
            {
                await Assert.That(additive.GetStringProperty("m_additiveType")).IsEqualTo("RelativeToFrame");
                await Assert.That(additive.GetStringProperty("m_additiveBaseFrame")).IsEqualTo("UserSpecifiedFrame");
                await Assert.That(additive.GetInt32Property("m_nAdditiveBaseFrameIdx")).IsEqualTo(12);

                await Assert.That(idEvent.GetStringProperty("m_ID")).IsEqualTo("WPN_BLOCK_INSPECT");
                await Assert.That(events.Any(ev => ev.ContainsKey("m_syncID"))).IsFalse();
            }
        }

        /// <summary>
        /// CompileNmSkeleton emits the hierarchy walk filtered to the low-LOD bones, then the same walk
        /// filtered to the rest, so the exported DAG has to order siblings such that walking it that way
        /// reproduces the compiled bone order. root_motion is the sole root and the export re-frames it
        /// into the model axis convention, where the NM resource stores it at identity.
        /// </summary>
        /// <remarks>chicken.vnmskel_c is the only CS2 skeleton whose compiled bone order differs from a
        /// hierarchy walk in bone index order.</remarks>
        [Test]
        public async Task ExportedDagReproducesCompiledBoneOrder()
        {
            using var resource = TestFixtures.Load("chicken.vnmskel_c");

            KVObject kv = ((BinaryKV3)resource.DataBlock!).Data;
            var boneIds = kv.GetArray<string>("m_boneIDs")!;
            var lowLodBoneCount = kv.GetInt32Property("m_numBonesToSampleAtLowLOD");

            using var contentFile = new NmSkeletonExtract(resource).ToContentFile();
            using var ms = new MemoryStream(contentFile.SubFiles.Single().Extract!.Invoke());
            var skeleton = (Datamodel.Element)Datamodel.Datamodel.Load(ms, Datamodel.Codecs.DeferredMode.Disabled).Root!["skeleton"]!;

            var roots = ((Datamodel.ElementArray)skeleton["children"]!).Cast<Datamodel.Element>().ToArray();

            var walk = new List<string>();
            foreach (var root in roots)
            {
                Visit(root, walk);
            }

            var lowLod = boneIds.Take(lowLodBoneCount).ToHashSet();
            var compilerOrder = walk.Where(lowLod.Contains).Concat(walk.Where(name => !lowLod.Contains(name)));

            var orientation = (Quaternion)((Datamodel.Element)roots[0]["transform"]!)["orientation"]!;
            var dot = Quaternion.Dot(orientation, new Quaternion(0.5f, 0.5f, 0.5f, 0.5f));

            using (Assert.Multiple())
            {
                await Assert.That(compilerOrder).IsEquivalentTo(boneIds, CollectionOrdering.Matching);
                await Assert.That(roots).Count().IsEqualTo(1);
                await Assert.That(roots[0].Name).IsEqualTo("root_motion");
                await Assert.That(MathF.Abs(dot)).IsEqualTo(1f).Within(0.0001f);
            }
        }

        private static void Visit(Datamodel.Element joint, List<string> walk)
        {
            walk.Add(joint.Name);

            foreach (var child in ((Datamodel.ElementArray)joint["children"]!).Cast<Datamodel.Element>())
            {
                Visit(child, walk);
            }
        }

        private static KVObject ExtractClipDoc(string fileName)
        {
            using var resource = TestFixtures.Load(fileName);
            using var contentFile = new NmClipExtract(resource, new NullFileLoader()).ToContentFile();

            return TestFixtures.ParseKV3(System.Text.Encoding.UTF8.GetString(contentFile.Data!));
        }

        private static KVObject ExtractSkeletonDoc(string fileName)
        {
            using var resource = TestFixtures.Load(fileName);
            using var contentFile = new NmSkeletonExtract(resource).ToContentFile();

            return TestFixtures.ParseKV3(System.Text.Encoding.UTF8.GetString(contentFile.Data!));
        }
    }
}
