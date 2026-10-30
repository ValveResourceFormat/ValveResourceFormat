namespace ValveResourceFormat.Source1.Bsp
{
    internal sealed class Displacement
    {
        private sealed class Neighbor
        {
            private NeighborOrientation relativeOrientation;
            private readonly Vector2 relativeMin;
            private readonly Vector2 relativeMax;

            public Displacement Displacement { get; }

            public Neighbor(Displacement orig, Displacement disp, NeighborOrientation orientation, Vector2 min, Vector2 max)
            {
                Displacement = disp;
                relativeOrientation = orientation;
                relativeMin = min;
                relativeMax = max;

                if (orientation != NeighborOrientation.Unknown)
                {
                    return;
                }

                var testPos = Vector2.Clamp((min + max) * 0.5f, Vector2.Zero, Vector2.One);
                var origSample = orig.GetInnerPosition(testPos);
                var bestDist2 = float.PositiveInfinity;
                var bestOrientation = NeighborOrientation.CounterClockwise0;

                for (var i = 0; i < 4; ++i)
                {
                    relativeOrientation = (NeighborOrientation)i;
                    var sample = GetPosition(testPos);
                    var dist2 = (sample - origSample).LengthSquared();

                    if (dist2 < bestDist2)
                    {
                        bestDist2 = dist2;
                        bestOrientation = relativeOrientation;
                    }
                }

                relativeOrientation = bestOrientation;
            }

            public bool Contains(Vector2 relativePos)
            {
                return relativePos.X >= relativeMin.X && relativePos.Y >= relativeMin.Y
                    && relativePos.X <= relativeMax.X && relativePos.Y <= relativeMax.Y;
            }

            public Vector3 GetPosition(Vector2 relativePos)
            {
                relativePos -= relativeMin;
                relativePos /= relativeMax - relativeMin;

                relativePos = relativeOrientation switch
                {
                    NeighborOrientation.CounterClockwise90 => new Vector2(relativePos.Y, 1f - relativePos.X),
                    NeighborOrientation.CounterClockwise180 => new Vector2(1f - relativePos.X, 1f - relativePos.Y),
                    NeighborOrientation.CounterClockwise270 => new Vector2(1f - relativePos.Y, relativePos.X),
                    _ => relativePos,
                };

                return Displacement.GetInnerPosition(relativePos);
            }
        }

        private readonly BspFile bsp;
        private readonly BspDispInfo dispInfo;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly int firstCorner;

        private Vector3[]? positions;
        private float[]? alphas;
        private List<Neighbor>? neighbors;

        public int Subdivisions => 1 << dispInfo.Power;
        public int Size => Subdivisions + 1;
        public Vector3 Normal { get; }

        public Displacement(BspFile bsp, int index)
        {
            this.bsp = bsp;
            dispInfo = bsp.DisplacementInfos[index];

            var face = bsp.LightmappedFaces[dispInfo.MapFace];
            var firstCornerDist2 = float.MaxValue;

            Normal = bsp.Planes[face.PlaneNum].Normal;

            for (var i = 0; i < 4; ++i)
            {
                var vert = bsp.GetVertexFromSurfEdge(face.FirstEdge + i);
                corners[i] = vert;

                var dist2 = (dispInfo.StartPosition - vert).LengthSquared();

                if (dist2 < firstCornerDist2)
                {
                    firstCorner = i;
                    firstCornerDist2 = dist2;
                }
            }
        }

        private void AddNeighbor(Vector2 min, Displacement disp, NeighborOrientation orientation)
        {
            foreach (var existing in neighbors!)
            {
                if (existing.Displacement == disp)
                {
                    return;
                }
            }

            neighbors.Add(new Neighbor(this, disp, orientation, min, min + Vector2.One));
        }

        private void AddEdgeNeighbor(NeighborEdge edge, DispSubNeighbor neighbor)
        {
            if (neighbor.Span != NeighborSpan.CornerToCorner || neighbor.NeighborSpan != NeighborSpan.CornerToCorner)
            {
                return;
            }

            if (neighbor.NeighborIndex >= bsp.DisplacementInfos.Length)
            {
                return;
            }

            var min = edge switch
            {
                NeighborEdge.Left => new Vector2(-1f, 0f),
                NeighborEdge.Bottom => new Vector2(0f, -1f),
                NeighborEdge.Right => new Vector2(1f, 0f),
                _ => new Vector2(0f, 1f),
            };

            AddNeighbor(min, bsp.DisplacementManager[neighbor.NeighborIndex], neighbor.NeighborOrientation);
        }

        private void AddCornerNeighbor(NeighborCorner corner, ushort index)
        {
            if (index >= bsp.DisplacementInfos.Length)
            {
                return;
            }

            var min = corner switch
            {
                NeighborCorner.LowerLeft => new Vector2(-1f, -1f),
                NeighborCorner.LowerRight => new Vector2(1f, -1f),
                NeighborCorner.UpperLeft => new Vector2(-1f, 1f),
                _ => new Vector2(1f, 1f),
            };

            AddNeighbor(min, bsp.DisplacementManager[index], NeighborOrientation.Unknown);
        }

        private void UpdateNeighbors()
        {
            if (neighbors != null)
            {
                return;
            }

            neighbors = [];

            for (var i = 0; i < 4; ++i)
            {
                var edgeNeighbors = dispInfo.GetEdgeNeighbor((NeighborEdge)i);

                if (edgeNeighbors[0].IsValid)
                {
                    AddEdgeNeighbor((NeighborEdge)i, edgeNeighbors[0]);
                }

                if (edgeNeighbors[1].IsValid)
                {
                    AddEdgeNeighbor((NeighborEdge)i, edgeNeighbors[1]);
                }
            }

            for (var i = 0; i < 4; ++i)
            {
                var cornerNeighbors = dispInfo.GetCornerNeighbor((NeighborCorner)i);

                for (var j = 0; j < cornerNeighbors.NumNeighbors && j < 4; ++j)
                {
                    AddCornerNeighbor((NeighborCorner)i, cornerNeighbors[j]);
                }
            }
        }

        private bool Contains(int x, int y) => x >= 0 && y >= 0 && x <= Subdivisions && y <= Subdivisions;

        public Vector3 GetPosition(int x, int y)
        {
            var total = 0;
            var sum = Vector3.Zero;

            if (Contains(x, y))
            {
                sum += GetInnerPosition(x, y);
                ++total;
            }

            UpdateNeighbors();

            var relativePos = new Vector2(x / (float)Subdivisions, y / (float)Subdivisions);

            foreach (var neighbor in neighbors!)
            {
                if (neighbor.Contains(relativePos))
                {
                    sum += neighbor.GetPosition(relativePos);
                    ++total;
                }
            }

            if (total == 0)
            {
                return GetInnerPosition(x, y);
            }

            return sum * (1f / total);
        }

        public Vector3 GetInnerPosition(Vector2 pos)
        {
            pos *= Subdivisions;

            var x0 = (int)MathF.Floor(pos.X);
            var x1 = (int)MathF.Ceiling(pos.X);
            var y0 = (int)MathF.Floor(pos.Y);
            var y1 = (int)MathF.Ceiling(pos.Y);

            var s00 = GetInnerPosition(x0, y0);

            if (x1 == x0 && y1 == y0)
            {
                return s00;
            }

            var s10 = x1 == x0 ? s00 : GetInnerPosition(x1, y0);
            var s01 = y1 == y0 ? s00 : GetInnerPosition(x0, y1);
            var s11 = GetInnerPosition(x1, y1);

            var tx = pos.X - x0;
            var sx = 1f - tx;
            var ty = pos.Y - y0;
            var sy = 1f - ty;

            return (((s00 * sx) + (s10 * tx)) * sy) + (((s01 * sx) + (s11 * tx)) * ty);
        }

        public void GetCorners(out Vector3 c0, out Vector3 c1, out Vector3 c2, out Vector3 c3)
        {
            c0 = corners[(0 + firstCorner) & 3];
            c1 = corners[(1 + firstCorner) & 3];
            c2 = corners[(2 + firstCorner) & 3];
            c3 = corners[(3 + firstCorner) & 3];
        }

        private void UpdatePositions()
        {
            if (positions != null)
            {
                return;
            }

            var size = Size;
            var cornerA = corners[(0 + firstCorner) & 3];
            var cornerB = corners[(1 + firstCorner) & 3];
            var cornerC = corners[(2 + firstCorner) & 3];
            var cornerD = corners[(3 + firstCorner) & 3];

            var newPositions = new Vector3[size * size];
            var newAlphas = new float[size * size];
            var verts = bsp.DisplacementVerts;

            for (var y = 0; y < size; ++y)
            {
                for (var x = 0; x < size; ++x)
                {
                    var index = x + (y * size);
                    var vertIndex = dispInfo.DispVertStart + index;

                    var tx = x / (size - 1f);
                    var ty = y / (size - 1f);
                    var sx = 1f - tx;
                    var sy = 1f - ty;

                    var origin = (ty * ((sx * cornerB) + (tx * cornerC))) + (sy * ((sx * cornerA) + (tx * cornerD)));

                    if (vertIndex < 0 || vertIndex >= verts.Length)
                    {
                        newPositions[index] = origin;
                        continue;
                    }

                    var vert = verts[vertIndex];
                    newPositions[index] = origin + (vert.Vector * vert.Distance);
                    newAlphas[index] = vert.Alpha / 255f;
                }
            }

            positions = newPositions;
            alphas = newAlphas;
        }

        public Vector3 GetInnerPosition(int x, int y)
        {
            x = Math.Clamp(x, 0, Subdivisions);
            y = Math.Clamp(y, 0, Subdivisions);

            UpdatePositions();
            return positions![x + (y * Size)];
        }

        public float GetAlpha(int x, int y)
        {
            UpdatePositions();
            return alphas![x + (y * Size)];
        }

        public Vector3 GetNormal(int x, int y)
        {
            Span<Vector3> kernel = stackalloc Vector3[9];

            for (var i = 0; i < 3; ++i)
            {
                for (var j = 0; j < 3; ++j)
                {
                    kernel[i + (j * 3)] = GetPosition(x + i - 1, y + j - 1);
                }
            }

            var sum = Vector3.Zero;

            for (var i = 0; i < 2; ++i)
            {
                for (var j = 0; j < 2; ++j)
                {
                    var a = kernel[i + 0 + ((j + 0) * 3)];
                    var b = kernel[i + 1 + ((j + 0) * 3)];
                    var c = kernel[i + 0 + ((j + 1) * 3)];
                    var d = kernel[i + 1 + ((j + 1) * 3)];

                    var abd = Vector3.Cross(b - a, d - a);
                    var adc = Vector3.Cross(d - a, c - d);

                    sum += abd + adc;
                }
            }

            var normal = Vector3.Normalize(sum);

            if (float.IsNaN(normal.X) || float.IsNaN(normal.Y) || float.IsNaN(normal.Z))
            {
                normal = Normal;
            }

            return normal;
        }
    }
}
