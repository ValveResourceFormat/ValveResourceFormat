using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// Looks the named parts of a model up by name the way particle functions address them: sequences
    /// by their index among the model's sequences, body parts by index, and mesh groups by their bit in
    /// a mesh group mask. Names match ignoring case.
    /// </summary>
    internal static class ModelElements
    {
        /// <summary>The most names a function resolves from one list.</summary>
        public const int MaxNames = 64;

        /// <summary>
        /// Looks every name up in <paramref name="model"/>, at most <see cref="MaxNames"/> of them. A
        /// name the model lacks, or every name without a model, gives -1 for a sequence or body part
        /// and 0 for a mesh group; a mesh group name the model lacks gives -1.
        /// </summary>
        public static int[] Resolve(ModelElementKind kind, IReadOnlyList<string> names, Model? model, IFileLoader? fileLoader)
        {
            var indices = new int[Math.Min(names.Count, MaxNames)];

            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = (kind, model) switch
                {
                    (ModelElementKind.MeshGroup, null) => 0,
                    (_, null) => -1,
                    (ModelElementKind.Sequence, _) => FindSequence(model, names[i], fileLoader),
                    (ModelElementKind.BodyPart, _) => FindBodyPart(model, names[i]),
                    _ => FindMeshGroupMask(model, names[i]),
                };
            }

            return indices;
        }

        private static readonly ModelCache<string[]> SequenceNameCache = new(ReadSequenceNames);
        private static readonly ModelCache<AABB> BoundsCache = new(ReadBounds);

        /// <summary>
        /// The names of the model's sequences in index order: the sequences it embeds, then those of
        /// each animation group it references, which only a file loader reaches.
        /// </summary>
        public static string[] SequenceNames(Model model, IFileLoader? fileLoader) => SequenceNameCache.Get(model, fileLoader);

        private static string[] ReadSequenceNames(Model model, IFileLoader? fileLoader)
        {
            var names = new List<string>();
            names.AddRange(model.SequenceGroup.SequenceData?.GetArray<string>("m_localSequenceNameArray") ?? []);

            if (fileLoader != null)
            {
                foreach (var groupPath in model.GetReferencedAnimationGroupNames() ?? [])
                {
                    if (string.IsNullOrEmpty(groupPath)
                        || fileLoader.LoadFileCompiled(groupPath)?.DataBlock?.AsKeyValueCollection() is not { } group)
                    {
                        continue;
                    }

                    var sequenceGroupPath = group.GetStringProperty("m_directHSeqGroup_Handle");

                    if (!string.IsNullOrEmpty(sequenceGroupPath)
                        && fileLoader.LoadFileCompiled(sequenceGroupPath)?.DataBlock?.AsKeyValueCollection() is { } sequenceGroup)
                    {
                        names.AddRange(sequenceGroup.GetArray<string>("m_localSequenceNameArray") ?? []);
                    }
                }
            }

            return [.. names];
        }

        /// <summary>The index of the sequence named <paramref name="name"/>, or -1 when the model has none.</summary>
        public static int FindSequence(Model model, string name, IFileLoader? fileLoader)
            => IndexOf(SequenceNames(model, fileLoader), name);

        /// <summary>The index of the body part named <paramref name="name"/>, or -1 when the model has none.</summary>
        public static int FindBodyPart(Model model, string name)
        {
            var bodyGroups = model.MeshGroups.BodyGroups;

            for (var i = 0; i < bodyGroups.Count; i++)
            {
                if (NameEquals(bodyGroups[i].Name, name))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The mesh group mask bit of the mesh group named <paramref name="name"/>, cut to 32 bits, so
        /// group 31 gives <see cref="int.MinValue"/> and later groups give 0. An unknown name gives -1,
        /// every group.
        /// </summary>
        public static int FindMeshGroupMask(Model model, string name)
        {
            var group = IndexOf(model.MeshGroups.Names, name);

            return group switch
            {
                < 0 => -1,
                < 32 => 1 << group,
                _ => 0,
            };
        }

        /// <summary>
        /// The box around the render meshes of <see cref="RenderMeshes"/>, in the model's own space.
        /// Zero when there are none.
        /// </summary>
        public static AABB Bounds(Model model, IFileLoader? fileLoader) => BoundsCache.Get(model, fileLoader);

        private static AABB ReadBounds(Model model, IFileLoader? fileLoader)
        {
            AABB? bounds = null;

            foreach (var (mesh, _) in RenderMeshes(model, fileLoader))
            {
                mesh.GetBounds();
                var meshBounds = new AABB(mesh.MinBounds, mesh.MaxBounds);
                bounds = bounds?.Union(meshBounds) ?? meshBounds;
            }

            return bounds ?? default;
        }

        /// <summary>
        /// The render meshes of the model's most detailed level of detail with their mesh indices: the
        /// ones it embeds, then the ones it references that <paramref name="fileLoader"/> loads.
        /// </summary>
        public static IEnumerable<(Mesh Mesh, int MeshIndex)> RenderMeshes(Model model, IFileLoader? fileLoader)
        {
            var level = model.LodInfo.LowestLevel;

            foreach (var embedded in model.GetEmbeddedMeshesForLod(level))
            {
                yield return (embedded.Mesh, embedded.MeshIndex);
            }

            foreach (var reference in model.GetReferenceMeshNamesForLod(level))
            {
                if (fileLoader?.LoadFileCompiled(reference.MeshName)?.DataBlock is Mesh mesh)
                {
                    yield return (mesh, reference.MeshIndex);
                }
            }
        }

        /// <summary>The smallest and largest of <paramref name="indices"/>, or <see cref="int.MaxValue"/> and 0 when there are none.</summary>
        public static (int Min, int Max) Range(int[] indices)
            => indices.Length > 0 ? (indices.Min(), indices.Max()) : (int.MaxValue, 0);

        /// <summary>
        /// The place of <paramref name="value"/> among <paramref name="indices"/>, whose range
        /// <see cref="Range"/> gives: 0 below it, <paramref name="lastPlace"/> above it, and the count of
        /// indices for a value inside it that is not listed.
        /// </summary>
        public static int PlaceOf(int[] indices, (int Min, int Max) range, int value, int lastPlace)
        {
            if (value < range.Min)
            {
                return 0;
            }

            if (value > range.Max)
            {
                return lastPlace;
            }

            var place = Array.IndexOf(indices, value);
            return place < 0 ? indices.Length : place;
        }

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (NameEquals(names[i], name))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool NameEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
