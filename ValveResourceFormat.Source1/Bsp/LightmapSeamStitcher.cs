using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Blends the luxels of coplanar faces that share an edge towards each other, so the lightmap is continuous across the
/// edge instead of showing a line where two separately lit luxel grids meet.
/// </summary>
internal static class LightmapSeamStitcher
{
    /// <summary>How far from the shared edge, in luxels, a luxel is blended with the neighbour.</summary>
    private const float BlendRadius = 2f;

    /// <summary>The largest share of the neighbours in a blended luxel, which keeps the own light dominant at corners.</summary>
    private const float MaxBlend = 0.75f;

    private const float MinNormalDot = 0.999f;

    private readonly record struct SharedEdge(int Face, int Neighbor, Vector3 Start, Vector3 End);

    /// <summary>A blended luxel. Page is <see cref="AllPages"/> for a luxel shared by every page.</summary>
    private readonly record struct PendingLuxel(int Page, int Index, Vector4 Value);

    private const int AllPages = -1;

    /// <summary>The atlas indices of the four luxels around a sample point, and where the point lies between them.</summary>
    private readonly record struct BilinearTap(int Index00, int Index10, int Index01, int Index11, float FractionX, float FractionY);

    /// <summary>Stitches the atlas.</summary>
    public static void Stitch(BspFile bsp, BspFace[] faces, LightmapLayout layout, Half[][] pages, int atlasWidth)
    {
        var stitchable = new bool[faces.Length];

        for (var i = 0; i < faces.Length; i++)
        {
            var rect = layout.GetLightmapRegion(i);
            stitchable[i] = IsStitchable(bsp, faces[i], rect);
        }

        var sharedEdges = FindSharedEdges(bsp, faces, stitchable);

        if (sharedEdges.Count == 0)
        {
            return;
        }

        sharedEdges.Sort((a, b) => a.Face.CompareTo(b.Face));

        var pending = new List<PendingLuxel>();
        var touchedFaces = new List<int>();
        var start = 0;

        while (start < sharedEdges.Count)
        {
            var faceIndex = sharedEdges[start].Face;
            var end = start;

            while (end < sharedEdges.Count && sharedEdges[end].Face == faceIndex)
            {
                end++;
            }

            var group = CollectionsMarshal.AsSpan(sharedEdges).Slice(start, end - start);

            if (BlendFace(bsp, faces, layout, pages, atlasWidth, faceIndex, group, pending))
            {
                touchedFaces.Add(faceIndex);
            }

            start = end;
        }

        foreach (var luxel in pending)
        {
            if (luxel.Page != AllPages)
            {
                WriteLuxel(pages[luxel.Page], luxel.Index, luxel.Value);
                continue;
            }

            foreach (var page in pages)
            {
                WriteLuxel(page, luxel.Index, luxel.Value);
            }
        }

        foreach (var faceIndex in touchedFaces)
        {
            var rect = layout.GetLightmapRegion(faceIndex);

            foreach (var page in pages)
            {
                RepadBorder(page, rect, atlasWidth);
            }
        }
    }

    private static bool IsStitchable(BspFile bsp, BspFace face, LightmapRect rect) =>
        face.HasLightmap
        && face.DispInfo < 0
        && face.NumEdges >= 3
        && rect.Width > 0
        && rect.Height > 0
        && face.PlaneNum < bsp.Planes.Length
        && face.TexInfo >= 0
        && face.TexInfo < bsp.TextureInfos.Length;

    private static Vector3 GetFaceNormal(BspFile bsp, BspFace face)
    {
        var normal = bsp.Planes[face.PlaneNum].Normal;
        return face.Side != 0 ? -normal : normal;
    }

    private static List<SharedEdge> FindSharedEdges(BspFile bsp, BspFace[] faces, bool[] stitchable)
    {
        var sharedEdges = new List<SharedEdge>();
        var firstFaceOfEdge = new Dictionary<int, int>();
        var surfEdges = bsp.SurfEdges;
        var edges = bsp.Edges;
        var vertices = bsp.Vertices;

        for (var faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            if (!stitchable[faceIndex])
            {
                continue;
            }

            var face = faces[faceIndex];

            for (var i = 0; i < face.NumEdges; i++)
            {
                var surfEdge = surfEdges[face.FirstEdge + i];
                var edgeIndex = Math.Abs(surfEdge);

                if (!firstFaceOfEdge.TryGetValue(edgeIndex, out var otherIndex))
                {
                    firstFaceOfEdge.Add(edgeIndex, faceIndex);
                    continue;
                }

                var normalDot = Vector3.Dot(GetFaceNormal(bsp, face), GetFaceNormal(bsp, faces[otherIndex]));

                if (otherIndex == faceIndex || normalDot < MinNormalDot)
                {
                    continue;
                }

                var edge = edges[edgeIndex];
                var start = vertices[edge.A];
                var end = vertices[edge.B];

                sharedEdges.Add(new SharedEdge(faceIndex, otherIndex, start, end));
                sharedEdges.Add(new SharedEdge(otherIndex, faceIndex, start, end));
            }
        }

        return sharedEdges;
    }

