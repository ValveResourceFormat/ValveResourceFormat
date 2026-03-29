using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "RED2" block. CResourceEditInfo.
    /// </summary>
    public class ResourceEditInfo2 : ResourceEditInfo
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.RED2;

        private BinaryKV3? BackingData;

        /// <summary>
        /// Gets the list of weak references.
        /// </summary>
        public List<string> WeakReferenceList { get; } = [];

        /// <summary>
        /// Gets the subasset references.
        /// </summary>
        public Dictionary<string, Dictionary<string, int>>? SubassetReferences { get; private set; }

        /// <summary>
        /// Gets the subasset definitions.
        /// </summary>
        public Dictionary<string, string[]>? SubassetDefinitions { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceEditInfo2"/> class.
        /// </summary>
        public ResourceEditInfo2()
        {
            //
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            var kv3 = new BinaryKV3
            {
                Offset = Offset,
                Size = Size,
                Resource = Resource,
            };

            kv3.Read(reader);
            BackingData = kv3;

            static void ReadItems<T>(BinaryKV3 kv3, List<T> list, string key, Func<KVObject, T> constructor)
            {
                var container = kv3.Data.GetChild(key);
                ArgumentNullException.ThrowIfNull(container, key);
                ArgumentOutOfRangeException.ThrowIfEqual(container.IsArray, false, key);

                list.EnsureCapacity(container.Count);

                var items = kv3.Data.GetArray(key);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var newItem = constructor.Invoke(item);
                        list.Add(newItem);
                    }
                }
            }

            ReadItems(kv3, InputDependencies, "m_InputDependencies", static (KVObject data) => new InputDependency(data));
            ReadItems(kv3, AdditionalInputDependencies, "m_AdditionalInputDependencies", static (KVObject data) => new InputDependency(data));
            ReadItems(kv3, ArgumentDependencies, "m_ArgumentDependencies", static (KVObject data) => new ArgumentDependency(data));
            ReadItems(kv3, SpecialDependencies, "m_SpecialDependencies", static (KVObject data) => new SpecialDependency(data));
            ReadItems(kv3, AdditionalRelatedFiles, "m_AdditionalRelatedFiles", static (KVObject data) => new AdditionalRelatedFile(data));

            var childResources = kv3.Data.GetArray<string>("m_ChildResourceList");
            if (childResources != null)
            {
                ChildResourceList.AddRange(childResources);
            }

            var weakReferences = kv3.Data.GetArray<string>("m_WeakReferenceList");
            if (weakReferences is not null)
            {
                WeakReferenceList.AddRange(weakReferences);
            }

            var searchableData = kv3.Data.GetProperty<KVObject>("m_SearchableUserData");
            if (searchableData is not null)
            {
                foreach (var child in searchableData.Children)
                {
                    SearchableUserData.Add(child.Name, child.Value);
                }
            }

            var subassetReferences = kv3.Data.GetProperty<KVObject>("m_SubassetReferences");
            if (subassetReferences != null)
            {
                SubassetReferences = new(capacity: subassetReferences.Count);

                foreach (var property in subassetReferences.Children)
                {
                    if (property.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }

                    var perTypeReferences = new Dictionary<string, int>(capacity: property.Count);

                    foreach (var child in property.Children)
                    {
                        perTypeReferences.Add(child.Name, Convert.ToInt32(child, CultureInfo.InvariantCulture));
                    }

                    SubassetReferences.Add(property.Name, perTypeReferences);
                }
            }

            var subassetDefinitions = kv3.Data.GetProperty<KVObject>("m_SubassetDefinitions");
            if (subassetDefinitions != null)
            {
                SubassetDefinitions = new(capacity: subassetDefinitions.Count);

                foreach (var property in subassetDefinitions.Children)
                {
                    if (property.ValueType != KVValueType.Array)
                    {
                        continue;
                    }

                    var definitions = new string[property.Count];
                    for (var i = 0; i < property.Count; i++)
                    {
                        definitions[i] = (string)property[i]!.Value;
                    }

                    SubassetDefinitions.Add(property.Name, definitions);
                }
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            BackingData?.Serialize(stream);
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            BackingData?.WriteText(writer);
        }
    }
}
