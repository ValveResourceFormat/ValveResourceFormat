using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Resolves human-readable names (bones, sequences, weight lists, IK chains, feet, attachments,
/// look-at chains) for an animation graph from its referenced model. Shared by the animation graph
/// extractor and the GUI graph viewer so the model-introspection logic lives in one place.
/// </summary>
/// <remarks>
/// The referenced model <see cref="Resource"/> is supplied lazily through a provider so each owner
/// keeps control of its loading and disposal; this type only reads from it.
/// </remarks>
public sealed class AnimGraphModelInfo
{
    private readonly IFileLoader fileLoader;
    private readonly Func<Resource?> resourceProvider;
    private Resource? resource;
    private bool resourceResolved;

    private string[]? boneNamesCache;
    private string[]? ikChainNamesCache;
    private string[]? footNamesCache;
    private Dictionary<int, string>? weightListNamesCache;
    private Dictionary<int, string>? sequenceNamesCache;
    private Dictionary<string, List<string>>? ikChainBonesCache;
    private Dictionary<string, Attachment>? attachmentsCache;
    private LookAtChainInfo[]? lookAtChainInfoCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimGraphModelInfo"/> class.
    /// </summary>
    /// <param name="fileLoader">Loader used to resolve animations referenced by the model.</param>
    /// <param name="resourceProvider">Lazily returns the referenced model resource (or null).</param>
    public AnimGraphModelInfo(IFileLoader fileLoader, Func<Resource?> resourceProvider)
    {
        this.fileLoader = fileLoader;
        this.resourceProvider = resourceProvider;
    }

    private Resource? ModelResource
    {
        get
        {
            if (!resourceResolved)
            {
                resourceResolved = true;
                resource = resourceProvider();
            }
            return resource;
        }
    }

    private Model? ModelData => ModelResource?.DataBlock as Model;

    private Dictionary<string, Attachment> Attachments => attachmentsCache ??= ModelData?.Attachments ?? [];

    private static string GetNameByIndex(string[] names, int index)
    {
        return index >= 0 && index < names.Length ? names[index] : string.Empty;
    }

    /// <summary>Returns the bone name at the given skeleton index, or empty when out of range.</summary>
    public string GetBoneName(int boneIndex) => GetNameByIndex(LoadBoneNames(), boneIndex);

    /// <summary>Returns the IK chain name at the given index, or empty when out of range.</summary>
    public string GetIKChainName(int ikChainIndex) => GetNameByIndex(LoadIKChainNames(), ikChainIndex);

    /// <summary>Returns the foot name at the given index, or empty when out of range.</summary>
    public string GetFootName(int footIndex) => GetNameByIndex(LoadFootNames(), footIndex);

    /// <summary>Returns the weight list (bone mask) name, falling back to a generated placeholder.</summary>
    public string GetWeightListName(long weightListIndex)
    {
        weightListNamesCache ??= LoadWeightListNames();
        return weightListNamesCache.TryGetValue((int)weightListIndex, out var name)
            ? name
            : weightListIndex == 0 ? "default" : $"weightlist_{weightListIndex}";
    }

    /// <summary>Returns the sequence name, falling back to a generated placeholder.</summary>
    public string GetSequenceName(long sequenceIndex)
    {
        sequenceNamesCache ??= LoadSequenceNames();
        return sequenceNamesCache.TryGetValue((int)sequenceIndex, out var name)
            ? name
            : $"sequence_{sequenceIndex}";
    }

    private string[] LoadBoneNames()
    {
        if (boneNamesCache is not null)
        {
            return boneNamesCache;
        }
        try
        {
            boneNamesCache = ModelData?.Skeleton.Bones.Select(b => b.Name).ToArray() ?? [];
        }
        catch (Exception)
        {
            boneNamesCache = [];
        }
        return boneNamesCache;
    }

    private static IReadOnlyList<KVObject>? GetIKChainsFromModel(Model? modelData)
    {
        if (modelData is null)
        {
            return null;
        }

        var keyvalues = modelData.KeyValues;
        if (!keyvalues.ContainsKey("ikdata"))
        {
            return null;
        }

        var ikdata = keyvalues.GetSubCollection("ikdata");
        return ikdata.ContainsKey("m_IKChains") ? ikdata.GetArray("m_IKChains") : null;
    }

    private string[] LoadIKChainNames()
    {
        if (ikChainNamesCache is not null)
        {
            return ikChainNamesCache;
        }
        try
        {
            ikChainNamesCache = GetIKChainsFromModel(ModelData)?
                .Select(c => c.GetStringProperty("m_Name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray() ?? [];
        }
        catch (Exception)
        {
            ikChainNamesCache = [];
        }
        return ikChainNamesCache;
    }

    private Dictionary<string, List<string>> LoadIKChainBones()
    {
        if (ikChainBonesCache is not null)
        {
            return ikChainBonesCache;
        }
        var chainBones = new Dictionary<string, List<string>>();
        try
        {
            var ikChains = GetIKChainsFromModel(ModelData);
            if (ikChains is null)
            {
                return chainBones;
            }

            foreach (var chain in ikChains)
            {
                var name = chain.GetStringProperty("m_Name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var boneList = new List<string>();
                if (chain.ContainsKey("m_Joints"))
                {
                    foreach (var joint in chain.GetArray("m_Joints"))
                    {
                        if (joint.ContainsKey("m_Bone"))
                        {
                            var boneName = joint.GetSubCollection("m_Bone").GetStringProperty("m_Name");
                            if (!string.IsNullOrEmpty(boneName))
                            {
                                boneList.Add(boneName);
                            }
                        }
                    }
                }
                chainBones[name] = boneList;
            }
        }
        catch (Exception)
        {
            chainBones.Clear();
        }
        ikChainBonesCache = chainBones;
        return chainBones;
    }

    /// <summary>Finds the IK chain whose three joints match the given fixed/middle/end bone indices.</summary>
    public string GetIKChainNameByBoneIndices(int fixedBoneIndex, int middleBoneIndex, int endBoneIndex)
    {
        var fixedBoneName = GetBoneName(fixedBoneIndex);
        var middleBoneName = GetBoneName(middleBoneIndex);
        var endBoneName = GetBoneName(endBoneIndex);

        if (string.IsNullOrEmpty(fixedBoneName) || string.IsNullOrEmpty(middleBoneName) || string.IsNullOrEmpty(endBoneName))
        {
            return string.Empty;
        }

        foreach (var (chainName, bones) in LoadIKChainBones())
        {
            if (bones.Count == 3 && bones[0] == fixedBoneName && bones[1] == middleBoneName && bones[2] == endBoneName)
            {
                return chainName;
            }
        }
        return string.Empty;
    }

    private string[] LoadFootNames()
    {
        if (footNamesCache is not null)
        {
            return footNamesCache;
        }
        var footNames = new List<string>();
        try
        {
            var keyvalues = ModelData?.KeyValues;
            if (keyvalues?.ContainsKey("FeetSettings") == true)
            {
                foreach (var (footKey, _) in keyvalues.GetSubCollection("FeetSettings").Children)
                {
                    if (!string.IsNullOrEmpty(footKey) && footKey != "_class")
                    {
                        footNames.Add(footKey);
                    }
                }
            }
        }
        catch (Exception)
        {
            footNames.Clear();
        }
        footNamesCache = [.. footNames];
        return footNamesCache;
    }

    private Dictionary<int, string> LoadWeightListNames()
    {
        var weightListNames = new Dictionary<int, string>();
        try
        {
            var localBoneMaskArray = ModelResource is not null
                ? GetAseqDataFromResource(ModelResource)?.GetArray("m_localBoneMaskArray")
                : null;
            if (localBoneMaskArray is { Count: > 0 })
            {
                for (var i = 0; i < localBoneMaskArray.Count; i++)
                {
                    var weightListName = localBoneMaskArray[i].GetStringProperty("m_sName");
                    weightListNames[i] = !string.IsNullOrEmpty(weightListName)
                        ? weightListName
                        : i == 0 ? "default" : $"weightlist_{i}";
                }
            }
        }
        catch
        {
        }

        weightListNames.TryAdd(0, "default");
        return weightListNames;
    }

    private Dictionary<int, string> LoadSequenceNames()
    {
        var sequenceNames = new Dictionary<int, string>();
        try
        {
            var modelResource = ModelResource;
            if (modelResource is null)
            {
                return sequenceNames;
            }

            var index = 0;
            var localSequenceNameArray = GetAseqDataFromResource(modelResource)?.GetArray<string>("m_localSequenceNameArray");
            if (localSequenceNameArray is not null)
            {
                foreach (var sequenceName in localSequenceNameArray)
                {
                    if (!string.IsNullOrEmpty(sequenceName))
                    {
                        sequenceNames[index++] = sequenceName;
                    }
                }
            }
            if (modelResource.DataBlock is Model modelData)
            {
                foreach (var animation in modelData.GetReferencedAnimations(fileLoader))
                {
                    if (!string.IsNullOrEmpty(animation.Name))
                    {
                        sequenceNames[index++] = animation.Name;
                    }
                }
            }
        }
        catch
        {
        }
        return sequenceNames;
    }

    private static KVObject? GetAseqDataFromResource(Resource modelResource)
    {
        if (!modelResource.ContainsBlockType(BlockType.ASEQ))
        {
            return null;
        }

        var aseqBlock = modelResource.GetBlockByType(BlockType.ASEQ);

        if (aseqBlock is not KeyValuesOrNTRO keyValuesOrNTRO)
        {
            return null;
        }

        var data = keyValuesOrNTRO.Data;

        if (data is not KVObject kvData)
        {
            return null;
        }

        if (kvData.ContainsKey("m_localBoneMaskArray") ||
            kvData.ContainsKey("m_localSequenceNameArray") ||
            kvData.GetStringProperty("m_sName")?.Contains("embedded_sequence_data", StringComparison.Ordinal) == true)
        {
            return kvData;
        }

        return kvData.ContainsKey("ASEQ")
            ? kvData.GetSubCollection("ASEQ")
            : null;
    }

    /// <summary>
    /// Resolves the model attachment that matches a compiled attachment, by name when available or
    /// otherwise by comparing bone influences. Returns empty when no match is found.
    /// </summary>
    public string FindMatchingAttachmentName(KVObject compiledAttachment)
    {
        if (compiledAttachment is null)
        {
            return string.Empty;
        }

        // Try to get attachment name directly if stored as a string property
        if (compiledAttachment.ContainsKey("m_attachmentName"))
        {
            return compiledAttachment.GetStringProperty("m_attachmentName");
        }

        if (compiledAttachment.ContainsKey("m_name"))
        {
            return compiledAttachment.GetStringProperty("m_name");
        }

        var attachments = Attachments;
        if (attachments.Count == 0 || !compiledAttachment.ContainsKey("m_influenceIndices"))
        {
            return string.Empty;
        }

        // not exactly the same keys as model attachment.
        var influenceIndices = compiledAttachment.GetArray<int>("m_influenceIndices");
        var influenceRotations = compiledAttachment.GetArray("m_influenceRotations").Select(v => v.ToQuaternion()).ToArray();
        var influenceOffsets = compiledAttachment.GetArray("m_influenceOffsets").Select(v => v.ToVector3()).ToArray();
        var influenceWeights = compiledAttachment.GetArray<double>("m_influenceWeights");

        var influenceCount = compiledAttachment.GetInt32Property("m_numInfluences");
        if (influenceCount == 0 || influenceIndices.Length < influenceCount)
        {
            return string.Empty;
        }

        var influences = new Attachment.Influence[influenceCount];
        for (var i = 0; i < influenceCount; i++)
        {
            var boneName = GetBoneName(influenceIndices[i]);
            influences[i] = new Attachment.Influence
            {
                Name = boneName,
                Rotation = influenceRotations[i],
                Offset = influenceOffsets[i],
                Weight = (float)influenceWeights[i]
            };
        }

        foreach (var (name, attachment) in attachments)
        {
            if (attachment.Length != influenceCount)
            {
                continue;
            }

            // Some rudamentary attachment matching
            const float epsilon = 0.001f;
            var posDiff = Vector3.DistanceSquared(attachment[0].Offset, influences[0].Offset);

            if (posDiff > epsilon)
            {
                continue;
            }

            var dot = Quaternion.Dot(attachment[0].Rotation, influences[0].Rotation);
            var absDot = Math.Abs(dot);

            if (Math.Abs(absDot - 1.0f) > epsilon)
            {
                continue;
            }

            return name;
        }

        return string.Empty;
    }

    private sealed class LookAtChainInfo
    {
        public string Name { get; set; } = string.Empty;
        public string[] BoneNames { get; set; } = [];
        public float[] BoneWeights { get; set; } = [];
    }

    /// <summary>Returns the bone names referenced by a compiled <c>m_bones</c> collection.</summary>
    public string[] GetBoneNamesFromIndices(KVObject compiledBones)
    {
        if (compiledBones is null || !compiledBones.ContainsKey("m_bones"))
        {
            return [];
        }
        var compiledBonesArray = compiledBones.GetArray("m_bones");
        var boneNames = new string[compiledBonesArray.Count];

        for (var i = 0; i < compiledBonesArray.Count; i++)
        {
            var boneIndex = (int)compiledBonesArray[i].GetIntegerProperty("m_index");
            boneNames[i] = GetBoneName(boneIndex);
        }
        return boneNames;
    }

    private LookAtChainInfo[] LoadLookAtChainInfo()
    {
        if (lookAtChainInfoCache is not null)
        {
            return lookAtChainInfoCache;
        }
        var lookAtChains = new List<LookAtChainInfo>();
        try
        {
            var keyvalues = ModelData?.KeyValues;
            if (keyvalues?.ContainsKey("LookAtList") == true)
            {
                foreach (var (_, chainEntryValue) in keyvalues.GetSubCollection("LookAtList").Children)
                {
                    if (chainEntryValue.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }
                    var chain = new LookAtChainInfo
                    {
                        Name = chainEntryValue.GetStringProperty("name"),
                    };
                    if (chainEntryValue.ContainsKey("bones"))
                    {
                        var bones = chainEntryValue.GetArray("bones");
                        chain.BoneNames = bones.Select(b => b.GetStringProperty("name")).ToArray();
                        chain.BoneWeights = bones.Select(b => b.GetFloatProperty("weight")).ToArray();
                    }
                    lookAtChains.Add(chain);
                }
            }
        }
        catch (Exception)
        {
            lookAtChains.Clear();
        }
        lookAtChainInfoCache = [.. lookAtChains];
        return lookAtChainInfoCache;
    }

    /// <summary>Finds the model look-at chain whose bones match the compiled <c>m_bones</c> collection.</summary>
    public string FindMatchingLookAtChainName(KVObject compiledBones)
    {
        if (compiledBones is null || !compiledBones.ContainsKey("m_bones"))
        {
            return string.Empty;
        }
        var lookAtChains = LoadLookAtChainInfo();
        if (lookAtChains.Length == 0)
        {
            return string.Empty;
        }
        var compiledBoneNames = GetBoneNamesFromIndices(compiledBones);

        foreach (var chain in lookAtChains)
        {
            if (chain.BoneNames.SequenceEqual(compiledBoneNames))
            {
                return chain.Name;
            }
        }

        // Fall back to unordered set equality
        var compiledSet = new HashSet<string>(compiledBoneNames);
        foreach (var chain in lookAtChains)
        {
            if (compiledSet.SetEquals(chain.BoneNames))
            {
                return chain.Name;
            }
        }
        return string.Empty;
    }
}
