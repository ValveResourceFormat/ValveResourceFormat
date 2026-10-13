using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Datamodel;
using Datamodel.Codecs;
using TUnit.Assertions.Enums;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using DM = Datamodel.Datamodel;

namespace Tests.IO
{
    /// <summary>
    /// Loading a map and saving it again must reproduce every element, every attribute and the prefix attributes,
    /// whether the elements were read as plain elements, as the classes of the map schema, or as a map document.
    /// </summary>
    public class MapRoundTripTest
    {
        // The flag of a hidden node that does not outlast a session
        private const int TransientHiddenFlag = (int)MapNodeHiddenFlags.Transient;

        private static string Resource(string name) => TestFixtures.Path("maps", name);

        // A map made for this purpose: every node class, all selection set kinds, nested prefabs and instances,
        // subdivision, vertex paint, baked lighting, a thumbnail and asset references in the prefix
        public static IEnumerable<string> VmapFiles() =>
        [
            "roundtrip_test.vmap",
            Path.Combine("prefabs", "roundtrip_test_prefab1.vmap"),
            Path.Combine("prefabs", "roundtrip_test_prefab2.vmap"),
            Path.Combine("prefabs", "roundtrip_test_prefab3.vmap"),
        ];

        private static DM LoadTyped(string path)
        {
            using var stream = File.OpenRead(path);
            return DM.Load<CMapRootElement>(stream, DeferredMode.Disabled, new LoadOptions { Factory = MapDocument.MapElementFactory.Instance });
        }

        [Test]
        [MethodDataSource(nameof(VmapFiles))]
        public async Task UntypedBinaryRoundTrips(string file)
        {
            using var original = DM.Load(Resource(file), DeferredMode.Disabled);
            var saved = Save(original);

            using var reloaded = DM.Load(saved);
            await AssertEquivalent(original, reloaded, orderSensitive: true);
            await Assert.That(reloaded.PrefixElementId).IsEqualTo(original.PrefixElementId);

            await Assert.That(Save(reloaded)).IsEquivalentTo(saved, CollectionOrdering.Matching).Because("saving the reloaded datamodel must reproduce the same bytes");
        }

        [Test]
        [MethodDataSource(nameof(VmapFiles))]
        public async Task PrefixElementIsNotAnOrphan(string file)
        {
            using var dm = DM.Load(Resource(file), DeferredMode.Disabled);

            await Assert.That(dm.PrefixAttributes.Keys).Contains("map_asset_references");

            var reachable = new HashSet<Element>();
            Visit(dm.Root);
            await Assert.That(dm.AllElements.Count).IsEqualTo(reachable.Count).Because("every element must be reachable from the root");

            void Visit(Element? element)
            {
                if (element == null || !reachable.Add(element))
                {
                    return;
                }

                foreach (var attribute in element)
                {
                    if (attribute.Value is Element child)
                    {
                        Visit(child);
                    }
                    else if (attribute.Value is IEnumerable<Element> children)
                    {
                        foreach (var arrayChild in children)
                        {
                            Visit(arrayChild);
                        }
                    }
                }
            }
        }

        [Test]
        [MethodDataSource(nameof(VmapFiles))]
        public async Task TypedBinaryRoundTrips(string file)
        {
            using var original = DM.Load(Resource(file), DeferredMode.Disabled);
            using var typed = LoadTyped(Resource(file));

            await Assert.That(typed.Root).IsTypeOf<CMapRootElement>();

            // Typed elements write their class properties first, in declaration order, so only the set of attributes is compared
            await AssertEquivalent(original, typed, orderSensitive: false);

            var saved = Save(typed);
            using var reloaded = DM.Load(saved);
            await AssertEquivalent(original, reloaded, orderSensitive: false);

            await Assert.That(Save(reloaded)).IsEquivalentTo(saved, CollectionOrdering.Matching);
        }

