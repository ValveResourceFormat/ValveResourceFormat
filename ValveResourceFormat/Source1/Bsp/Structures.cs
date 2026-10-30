using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspModel
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly Vector3 Origin;
        public readonly int HeadNode;
        public readonly int FirstFace;
        public readonly int NumFaces;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspPlane
    {
        public readonly Vector3 Normal;
        public readonly float Dist;
        public readonly int Type;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspEdge
    {
        public readonly ushort A;
        public readonly ushort B;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspFace
    {
        public readonly ushort PlaneNum;
        public readonly byte Side;
        public readonly byte OnNode;
        public readonly int FirstEdge;
        public readonly short NumEdges;
        public readonly short TexInfo;
        public readonly short DispInfo;
        public readonly short FogVolumeId;
        private readonly uint styles;
        public readonly int LightOffset;
        public readonly float Area;
        public readonly int LightMapOffsetX;
        public readonly int LightMapOffsetY;
        public readonly int LightMapSizeX;
        public readonly int LightMapSizeY;
        public readonly int OriginalFace;
        public readonly ushort NumPrimitives;
        public readonly ushort FirstPrimitive;
        public readonly uint SmoothingGroups;

        public bool HasLightmap => LightOffset != -1;

        /// <summary>The number of primitives of the face. The top bit of the stored value disables dynamic shadows.</summary>
        public int PrimitiveCount => NumPrimitives & 0x7fff;

        public byte GetLightStyle(int index) => (byte)((styles >> (index << 3)) & 0xff);
    }

    internal enum PrimitiveType : ushort
    {
        TriangleList,
        TriangleStrip,
        TriangleFan,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspPrimitive
    {
        public readonly PrimitiveType Type;
        public readonly ushort FirstIndex;
        public readonly ushort IndexCount;
        public readonly ushort FirstVert;
        public readonly ushort VertCount;
    }

    [Flags]
    internal enum SurfFlags
    {
        Light = 0x1,
        Sky2D = 0x2,
        Sky = 0x4,
        Warp = 0x8,
        Trans = 0x10,
        NoPortal = 0x20,
        Trigger = 0x40,
        NoDraw = 0x80,
        Hint = 0x100,
        Skip = 0x200,
        NoLight = 0x400,
        BumpLight = 0x800,
        NoShadows = 0x1000,
        NoDecals = 0x2000,
        NoChop = 0x4000,
        Hitbox = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct TexAxis
    {
        public readonly Vector3 Normal;
        public readonly float Offset;

        public float GetCoordinate(Vector3 position) => Vector3.Dot(position, Normal) + Offset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspTextureInfo
    {
        public readonly TexAxis TextureUAxis;
        public readonly TexAxis TextureVAxis;
        public readonly TexAxis LightmapUAxis;
        public readonly TexAxis LightmapVAxis;
        public readonly SurfFlags Flags;
        public readonly int TexData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspTextureData
    {
        public readonly Vector3 Reflectivity;
        public readonly int NameStringTableId;
        public readonly int Width;
        public readonly int Height;
        public readonly int ViewWidth;
        public readonly int ViewHeight;
    }

    [Flags]
    internal enum BrushContents
    {
        Empty = 0,
        Solid = 0x1,
        Window = 0x2,
        Aux = 0x4,
        Grate = 0x8,
        Slime = 0x10,
        Water = 0x20,
        Mist = 0x40,
        Opaque = 0x80,
        TestFogVolume = 0x100,
        Team1 = 0x800,
        Team2 = 0x1000,
        IgnoreNoDrawOpaque = 0x2000,
        Moveable = 0x4000,
        AreaPortal = 0x8000,
        PlayerClip = 0x10000,
        MonsterClip = 0x20000,
        Origin = 0x1000000,
        Monster = 0x2000000,
        Debris = 0x4000000,
        Detail = 0x8000000,
        Translucent = 0x10000000,
        Ladder = 0x20000000,
        Hitbox = 0x40000000,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspBrush
    {
        public readonly int FirstSide;
        public readonly int NumSides;
        public readonly BrushContents Contents;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspBrushSide
    {
        public readonly ushort PlaneNum;
        public readonly short TexInfo;
        public readonly short DispInfo;
        public readonly short Bevel;
    }

    internal enum NeighborCorner
    {
        LowerLeft,
        UpperLeft,
        UpperRight,
        LowerRight,
    }

    internal enum NeighborSpan : byte
    {
        CornerToCorner,
        CornerToMidpoint,
        MidpointToCorner,
    }

    internal enum NeighborEdge : byte
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    internal enum NeighborOrientation : byte
    {
        CounterClockwise0,
        CounterClockwise90,
        CounterClockwise180,
        CounterClockwise270,
        Unknown = 255,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 6)]
    internal readonly struct DispSubNeighbor
    {
        public readonly ushort NeighborIndex;
        public readonly NeighborOrientation NeighborOrientation;
        public readonly NeighborSpan Span;
        public readonly NeighborSpan NeighborSpan;

        public bool IsValid => NeighborIndex != 0xffff;

        public override string ToString() => IsValid ? $"{NeighborIndex}" : "null";
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct DispNeighbor
    {
        private readonly DispSubNeighbor subNeighbor0;
        private readonly DispSubNeighbor subNeighbor1;

        public bool Any => subNeighbor0.IsValid || subNeighbor1.IsValid;

        public DispSubNeighbor this[int index] => index switch
        {
            0 => subNeighbor0,
            1 => subNeighbor1,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override string ToString() => $"({subNeighbor0}, {subNeighbor1})";
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 10)]
    internal readonly struct DispCornerNeighbors
    {
        private readonly ushort neighbor0;
        private readonly ushort neighbor1;
        private readonly ushort neighbor2;
        private readonly ushort neighbor3;

        public readonly byte NumNeighbors;

        public ushort this[int index]
        {
            get
            {
                if (index < 0 || index >= NumNeighbors)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return index switch
                {
                    0 => neighbor0,
                    1 => neighbor1,
                    2 => neighbor2,
                    _ => neighbor3,
                };
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 176)]
    internal readonly struct BspDispInfo
    {
        public readonly Vector3 StartPosition;
        public readonly int DispVertStart;
        public readonly int DispTriStart;
        public readonly int Power;
        public readonly int MinTess;
        public readonly float SmoothingAngle;
        public readonly int Contents;
        public readonly ushort MapFace;
        private readonly ushort unused;
        public readonly int LightmapAlphaStart;
        public readonly int LightmapSamplePositionStart;

        private readonly DispNeighbor edgeNeighbor0;
        private readonly DispNeighbor edgeNeighbor1;
        private readonly DispNeighbor edgeNeighbor2;
        private readonly DispNeighbor edgeNeighbor3;

        private readonly DispCornerNeighbors cornerNeighbors0;
        private readonly DispCornerNeighbors cornerNeighbors1;
        private readonly DispCornerNeighbors cornerNeighbors2;
        private readonly DispCornerNeighbors cornerNeighbors3;

        public DispNeighbor GetEdgeNeighbor(NeighborEdge edge) => edge switch
        {
            NeighborEdge.Left => edgeNeighbor0,
            NeighborEdge.Top => edgeNeighbor1,
            NeighborEdge.Right => edgeNeighbor2,
            NeighborEdge.Bottom => edgeNeighbor3,
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };

        public DispCornerNeighbors GetCornerNeighbor(NeighborCorner corner) => corner switch
        {
            NeighborCorner.LowerLeft => cornerNeighbors0,
            NeighborCorner.UpperLeft => cornerNeighbors1,
            NeighborCorner.UpperRight => cornerNeighbors2,
            NeighborCorner.LowerRight => cornerNeighbors3,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct BspDispVert
    {
        public readonly Vector3 Vector;
        public readonly float Distance;
        public readonly float Alpha;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct ColorRGBExp32
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly sbyte Exponent;

        public Vector3 ToLinear()
        {
            var scale = MathF.Pow(2f, Exponent) / 255f;
            return new Vector3(R, G, B) * scale;
        }

        // Leaf ambient cubes are not scaled by 1/255 like lightmap luxels
        public Vector3 ToAmbientLinear() => new Vector3(R, G, B) * MathF.Pow(2f, Exponent);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct CubemapSample
    {
        public readonly int OriginX;
        public readonly int OriginY;
        public readonly int OriginZ;
        public readonly int Size;

        public Vector3 Origin => new(OriginX, OriginY, OriginZ);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct CompressedLightCube
    {
        public readonly ColorRGBExp32 Face0;
        public readonly ColorRGBExp32 Face1;
        public readonly ColorRGBExp32 Face2;
        public readonly ColorRGBExp32 Face3;
        public readonly ColorRGBExp32 Face4;
        public readonly ColorRGBExp32 Face5;

        public ColorRGBExp32 this[int face] => face switch
        {
            0 => Face0,
            1 => Face1,
            2 => Face2,
            3 => Face3,
            4 => Face4,
            5 => Face5,
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct LeafAmbientLighting
    {
        public readonly CompressedLightCube Cube;
        public readonly byte X;
        public readonly byte Y;
        public readonly byte Z;
        private readonly byte padding;

        public LeafAmbientLighting(CompressedLightCube cube)
        {
            Cube = cube;
            X = 0;
            Y = 0;
            Z = 0;
            padding = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct LeafAmbientIndex
    {
        public readonly ushort AmbientSampleCount;
        public readonly ushort FirstAmbientSample;

        public LeafAmbientIndex(ushort ambientSampleCount, ushort firstAmbientSample)
        {
            AmbientSampleCount = ambientSampleCount;
            FirstAmbientSample = firstAmbientSample;
        }
    }
}
