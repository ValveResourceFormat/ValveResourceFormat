#pragma warning disable CS1591
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO.Smd;

public sealed class SmdExporter : IDisposable
{
    private readonly string? outputFileName;
    public IndentedTextWriter TextWriter { get; }

    public SmdExporter()
    {
        TextWriter = new IndentedTextWriter();
    }

    public SmdExporter(string filename)
    {
        outputFileName = filename;
        TextWriter = new IndentedTextWriter();
    }

    public void WriteData(SmdData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        TextWriter.WriteLine("version 1");
        WriteNodes(data.Bones);
        WriteSkeleton(data.Frames);
        WriteTriangles(data);
    }

    private void WriteNodes(Dictionary<string, SmdData.Bone> bones)
    {
        TextWriter.WriteLine("nodes");
        if (bones.Count > 0)
        {
            foreach (var (name, bone) in bones)
            {
                TextWriter.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,3} {1,-24} {2,3}", bone.Index, $"\"{name}\"", bone.ParentIndex));
            }
        }
        else
        {
            TextWriter.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,3} {1,-24} {2,3}", 0, "\"root\"", -1));
        }
        TextWriter.WriteLine("end");
    }

    private void WriteSkeleton(List<List<SmdData.KeyFrame>> frames)
    {
        TextWriter.WriteLine("skeleton");
        if (frames.Count > 0)
        {
            for (var i = 0; i < frames.Count; i++)
            {
                TextWriter.WriteLine(string.Format(CultureInfo.InvariantCulture, "time {0}", i));
                foreach (var keyFrame in frames[i])
                {
                    TextWriter.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,3} {1,12:F6} {2,12:F6} {3,12:F6} {4,12:F6} {5,12:F6} {6,12:F6}",
                        keyFrame.BoneID,
                        keyFrame.Position.X, keyFrame.Position.Y, keyFrame.Position.Z,
                        keyFrame.Rotation.X, keyFrame.Rotation.Y, keyFrame.Rotation.Z));
                }
            }
        }
        else
        {
            TextWriter.WriteLine("time 0");
            TextWriter.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,3} {1,12:F6} {2,12:F6} {3,12:F6} {4,12:F6} {5,12:F6} {6,12:F6}", 0, 0f, 0f, 0f, 0f, 0f, 0f));
        }
        TextWriter.WriteLine("end");
    }

    private void WriteTriangles(SmdData data)
    {
        if (data.Meshes.Count > 0)
        {
            TextWriter.WriteLine("triangles");
            foreach (var triangle in data.Meshes)
            {
                TextWriter.WriteLine(data.Materials[triangle.MaterialIndex]);
                WriteVertex(triangle.A);
                WriteVertex(triangle.B);
                WriteVertex(triangle.C);
            }
            TextWriter.WriteLine("end");
        }
    }

    private void WriteVertex(SmdData.Vertex vertex)
    {
        TextWriter.Write(" 0 ");
        TextWriter.Write(string.Format(CultureInfo.InvariantCulture, "{0,8:F6} {1,8:F6} {2,8:F6} ", vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
        TextWriter.Write(string.Format(CultureInfo.InvariantCulture, "{0,8:F6} {1,8:F6} ", vertex.Normal.X, vertex.Normal.Y));
        TextWriter.Write(string.Format(CultureInfo.InvariantCulture, "{0,8:F6} ", vertex.Normal.Z));
        TextWriter.Write(string.Format(CultureInfo.InvariantCulture, "{0,8:F6} {1,8:F6} ", vertex.UV.X, vertex.UV.Y));
        TextWriter.Write(vertex.Weights.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var weight in vertex.Weights)
        {
            TextWriter.Write(string.Format(CultureInfo.InvariantCulture, " {0} {1,8:F6}", weight.BoneID, weight.Value));
        }
        TextWriter.WriteLine();
    }

    public override string ToString()
    {
        return TextWriter.ToString();
    }

    public void Dispose()
    {
        if (outputFileName != null)
        {
            File.WriteAllText(outputFileName, TextWriter.ToString(), System.Text.Encoding.UTF8);
        }
        TextWriter.Dispose();
        GC.SuppressFinalize(this);
    }
}
