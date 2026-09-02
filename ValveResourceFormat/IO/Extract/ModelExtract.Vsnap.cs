using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the particle snapshots a model references.
/// </summary>
partial class ModelExtract
{

    /// <summary>
    /// Rebuilds a VSNAPEmpty node for every particle snapshot the model references.
    /// </summary>
    private void AddVsnapNodes(ModelDocLists lists)
    {
        if (modelResource is null || fileLoader is null)
        {
            return;
        }

        var externalReferences = modelResource.ExternalReferences?.ResourceRefInfoList;

        if (externalReferences is null)
        {
            return;
        }

        foreach (var reference in externalReferences)
        {
            if (!reference.Name.EndsWith(".vsnap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var vsnapResource = fileLoader.LoadFileCompiled(reference.Name);

            if (vsnapResource?.GetBlockByType(BlockType.SNAP) is not ParticleSnapshot snapshot)
            {
                continue;
            }

            snapshot.AttributeData.TryGetValue(("position", "float3"), out var positionData);
            snapshot.AttributeData.TryGetValue(("skinning", "skinning"), out var skinningData);

            var positions = positionData as Vector3[];
            var skinning = skinningData as ParticleSnapshot.SkinningData[];
            var particles = KVObject.Array();

            for (var i = 0; i < snapshot.NumParticles; i++)
            {
                var particle = MakeNode("VSNAPParticle", ("origin", ToKVArray(positions?[i] ?? Vector3.Zero)));

                if (skinning is not null)
                {
                    var boneSlot = 0;
                    var bones = skinning[i];

                    for (var j = 0; j < bones.Weights.Length && boneSlot < 4; j++)
                    {
                        if (bones.Weights[j] <= 0f || string.IsNullOrEmpty(bones.JointNames[j]))
                        {
                            continue;
                        }

                        particle.Add($"bone_{boneSlot}", bones.JointNames[j]);
                        particle.Add($"bone_weight_{boneSlot}", bones.Weights[j]);
                        boneSlot++;
                    }
                }

                particles.Add(particle);
            }

            if (particles.Count == 0)
            {
                // The compiler rejects a VSNAPEmpty with no particles.
                continue;
            }

            var vsnapNode = MakeNode("VSNAPEmpty",
                ("name", Path.GetFileNameWithoutExtension(reference.Name)),
                ("children", particles)
            );
            vsnapNode.Add("output_vsnap", new KVObject(reference.Name) { Flag = KVFlag.Resource });

            lists.Vsnaps.Add(vsnapNode);
        }
    }
}
