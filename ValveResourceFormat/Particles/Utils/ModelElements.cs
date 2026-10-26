using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
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
        private static readonly ModelCache<AABB> PhysicsBoundsCache = new(ReadPhysicsBounds);
        private static readonly ModelCache<float> PhysicsVolumeCache = new(ReadPhysicsVolume);

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

        /// <summary>
        /// The box around the model's physics shapes, each placed by its part's bind pose, in the model's
        /// own space. A model with no physics shapes gives its stored view bounds, or its hull bounds
        /// when those are zero.
        /// </summary>
        public static AABB PhysicsBounds(Model model, IFileLoader? fileLoader) => PhysicsBoundsCache.Get(model, fileLoader);

        /// <summary>
        /// The summed volume of the model's physics spheres, capsules and hulls, in the model's own space.
        /// Zero when there are none.
        /// </summary>
        public static float PhysicsVolume(Model model, IFileLoader? fileLoader) => PhysicsVolumeCache.Get(model, fileLoader);

        private static PhysAggregateData? LoadPhysics(Model model, IFileLoader? fileLoader)
        {
            var embedded = model.GetEmbeddedPhys();

            if (embedded != null)
            {
                return embedded;
            }

            var referenced = model.GetReferencedPhysNames().FirstOrDefault();

            if (referenced == null)
            {
                return null;
            }

            return fileLoader?.LoadFileCompiled(referenced)?.DataBlock as PhysAggregateData;
        }

        private static AABB ReadPhysicsBounds(Model model, IFileLoader? fileLoader)
        {
            var physics = LoadPhysics(model, fileLoader);
            AABB? bounds = null;

            if (physics != null)
            {
                var parts = physics.Parts;
                var bindPoses = physics.BindPose;

                for (var partIndex = 0; partIndex < parts.Length; partIndex++)
                {
                    if (ShapeBounds(parts[partIndex].Shape) is not { } partBounds)
                    {
                        continue;
                    }

                    if (partIndex < bindPoses.Length)
                    {
                        partBounds = partBounds.Transform(bindPoses[partIndex]);
                    }

                    bounds = bounds?.Union(partBounds) ?? partBounds;
                }
            }

            if (bounds is { } physicsBounds && physicsBounds != default)
            {
                return physicsBounds;
            }

            var viewBounds = StoredBounds(model, "m_vViewMin", "m_vViewMax");

            if (viewBounds != default)
            {
                return viewBounds;
            }

            return StoredBounds(model, "m_vHullMin", "m_vHullMax");
        }

        private static AABB StoredBounds(Model model, string minKey, string maxKey)
        {
            var min = model.Data.GetSubCollection(minKey);
            var max = model.Data.GetSubCollection(maxKey);

            if (min == null || max == null)
            {
                return default;
            }

            return new AABB(min.ToVector3(), max.ToVector3());
        }

        private static AABB? ShapeBounds(Shape shape)
        {
            AABB? bounds = null;

            foreach (var sphere in shape.GetAllSpheres())
            {
                var sphereBounds = new AABB(sphere.Shape.Center, sphere.Shape.Radius);
                bounds = bounds?.Union(sphereBounds) ?? sphereBounds;
            }

            foreach (var capsule in shape.GetAllCapsules())
            {
                var radius = new Vector3(capsule.Shape.Radius);

                foreach (var center in capsule.Shape.Center)
                {
                    var endBounds = new AABB(center - radius, center + radius);
                    bounds = bounds?.Union(endBounds) ?? endBounds;
                }
            }

            foreach (var hull in shape.GetAllHulls())
            {
                var hullBounds = new AABB(hull.Shape.Min, hull.Shape.Max);
                bounds = bounds?.Union(hullBounds) ?? hullBounds;
            }

            foreach (var mesh in shape.GetAllMeshes())
            {
                var meshBounds = new AABB(mesh.Shape.Min, mesh.Shape.Max);
                bounds = bounds?.Union(meshBounds) ?? meshBounds;
            }

            return bounds;
        }

        private static float ReadPhysicsVolume(Model model, IFileLoader? fileLoader)
        {
            var physics = LoadPhysics(model, fileLoader);

            if (physics == null)
            {
                return 0f;
            }

            var volume = 0f;

            foreach (var part in physics.Parts)
            {
                foreach (var sphere in part.Shape.Spheres)
                {
                    var radius = sphere.Shape.Radius;
                    volume += 4f / 3f * MathF.PI * radius * radius * radius;
                }

                foreach (var capsule in part.Shape.Capsules)
                {
                    var radius = capsule.Shape.Radius;
                    var centers = capsule.Shape.Center;
                    var length = centers.Length >= 2 ? Vector3.Distance(centers[0], centers[1]) : 0f;
                    volume += MathF.PI * radius * radius * (length + (4f / 3f * radius));
                }

                foreach (var hull in part.Shape.Hulls)
                {
                    volume += hull.Shape.Volume;
                }
            }

            return volume;
        }

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

        /// <summary>The smallest of <paramref name="indices"/> and the largest of them, which is never below 0; both are 0 when there are none.</summary>
        public static (int Min, int Max) Range(int[] indices)
            => indices.Length > 0 ? (indices.Min(), Math.Max(indices.Max(), 0)) : (0, 0);

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
