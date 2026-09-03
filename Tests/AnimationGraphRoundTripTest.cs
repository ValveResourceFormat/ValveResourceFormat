using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Checks the animation graph decompiler against a document whose authored form is known.
    /// <c>Files/AuthoredInput/vrf_all_nodes.vanmgrph</c> was written for this suite, holds one node of
    /// every class the CS2 animgraph compiler accepts, and was compiled into
    /// <c>vrf_all_nodes.vanmgrph_c</c>. Each test states a property of that document that has to hold
    /// on the way back, so a failure names what the decompiler got wrong rather than that some bytes
    /// moved.
    /// </summary>
    public class AnimationGraphRoundTripTest
    {
        private static KVObject Decompiled()
        {
            using var resource = TestFixtures.Load("vrf_all_nodes.vanmgrph_c");
            using var content = FileExtract.Extract(resource, new NullFileLoader());

            return TestFixtures.ParseKV3(Encoding.UTF8.GetString(content.Data!));
        }

        private static List<KVObject> Collect(KVObject document, string classSuffix)
        {
            var found = new List<KVObject>();

            Visit(document);

            return found;

            void Visit(KVObject node)
            {
                if (node.ContainsKey("_class")
                    && node.GetStringProperty("_class").EndsWith(classSuffix, StringComparison.Ordinal))
                {
                    found.Add(node);
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

        private static KVObject Node(KVObject document, string name)
            => Collect(document, "AnimNode").Single(node => node.GetStringProperty("m_sName") == name);

        /// <summary>
        /// Every node id the document mentions resolves to a node the document declares, and no two
        /// nodes share an id. A decompiler that drops a node or renumbers one produces a document the
        /// editor cannot load, and that shows up here as a reference pointing at nothing.
        /// </summary>
        [Test]
        public async Task EveryNodeReferenceResolvesToADeclaredNode()
        {
            var document = Decompiled();
            var nodes = Collect(document, "AnimNode");

            var declared = nodes
                .Select(node => node.GetSubCollection("m_nNodeID")?.GetIntegerProperty("m_id"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var referenced = new List<long>();
            CollectReferences(document);

            using (Assert.Multiple())
            {
                await Assert.That(declared).IsNotEmpty();
                await Assert.That(declared.Distinct().Count()).IsEqualTo(declared.Count);
                await Assert.That(referenced).IsNotEmpty();
                await Assert.That(referenced.Where(id => !declared.Contains(id))).IsEmpty();
            }

            void CollectReferences(KVObject node)
            {
                foreach (var (key, value) in node)
                {
                    if (value is not KVObject child)
                    {
                        continue;
                    }

                    // A connection names the node it reads from; m_nNodeID is the node's own id.
                    if (key is "m_inputConnection" or "m_childID" or "m_nodeID" && child.ContainsKey("m_id"))
                    {
                        var id = child.GetIntegerProperty("m_id");

                        if (id != uint.MaxValue)
                        {
                            referenced.Add(id);
                        }
                    }

                    CollectReferences(child);
                }
            }
        }

        /// <summary>
        /// Every node the compiler kept comes back, under the class and the name the document gave it.
        /// The compiler discards some node names outright, so the compiled resource says which names
        /// are recoverable; one it kept that comes back under <c>m_name</c> rather than <c>m_sName</c>
        /// is a missing name conversion.
        /// </summary>
        [Test]
        public async Task EveryNodeTheCompilerKeptComesBack()
        {
            using var resource = TestFixtures.Load("vrf_all_nodes.vanmgrph_c");
            var compiled = ((BinaryKV3)resource.DataBlock!).Data;

            var keptNames = new List<string>();
            var keptClasses = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in Collect(compiled, "UpdateNode"))
            {
                var name = node.GetStringProperty("_class");
                keptClasses.Add(string.Concat(name.AsSpan(0, name.Length - "UpdateNode".Length), "AnimNode"));

                if (node.ContainsKey("m_name"))
                {
                    keptNames.Add(node.GetStringProperty("m_name"));
                }
            }

            var document = Decompiled();
            var nodes = Collect(document, "AnimNode");
            var names = nodes.Select(node => node.GetStringProperty("m_sName")).ToHashSet();
            var classes = nodes.Select(node => node.GetStringProperty("_class")).ToHashSet();

            using (Assert.Multiple())
            {
                await Assert.That(keptClasses.Count).IsGreaterThanOrEqualTo(30);
                await Assert.That(keptNames).IsNotEmpty();
                await Assert.That(keptClasses.Where(cls => !classes.Contains(cls))).IsEmpty();
                await Assert.That(keptNames.Where(name => !names.Contains(name))).IsEmpty();
            }
        }

        /// <summary>
        /// The settings the document gives individual nodes come back as authored. These are spelled out
        /// one by one so the expected value can be read straight out of the authored document rather
        /// than trusted because a comparison passed.
        /// </summary>
        [Test]
        public async Task TheAuthoredNodeSettingsComeBack()
        {
            var document = Decompiled();

            var jiggle = Node(document, "JiggleBone").GetArray("m_items").Single();
            var slopes = Node(document, "SlowDownOnSlopes");
            var turn = Node(document, "TurnHelper");
            var speed = Node(document, "SpeedScale");

            using (Assert.Multiple())
            {
                await Assert.That(jiggle.GetFloatProperty("m_flSpringStrength")).IsEqualTo(10f);
                await Assert.That(jiggle.GetFloatProperty("m_flDamping")).IsEqualTo(0.059f).Within(1e-6f);
                await Assert.That(jiggle.GetFloatProperty("m_flSimRateFPS")).IsEqualTo(90f);
                await Assert.That(jiggle.GetStringProperty("m_eSimSpace")).IsEqualTo("SimSpace_Model");

                await Assert.That(slopes.GetFloatProperty("m_flSlowDownStrength")).IsEqualTo(1f);

                await Assert.That(turn.GetBooleanProperty("m_bMatchChildDuration")).IsFalse();
                await Assert.That(turn.GetBooleanProperty("m_bUseManualTurnOffset")).IsFalse();

                await Assert.That(speed.GetStringProperty("m_sName")).IsEqualTo("SpeedScale");
            }
        }

        /// <summary>
        /// The document hangs every node off one choice node, so the shape of the graph and not just
        /// its node set has to survive: the hub keeps a child per node it was given, and each child
        /// still reads from a distinct node. The child labels are not compared, because the compiler
        /// does not keep them and the decompiler numbers them instead.
        /// </summary>
        [Test]
        public async Task TheHubKeepsAChildPerNodeItWasGiven()
        {
            var document = Decompiled();
            var hub = Node(document, "hub");

            var targets = hub.GetArray("m_children")
                .Select(child => child.GetSubCollection("m_inputConnection")?.GetSubCollection("m_nodeID")?.GetIntegerProperty("m_id"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var declared = Collect(document, "AnimNode")
                .Select(node => node.GetSubCollection("m_nNodeID")?.GetIntegerProperty("m_id"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            using (Assert.Multiple())
            {
                // The authored document gives the hub one child per node class it instantiates.
                await Assert.That(targets.Count).IsEqualTo(29);
                await Assert.That(targets.Distinct().Count()).IsEqualTo(targets.Count);
                await Assert.That(targets.Where(id => !declared.Contains(id))).IsEmpty();
            }
        }

        /// <summary>
        /// The document declares one float parameter, <c>vrf_blend</c> over -1 to 1, and binds every
        /// node that takes one to it. The compiler stores the binding as the parameter's id, so the
        /// parameter has to come back and the bindings have to point at it.
        /// </summary>
        [Test]
        public async Task TheAuthoredParameterComesBackAndItsBindingsResolve()
        {
            var document = Decompiled();

            var declared = Collect(document, "AnimParameter");
            var ids = declared
                .Select(parameter => parameter.GetSubCollection("m_id")?.GetIntegerProperty("m_id"))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var authored = declared.SingleOrDefault(parameter => parameter.GetStringProperty("m_name") == "vrf_blend");

            var bound = Collect(document, "AnimNode")
                .Select(node => node.GetSubCollection("m_param")?.GetIntegerProperty("m_id"))
                .Where(id => id.HasValue && id.Value != uint.MaxValue)
                .Select(id => id!.Value)
                .ToList();

            using (Assert.Multiple())
            {
                await Assert.That(authored).IsNotNull();
                await Assert.That(authored!.GetFloatProperty("m_fMinValue")).IsEqualTo(-1f);
                await Assert.That(authored.GetFloatProperty("m_fMaxValue")).IsEqualTo(1f);
                await Assert.That(bound).IsNotEmpty();
                await Assert.That(bound.Where(id => !ids.Contains(id))).IsEmpty();
            }
        }
    }
}
