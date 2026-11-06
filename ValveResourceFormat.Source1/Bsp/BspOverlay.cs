using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>The distances at which an overlay fades out (LUMP_OVERLAY_FADES), squared.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspOverlayFade
{
    public readonly float FadeDistMinSq;
    public readonly float FadeDistMaxSq;
}

/// <summary>
/// An overlay: a textured quad that is projected along <see cref="BasisNormal"/> onto the faces it lists.
/// Water overlays (LUMP_WATEROVERLAYS) have the same layout and follow the regular overlays.
/// </summary>
internal sealed class BspOverlay
{
    private const int RegularSize = 352;

    // Version 23 maps store one more value after the normal
    private const int RegularSizeV23 = 356;
    private const int WaterSize = 1120;

    // Maps with 32-bit indices store the texture info and the face count as 32-bit values
    private const int WaterSizeWide = 1124;
    private const int FacesOffset = 8;
    private const int WideFacesOffset = 12;
    private const int RegularFaceCount = 64;
    private const int WaterFaceCount = 256;

    public int Index { get; init; }
    public int Id { get; init; }
    public int TexInfo { get; init; }

    /// <summary>Overlays with a higher order draw on top of overlays with a lower order (0 to 3).</summary>
    public int RenderOrder { get; init; }

    public bool IsWater { get; init; }

    /// <summary>The indices into the face lump of the faces the overlay lands on.</summary>
    public int[] Faces { get; init; } = [];

    /// <summary>The U range of the texture, start and end.</summary>
    public Vector2 U { get; init; }

    /// <summary>The V range of the texture, start and end.</summary>
    public Vector2 V { get; init; }

    /// <summary>The four corners of the quad as (x, y) offsets along <see cref="BasisU"/> and <see cref="BasisV"/> from <see cref="Origin"/>.</summary>
    public Vector2[] Corners { get; init; } = [];

    public Vector3 Origin { get; init; }
    public Vector3 BasisU { get; init; }
    public Vector3 BasisV { get; init; }
    public Vector3 BasisNormal { get; init; }

    /// <summary>The squared distance where the overlay starts to fade, or a negative value for no fade.</summary>
    public float FadeDistMinSq { get; init; } = -1f;

    /// <summary>The squared distance where the overlay is fully faded.</summary>
    public float FadeDistMaxSq { get; init; }

    internal static BspOverlay[] Read(BspFile bsp)
    {
        var result = new List<BspOverlay>();
        var fades = bsp.OverlayFades;

        var regularSize = bsp.Version >= 23 ? RegularSizeV23 : RegularSize;
        var waterSize = WaterSize;
        var facesOffset = FacesOffset;
        var lumpVersion = 0;

        if (bsp.IsWide)
        {
            waterSize = WaterSizeWide;
            facesOffset = WideFacesOffset;
            lumpVersion = BspFile.WideLumpVersion;
        }

        ReadLump(bsp.GetVersionedLumpData(LumpType.Overlays, lumpVersion).Span, regularSize, RegularFaceCount, facesOffset, isWater: false, fades, result);
        ReadLump(bsp.GetVersionedLumpData(LumpType.WaterOverlays, lumpVersion).Span, waterSize, WaterFaceCount, facesOffset, isWater: true, [], result);

        return [.. result];
    }

    private static void ReadLump(ReadOnlySpan<byte> data, int size, int faceSlots, int facesOffset, bool isWater, BspOverlayFade[] fades, List<BspOverlay> result)
    {
        if (data.Length % size != 0)
        {
            return;
        }

        var count = data.Length / size;

        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(i * size, size);
            var id = BinaryPrimitives.ReadInt32LittleEndian(entry);
            int texInfo;
            int countAndOrder;

            if (facesOffset == WideFacesOffset)
            {
                texInfo = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
                countAndOrder = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            }
            else
            {
                texInfo = BinaryPrimitives.ReadInt16LittleEndian(entry[4..]);
                countAndOrder = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            }

            var faceCount = Math.Min(countAndOrder & 0x3fff, faceSlots);
            var renderOrder = (countAndOrder >> 14) & 3;

            var faces = new int[faceCount];

            for (var f = 0; f < faceCount; f++)
            {
                faces[f] = BinaryPrimitives.ReadInt32LittleEndian(entry[(facesOffset + (f * 4))..]);
            }

            var offset = facesOffset + (faceSlots * 4);
            var u = MemoryMarshal.Read<Vector2>(entry[offset..]);
            var v = MemoryMarshal.Read<Vector2>(entry[(offset + 8)..]);
            offset += 16;

            var uvPoints = new Vector3[4];

            for (var p = 0; p < 4; p++)
            {
                uvPoints[p] = MemoryMarshal.Read<Vector3>(entry[(offset + (p * 12))..]);
            }

            offset += 48;

            var origin = MemoryMarshal.Read<Vector3>(entry[offset..]);
            var basisNormal = MemoryMarshal.Read<Vector3>(entry[(offset + 12)..]);

            var basisU = new Vector3(uvPoints[0].Z, uvPoints[1].Z, uvPoints[2].Z);
            var flip = uvPoints[3].Z == 1f;
            var basisV = Vector3.Cross(basisNormal, basisU);

            if (flip)
            {
                basisV = -basisV;
            }

            var corners = new Vector2[4];

            for (var p = 0; p < 4; p++)
            {
                corners[p] = new Vector2(uvPoints[p].X, uvPoints[p].Y);
            }

            var fadeMin = -1f;
            var fadeMax = 0f;

            if (!isWater && i < fades.Length)
            {
                fadeMin = fades[i].FadeDistMinSq;
                fadeMax = fades[i].FadeDistMaxSq;
            }

            result.Add(new BspOverlay
            {
                Index = result.Count,
                Id = id,
                TexInfo = texInfo,
                RenderOrder = renderOrder,
                IsWater = isWater,
                Faces = faces,
                U = u,
                V = v,
                Corners = corners,
                Origin = origin,
                BasisU = basisU,
                BasisV = basisV,
                BasisNormal = basisNormal,
                FadeDistMinSq = fadeMin,
                FadeDistMaxSq = fadeMax,
            });
        }
    }
}
