using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.IO;

public sealed class SnapshotExtract
{
    private readonly ParticleSnapshot snap;

    public SnapshotExtract(ParticleSnapshot snap)
    {
        this.snap = snap;
    }

    public SnapshotExtract(Resource resource)
        : this((ParticleSnapshot)resource.GetBlockByType(BlockType.SNAP))
    {
    }

    public ContentFile ToContentFile()
    {
        var vsnap = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveSnap())
        };

        return vsnap;
    }

    public string ToValveSnap()
    {
        var outKV3 = new KVObject(null);
        var data = new KVObject("");
        data.AddProperty("num_values", snap.NumParticles);

        var streams = new KVObject(null, isArray: true);
        data.AddProperty("streams", streams);

        foreach (var (attribute, attributeStream) in snap.AttributeData)
        {
            var stream = new KVObject(null);
            {
                stream.AddProperty("name", attribute.Name);
                stream.AddProperty("type", attribute.Name switch
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


                var values = new KVObject(null, isArray: true);

                foreach (var datum in attributeStream)
                {
                    if (datum is int i)
                    {
                        values.AddItem(i);
                    }
                    else if (datum is float f)
                    {
                        values.AddItem((double)f);
                    }
                    else if (datum is Vector3 v)
                    {
                        var array = new KVObject(null, isArray: true);
                        {
                            array.AddItem((double)v.X);
                            array.AddItem((double)v.Y);
                            array.AddItem((double)v.Z);
                        }

                        values.AddProperty(null, array);
                    }
                    else if (datum is string s)
                    {
                        values.AddItem(s);
                    }
                    else if (datum is ParticleSnapshot.SkinningData skinning)
                    {
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected snapshot stream type: {datum.GetType()}");
                    }
                }

                stream.AddProperty("values", values);
            }

            streams.AddItem(stream);
        }

        outKV3.AddProperty("stream_data", data);
        return new KV3File(outKV3).ToString();
    }
}