        [Test]
        [MethodDataSource(nameof(VmapFiles))]
        public async Task UntypedKeyValues2RoundTrips(string file)
        {
            using var original = DM.Load(Resource(file), DeferredMode.Disabled);

            using var text = new MemoryStream();
            original.Save(text, "keyvalues2", 4);

            using var reloaded = DM.Load(text.ToArray());

            // keyvalues2 prints floats with ten decimals, so values that small lose precision in that encoding
            await AssertEquivalent(original, reloaded, orderSensitive: true, floatTolerance: 1e-9);
            await Assert.That(reloaded.PrefixElementId).IsEqualTo(original.PrefixElementId);

            using var text2 = new MemoryStream();
            reloaded.Save(text2, "keyvalues2", 4);
            await Assert.That(text2.ToArray()).IsEquivalentTo(text.ToArray(), CollectionOrdering.Matching);
        }

        /// <summary>
        /// A map that goes through a map document comes out with everything it went in with. Loading converts the
        /// saved meshes and entity IO into their runtime form and saving rebuilds them, so those elements are new
        /// ones and the two maps are compared by their structure, not by element id.
        /// </summary>
        [Test]
        [MethodDataSource(nameof(VmapFiles))]
        public async Task MapDocumentRoundTrips(string file)
        {
            using var original = DM.Load(Resource(file), DeferredMode.Disabled);

            byte[] saved;
            int elementsHeld;

            using (var document = MapDocument.Load(Resource(file)))
            {
                using var stream = new MemoryStream();
                document.Save(stream, "binary", 9);
                saved = stream.ToArray();

                using var again = new MemoryStream();
                document.Save(again, "binary", 9);
                await Assert.That(again.ToArray().Length).IsEqualTo(saved.Length).Because("saving twice must write the same amount");

                elementsHeld = document.Root.Owner!.AllElements.Count;
            }

            using var reloaded = DM.Load(saved);
            var differences = new List<string>();
            var matched = new Dictionary<Element, Element>();

            CompareAttributes(original.PrefixAttributes, reloaded.PrefixAttributes, "prefix", differences, matched, original);
            CompareElements(original.Root!, reloaded.Root!, "root", differences, matched, original);

            using (Assert.Multiple())
            {
                await Assert.That(string.Join('\n', differences.Take(25))).IsEqualTo(string.Empty);
                await Assert.That(matched.Count).IsEqualTo(original.AllElements.Count).Because("every element of the file is reached");
                await Assert.That(reloaded.AllElements.Count).IsEqualTo(original.AllElements.Count).Because("nothing is written that the file did not have");
                await Assert.That(elementsHeld).IsLessThan(original.AllElements.Count).Because("a loaded map does not keep the saved data of its meshes and entity IO");
            }
        }

        private static void CompareElements(Element expected, Element actual, string context, List<string> differences, Dictionary<Element, Element> matched, DM original)
        {
            if (matched.TryGetValue(expected, out var earlier))
            {
                if (!ReferenceEquals(earlier, actual))
                {
                    differences.Add($"{context}: refers to a different element than the other references to {expected.ClassName} '{expected.Name}'");
                }

                return;
            }

            matched[expected] = actual;

            if (expected.ClassName != actual.ClassName || expected.Name != actual.Name || expected.Stub != actual.Stub)
            {
                differences.Add($"{context}: is {actual.ClassName} '{actual.Name}', was {expected.ClassName} '{expected.Name}'");
                return;
            }

            if (!expected.Stub)
            {
                CompareAttributes(expected, actual, $"{context}<{expected.ClassName}>", differences, matched, original);
            }
        }

        private static void CompareAttributes(AttributeList expected, AttributeList actual, string context, List<string> differences, Dictionary<Element, Element> matched, DM original)
        {
            var actualByName = actual.GetAllAttributesForSerialization().ToDictionary(static attribute => attribute.Key, static attribute => attribute.Value);

            foreach (var (name, expectedValue) in expected.GetAllAttributesForSerialization().ToArray())
            {
                // A typed element also writes class properties the source lacked, so only what the source had is looked for
                if (!actualByName.TryGetValue(name, out var actualValue))
                {
                    differences.Add($"{context}.{name}: missing");
                    continue;
                }

                if (expected is Element { ClassName: "CVisibilityMgr" } manager && name is "nodes" or "hiddenFlags")
                {
                    CompareVisibility(manager, name, actualValue, $"{context}.{name}", differences, matched, original);
                    continue;
                }

                CompareValues(expectedValue, actualValue, $"{context}.{name}", differences, matched, original);
            }
        }

