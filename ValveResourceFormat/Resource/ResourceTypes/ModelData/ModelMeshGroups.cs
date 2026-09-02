using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// One choice of a body group: a mesh group whose name encodes the group it belongs to.
    /// </summary>
    /// <param name="GroupIndex">Index of this choice in <see cref="ModelMeshGroups.Names"/>, which is the bit it occupies in a mesh's group mask.</param>
    /// <param name="FullName">The mesh group name as compiled, e.g. <c>head_@bald</c>.</param>
    /// <param name="Name">The authored choice name, e.g. <c>bald</c>.</param>
    /// <param name="Indexed">
    /// Whether the compiled name spelled out the choice's index. A group holding a single choice can
    /// be compiled without it, which ModelDoc calls <c>non_bodygroup_single_choice</c>.
    /// </param>
    public sealed record BodyGroupChoice(int GroupIndex, string FullName, string Name, bool Indexed);

    /// <summary>
    /// A body group: a set of mesh groups the model switches between, recovered from the mesh group
    /// names that share a prefix.
    /// </summary>
    /// <param name="Name">The body group name, the part before the separator.</param>
    /// <param name="Choices">Its choices, in the order their mesh groups are declared.</param>
    public sealed record BodyGroup(string Name, IReadOnlyList<BodyGroupChoice> Choices);

    /// <summary>
    /// Describes a model's mesh groups: which groups exist, which meshes belong to each, which are on
    /// by default, and which the tools hide. Built once from the compiled model's <c>m_meshGroups</c>,
    /// <c>m_refMeshGroupMasks</c> (bit N set =&gt; mesh is in group N), <c>m_nDefaultMeshGroupMask</c>
    /// and <c>m_BodyGroupsHiddenInTools</c>.
    /// </summary>
    public sealed class ModelMeshGroups
    {
        /// <summary>A mask has one bit per group, so nothing past the 64th group is addressable.</summary>
        private const int MaxGroups = 64;

        private readonly ulong[] meshMasks;
        private readonly ulong defaultMask;
        private readonly HashSet<string> hiddenInTools;

        /// <summary>Gets the mesh group names, in declaration order. Index N is bit N of a mesh's mask.</summary>
        public IReadOnlyList<string> Names { get; }

        /// <summary>
        /// Gets the body groups the mesh group names encode. A name of the form <c>group_@choice</c>
        /// declares one choice of a body group; a name without the separator belongs to no body group.
        /// Groups and their choices keep the order their mesh groups were declared in.
        /// </summary>
        public IReadOnlyList<BodyGroup> BodyGroups { get; }

        /// <summary>Gets the group names that are on by default.</summary>
        public IEnumerable<string> Defaults
            => Names.Where((_, index) => index < MaxGroups && (defaultMask & 1UL << index) != 0);

        /// <summary>
        /// Initializes mesh group info from a compiled model's data block.
        /// </summary>
        public ModelMeshGroups(KVObject data)
        {
            ArgumentNullException.ThrowIfNull(data);

            Names = data.GetArray<string>("m_meshGroups") ?? [];
            meshMasks = data.GetUnsignedIntegerArray("m_refMeshGroupMasks") ?? [];
            defaultMask = data.GetUnsignedIntegerProperty("m_nDefaultMeshGroupMask");
            hiddenInTools = [.. data.GetArray<string>("m_BodyGroupsHiddenInTools") ?? []];

            BodyGroups = BuildBodyGroups(Names);
        }

        /// <summary>Gets the group mask of a mesh, zero when the mesh has no entry.</summary>
        public ulong GetMeshMask(int meshIndex)
            => meshIndex >= 0 && meshIndex < meshMasks.Length ? meshMasks[meshIndex] : 0UL;

        /// <summary>Determines whether a mesh belongs to the group at <paramref name="groupIndex"/>.</summary>
        public bool IsMeshInGroup(int meshIndex, int groupIndex)
            => groupIndex >= 0 && groupIndex < MaxGroups && (GetMeshMask(meshIndex) & 1UL << groupIndex) != 0;

        /// <summary>
        /// Determines whether a mesh belongs to any of the named groups. A model with no groups declared
        /// draws every mesh, so it is treated as belonging to whatever is asked for.
        /// </summary>
        public bool IsMeshInAnyGroup(int meshIndex, ICollection<string> groupNames)
        {
            if (Names.Count <= 1 || meshMasks.Length == 0)
            {
                return true;
            }

            foreach (var groupName in groupNames)
            {
                if (IsMeshInGroup(meshIndex, IndexOf(groupName)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Gets the index of a group name, or -1 when the model declares no such group.</summary>
        public int IndexOf(string groupName)
        {
            for (var i = 0; i < Names.Count; i++)
            {
                if (Names[i] == groupName)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Determines whether a body group or one of its choices is hidden in the tools. Choices are
        /// listed by their full mesh group name.
        /// </summary>
        public bool IsHiddenInTools(string name) => hiddenInTools.Contains(name);

        /// <summary>The separator a mesh group name puts between its body group and its choice.</summary>
        private const string ChoiceSeparator = "_@";

        /// <summary>Newer models bury the authored choice name behind this marker.</summary>
        private const string ChoiceNameMarker = "#&";

        private static BodyGroup[] BuildBodyGroups(IReadOnlyList<string> names)
        {
            var groups = new List<BodyGroup>();
            var choicesByGroup = new Dictionary<string, List<BodyGroupChoice>>();

            for (var index = 0; index < names.Count; index++)
            {
                var fullName = names[index];
                var (groupName, choiceName, indexed) = SplitMeshGroupName(fullName);

                if (!indexed && choiceName.Length == 0)
                {
                    // Neither part present means the compiler made this group up for the meshes no
                    // authored group claims, so writing it back would invent a body group.
                    continue;
                }

                if (!choicesByGroup.TryGetValue(groupName, out var choices))
                {
                    choicesByGroup[groupName] = choices = [];
                    groups.Add(new BodyGroup(groupName, choices));
                }

                choices.Add(new BodyGroupChoice(index, fullName, choiceName, indexed));
            }

            return [.. groups];
        }

        /// <summary>
        /// Splits a compiled mesh group name into the body group it belongs to, the authored choice
        /// name, and whether the choice's index was spelled out.
        /// </summary>
        /// <remarks>
        /// The compiler writes the group, then the choice index behind
        /// <see cref="ChoiceSeparator"/> unless the group holds a single choice and is marked
        /// <c>non_bodygroup_single_choice</c>, then the authored name behind
        /// <see cref="ChoiceNameMarker"/>. A name carrying neither part is one the compiler generated
        /// itself. The group name can contain the separator, so the index comes off the last one.
        /// </remarks>
        private static (string Group, string ChoiceName, bool Indexed) SplitMeshGroupName(string fullName)
        {
            var body = fullName;
            var choiceName = string.Empty;
            var marker = fullName.IndexOf(ChoiceNameMarker, StringComparison.Ordinal);

            if (marker >= 0)
            {
                choiceName = fullName[(marker + ChoiceNameMarker.Length)..];
                body = fullName[..marker];

                if (body.EndsWith('_'))
                {
                    body = body[..^1];
                }
            }

            var separator = body.LastIndexOf(ChoiceSeparator, StringComparison.Ordinal);

            if (separator >= 0 && IsChoiceIndex(body[(separator + ChoiceSeparator.Length)..]))
            {
                return (body[..separator], choiceName, true);
            }

            return (body, choiceName, false);
        }

        private static bool IsChoiceIndex(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (var character in text)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
