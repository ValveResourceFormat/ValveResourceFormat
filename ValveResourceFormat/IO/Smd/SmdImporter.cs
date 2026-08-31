#pragma warning disable CS1591, CA1063, CA1822
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ValveResourceFormat.IO.Smd;

public sealed class SmdImporter : IDisposable
{
    private readonly TextReader reader;

    public SmdImporter(string filename)
    {
        reader = new StreamReader(filename);
    }

    public SmdImporter(Stream stream)
    {
        reader = new StreamReader(stream);
    }

    public void Parse(SmdData data)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Equals("nodes", StringComparison.OrdinalIgnoreCase))
            {
                ReadNodes(data);
            }
            else if (line.Equals("skeleton", StringComparison.OrdinalIgnoreCase))
            {
                ReadSkeleton(data);
            }
            else if (line.Equals("triangles", StringComparison.OrdinalIgnoreCase))
            {
                ReadTriangles(data);
            }
        }
    }

    private void ReadNodes(SmdData data)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var idx = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                var name = parts[1].Trim();
                var parentIdx = int.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                data.Bones[name] = new SmdData.Bone(parentIdx, idx);
            }
        }
    }

    private void ReadSkeleton(SmdData data)
    {
        string? line;
        List<SmdData.KeyFrame>? currentFrame = null;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (line.StartsWith("time", StringComparison.OrdinalIgnoreCase))
            {
                currentFrame = new List<SmdData.KeyFrame>();
                data.Frames.Add(currentFrame);
                continue;
            }

            if (currentFrame != null)
            {
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 7)
                {
                    var boneId = int.Parse(tokens[0], CultureInfo.InvariantCulture);
                    var pos = new Vector3(
                        float.Parse(tokens[1], CultureInfo.InvariantCulture),
                        float.Parse(tokens[2], CultureInfo.InvariantCulture),
                        float.Parse(tokens[3], CultureInfo.InvariantCulture));
                    var rot = new Vector3(
                        float.Parse(tokens[4], CultureInfo.InvariantCulture),
                        float.Parse(tokens[5], CultureInfo.InvariantCulture),
                        float.Parse(tokens[6], CultureInfo.InvariantCulture));
                    currentFrame.Add(new SmdData.KeyFrame(boneId, pos, rot));
                }
            }
        }
    }

    private void ReadTriangles(SmdData data)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var material = line;
            var matIdx = data.AddMaterial(material);

            var v1 = ReadVertex();
            var v2 = ReadVertex();
            var v3 = ReadVertex();

            if (v1.HasValue && v2.HasValue && v3.HasValue)
            {
                data.Meshes.Add(new SmdData.Triangle(matIdx, v1.Value, v2.Value, v3.Value));
            }
        }
    }

    private SmdData.Vertex? ReadVertex()
    {
        var line = reader.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 9)
        {
            return null;
        }

        var pos = new Vector3(
            float.Parse(tokens[1], CultureInfo.InvariantCulture),
            float.Parse(tokens[2], CultureInfo.InvariantCulture),
            float.Parse(tokens[3], CultureInfo.InvariantCulture));
        var norm = new Vector3(
            float.Parse(tokens[4], CultureInfo.InvariantCulture),
            float.Parse(tokens[5], CultureInfo.InvariantCulture),
            float.Parse(tokens[6], CultureInfo.InvariantCulture));
        var uv = new Vector2(
            float.Parse(tokens[7], CultureInfo.InvariantCulture),
            float.Parse(tokens[8], CultureInfo.InvariantCulture));

        var weights = new List<SmdData.Weight>();
        if (tokens.Length > 9)
        {
            var numWeights = int.Parse(tokens[9], CultureInfo.InvariantCulture);
            for (var i = 0; i < numWeights && 10 + i * 2 + 1 < tokens.Length; i++)
            {
                var boneId = int.Parse(tokens[10 + i * 2], CultureInfo.InvariantCulture);
                var weightVal = float.Parse(tokens[10 + i * 2 + 1], CultureInfo.InvariantCulture);
                weights.Add(new SmdData.Weight(boneId, weightVal));
            }
        }

        return new SmdData.Vertex(pos, norm, uv, weights.ToArray());
    }

    public void Dispose()
    {
        reader.Dispose();
        GC.SuppressFinalize(this);
    }
}
