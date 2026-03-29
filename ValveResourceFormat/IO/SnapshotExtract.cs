using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 particle snapshots to editable vsnap format.
/// </summary>
public sealed class SnapshotExtract
{
    private readonly ParticleSnapshot snap;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotExtract"/> class.
    /// </summary>
    public SnapshotExtract(ParticleSnapshot snap)
    {
        this.snap = snap;
    }

    /// <inheritdoc cref="SnapshotExtract(ParticleSnapshot)"/>
    public SnapshotExtract(Resource resource)
        : this((ParticleSnapshot)resource.GetBlockByType(BlockType.SNAP)!)
    {
    }

    /// <summary>
    /// Converts the snapshot to a content file.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var vsnap = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveSnap())
        };

        return vsnap;
    }

    /// <summary>
    /// Converts the snapshot to vsnap format string.
    /// </summary>
    public string ToValveSnap()
    {
        var outKV3 = new KVObject(null);
        var data = new KVObject("");
        data.Add("num_values", snap.NumParticles);

        var streams = KVObject.Array(null);
        data.Add("streams", streams.Value);

        foreach (var (attribute, attributeStream) in snap.AttributeData)
        {
            var stream = new KVObject(null);
            {
                stream.Add("name", attribute.Name);
                stream.Add("type", attribute.Name switch
                {
                    "position" => "position_3d",
                    "normal" => "normal_3d",
                    _ => attribute.Type switch
                    {
                        "int" => "generic_int",
                        "float" => "generic_float",
                        "float3" or "vector" => "generic_vector_3d",
                        "skinning" => "bone_index_and_weight",
                        _ => attribute.Type,
                    },
                });


                var values = KVObject.Array(null);

                foreach (var datum in attributeStream)
                {
                    if (datum is int i)
                    {
                        values.Add((KVValue)i);
                    }
                    else if (datum is float f)
                    {
                        values.Add((KVValue)(double)f);
                    }
                    else if (datum is Vector3 v)
                    {
                        var array = KVObject.Array(null);
                        {
                            array.Add((KVValue)(double)v.X);
                            array.Add((KVValue)(double)v.Y);
                            array.Add((KVValue)(double)v.Z);
                        }

                        values.Add(array.Value);
                    }
                    else if (datum is string s)
                    {
                        values.Add((KVValue)s);
                    }
                    else if (datum is ParticleSnapshot.SkinningData skinning)
                    {
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected snapshot stream type: {datum.GetType()}");
                    }
                }

                stream.Add("values", values.Value);
            }

            streams.Add(stream.Value);
        }

        outKV3.Add("stream_data", data.Value);
        return new KV3File(outKV3).ToString();
    }
}
