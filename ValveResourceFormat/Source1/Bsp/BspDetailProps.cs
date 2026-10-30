using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Source1.Bsp
{
    internal enum BspDetailPropType : byte
    {
        Model = 0,
        Sprite = 1,
        ShapeCross = 2,
        ShapeTri = 3,
    }

    internal enum BspDetailPropOrientation : byte
    {
        /// <summary>The sprite keeps the angles of the prop.</summary>
        Normal = 0,

        /// <summary>The sprite always faces the camera.</summary>
        ScreenAligned = 1,

        /// <summary>The sprite turns around the up axis to face the camera.</summary>
        ScreenAlignedVertical = 2,
    }

    /// <summary>The extents and texture coordinates of one sprite of the detail sprite sheet.</summary>
    internal readonly record struct BspDetailSprite(Vector2 UpperLeft, Vector2 LowerRight, Vector2 TexUpperLeft, Vector2 TexLowerRight);

    /// <summary>One detail prop of the dprp game lump.</summary>
    internal sealed class BspDetailProp
    {
        public Vector3 Origin { get; init; }

        /// <summary>Pitch, yaw and roll in degrees, pitch positive downwards.</summary>
        public Vector3 Angles { get; init; }

        /// <summary>The index into <see cref="BspDetailProps.ModelDictionary"/> for models or into <see cref="BspDetailProps.SpriteDictionary"/> for sprites and shapes.</summary>
        public int DetailModel { get; init; }

        public int Leaf { get; init; }

        /// <summary>The baked light at the prop, linear.</summary>
        public Vector3 Lighting { get; init; }

        /// <summary>The position of the prop in the lump, which alternates sprites between mirrored and not.</summary>
        public int Index { get; init; }

        public uint LightStyles { get; init; }
        public byte LightStyleCount { get; init; }

        /// <summary>How much the prop sways, from 0 to 1.</summary>
        public float SwayAmount { get; init; }

        /// <summary>The tilt in degrees of the sides of a <see cref="BspDetailPropType.ShapeTri"/>.</summary>
        public byte ShapeAngle { get; init; }

        /// <summary>How far the sides of a <see cref="BspDetailPropType.ShapeTri"/> sit from the centre, from 0 to 1 of the sprite width.</summary>
        public float ShapeSize { get; init; }

        public BspDetailPropOrientation Orientation { get; init; }
        public BspDetailPropType Type { get; init; }

        /// <summary>The scale of sprites and shapes.</summary>
        public float Scale { get; init; } = 1f;
    }

    /// <summary>A vertex of the generated detail sprite geometry.</summary>
    internal struct BspDetailVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;

        /// <summary>The baked light of the prop, linear.</summary>
        public Vector3 Color;

        /// <summary>The origin of the prop, for billboarding around it.</summary>
        public Vector3 PropOrigin;

        /// <summary>The corner of the sprite along its right and up axes, which a billboard rebuilds from the camera.</summary>
        public Vector2 LocalOffset;

        public BspDetailPropOrientation Orientation;
        public float SwayAmount;
    }

    /// <summary>The generated quads of all sprite, cross and tri detail props.</summary>
    internal sealed class BspDetailSpriteMesh
    {
        /// <summary>The material all sprites come from.</summary>
        public string Material { get; init; } = BspDetailProps.SpriteMaterial;

        public BspDetailVertex[] Vertices { get; init; } = [];
        public int[] Indices { get; init; } = [];
    }

    /// <summary>The detail props of a map: the model and sprite dictionaries and every placed prop.</summary>
    internal sealed class BspDetailProps
    {
        /// <summary>The material every detail sprite is cut from.</summary>
        public const string SpriteMaterial = "detail/detailsprites";

        private const int NameLength = 128;
        private const int SpriteSize = 32;
        private const int LegacyPropSize = 48;
        private const int PropSize = 52;

        public ushort LumpVersion { get; }
        public IReadOnlyList<string> ModelDictionary { get; } = [];
        public IReadOnlyList<BspDetailSprite> SpriteDictionary { get; } = [];
        public IReadOnlyList<BspDetailProp> Props { get; } = [];

        internal BspDetailProps(BspFile bsp)
        {
            var gameLump = bsp.GameLump;

            if (!gameLump.HasItem("dprp"))
            {
                return;
            }

            LumpVersion = gameLump.GetItemVersion("dprp");

            var data = gameLump.GetItemData("dprp");

            if (data == null)
            {
                return;
            }

            var span = data.AsSpan();
            var position = 0;

            var models = new List<string>();
            var sprites = new List<BspDetailSprite>();
            var props = new List<BspDetailProp>();

            if (!TryReadCount(span, ref position, NameLength, out var modelCount))
            {
                return;
            }

            for (var i = 0; i < modelCount; i++)
            {
                var name = span.Slice(position, NameLength);
                var length = name.IndexOf((byte)0);
                models.Add(Encoding.ASCII.GetString(name[..(length < 0 ? NameLength : length)]));
                position += NameLength;
            }

            ModelDictionary = models;

            if (!TryReadCount(span, ref position, SpriteSize, out var spriteCount))
            {
                return;
            }

            for (var i = 0; i < spriteCount; i++)
            {
                var entry = span.Slice(position, SpriteSize);

                sprites.Add(new BspDetailSprite(
                    MemoryMarshal.Read<Vector2>(entry),
                    MemoryMarshal.Read<Vector2>(entry[8..]),
                    MemoryMarshal.Read<Vector2>(entry[16..]),
                    MemoryMarshal.Read<Vector2>(entry[24..])));

                position += SpriteSize;
            }

            SpriteDictionary = sprites;

            if (position + 4 > span.Length)
            {
                return;
            }

            var propCount = BinaryPrimitives.ReadInt32LittleEndian(span[position..]);
            position += 4;

            if (propCount <= 0)
            {
                return;
            }

            var stride = (span.Length - position) / propCount;

            if (stride < LegacyPropSize)
            {
                return;
            }

            stride = stride >= PropSize ? PropSize : LegacyPropSize;

            for (var i = 0; i < propCount && position + stride <= span.Length; i++)
            {
                props.Add(ReadProp(span.Slice(position, stride), i));
                position += stride;
            }

            Props = props;
        }

        private static bool TryReadCount(ReadOnlySpan<byte> span, ref int position, int entrySize, out int count)
        {
            count = 0;

            if (position + 4 > span.Length)
            {
                return false;
            }

            count = BinaryPrimitives.ReadInt32LittleEndian(span[position..]);
            position += 4;

            if (count < 0 || position + ((long)count * entrySize) > span.Length)
            {
                count = 0;
                return false;
            }

            return true;
        }

        private static BspDetailProp ReadProp(ReadOnlySpan<byte> entry, int index)
        {
            var lighting = MemoryMarshal.Read<ColorRGBExp32>(entry[28..]);

            return new BspDetailProp
            {
                Origin = MemoryMarshal.Read<Vector3>(entry),
                Angles = MemoryMarshal.Read<Vector3>(entry[12..]),
                DetailModel = BinaryPrimitives.ReadUInt16LittleEndian(entry[24..]),
                Leaf = BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]),
                Index = index,
                Lighting = lighting.ToLinear(),
                LightStyles = BinaryPrimitives.ReadUInt32LittleEndian(entry[32..]),
                LightStyleCount = entry[36],
                SwayAmount = entry[37] / 255f,
                ShapeAngle = entry[38],
                ShapeSize = entry[39] / 255f,
                Orientation = (BspDetailPropOrientation)entry[40],
                Type = (BspDetailPropType)entry[44],
                Scale = entry.Length >= PropSize ? BinaryPrimitives.ReadSingleLittleEndian(entry[48..]) : 1f,
            };
        }

        /// <summary>
        /// Generates quads for all sprite, cross and tri props. Sprites use the angles of the prop; props whose
        /// <see cref="BspDetailPropOrientation"/> is not <see cref="BspDetailPropOrientation.Normal"/> are meant to be
        /// rotated around <see cref="BspDetailVertex.PropOrigin"/> by the renderer. Sway is not applied, the sides of
        /// tri shapes are an approximation of the engine layout, and the random horizontal flip is not applied.
        /// </summary>
        public BspDetailSpriteMesh BuildSpriteMesh()
        {
            var vertices = new List<BspDetailVertex>();
            var indices = new List<int>();

            foreach (var prop in Props)
            {
                if (prop.Type == BspDetailPropType.Model || prop.DetailModel >= SpriteDictionary.Count)
                {
                    continue;
                }

                var sprite = SpriteDictionary[prop.DetailModel];

                if (prop.Index % 2 == 0)
                {
                    sprite = sprite with
                    {
                        TexUpperLeft = new Vector2(sprite.TexLowerRight.X, sprite.TexUpperLeft.Y),
                        TexLowerRight = new Vector2(sprite.TexUpperLeft.X, sprite.TexLowerRight.Y),
                    };
                }

                var basis = new Basis(prop.Angles);

                switch (prop.Type)
                {
                    case BspDetailPropType.Sprite:
                        AddSprite(prop, sprite, basis, vertices, indices);
                        break;

                    case BspDetailPropType.ShapeCross:
                        AddCross(prop, sprite, basis, vertices, indices);
                        break;

                    case BspDetailPropType.ShapeTri:
                        AddTri(prop, sprite, basis, vertices, indices);
                        break;
                }
            }

            return new BspDetailSpriteMesh { Vertices = [.. vertices], Indices = [.. indices] };
        }

        internal readonly struct Basis
        {
            public Vector3 Forward { get; }
            public Vector3 Right { get; }
            public Vector3 Up { get; }

            public Basis(Vector3 angles)
            {
                var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);

                Forward = new Vector3(rotation.M11, rotation.M12, rotation.M13);
                Right = -new Vector3(rotation.M21, rotation.M22, rotation.M23);
                Up = new Vector3(rotation.M31, rotation.M32, rotation.M33);
            }

            private Basis(Vector3 forward, Vector3 right, Vector3 up)
            {
                Forward = forward;
                Right = right;
                Up = up;
            }

            public Basis Rotated(Vector3 localAngles)
            {
                var local = new Basis(localAngles);
                return new Basis(Transform(local.Forward), Transform(local.Right), Transform(local.Up));
            }

            private Vector3 Transform(Vector3 local)
            {
                return (local.X * Forward) - (local.Y * Right) + (local.Z * Up);
            }
        }

        private static void AddQuad(BspDetailProp prop, Vector3 origin, Vector3 width, Vector3 height, Vector2 uvOrigin, Vector2 uvOpposite,
            List<BspDetailVertex> vertices, List<int> indices, BspDetailPropOrientation orientation = BspDetailPropOrientation.Normal,
            Vector2 localOrigin = default, Vector2 localSize = default)
        {
            var normal = Vector3.Normalize(Vector3.Cross(height, width));
            var baseIndex = vertices.Count;
            var positions = new[] { origin, origin + height, origin + height + width, origin + width };
            var localOffsets = new Vector2[]
            {
                localOrigin,
                localOrigin + new Vector2(0f, localSize.Y),
                localOrigin + localSize,
                localOrigin + new Vector2(localSize.X, 0f),
            };
            var uvs = new Vector2[]
            {
                uvOrigin,
                new(uvOrigin.X, uvOpposite.Y),
                uvOpposite,
                new(uvOpposite.X, uvOrigin.Y),
            };

            for (var i = 0; i < 4; i++)
            {
                vertices.Add(new BspDetailVertex
                {
                    Position = positions[i],
                    Normal = normal,
                    TexCoord = uvs[i],
                    Color = prop.Lighting,
                    PropOrigin = prop.Origin,
                    LocalOffset = localOffsets[i],
                    Orientation = orientation,
                    SwayAmount = prop.SwayAmount,
                });
            }

            indices.Add(baseIndex);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }

        private static void AddSprite(BspDetailProp prop, BspDetailSprite sprite, Basis basis, List<BspDetailVertex> vertices, List<int> indices)
        {
            var upperLeft = sprite.UpperLeft * prop.Scale;
            var lowerRight = sprite.LowerRight * prop.Scale;
            var origin = prop.Origin + (upperLeft.X * basis.Right) + (upperLeft.Y * basis.Up);
            var width = basis.Right * (lowerRight.X - upperLeft.X);
            var height = basis.Up * (lowerRight.Y - upperLeft.Y);

            var localSize = lowerRight - upperLeft;

            AddQuad(prop, origin, width, height, sprite.TexUpperLeft, sprite.TexLowerRight, vertices, indices, prop.Orientation, upperLeft, localSize);
        }

        private static void AddCross(BspDetailProp prop, BspDetailSprite sprite, Basis basis, List<BspDetailVertex> vertices, List<int> indices)
        {
            var upperLeft = sprite.UpperLeft * prop.Scale;
            var lowerRight = sprite.LowerRight * prop.Scale;
            var halfWidth = (lowerRight.X - upperLeft.X) / 2f;
            var height = basis.Up * (lowerRight.Y - upperLeft.Y);
            var origin = prop.Origin + (upperLeft.Y * basis.Up);

            var middleU = (sprite.TexUpperLeft.X + sprite.TexLowerRight.X) / 2f;
            var middle = new Vector2(middleU, sprite.TexUpperLeft.Y);
            var rightEdge = new Vector2(sprite.TexLowerRight.X, sprite.TexLowerRight.Y);
            var leftEdge = new Vector2(sprite.TexUpperLeft.X, sprite.TexLowerRight.Y);

            AddQuad(prop, origin, -basis.Forward * halfWidth, height, middle, rightEdge, vertices, indices);
            AddQuad(prop, origin, -basis.Right * halfWidth, height, middle, leftEdge, vertices, indices);
            AddQuad(prop, origin, basis.Forward * halfWidth, height, middle, leftEdge, vertices, indices);
            AddQuad(prop, origin, basis.Right * halfWidth, height, middle, rightEdge, vertices, indices);
        }

        private static void AddTri(BspDetailProp prop, BspDetailSprite sprite, Basis basis, List<BspDetailVertex> vertices, List<int> indices)
        {
            var upperLeft = sprite.UpperLeft * prop.Scale;
            var lowerRight = sprite.LowerRight * prop.Scale;
            var width = lowerRight.X - upperLeft.X;
            var height = lowerRight.Y - upperLeft.Y;

            for (var side = 0; side < 3; side++)
            {
                var sideBasis = basis.Rotated(new Vector3(prop.ShapeAngle, side * 120f, 0f));
                var origin = prop.Origin
                    + (upperLeft.X * sideBasis.Right)
                    + (upperLeft.Y * sideBasis.Up)
                    + (prop.ShapeSize * width * sideBasis.Forward);

                AddQuad(prop, origin, sideBasis.Right * width, sideBasis.Up * height, sprite.TexUpperLeft, sprite.TexLowerRight, vertices, indices);
            }
        }
    }
}
