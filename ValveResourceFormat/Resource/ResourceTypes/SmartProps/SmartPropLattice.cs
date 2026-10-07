using System.Linq;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Builds and evaluates the cubic Bezier lattices of the smart prop deformer elements. A lattice has
    /// <c>nx</c> segments along X and two corners each along Y and Z.
    /// </summary>
    internal static class SmartPropLattice
    {
        private const int Corners = 4;

        private static Vector3 ResetPoint(Vector3 size, int nx, int layer, int corner)
            => new((float)layer / nx * size.X, (corner & 1) * size.Y, (corner >> 1) * size.Z);

        private static (Vector3[] Points, Vector3[] Handles) ResetLattice(Vector3 size, int nx)
        {
            var points = new Vector3[(nx + 1) * Corners];
            var handles = new Vector3[2 * nx * Corners];

            for (var layer = 0; layer <= nx; layer++)
            {
                for (var corner = 0; corner < Corners; corner++)
                {
                    points[layer * Corners + corner] = ResetPoint(size, nx, layer, corner);
                }
            }

            for (var layer = 0; layer < nx; layer++)
            {
                for (var corner = 0; corner < Corners; corner++)
                {
                    var p = points[layer * Corners + corner];
                    var next = points[(layer + 1) * Corners + corner];
                    handles[2 * (corner + layer * Corners)] = p * (2f / 3f) + next * (1f / 3f);
                    handles[2 * (corner + layer * Corners) + 1] = p * (1f / 3f) + next * (2f / 3f);
                }
            }

            return (points, handles);
        }

        public static SmartPropDeformer BuildBend(SmartPropTransform frame, Vector3 size, float angle, float bendPoint, float bendRadius)
        {
            var sign = angle >= 0f ? 1f : -1f;
            var nx = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(angle) / 90f));
            var (points, handles) = ResetLattice(size, nx);

            if (MathF.Abs(angle) >= 0.1f)
            {
                var radius = bendRadius > 0f ? bendRadius : size.X / (MathF.Abs(angle) * 0.017453292f);

                if (angle < 0f)
                {
                    radius += MathF.Abs(size.Y);
                }

                var center = new Vector3(size.X * bendPoint, 0f, 0f) - Vector3.UnitY * radius * sign;
                var segmentAngle = angle / nx;
                var startAngle = (90f - 90f * sign) - angle * bendPoint;

                for (var i = 0; i < nx; i++)
                {
                    var a0 = i * segmentAngle + startAngle;
                    var span = (a0 + segmentAngle) - a0;
                    var half = span * 0.017453292f * 0.5f;
                    var middle = (span * 0.5f + a0) * 0.017453292f;
                    var (sinHalf, cosHalf) = MathF.SinCos(half);
                    var (sinMiddle, cosMiddle) = MathF.SinCos(middle);
                    var k = (4f - cosHalf) / 3f;
                    var m = (cosHalf - k) * (1f / MathF.Tan(half)) + sinHalf;

                    Vector2 Rotate(float x, float y) => new(x * cosMiddle - y * sinMiddle, x * sinMiddle + y * cosMiddle);

                    var d0 = Rotate(cosHalf, -sinHalf);
                    var d1 = Rotate(k, -m);
                    var d2 = Rotate(k, m);
                    var d3 = Rotate(cosHalf, sinHalf);

                    for (var j = 0; j < Corners; j++)
                    {
                        var p = ResetPoint(size, nx, i, j);
                        var r = radius + sign * MathF.Abs(p.Y);
                        var q = new Vector3(center.X, center.Y, p.Z);

                        Vector3 Value(Vector2 d) => q + Vector3.UnitY * (d.X * r) + Vector3.UnitX * (d.Y * r);

                        points[j + Corners * i] = Value(d0);
                        handles[2 * (j + Corners * i)] = Value(d1);
                        handles[2 * (j + Corners * i) + 1] = Value(d2);
                        points[j + Corners * (i + 1)] = Value(d3);
                    }
                }
            }

            return new SmartPropDeformer { Frame = frame, Size = size, Divisions = (nx, 1, 1), Points = points, Handles = handles };
        }

        /// <summary>
        /// Builds the midpoint lattice and returns it with its deformed midpoint in smart prop space.
        /// </summary>
        public static (SmartPropDeformer Deformer, Vector3 Midpoint) BuildMidpoint(SmartPropTransform frame, Vector3 size, Matrix4x4 midpointMatrix, bool continuous)
        {
            var nx = continuous ? 2 : 1;
            var (points, handles) = ResetLattice(size, nx);
            Vector3 midpoint;

            if (continuous)
            {
                for (var i = 4; i < 8; i++)
                {
                    points[i] = Vector3.Transform(points[i], midpointMatrix);
                }

                foreach (var i in (int[])[1, 8, 3, 10, 5, 12, 7, 14])
                {
                    handles[i] = Vector3.Transform(handles[i], midpointMatrix);
                }

                midpoint = ((((Vector3.Zero + points[4]) + points[5]) + points[6]) + points[7]) * 0.25f;
            }
            else
            {
                for (var i = 0; i < handles.Length; i++)
                {
                    handles[i] = Vector3.Transform(handles[i], midpointMatrix);
                }

                static Vector3 Average(IEnumerable<Vector3> values) => values.Aggregate(Vector3.Zero, static (a, b) => a + b) / 4f;

                midpoint = Average(points[0..4]) * 0.125f
                    + Average(handles.Where((_, i) => i % 2 == 0)) * 0.375f
                    + Average(handles.Where((_, i) => i % 2 == 1)) * 0.375f
                    + Average(points[4..8]) * 0.125f;
            }

            var deformer = new SmartPropDeformer { Frame = frame, Size = size, Divisions = (nx, 1, 1), Points = points, Handles = handles };
            return (deformer, frame.TransformPoint(midpoint));
        }

        private static Vector3 LatticePoint(SmartPropDeformer deformer, int layer, int corner)
        {
            var nx = deformer.Divisions.X;
            var step = deformer.Size.X / nx;

            if (layer < 0)
            {
                var first = deformer.Points[corner];
                return first + -layer * (MathUtils.SafeNormalize(first - deformer.Handles[2 * corner]) * step);
            }

            if (layer > nx)
            {
                var last = deformer.Points[nx * Corners + corner];
                return last + (layer - nx) * (MathUtils.SafeNormalize(last - deformer.Handles[2 * (corner + (nx - 1) * Corners) + 1]) * step);
            }

            return deformer.Points[layer * Corners + corner];
        }

        private static Vector3 CornerCurve(SmartPropDeformer deformer, int segment, int corner, float f)
        {
            var p0 = LatticePoint(deformer, segment - 1, corner);
            var p3 = LatticePoint(deformer, segment, corner);
            Vector3 h1, h2;

            if (segment - 1 >= 0 && segment - 1 < deformer.Divisions.X)
            {
                h1 = deformer.Handles[2 * (corner + (segment - 1) * Corners)];
                h2 = deformer.Handles[2 * (corner + (segment - 1) * Corners) + 1];
            }
            else
            {
                h1 = p0 * (2f / 3f) + p3 * (1f / 3f);
                h2 = p0 * (1f / 3f) + p3 * (2f / 3f);
            }

            return MathUtils.CubicBezier(p0, h1, h2, p3, f);
        }

        /// <summary>Deforms a point given in lattice space.</summary>
        public static Vector3 EvaluateLatticePoint(SmartPropDeformer deformer, Vector3 point)
        {
            var nx = deformer.Divisions.X;
            var u = point.X / deformer.Size.X * nx + 1f;
            var segment = Math.Clamp((int)u, 0, nx + 1);
            var f = u - segment;
            var v = point.Y / deformer.Size.Y;
            var w = point.Z / deformer.Size.Z;

            var bottom = Vector3.Lerp(CornerCurve(deformer, segment, 0, f), CornerCurve(deformer, segment, 1, f), v);
            var top = Vector3.Lerp(CornerCurve(deformer, segment, 2, f), CornerCurve(deformer, segment, 3, f), v);
            return Vector3.Lerp(bottom, top, w);
        }

        /// <summary>Deforms a point given in smart prop space.</summary>
        public static Vector3 DeformPoint(SmartPropDeformer deformer, Vector3 point)
            => deformer.Frame.TransformPoint(EvaluateLatticePoint(deformer, deformer.Frame.InverseTransformPoint(point)));

        /// <summary>
        /// Moves a transform by the deformer: the position is deformed and the forward and left axes follow the
        /// deformation; the scale is kept.
        /// </summary>
        public static SmartPropTransform DeformTransform(SmartPropDeformer deformer, SmartPropTransform transform)
        {
            if (deformer.Divisions.X <= 0 || MathF.Min(MathF.Abs(deformer.Size.X), MathF.Min(MathF.Abs(deformer.Size.Y), MathF.Abs(deformer.Size.Z))) == 0f)
            {
                return transform;
            }

            var position = DeformPoint(deformer, transform.Position);
            var forward = MathUtils.SafeNormalize(DeformPoint(deformer, transform.Position + Vector3.Transform(Vector3.UnitX, transform.Rotation)) - position);
            var left = MathUtils.SafeNormalize(DeformPoint(deformer, transform.Position + Vector3.Transform(Vector3.UnitY, transform.Rotation)) - position);
            var up = MathUtils.SafeNormalize(Vector3.Cross(forward, left));
            left = MathUtils.SafeNormalize(Vector3.Cross(up, forward));

            return new SmartPropTransform(position, transform.Scale, SmartPropMath.QuaternionFromBasis(forward, left, up));
        }
    }
}
