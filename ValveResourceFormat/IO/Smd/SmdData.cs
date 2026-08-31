#pragma warning disable CS1591, CA1063, CA1822
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.IO.Smd;

public class SmdData
{
    public string Name { get; set; } = "Unnamed";
    public SmdType Type { get; set; }
    public Dictionary<string, Bone> Bones { get; } = new();
    public List<List<KeyFrame>> Frames { get; } = new();
    public List<string> Materials { get; } = new();
    public List<Triangle> Meshes { get; } = new();
    public List<List<FlexVertex>> FlexFrames { get; } = new();
    protected int CurrentBoneIndex { get; set; }

    public void Read(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var smdImporter = new SmdImporter(stream);
        smdImporter.Parse(this);
    }

    public void Read(string filename)
    {
        Name = Path.GetFileNameWithoutExtension(filename);
        using var smdImporter = new SmdImporter(filename);
        smdImporter.Parse(this);
    }

    public int AddBone(string parentBoneName, string boneName)
    {
        if (!Bones.TryGetValue(boneName, out var bone))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(parentBoneName) && Bones.TryGetValue(parentBoneName, out var parentBone))
                {
                    Bones[boneName] = new Bone(parentBone.Index, CurrentBoneIndex);
                }
                else
                {
                    Bones[boneName] = new Bone(-1, CurrentBoneIndex);
                }
                return CurrentBoneIndex;
            }
            finally
            {
                CurrentBoneIndex++;
            }
        }
        return bone.Index;
    }

    public int AddMaterial(string material)
    {
        var idx = Materials.IndexOf(material);
        if (idx == -1)
        {
            Materials.Add(material);
            idx = Materials.Count - 1;
        }
        return idx;
    }

    public Bone GetBoneByIndex(int index)
    {
        return Bones.ElementAt(index).Value;
    }

    public void Write(string fileName)
    {
        using var smdExporter = new SmdExporter(fileName);
        smdExporter.WriteData(this);
    }

    public byte[] ToBytes()
    {
        using var smdExporter = new SmdExporter();
        smdExporter.WriteData(this);
        return System.Text.Encoding.UTF8.GetBytes(smdExporter.ToString());
    }

    public override string ToString()
    {
        using var smdExporter = new SmdExporter();
        smdExporter.WriteData(this);
        return smdExporter.ToString();
    }

    public readonly record struct Bone(int ParentIndex, int Index);
    public readonly record struct KeyFrame(int BoneID, Vector3 Position, Vector3 Rotation);
    public readonly record struct Weight(int BoneID, float Value);
    public readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector2 UV, Weight[] Weights);
    public readonly record struct Triangle(int MaterialIndex, Vertex A, Vertex B, Vertex C);
    public readonly record struct FlexVertex(int VertexId, Vector3 Position, Vector3 Normal);
}
