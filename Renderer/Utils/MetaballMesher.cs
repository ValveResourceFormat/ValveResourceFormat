using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Utils
{
    /// <summary>
    /// Builds the surface where a sum of metaball kernels reaches a threshold, sampled on a regular grid
    /// over the centres. Each centre adds (1/2 - d^2 / 2c^2)^2 out to its cutoff radius c and nothing
    /// beyond, and the threshold is that kernel at the render radius, so an isolated centre gives a
    /// sphere of the render radius. Each grid cell is split into six tetrahedra around its main diagonal,
    /// which tile the grid without cracks.
    /// </summary>
    internal sealed class MetaballMesher
    {
        private const float MinCellSize = 0.15f;
        private const int MaxGridPoints = 1 << 21;

        /// <summary>The edge directions from a grid point, as corner bits: x is 1, y is 2 and z is 4.</summary>
        private const int EdgeDirections = 7;

        private static readonly int[][] Tetrahedra =
        [
            [0, 1, 3, 7],
            [0, 1, 5, 7],
            [0, 2, 3, 7],
            [0, 2, 6, 7],
            [0, 4, 5, 7],
            [0, 4, 6, 7],
        ];

        private float[] field = [];
        private bool[] rowInside = [];

        /// <summary>
        /// The vertex on each edge leaving the grid points of two z slices, plus one so that zero is none.
        /// A slice is cleared once the cells above it are done.
        /// </summary>
        private int[][] sliceEdgeVertices = [[], []];

        private Vector3[] centers = [];
        private int centerCount;
        private int[] binStarts = [];
        private int[] binnedCenters = [];
        private int[] binFill = [];
        private float binSize;
        private int binsX;
        private int binsY;
        private int binsZ;

        private int sizeX;
        private int sizeY;
        private int sizeZ;
        private Vector3 origin;
        private Vector3 step;
        private float cutoffSquared;
        private float falloff;
        private float threshold;
        private float texCoordScale;

        /// <summary>The vertices of the last surface built.</summary>
        public List<MetaballVertex> Vertices { get; } = [];

        /// <summary>The triangles of the last surface built, counter-clockwise seen from outside.</summary>
        public List<uint> Indices { get; } = [];

        /// <summary>A box holding the last surface built.</summary>
        public AABB Bounds { get; private set; }

        /// <summary>
        /// Builds the surface around <paramref name="positions"/>, replacing the last one.
        /// </summary>
        /// <param name="positions">The metaball centres.</param>
        /// <param name="cubeWidth">The authored grid cell size. Cells are never smaller than 0.15 units, and grow when the grid would get too large.</param>
        /// <param name="cutoffRadius">How far each centre reaches.</param>
        /// <param name="renderRadius">The radius of the surface around an isolated centre, less than <paramref name="cutoffRadius"/>.</param>
        /// <param name="uvScale">Scales the world position into texture coordinates, projected along the axis the surface faces most.</param>
        public void Build(ReadOnlySpan<Vector3> positions, float cubeWidth, float cutoffRadius, float renderRadius, float uvScale)
        {
            Vertices.Clear();
            Indices.Clear();
            Bounds = new AABB();

            if (positions.IsEmpty)
            {
                return;
            }

            cutoffSquared = cutoffRadius * cutoffRadius;
            falloff = 1f / (2f * cutoffSquared);
            var thresholdRoot = (renderRadius * renderRadius * falloff) - 0.5f;
            threshold = thresholdRoot * thresholdRoot;
            texCoordScale = uvScale;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var position in positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var influence = new Vector3(cutoffRadius + (0.01f * cubeWidth));
            origin = min - influence;
            var extent = max + influence - origin;

            if (!float.IsFinite(extent.X + extent.Y + extent.Z))
            {
                return;
            }

            SizeGrid(extent, MathF.Max(MinCellSize, cubeWidth));
            Bounds = new AABB(origin, origin + extent);

            BinCenters(positions, extent, cutoffRadius);
            Splat(cutoffRadius);
            March();
        }

        private void SizeGrid(Vector3 extent, float cellSize)
        {
            while (true)
            {
                var cells = Vector3.Max(Vector3.One, Ceiling(extent / cellSize));
                var points = (cells.X + 1f) * (cells.Y + 1f) * (cells.Z + 1f);

                if (points > MaxGridPoints)
                {
                    cellSize *= MathF.Cbrt(points / MaxGridPoints) * 1.01f;
                    continue;
                }

                sizeX = (int)cells.X + 1;
                sizeY = (int)cells.Y + 1;
                sizeZ = (int)cells.Z + 1;
                step = extent / cells;
                return;
            }
        }

        /// <summary>
        /// Sorts the centres into cubic bins at least as wide as the cutoff radius, so that the centres
        /// reaching a point all lie in the bin holding it and the 26 around it.
        /// </summary>
        private void BinCenters(ReadOnlySpan<Vector3> positions, Vector3 extent, float cutoffRadius)
        {
            centerCount = positions.Length;

            if (centers.Length < centerCount)
            {
                centers = new Vector3[centerCount];
                binnedCenters = new int[centerCount];
            }

            positions.CopyTo(centers);

            binSize = MathF.Max(cutoffRadius, step.MaxComponent());
            var bins = Vector3.Max(Vector3.One, Ceiling(extent / binSize));
            (binsX, binsY, binsZ) = ((int)bins.X, (int)bins.Y, (int)bins.Z);

            var binCount = binsX * binsY * binsZ;

            if (binStarts.Length < binCount + 1)
            {
                binStarts = new int[binCount + 1];
            }

            Array.Clear(binStarts, 0, binCount + 1);

            for (var i = 0; i < centerCount; i++)
            {
                binStarts[BinOf(centers[i]) + 1]++;
            }

            for (var bin = 0; bin < binCount; bin++)
            {
                binStarts[bin + 1] += binStarts[bin];
            }

            if (binFill.Length < binCount)
            {
                binFill = new int[binCount];
            }

            binStarts.AsSpan(0, binCount).CopyTo(binFill);

            for (var i = 0; i < centerCount; i++)
            {
                binnedCenters[binFill[BinOf(centers[i])]++] = i;
            }
        }

        private int BinOf(Vector3 position)
        {
            var bin = Vector3.Clamp(Floor((position - origin) / binSize), Vector3.Zero, new Vector3(binsX - 1, binsY - 1, binsZ - 1));

            return (int)bin.X + (binsX * ((int)bin.Y + (binsY * (int)bin.Z)));
        }

        private int GridIndex(int x, int y, int z) => x + (sizeX * (y + (sizeY * z)));

        private void Splat(float cutoffRadius)
        {
            var pointCount = sizeX * sizeY * sizeZ;

            if (field.Length < pointCount)
            {
                field = new float[pointCount];
            }

            Array.Clear(field, 0, pointCount);

            var reach = new Vector3(cutoffRadius);
            var last = new Vector3(sizeX - 1, sizeY - 1, sizeZ - 1);

            foreach (var center in centers.AsSpan(0, centerCount))
            {
                var low = Vector3.Max(Vector3.Zero, Ceiling((center - reach - origin) / step));
                var high = Vector3.Min(last, Floor((center + reach - origin) / step));

                for (var z = (int)low.Z; z <= (int)high.Z; z++)
                {
                    var dz = origin.Z + (z * step.Z) - center.Z;

                    for (var y = (int)low.Y; y <= (int)high.Y; y++)
                    {
                        var dy = origin.Y + (y * step.Y) - center.Y;
                        var row = GridIndex(0, y, z);

                        for (var x = (int)low.X; x <= (int)high.X; x++)
                        {
                            var dx = origin.X + (x * step.X) - center.X;
                            var distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);

                            if (distanceSquared <= cutoffSquared)
                            {
                                var kernel = 0.5f - (distanceSquared * falloff);
                                field[row + x] += kernel * kernel;
                            }
                        }
                    }
                }
            }

            var rowCount = sizeY * sizeZ;

            if (rowInside.Length < rowCount)
            {
                rowInside = new bool[rowCount];
            }

            for (var row = 0; row < rowCount; row++)
            {
                rowInside[row] = field.AsSpan(row * sizeX, sizeX).ContainsAnyExceptInRange(float.MinValue, threshold);
            }
        }

        private static Vector3 Ceiling(Vector3 value) => new(MathF.Ceiling(value.X), MathF.Ceiling(value.Y), MathF.Ceiling(value.Z));

        private static Vector3 Floor(Vector3 value) => new(MathF.Floor(value.X), MathF.Floor(value.Y), MathF.Floor(value.Z));

        private void March()
        {
            var sliceLength = sizeX * sizeY * EdgeDirections;

            for (var slice = 0; slice < 2; slice++)
            {
                if (sliceEdgeVertices[slice].Length < sliceLength)
                {
                    sliceEdgeVertices[slice] = new int[sliceLength];
                }

                Array.Clear(sliceEdgeVertices[slice], 0, sliceLength);
            }

            Span<float> values = stackalloc float[8];
            Span<int> cornerOffsets = stackalloc int[8];

            for (var corner = 0; corner < 8; corner++)
            {
                cornerOffsets[corner] = CornerIndex(0, 0, 0, corner);
            }

            for (var z = 0; z < sizeZ - 1; z++)
            {
                for (var y = 0; y < sizeY - 1; y++)
                {
                    var row = y + (sizeY * z);

                    if (!rowInside[row] && !rowInside[row + 1] && !rowInside[row + sizeY] && !rowInside[row + sizeY + 1])
                    {
                        continue;
                    }

                    var rowStart = GridIndex(0, y, z);

                    for (var x = 0; x < sizeX - 1; x++)
                    {
                        var insideCorners = 0;

                        for (var corner = 0; corner < 8; corner++)
                        {
                            values[corner] = field[rowStart + x + cornerOffsets[corner]];

                            if (values[corner] > threshold)
                            {
                                insideCorners |= 1 << corner;
                            }
                        }

                        if (insideCorners is 0 or 0xFF)
                        {
                            continue;
                        }

                        foreach (var tetrahedron in Tetrahedra)
                        {
                            PolygonizeTetrahedron(x, y, z, tetrahedron, values);
                        }
                    }
                }

                Array.Clear(sliceEdgeVertices[z & 1], 0, sliceLength);
            }
        }

        private int CornerIndex(int x, int y, int z, int corner)
            => GridIndex(x + (corner & 1), y + ((corner >> 1) & 1), z + ((corner >> 2) & 1));

        private static Vector3 CornerOffset(int corner) => new(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);

        /// <remarks>
        /// The corners of a tetrahedron run along a path from the cell's first corner to its last, each
        /// adding one axis, so every corner's axes include those of the corners before it.
        /// </remarks>
        private void PolygonizeTetrahedron(int x, int y, int z, int[] corners, ReadOnlySpan<float> values)
        {
            var inside = 0;

            for (var i = 0; i < 4; i++)
            {
                if (values[corners[i]] > threshold)
                {
                    inside |= 1 << i;
                }
            }

            var insideCount = BitOperations.PopCount((uint)inside);

            if (insideCount is 1 or 3)
            {
                var lone = BitOperations.TrailingZeroCount(insideCount == 1 ? inside : ~inside & 0xF);
                var (a, b, c) = Others(lone);
                var outward = Outward(corners, insideCount == 1 ? lone : a, insideCount == 1 ? a : lone);

                AddTriangle(
                    EdgeVertex(x, y, z, corners, lone, a),
                    EdgeVertex(x, y, z, corners, lone, b),
                    EdgeVertex(x, y, z, corners, lone, c), outward);
            }
            else if (insideCount == 2)
            {
                var first = BitOperations.TrailingZeroCount(inside);
                var second = BitOperations.TrailingZeroCount(inside & ~(1 << first));
                var outside = ~inside & 0xF;
                var third = BitOperations.TrailingZeroCount(outside);
                var fourth = BitOperations.TrailingZeroCount(outside & ~(1 << third));

                var firstThird = EdgeVertex(x, y, z, corners, first, third);
                var secondFourth = EdgeVertex(x, y, z, corners, second, fourth);
                var outward = Outward(corners, first, third);

                AddTriangle(firstThird, EdgeVertex(x, y, z, corners, first, fourth), secondFourth, outward);
                AddTriangle(firstThird, secondFourth, EdgeVertex(x, y, z, corners, second, third), outward);
            }
        }

        private static (int, int, int) Others(int corner) => corner switch
        {
            0 => (1, 2, 3),
            1 => (0, 2, 3),
            2 => (0, 1, 3),
            _ => (0, 1, 2),
        };

        /// <summary>
        /// The direction from an inside corner of a tetrahedron to an outside one. Every triangle in the
        /// tetrahedron has a vertex on the edge between them, so the two lie on either side of it.
        /// </summary>
        private Vector3 Outward(int[] corners, int insideCorner, int outsideCorner)
            => (CornerOffset(corners[outsideCorner]) - CornerOffset(corners[insideCorner])) * step;

        /// <summary>
        /// The vertex where the surface crosses the edge between two corners of a tetrahedron, shared
        /// with every other tetrahedron along that edge.
        /// </summary>
        private uint EdgeVertex(int x, int y, int z, int[] corners, int first, int second)
        {
            var low = corners[Math.Min(first, second)];
            var high = corners[Math.Max(first, second)];

            var lowZ = z + ((low >> 2) & 1);
            var slot = ((x + (low & 1) + (sizeX * (y + ((low >> 1) & 1)))) * EdgeDirections) + (high ^ low) - 1;
            ref var cached = ref sliceEdgeVertices[lowZ & 1][slot];

            if (cached != 0)
            {
                return (uint)(cached - 1);
            }

            var lowValue = field[CornerIndex(x, y, z, low)];
            var highValue = field[CornerIndex(x, y, z, high)];
            var t = (threshold - lowValue) / (highValue - lowValue);

            var cell = new Vector3(x, y, z);
            var position = origin + (Vector3.Lerp(cell + CornerOffset(low), cell + CornerOffset(high), t) * step);

            var index = Vertices.Count;
            Vertices.Add(CreateVertex(position, Normal(position)));
            cached = index + 1;

            return (uint)index;
        }

        /// <summary>The direction the field falls off fastest at <paramref name="position"/>, out of the surface.</summary>
        private Vector3 Normal(Vector3 position)
        {
            var bin = Floor((position - origin) / binSize);
            var low = Vector3.Max(Vector3.Zero, bin - Vector3.One);
            var high = Vector3.Min(new Vector3(binsX - 1, binsY - 1, binsZ - 1), bin + Vector3.One);
            var normal = Vector3.Zero;

            for (var z = (int)low.Z; z <= (int)high.Z; z++)
            {
                for (var y = (int)low.Y; y <= (int)high.Y; y++)
                {
                    var row = binsX * (y + (binsY * z));

                    for (var i = binStarts[row + (int)low.X]; i < binStarts[row + (int)high.X + 1]; i++)
                    {
                        var offset = position - centers[binnedCenters[i]];
                        var distanceSquared = offset.LengthSquared();

                        if (distanceSquared < cutoffSquared)
                        {
                            normal += offset * (0.5f - (distanceSquared * falloff));
                        }
                    }
                }
            }

            return MathUtils.SafeNormalize(normal, Vector3.UnitZ);
        }

        /// <summary>
        /// A vertex textured by projecting its position along the axis its normal is closest to, with the
        /// tangent along the projected U axis.
        /// </summary>
        private MetaballVertex CreateVertex(Vector3 position, Vector3 normal)
        {
            var facing = Vector3.Abs(normal);

            var (uv, uAxis) = facing.Z >= facing.X && facing.Z >= facing.Y
                ? (new Vector2(position.X, position.Y), Vector3.UnitX)
                : facing.X >= facing.Y
                    ? (new Vector2(position.Y, position.Z), Vector3.UnitY)
                    : (new Vector2(position.X, position.Z), Vector3.UnitX);

            var tangent = MathUtils.SafeNormalize(MathUtils.ProjectOntoPlane(uAxis, normal), Vector3.UnitX);

            return new MetaballVertex(position, normal, new Vector4(tangent, 1f), uv * texCoordScale);
        }

        /// <summary>Adds a triangle, wound counter-clockwise seen from <paramref name="outward"/>.</summary>
        private void AddTriangle(uint a, uint b, uint c, Vector3 outward)
        {
            var vertices = CollectionsMarshal.AsSpan(Vertices);
            var faceNormal = MathUtils.TriangleCross(vertices[(int)a].Position, vertices[(int)b].Position, vertices[(int)c].Position);
            var flip = Vector3.Dot(faceNormal, outward) < 0f;

            Indices.Add(a);
            Indices.Add(flip ? c : b);
            Indices.Add(flip ? b : c);
        }
    }
}