    private static bool BlendFace(BspFile bsp, BspFace[] faces, LightmapLayout layout, Half[][] pages, int atlasWidth,
        int faceIndex, ReadOnlySpan<SharedEdge> edges, List<PendingLuxel> pending)
    {
        var face = faces[faceIndex];
        var rect = layout.GetLightmapRegion(faceIndex);
        var texInfo = bsp.TextureInfos[face.TexInfo];
        var plane = bsp.Planes[face.PlaneNum];
        var axes = new Matrix4x4(
            texInfo.LightmapUAxis.Normal.X, texInfo.LightmapVAxis.Normal.X, plane.Normal.X, 0f,
            texInfo.LightmapUAxis.Normal.Y, texInfo.LightmapVAxis.Normal.Y, plane.Normal.Y, 0f,
            texInfo.LightmapUAxis.Normal.Z, texInfo.LightmapVAxis.Normal.Z, plane.Normal.Z, 0f,
            0f, 0f, 0f, 1f);

        if (!Matrix4x4.Invert(axes, out var luxelToWorld))
        {
            return false;
        }

        var pageCount = layout.IsBumped(faceIndex) ? LightmapAtlas.PageCount : 1;
        var luxelCount = rect.Width * rect.Height;
        var weights = new float[luxelCount];
        var neighborSums = new Vector4[pageCount][];

        for (var page = 0; page < pageCount; page++)
        {
            neighborSums[page] = new Vector4[luxelCount];
        }

        var offset = new Vector2(face.LightMapOffsetX, face.LightMapOffsetY);

        foreach (var edge in edges)
        {
            var neighbor = faces[edge.Neighbor];
            var neighborRect = layout.GetLightmapRegion(edge.Neighbor);
            var neighborInfo = bsp.TextureInfos[neighbor.TexInfo];
            var neighborOffset = new Vector2(neighbor.LightMapOffsetX, neighbor.LightMapOffsetY);
            var neighborIsBumped = layout.IsBumped(edge.Neighbor);

            var edgeStart = GetLuxelPosition(texInfo, edge.Start, offset);
            var edgeEnd = GetLuxelPosition(texInfo, edge.End, offset);
            var minX = Math.Max((int)MathF.Floor(MathF.Min(edgeStart.X, edgeEnd.X) - BlendRadius), 0);
            var maxX = Math.Min((int)MathF.Ceiling(MathF.Max(edgeStart.X, edgeEnd.X) + BlendRadius), rect.Width - 1);
            var minY = Math.Max((int)MathF.Floor(MathF.Min(edgeStart.Y, edgeEnd.Y) - BlendRadius), 0);
            var maxY = Math.Min((int)MathF.Ceiling(MathF.Max(edgeStart.Y, edgeEnd.Y) + BlendRadius), rect.Height - 1);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var distance = DistanceToSegment(new Vector2(x, y), edgeStart, edgeEnd);

                    if (distance >= BlendRadius)
                    {
                        continue;
                    }

                    var weight = 0.5f * (1f - (distance / BlendRadius));
                    var projected = new Vector3(
                        x + offset.X - texInfo.LightmapUAxis.Offset,
                        y + offset.Y - texInfo.LightmapVAxis.Offset,
                        plane.Dist);
                    var position = Vector3.Transform(projected, luxelToWorld);
                    var neighborLuxel = GetLuxelPosition(neighborInfo, position, neighborOffset);
                    var index = x + (y * rect.Width);

                    weights[index] += weight;

                    var tap = GetBilinearTap(neighborRect, atlasWidth, neighborLuxel);

                    if (neighborIsBumped)
                    {
                        for (var page = 0; page < pageCount; page++)
                        {
                            var sample = SampleBilinear(pages[page], tap);
                            neighborSums[page][index] += weight * sample;
                        }
                    }
                    else
                    {
                        var weighted = weight * SampleBilinear(pages[0], tap);

                        for (var page = 0; page < pageCount; page++)
                        {
                            neighborSums[page][index] += weighted;
                        }
                    }
                }
            }
        }

        var changed = false;

        for (var index = 0; index < luxelCount; index++)
        {
            var weight = weights[index];

            if (weight <= 0f)
            {
                continue;
            }

            var scale = weight > MaxBlend ? MaxBlend / weight : 1f;
            var blend = weight * scale;
            var x = index % rect.Width;
            var y = index / rect.Width;
            var atlasIndex = ((rect.X + x) + ((rect.Y + y) * atlasWidth)) * 4;

            for (var page = 0; page < pageCount; page++)
            {
                var own = ReadLuxel(pages[page], atlasIndex);
                var blended = (own * (1f - blend)) + (neighborSums[page][index] * scale);

                var pendingPage = pageCount == 1 ? AllPages : page;
                pending.Add(new PendingLuxel(pendingPage, atlasIndex, blended));
            }

            changed = true;
        }

        return changed;
    }

    private static Vector2 GetLuxelPosition(BspTextureInfo texInfo, Vector3 position, Vector2 offset) =>
        new(texInfo.LightmapUAxis.GetCoordinate(position) - offset.X, texInfo.LightmapVAxis.GetCoordinate(position) - offset.Y);

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();

        if (lengthSquared < 1e-8f)
        {
            return Vector2.Distance(point, start);
        }

        var t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, start + (segment * t));
    }

    private static Vector4 ReadLuxel(Half[] page, int index) =>
        new((float)page[index], (float)page[index + 1], (float)page[index + 2], (float)page[index + 3]);

    private static BilinearTap GetBilinearTap(LightmapRect rect, int atlasWidth, Vector2 luxel)
    {
        var x = Math.Clamp(luxel.X, 0f, rect.Width - 1f);
        var y = Math.Clamp(luxel.Y, 0f, rect.Height - 1f);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, rect.Width - 1);
        var y1 = Math.Min(y0 + 1, rect.Height - 1);

        return new BilinearTap(
            ((rect.X + x0) + ((rect.Y + y0) * atlasWidth)) * 4,
            ((rect.X + x1) + ((rect.Y + y0) * atlasWidth)) * 4,
            ((rect.X + x0) + ((rect.Y + y1) * atlasWidth)) * 4,
            ((rect.X + x1) + ((rect.Y + y1) * atlasWidth)) * 4,
            x - x0,
            y - y0);
    }

    private static Vector4 SampleBilinear(Half[] page, BilinearTap tap)
    {
        var v00 = ReadLuxel(page, tap.Index00);
        var v10 = ReadLuxel(page, tap.Index10);
        var v01 = ReadLuxel(page, tap.Index01);
        var v11 = ReadLuxel(page, tap.Index11);

        return Vector4.Lerp(Vector4.Lerp(v00, v10, tap.FractionX), Vector4.Lerp(v01, v11, tap.FractionX), tap.FractionY);
    }

    private static void WriteLuxel(Half[] page, int index, Vector4 value)
    {
        page[index] = (Half)value.X;
        page[index + 1] = (Half)value.Y;
        page[index + 2] = (Half)value.Z;
        page[index + 3] = (Half)value.W;
    }

    private static void RepadBorder(Half[] page, LightmapRect rect, int atlasWidth)
    {
        var padding = LightmapLayout.Padding;

        for (var y = -padding; y < rect.Height + padding; y++)
        {
            var insideY = y >= 0 && y < rect.Height;

            if (insideY)
            {
                RepadRow(page, rect, atlasWidth, y, -padding, 0);
                RepadRow(page, rect, atlasWidth, y, rect.Width, rect.Width + padding);
            }
            else
            {
                RepadRow(page, rect, atlasWidth, y, -padding, rect.Width + padding);
            }
        }
    }

    private static void RepadRow(Half[] page, LightmapRect rect, int atlasWidth, int y, int startX, int endX)
    {
        var sourceY = Math.Clamp(y, 0, rect.Height - 1);

        for (var x = startX; x < endX; x++)
        {
            var sourceX = Math.Clamp(x, 0, rect.Width - 1);
            var source = ((rect.X + sourceX) + ((rect.Y + sourceY) * atlasWidth)) * 4;
            var target = ((rect.X + x) + ((rect.Y + y) * atlasWidth)) * 4;

            page[target] = page[source];
            page[target + 1] = page[source + 1];
            page[target + 2] = page[source + 2];
            page[target + 3] = page[source + 3];
        }
    }
}
