using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Covers the animation graph decompiler against the committed vanmgrph fixtures. Converting a graph
    /// node is a long chain of per-class arms, so these assert on what every arm has to produce rather
    /// than on any one of them.
    /// </summary>
    public class AnimationGraphExtractTest
    {
        // Set VRF_REGEN_FIXTURES=1 to rewrite the mismatching dump in the source tree.
        private static readonly bool RegenerateFixtures = Environment.GetEnvironmentVariable("VRF_REGEN_FIXTURES") == "1";

        private static string GetSourceExtractOutputPath([CallerFilePath] string sourceFile = "")
            => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Files", "ExtractOutput");

        private static string Extract(string fileName)
        {
            using var resource = TestFixtures.Load(fileName);
            using var content = FileExtract.Extract(resource, new NullFileLoader());

            return Encoding.UTF8.GetString(content.Data!);
        }

        /// <summary>
        /// The whole decompiled document, pinned. Small enough that the diff is readable when it moves,
        /// which is what makes it useful for a converter this size.
        /// </summary>
        [Test]
        public async Task DecompilesAGraphToItsPinnedDocument()
        {
            var actual = Extract("box_creature_model.vanmgrph_c").ReplaceLineEndings("\n");

            const string DumpName = "box_creature_model.vanmgrph.txt";
            var expectedPath = Path.Combine(TestContext.TestDirectory!, "Files", "ExtractOutput", DumpName);
            var expected = (await File.ReadAllTextAsync(expectedPath)).ReplaceLineEndings("\n");

            if (RegenerateFixtures && actual != expected)
            {
                await File.WriteAllTextAsync(Path.Combine(GetSourceExtractOutputPath(), DumpName), actual);
                return;
            }

            await Assert.That(actual).IsEqualTo(expected);
        }

        /// <summary>
        /// Every animation node the converter emits carries the name the document refers to it by,
        /// whichever arm produced it. The larger fixture is asserted structurally rather than pinned,
        /// because its document is over a megabyte.
        /// </summary>
        [Test]
        public async Task EveryConvertedNodeIsNamed()
        {
            var nodes = AnimNodes(Extract("slork_kv3_v5_zstd.vanmgrph_c"));

            var unnamed = nodes.Count(node => node.GetStringProperty("m_sName") == "Unnamed");

            using (Assert.Multiple())
            {
                // Fifteen node classes, so this covers a real slice of the converter's arms.
                await Assert.That(nodes.Select(node => node.GetStringProperty("_class")).Distinct().Order()).IsEquivalentTo([
                    "CAddAnimNode", "CAimMatrixAnimNode", "CBlend2DAnimNode", "CBlendAnimNode",
                    "CBoneMaskAnimNode", "CChoiceAnimNode", "CMoverAnimNode", "CRootAnimNode",
                    "CSelectorAnimNode", "CSequenceAnimNode", "CSpeedScaleAnimNode",
                    "CStateMachineAnimNode", "CSubtractAnimNode", "CTurnHelperAnimNode",
                    "CTwoBoneIKAnimNode",
                ], CollectionOrdering.Matching);

                await Assert.That(nodes).Count().IsEqualTo(450);

                // Every node is named, under the authored key rather than the compiled one.
                await Assert.That(nodes).All(node => node.ContainsKey("m_sName"));
                await Assert.That(nodes).All(node => !node.ContainsKey("m_name"));

                // Every node in this graph was authored with a name, so the placeholder is not reached
                // here; pinning that at zero catches it starting to fire where a real name exists.
                await Assert.That(unnamed).IsZero();
            }
        }

        /// <summary>
        /// Every object in the document whose class names an animation node. State machine states and
        /// other sub-objects keep their own <c>m_name</c> and are not nodes.
        /// </summary>
        private static List<KVObject> AnimNodes(string document)
        {
            var nodes = new List<KVObject>();

            Visit(TestFixtures.ParseKV3(document));

            return nodes;

            void Visit(KVObject node)
            {
                if (node.ContainsKey("_class") && node.GetStringProperty("_class")?.EndsWith("AnimNode", StringComparison.Ordinal) == true)
                {
                    nodes.Add(node);
                }

                foreach (var (_, child) in node)
                {
                    if (child is KVObject childNode)
                    {
                        Visit(childNode);
                    }
                }
            }
        }
    }
}