        // Loading keeps a visibility entry only for a node under the world, and clears the flag that does not outlast a session
        private static void CompareVisibility(Element manager, string name, object? actualValue, string context, List<string> differences, Dictionary<Element, Element> matched, DM original)
        {
            var nodes = (ElementArray)manager["nodes"]!;
            var flags = (IntArray)manager["hiddenFlags"]!;
            var underWorld = new HashSet<Element>();
            var pending = new Stack<Element>();
            pending.Push((Element)original.Root!["world"]!);

            while (pending.TryPop(out var node))
            {
                if (node.ContainsKey("children") && node["children"] is ElementArray children)
                {
                    foreach (var child in children)
                    {
                        if (child != null && underWorld.Add(child))
                        {
                            pending.Push(child);
                        }
                    }
                }
            }

            var kept = Enumerable.Range(0, Math.Min(nodes.Count, flags.Count)).Where(i => nodes[i] is { } node && underWorld.Contains(node)).ToList();

            if (name == "nodes")
            {
                CompareValues(new ElementArray(kept.Select(i => nodes[i])), actualValue, context, differences, matched, original);
            }
            else
            {
                CompareValues(new IntArray(kept.Select(i => flags[i] & ~TransientHiddenFlag)), actualValue, context, differences, matched, original);
            }
        }

        private static void CompareValues(object? expected, object? actual, string context, List<string> differences, Dictionary<Element, Element> matched, DM original)
        {
            if (expected is null || actual is null)
            {
                if (!ReferenceEquals(expected, actual))
                {
                    differences.Add($"{context}: is {actual ?? "null"}, was {expected ?? "null"}");
                }

                return;
            }

            switch (expected)
            {
                case Element expectedElement when actual is Element actualElement:
                    CompareElements(expectedElement, actualElement, context, differences, matched, original);
                    break;
                case byte[] expectedBytes when actual is byte[] actualBytes:
                    if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
                    {
                        differences.Add($"{context}: bytes differ");
                    }

                    break;
                case IList expectedList when actual is IList actualList && actual.GetType() == expected.GetType():
                    if (expectedList.Count != actualList.Count)
                    {
                        differences.Add($"{context}: has {actualList.Count} values, had {expectedList.Count}");
                        break;
                    }

                    for (var i = 0; i < expectedList.Count; i++)
                    {
                        CompareValues(expectedList[i], actualList[i], $"{context}[{i}]", differences, matched, original);
                    }

                    break;
                default:
                    if (actual.GetType() != expected.GetType() || !actual.Equals(expected))
                    {
                        differences.Add($"{context}: is {actual.GetType().Name} {actual}, was {expected.GetType().Name} {expected}");
                    }

                    break;
            }
        }

        private static byte[] Save(DM dm)
        {
            using var ms = new MemoryStream();
            dm.Save(ms, "binary", 9);
            return ms.ToArray();
        }

        private static async Task AssertEquivalent(DM expected, DM actual, bool orderSensitive, double floatTolerance = 0)
        {
            await AssertAttributesEquivalent(expected.PrefixAttributes, actual.PrefixAttributes, "prefix", orderSensitive, floatTolerance);

            var expectedElements = expected.AllElements.ToDictionary(static element => element.ID);
            var actualElements = actual.AllElements.ToDictionary(static element => element.ID);

            await Assert.That(actualElements.Keys).IsEquivalentTo(expectedElements.Keys).Because("element ids");
            await Assert.That(actual.Root?.ID).IsEqualTo(expected.Root?.ID).Because("root");

            foreach (var (id, expectedElement) in expectedElements)
            {
                var actualElement = actualElements[id];
                await Assert.That(actualElement.ClassName).IsEqualTo(expectedElement.ClassName).Because($"class of {id}");
                await Assert.That(actualElement.Name).IsEqualTo(expectedElement.Name).Because($"name of {id}");
                await Assert.That(actualElement.Stub).IsEqualTo(expectedElement.Stub).Because($"stub of {id}");

                if (!expectedElement.Stub)
                {
                    await AssertAttributesEquivalent(expectedElement, actualElement, $"{expectedElement.ClassName} {id}", orderSensitive, floatTolerance);
                }
            }
        }

