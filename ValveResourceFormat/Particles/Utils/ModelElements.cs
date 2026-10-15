using System.Linq;
using System.Runtime.CompilerServices;
using ValveKeyValue;
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

        private static readonly ConditionalWeakTable<Model, string[]> SequenceNameCache = [];

        /// <summary>
        /// The names of the model's sequences in index order: the sequences it embeds, then those of
        /// each animation group it references.
        /// </summary>
        public static string[] SequenceNames(Model model, IFileLoader? fileLoader)
        {
            if (SequenceNameCache.TryGetValue(model, out var cached))
            {
                return cached;
            }

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

            string[] result = [.. names];
            SequenceNameCache.AddOrUpdate(model, result);
            return result;
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
                if (string.Equals(bodyGroups[i].Name, name, StringComparison.OrdinalIgnoreCase))
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
            var groupNames = model.MeshGroups.Names;

            for (var i = 0; i < groupNames.Count; i++)
            {
                if (string.Equals(groupNames[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i < 32 ? 1 << i : 0;
                }
            }

            return -1;
        }

        /// <summary>
        /// The box around the model's first level of detail, in its own space, from the meshes it
        /// embeds. Zero when it embeds none.
        /// </summary>
        public static (Vector3 Mins, Vector3 Maxs) Bounds(Model model)
        {
            var mins = new Vector3(float.MaxValue);
            var maxs = new Vector3(float.MinValue);

            foreach (var modelMesh in model.GetEmbeddedMeshesForLod(0))
            {
                modelMesh.Mesh.GetBounds();
                mins = Vector3.Min(mins, modelMesh.Mesh.MinBounds);
                maxs = Vector3.Max(maxs, modelMesh.Mesh.MaxBounds);
            }

            return mins.X <= maxs.X ? (mins, maxs) : (Vector3.Zero, Vector3.Zero);
        }

        private static int IndexOf(string[] names, string name)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// The model a function names in <c>m_hModel</c>, or with <c>m_bModelFromRenderer</c> the first
    /// model the system's renderers draw, loaded through the running system.
    /// </summary>
    internal sealed class ModelReference
    {
        private readonly string path;
        private readonly bool fromRenderer;

        public ModelReference(ParticleDefinitionParser parse)
        {
            path = parse.String("m_hModel");
            fromRenderer = parse.Boolean("m_bModelFromRenderer");
        }

        /// <summary>The model, or null when none is named or it does not load.</summary>
        public Model? Resolve(ParticleSystemState systemState)
        {
            if (systemState.Data is not { } simulation)
            {
                return null;
            }

            var modelPath = fromRenderer ? FirstRendererModel(simulation.Definition) : path;

            if (string.IsNullOrEmpty(modelPath))
            {
                return null;
            }

            return simulation.FileLoader.LoadFileCompiled(modelPath)?.DataBlock as Model;
        }

        private static string? FirstRendererModel(KVObject definition)
        {
            foreach (var renderer in definition.GetArray("m_Renderers") ?? [])
            {
                foreach (var entry in renderer.GetArray("m_ModelList") ?? [])
                {
                    var model = entry.GetStringProperty("m_model");

                    if (!string.IsNullOrEmpty(model))
                    {
                        return model;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// A function's <c>m_modelInput</c>: where it takes the model it works on from. Only the model a
    /// control point carries is supported.
    /// </summary>
    internal static class ModelInput
    {
        /// <summary>
        /// The control point the model input reads, or -1 when it reads a named value or nothing. A
        /// missing input reads control point 0.
        /// </summary>
        public static int ControlPoint(ParticleDefinitionParser parse, string key = "m_modelInput")
        {
            if (parse.Data.GetSubCollection(key) is not { IsCollection: true } data)
            {
                return 0;
            }

            var input = parse.Nested(data);

            return input.Enum("m_nType", ParticleModelType.PM_TYPE_CONTROL_POINT) == ParticleModelType.PM_TYPE_CONTROL_POINT
                ? Math.Clamp(input.Int32("m_nControlPoint"), 0, ParticleSystemState.MaxControlPoint)
                : -1;
        }
    }

    /// <summary>
    /// Maps a sequence, body part or mesh group through three lists of names of a model: the place
    /// of the held value in <c>m_inNames</c> selects the entry of <c>m_outNames</c>, and a value not
    /// listed takes a random <c>m_fallbackNames</c> entry when there are any.
    /// </summary>
    internal sealed class NamedModelElementRemap
    {
        private readonly ModelElementKind kind;
        private readonly ModelReference model;
        private readonly string[] inNames;
        private readonly string[] outNames;
        private readonly string[] fallbackNames;

        private int[] inIndices = [];
        private int[] outIndices = [];
        private int[] fallbackIndices = [];
        private int inMin;
        private int inMax;

        public NamedModelElementRemap(ParticleDefinitionParser parse, ModelElementKind kind)
        {
            this.kind = kind;
            model = new ModelReference(parse);
            inNames = parse.Data.GetArray<string>("m_inNames") ?? [];
            outNames = parse.Data.GetArray<string>("m_outNames") ?? [];
            fallbackNames = parse.Data.GetArray<string>("m_fallbackNames") ?? [];
        }

        /// <summary>Looks the names up in the model, which the system must be running to load.</summary>
        public void Resolve(ParticleSystemState particleSystemState)
        {
            var resolved = model.Resolve(particleSystemState);
            var fileLoader = particleSystemState.Data?.FileLoader;

            inIndices = ModelElements.Resolve(kind, inNames, resolved, fileLoader);
            outIndices = ModelElements.Resolve(kind, outNames, resolved, fileLoader);
            fallbackIndices = ModelElements.Resolve(kind, fallbackNames, resolved, fileLoader);
            inMin = inIndices.Length > 0 ? inIndices.Min() : int.MaxValue;
            inMax = inIndices.Length > 0 ? inIndices.Max() : 0;
        }

        /// <summary>
        /// The value <paramref name="input"/> maps to. Without fallbacks a value below every listed
        /// one maps to the first output and one above them to the last.
        /// </summary>
        public int Remap(int input, ParticleSystemState particleSystemState)
        {
            int position;

            if (fallbackIndices.Length <= 0)
            {
                position = input < inMin ? 0
                    : input > inMax ? outIndices.Length - 1
                    : IndexOrCount(inIndices, input);
            }
            else
            {
                position = Array.IndexOf(inIndices, input);

                if (position < 0)
                {
                    return fallbackIndices.Length == 1
                        ? fallbackIndices[0]
                        : fallbackIndices[(int)(fallbackIndices.Length * particleSystemState.Random.Next())];
                }
            }

            if (outIndices.Length <= 0)
            {
                return 0;
            }

            if (position < 0 || outIndices.Length == 1)
            {
                return outIndices[0];
            }

            return position < outIndices.Length ? outIndices[position] : outIndices[^1];
        }

        private static int IndexOrCount(int[] indices, int value)
        {
            var position = Array.IndexOf(indices, value);
            return position < 0 ? indices.Length : position;
        }
    }
}
