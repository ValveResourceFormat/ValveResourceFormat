using System.Globalization;
using System.IO;
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
                var container = kv3.Data.Properties.GetValueOrDefault(key).Value as KVObject;
                ArgumentNullException.ThrowIfNull(container, key);
                ArgumentOutOfRangeException.ThrowIfEqual(container.IsArray, false, key);

                list.EnsureCapacity(container.Count);

                foreach (var item in container)
                {
                    if (item.Value is KVObject kvObject)
                    {
                        var newItem = constructor.Invoke(kvObject);
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
                SearchableUserData.Properties.EnsureCapacity(searchableData.Properties.Count);

                foreach (var property in searchableData.Properties)
                {
                    SearchableUserData.Properties.Add(property.Key, property.Value);
                }
            }

            var subassetReferences = kv3.Data.GetProperty<KVObject>("m_SubassetReferences");
            if (subassetReferences != null)
            {
                SubassetReferences = new(capacity: subassetReferences.Count);

                foreach (var property in subassetReferences)
                {
                    if (property.Value is KVObject perTypeReferencesKv)
                    {
                        var perTypeReferences = new Dictionary<string, int>(capacity: perTypeReferencesKv.Count);

                        foreach (var (refName, refCount) in perTypeReferencesKv)
                        {
                            perTypeReferences.Add(refName, Convert.ToInt32(refCount, CultureInfo.InvariantCulture));
                        }

                        var subassetType = property.Key;
                        SubassetReferences.Add(subassetType, perTypeReferences);
                    }
                }
            }

            var subassetDefinitions = kv3.Data.GetProperty<KVObject>("m_SubassetDefinitions");
            if (subassetDefinitions != null)
            {
                SubassetDefinitions = new(capacity: subassetDefinitions.Count);

                foreach (var property in subassetDefinitions)
                {
                    var subassetType = property.Key;
                    var definitions = subassetDefinitions.GetArray<string>(subassetType);

                    if (definitions != null)
                    {
                        SubassetDefinitions.Add(subassetType, definitions);
                    }
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