        private static async Task AssertAttributesEquivalent(AttributeList expected, AttributeList actual, string context, bool orderSensitive, double floatTolerance)
        {
            var expectedAttributes = expected.GetAllAttributesForSerialization().ToArray();
            var actualAttributes = actual.GetAllAttributesForSerialization().ToArray();

            var expectedNames = expectedAttributes.Select(static attribute => attribute.Key).ToArray();
            var actualNames = actualAttributes.Select(static attribute => attribute.Key).ToArray();

            if (orderSensitive)
            {
                await Assert.That(actualNames).IsEquivalentTo(expectedNames, CollectionOrdering.Matching).Because($"attribute names and order of {context}");
            }
            else
            {
                // A typed element also writes class properties the source lacked, with their default values
                await Assert.That(expectedNames.Except(actualNames)).IsEmpty().Because($"attribute names of {context}");
            }

            var actualByName = actualAttributes.ToDictionary(static attribute => attribute.Key, static attribute => attribute.Value);

            foreach (var (name, expectedValue) in expectedAttributes)
            {
                await AssertValueEquivalent(expectedValue, actualByName[name], $"{context}.{name}", floatTolerance);
            }
        }

        private static async Task AssertValueEquivalent(object? expected, object? actual, string context, double floatTolerance)
        {
            if (expected is null || actual is null)
            {
                await Assert.That(actual).IsEqualTo(expected).Because(context);
                return;
            }

            switch (expected)
            {
                case Element expectedElement:
                    await Assert.That(actual).IsAssignableTo<Element>().Because($"type of {context}");
                    await Assert.That(((Element)actual).ID).IsEqualTo(expectedElement.ID).Because(context);
                    break;
                case byte[] expectedBytes:
                    await Assert.That((byte[])actual).IsEquivalentTo(expectedBytes, CollectionOrdering.Matching).Because(context);
                    break;
                case IList expectedList:
                    await Assert.That(actual.GetType()).IsEqualTo(expected.GetType()).Because($"type of {context}");
                    var actualList = (IList)actual;
                    await Assert.That(actualList.Count).IsEqualTo(expectedList.Count).Because($"count of {context}");

                    for (var i = 0; i < expectedList.Count; i++)
                    {
                        await AssertValueEquivalent(expectedList[i], actualList[i], $"{context}[{i}]", floatTolerance);
                    }

                    break;
                default:
                    await Assert.That(actual.GetType()).IsEqualTo(expected.GetType()).Because($"type of {context}");

                    if (floatTolerance > 0 && TryGetComponents(expected, out var expectedComponents) && TryGetComponents(actual, out var actualComponents))
                    {
                        for (var i = 0; i < expectedComponents.Length; i++)
                        {
                            await Assert.That((double)actualComponents[i]).IsEqualTo(expectedComponents[i]).Within(floatTolerance).Because($"{context} component {i}");
                        }

                        break;
                    }

                    await Assert.That(actual).IsEqualTo(expected).Because(context);
                    break;
            }
        }

        private static bool TryGetComponents(object value, out float[] components)
        {
            components = value switch
            {
                float f => [f],
                Vector2 v => [v.X, v.Y],
                Vector3 v => [v.X, v.Y, v.Z],
                Vector4 v => [v.X, v.Y, v.Z, v.W],
                Quaternion q => [q.X, q.Y, q.Z, q.W],
                QAngle a => [a.Pitch, a.Yaw, a.Roll],
                _ => [],
            };

            return components.Length > 0;
        }
    }
}
