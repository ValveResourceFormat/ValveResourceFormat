using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// A control point snapshot whose points are skinned to bones by name, such as one taken off a
    /// model's surface, read by the functions that deform it with the bones of a control point's
    /// model.
    /// </summary>
    internal sealed class SkinnedSnapshot
    {
        private readonly SnapshotBinding binding;

        /// <param name="parse">The function's definition.</param>
        public SkinnedSnapshot(ParticleDefinitionParser parse)
        {
            binding = new SnapshotBinding(parse, "m_nSnapshotControlPointNumber", 1, clampControlPoint: true);
        }

        /// <summary>The columns of the bound snapshot a skinned read uses.</summary>
        /// <param name="Count">How many points the snapshot holds.</param>
        /// <param name="Positions">Each point's rest position, in the model's space.</param>
        /// <param name="Skinning">Each point's bones and weights, or null when the points name one hitbox each instead.</param>
        /// <param name="Hitboxes">Each point's one bone, taken whole, read when there is no <paramref name="Skinning"/>.</param>
        /// <param name="Normals">Each point's normal, or null when the snapshot has none.</param>
        /// <param name="Radii">Each point's radius, or null when the snapshot has none.</param>
        /// <param name="Colors">Each point's colour, or null when the snapshot has none.</param>
        /// <param name="Alphas">Each point's opacity, or null when the snapshot has none.</param>
        public readonly record struct Columns(
            int Count,
            Vector3[] Positions,
            ParticleSnapshot.SkinningData[]? Skinning,
            string[]? Hitboxes,
            Vector3[]? Normals,
            float[]? Radii,
            Vector3[]? Colors,
            float[]? Alphas)
        {
            /// <summary>
            /// Binds <paramref name="particle"/> to the bones of point <paramref name="row"/>, keeping the
            /// point's rest position for the functions that keep it skinned.
            /// </summary>
            /// <param name="particle">The particle to bind.</param>
            /// <param name="row">The snapshot point.</param>
            /// <param name="model">The model whose skeleton names the bones.</param>
            /// <param name="boneCount">How many boxes the bone cache holds.</param>
            public void Bind(ref Particle particle, int row, Model model, int boneCount)
            {
                particle.BoneIndices = BoneIndices(row, model, boneCount);
                particle.HitboxOffsetPosition = Positions[row];
                particle.BoneWeights = BoneWeights(row);
            }

            /// <summary>
            /// The four bones of point <paramref name="row"/> as indices into the boxes of a bone cache:
            /// each named bone's index in <paramref name="model"/>'s skeleton, 0 when it has none,
            /// wrapped to <paramref name="boneCount"/>. A point naming one hitbox leaves the other
            /// three slots unused, and every slot is unused when there are no bones.
            /// </summary>
            public ulong BoneIndices(int row, Model model, int boneCount)
            {
                if (boneCount <= 0)
                {
                    return PackedBones.AllNone;
                }

                if (Skinning == null)
                {
                    return PackedBones.Pack(BoneIndex(Hitboxes![row], model, boneCount), 0)
                        | PackedBones.Pack(PackedBones.None, 1) | PackedBones.Pack(PackedBones.None, 2) | PackedBones.Pack(PackedBones.None, 3);
                }

                var indices = 0UL;
                var jointNames = Skinning[row].JointNames;

                for (var k = 0; k < PackedBones.Count; k++)
                {
                    indices |= PackedBones.Pack(BoneIndex(jointNames[k], model, boneCount), k);
                }

                return indices;
            }

            /// <summary>The weights of the first three bones of point <paramref name="row"/>.</summary>
            public Vector3 BoneWeights(int row)
            {
                if (Skinning == null)
                {
                    return Vector3.UnitX;
                }

                var weights = Skinning[row].Weights;
                return new(weights[0], weights[1], weights[2]);
            }

            private static int BoneIndex(string name, Model model, int boneCount)
                => Math.Max(model.Skeleton.GetBoneIndex(name), 0) % boneCount;
        }

        /// <summary>
        /// The bound snapshot's columns, or null when nothing is bound, the snapshot is empty, or it
        /// lacks positions or both skinning and hitbox names.
        /// </summary>
        public Columns? Resolve(ParticleSystemState particleSystemState)
        {
            if (binding.Resolve(particleSystemState) is not { NumParticles: > 0 } snapshot)
            {
                return null;
            }

            Vector3[]? positions = null;
            ParticleSnapshot.SkinningData[]? skinning = null;
            string[]? hitboxes = null;
            Vector3[]? normals = null;
            float[]? radii = null;
            Vector3[]? colors = null;
            float[]? alphas = null;

            foreach (var ((name, _), data) in snapshot.AttributeData)
            {
                switch (data)
                {
                    case ParticleSnapshot.SkinningData[] skin:
                        skinning = skin;
                        break;
                    case string[] names when name == ParticleSnapshot.GetSnapshotAttributeName(ParticleField.HitboxIndex):
                        hitboxes = names;
                        break;
                    case Vector3[] vectors when name == ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Position):
                        positions = vectors;
                        break;
                    case Vector3[] vectors when name == ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Normal):
                        normals = vectors;
                        break;
                    case Vector3[] vectors when name == ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Color):
                        colors = vectors;
                        break;
                    case float[] floats when name == ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Radius):
                        radii = floats;
                        break;
                    case float[] floats when name == ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Alpha):
                        alphas = floats;
                        break;
                }
            }

            var boneRows = skinning?.Length ?? hitboxes?.Length;

            if (positions == null || boneRows == null)
            {
                return null;
            }

            var count = (int)Math.Min(snapshot.NumParticles, (uint)Math.Min(positions.Length, boneRows.Value));
            return count == 0 ? null : new Columns(count, positions, skinning, hitboxes, normals, radii, colors, alphas);
        }

        /// <summary>
        /// A point picked at random among <paramref name="count"/>: a draw from the system's sequence, or
        /// with a <paramref name="randomSeed"/> other than 0 from a sequence of the function's own, which
        /// <paramref name="randomCounter"/> walks.
        /// </summary>
        public static int RandomRow(ParticleSystemState particleSystemState, int count, int randomSeed, ref int randomCounter)
        {
            return (int)(count * particleSystemState.Random.NextOrSeeded(randomSeed, ref randomCounter));
        }

        /// <summary>
        /// The point a walk at <paramref name="index"/> reads: past the last of <paramref name="count"/>
        /// points it wraps round, moved on by <paramref name="fullLoopIncrement"/> for every loop made.
        /// </summary>
        public static int WalkRow(int index, int count, int fullLoopIncrement)
            => index >= count ? (index + (fullLoopIncrement * (index / count))) % count : index;
    }
}
