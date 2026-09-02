using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Loads the compiled resources under <c>Files/</c> and parses what the extractors produce from them.
    /// </summary>
    internal static class TestFixtures
    {
        public static string Path(string fileName)
            => System.IO.Path.Combine(TestContext.TestDirectory!, "Files", fileName);

        /// <summary>Reads a compiled resource. <see cref="Resource.Read(string)"/> sets its file name.</summary>
        public static Resource Load(string fileName)
        {
            var resource = new Resource();
            resource.Read(Path(fileName));

            return resource;
        }

        /// <summary>Decompiles a model to vmdl text, with no loader for its external references.</summary>
        public static string ExtractValveModel(string fileName)
        {
            using var resource = Load(fileName);

            return new ModelExtract(resource, new NullFileLoader()).ToValveModel();
        }

        /// <summary>Decompiles a model and parses the vmdl back into a document.</summary>
        public static KVObject ExtractValveModelDocument(string fileName)
            => ParseKV3(ExtractValveModel(fileName));

        public static KVObject ParseKV3(string text)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));

            return KVDocumentExtensions.ParseKV3(ms).Root;
        }

        /// <summary>The first node of the given class anywhere in the document, depth first.</summary>
        public static KVObject? FindNode(KVObject node, string className)
            => Find(node, candidate => candidate.GetStringProperty("_class") == className);

        /// <summary>The first named node anywhere in the document, depth first.</summary>
        public static KVObject? FindNamed(KVObject node, string name)
            => Find(node, candidate => candidate.GetStringProperty("name") == name
                && candidate.GetStringProperty("_class").Length > 0);

        private static KVObject? Find(KVObject node, Func<KVObject, bool> match)
        {
            if (match(node))
            {
                return node;
            }

            foreach (var (_, child) in node)
            {
                if (child is KVObject childNode && Find(childNode, match) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the part of a compiled model's data block <see cref="ModelLodInfo"/> reads, so the LOD
        /// structure can be exercised without a compiled model behind it.
        /// </summary>
        public static KVObject ModelLodData(long[] meshLodMasks, float[] switchDistances)
        {
            var masks = KVObject.Array();
            foreach (var mask in meshLodMasks)
            {
                masks.Add(mask);
            }

            var switches = KVObject.Array();
            foreach (var distance in switchDistances)
            {
                switches.Add(distance);
            }

            var data = KVObject.Collection();
            data.Add("m_refLODGroupMasks", masks);
            data.Add("m_lodGroupSwitchDistances", switches);

            return data;
        }
    }
}
