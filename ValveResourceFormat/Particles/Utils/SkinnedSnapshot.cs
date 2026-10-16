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
        private const ulong UnusedBones = 0xFFFF_FFFF_FFFF_FFFFUL;

        private readonly SnapshotBinding binding;

        /// <param name="parse">The function's definition.</param>
        public SkinnedSnapshot(ParticleDefinitionParser parse)
        {
            binding = new SnapshotBinding(parse, "m_nSnapshotControlPointNumber", 1, clampControlPoint: true);
        }

        /// <summary>The columns of the bound snapshot a skinned read uses.</summary>
        /// <param name="Count">How many points the snapshot holds.</param>
        /// <param name="Positions">Each point's rest position, in the model's space.</param>
        /// <param name="Skinning">Each point's bones and weights.</param>
        /// <param name="Normals">Each point's normal, or null when the snapshot has none.</param>
        /// <param name="Radii">Each point's radius, or null when the snapshot has none.</param>
        /// <param name="Colors">Each point's colour, or null when the snapshot has none.</param>
        /// <param name="Alphas">Each point's opacity, or null when the snapshot has none.</param>
        public readonly record struct Columns(
            int Count,
            Vector3[] Positions,
            ParticleSnapshot.SkinningData[] Skinning,
            Vector3[]? Normals,
            float[]? Radii,
            Vector3[]? Colors,
            float[]? Alphas);

        /// <summary>
        /// The bound snapshot's columns, or null when nothing is bound, the snapshot is empty, or it
        /// lacks positions or skinning.
        /// </summary>
        public Columns? Resolve(ParticleSystemState particleSystemState)
        {
            if (binding.Resolve(particleSystemState) is not { NumParticles: > 0 } snapshot)
            {
                return null;
            }

            Vector3[]? positions = null;
            ParticleSnapshot.SkinningData[]? skinning = null;
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

            if (positions == null || skinning == null)
            {
                return null;
            }

            var count = (int)Math.Min(snapshot.NumParticles, (uint)Math.Min(positions.Length, skinning.Length));
            return count == 0 ? null : new Columns(count, positions, skinning, normals, radii, colors, alphas);
        }

        /// <summary>
        /// The four bones of a snapshot point as indices into the boxes of a bone cache: each named
        /// bone's index in <paramref name="model"/>'s skeleton, 0 when it has none, wrapped to
        /// <paramref name="boneCount"/>. Every slot is unused when there are no bones.
        /// </summary>
        public static ulong BoneIndices(ParticleSnapshot.SkinningData skinning, Model model, int boneCount)
        {
            if (boneCount <= 0)
            {
                return UnusedBones;
            }

            var indices = 0UL;

            for (var k = 0; k < 4; k++)
            {
                var bone = Math.Max(model.Skeleton.GetBoneIndex(skinning.JointNames[k]), 0) % boneCount;
                indices |= (ulong)bone << (16 * k);
            }

            return indices;
        }

        /// <summary>The weights of the first three bones of a snapshot point.</summary>
        public static Vector3 BoneWeights(ParticleSnapshot.SkinningData skinning)
            => new(skinning.Weights[0], skinning.Weights[1], skinning.Weights[2]);
    }
}
