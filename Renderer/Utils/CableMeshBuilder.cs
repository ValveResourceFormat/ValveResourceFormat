using System.Buffers;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Utils
{
    /// <summary>
    /// A single sample along a cable spline: the initial (un-sagged) spline position plus the per-sample
    /// render attributes. The sample positions seed the rope particle simulation; the settled particles are
    /// tessellated into a tube by <see cref="Particles.Renderers.RenderCables"/> via
    /// <see cref="CableMeshBuilder.BuildTubeMesh"/>.
    /// </summary>
    readonly struct RopeSample(Vector3 position, float radius, Vector3 color, float u, bool pinned)
    {
        /// <summary>Initial, origin-relative spline position (no sag).</summary>
        public readonly Vector3 Position = position;
        public readonly float Radius = radius;
        public readonly Vector3 Color = color;
        /// <summary>Texture coordinate along the length of the cable.</summary>
        public readonly float U = u;
        /// <summary>True when this sample sits on a pinned path node (force_scale 0, immovable).</summary>
        public readonly bool Pinned = pinned;
    }

    /// <summary>
    /// Builds the cable geometry for a <c>path_particle_rope</c>: samples the stored cubic spline into
    /// rope particles, then (after the particle simulation has drooped them) tessellates a round tube
    /// through the settled positions (circular cross-section, sides = <c>2^clamp(roundness,0,3)*4</c>).
    /// </summary>
    static class CableMeshBuilder
    {
        // Absolute safety cap on the particle/sample count.
        private const int MaxSamples = 5000;
        private const int MaxStepsPerSegment = 256;

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct Vertex(Vector3 position, Vector3 normal, Vector2 uv, Color32 color)
        {
            public readonly Vector3 Position = position;
            public readonly Vector3 Normal = normal;
            public readonly Vector2 UV = uv;
            public readonly Color32 Color = color;
        }

        /// <summary>
        /// Samples the cable's cubic spline (no sag) at roughly <c>particle_spacing</c> intervals. Every
        /// path node becomes a sample and is pinned when its pin flag is set (default: all nodes pinned);
        /// the in-between samples are free and droop under the simulated gravity. The sample count is capped
        /// at <paramref name="maxSamples"/> (the rope particle system's max particle count);
        /// <paramref name="capped"/> is set when that cap is reached.
        /// </summary>
        public static List<RopeSample> SampleRope(CableGeometry geometry, int maxSamples, out bool capped)
        {
            capped = false;

            var nodes = geometry.Nodes;
            if (nodes.Count < 2)
            {
                return [];
            }

            var cap = Math.Clamp(maxSamples, 2, MaxSamples);
            var segments = nodes.Count - 1;
            var spacing = geometry.ParticleSpacing;

            // Estimate the sample count from straight-line node spacing so short cables don't pre-allocate
            // the full particle cap (up to ~1000 slots), while long cables still avoid repeated regrowth.
            var estimate = 1L;
            for (var seg = 0; seg < segments; seg++)
            {
                var chord = (nodes[seg + 1].Position - nodes[seg].Position).Length();
                estimate += Math.Max(4L, (long)MathF.Ceiling(chord / spacing));
            }

            var samples = new List<RopeSample>((int)Math.Min(cap, estimate));

            for (var seg = 0; seg < segments && !capped; seg++)
            {
                var n0 = nodes[seg];
                var n1 = nodes[seg + 1];

                var p0 = n0.Position;
                var p1 = n0.Position + n0.OutTangent;
                var p2 = n1.Position + n1.InTangent;
                var p3 = n1.Position;

                var r0 = geometry.RadiusAt(seg);
                var r1 = geometry.RadiusAt(seg + 1);
                var c0 = geometry.ColorAt(seg);
                var c1 = geometry.ColorAt(seg + 1);

                // Particles per segment = max(4, ceil(arcLength / spacing)). Emit (perSegment - 1) samples and
                // share the end node with the next segment so the total count matches exactly. The constraint's
                // base length is chainLength / particleCount, so the particle count sets the settled droop.
                var arcLength = BezierArcLength(p0, p1, p2, p3);
                var perSegment = Math.Max(4, (int)MathF.Ceiling(arcLength / spacing));
                var steps = Math.Min(MaxStepsPerSegment, perSegment - 1);

                // Emit the start node (pinned) and the interior samples (free); the end node is emitted by
                // the next segment, or once at the very end.
                for (var s = 0; s < steps; s++)
                {
                    var t = s / (float)steps;
                    var pos = MathUtils.CubicBezier(p0, p1, p2, p3, t);
                    var pinned = s == 0 && geometry.IsNodePinned(seg);

                    samples.Add(new RopeSample(pos, float.Lerp(r0, r1, t), Vector3.Lerp(c0, c1, t), 0f, pinned));

                    if (samples.Count >= cap)
                    {
                        capped = true;
                        break;
                    }
                }
            }

            if (capped)
            {
                // The cap was reached before the whole spline was sampled. Pin the last sample so it holds its
                // spline position, and leave the far terminal node off.
                var lastSample = samples[^1];
                samples[^1] = new RopeSample(lastSample.Position, lastSample.Radius, lastSample.Color, lastSample.U, true);
            }
            else
            {
                var last = nodes.Count - 1;
                samples.Add(new RopeSample(nodes[last].Position, geometry.RadiusAt(last), geometry.ColorAt(last),
                    0f, geometry.IsNodePinned(last)));
            }

            return samples;
        }

        private const int MaxRoundnessLevel = 3;

        /// <summary>The widest cross-section <see cref="SideCount"/> can return (roundness level 3).</summary>
        internal const int MaxSides = (1 << MaxRoundnessLevel) * 4;

        /// <summary>
        /// m_nRoundness is a discrete power-of-two level clamped to [0, 3], giving {4, 8, 16, 32} sides
        /// (roundness 0 is a real 4-gon prism, not a billboard ribbon).
        /// </summary>
        internal static int SideCount(int roundness) => (1 << Math.Clamp(roundness, 0, MaxRoundnessLevel)) * 4;

        /// <summary>
        /// Tessellates the round tube through <paramref name="positions"/> (index-aligned with
        /// <paramref name="samples"/> for per-point radius/colour/U) into exactly-sized caller buffers:
        /// <c>ringCount * (sides + 1)</c> vertices and <c>(ringCount - 1) * sides * 6</c> indices, with
        /// <paramref name="sides"/> from <see cref="SideCount"/>. Returns false for degenerate input.
        /// </summary>
        internal static bool BuildTubeMesh(ReadOnlySpan<Vector3> positions, ReadOnlySpan<RopeSample> samples,
            int sides, float circumferenceRepeats, Span<Vertex> vertices, Span<uint> indices)
        {
            if (positions.Length < 2 || positions.Length != samples.Length)
            {
                return false;
            }

            var count = positions.Length;
            var pool = ArrayPool<Vector3>.Shared;
            var tangents = pool.Rent(count);
            var normals = pool.Rent(count);
            var bitangents = pool.Rent(count);
            try
            {
                BuildFrames(positions, tangents, normals, bitangents);
                BuildTubeGeometry(positions, samples, circumferenceRepeats, normals, bitangents, sides, vertices, indices);
            }
            finally
            {
                pool.Return(tangents);
                pool.Return(normals);
                pool.Return(bitangents);
            }

            return true;
        }

        // Fills the first positions.Length entries of the (pooled, possibly larger) frame arrays.
        private static void BuildFrames(ReadOnlySpan<Vector3> positions, Vector3[] tangents, Vector3[] normals, Vector3[] bitangents)
        {
            var count = positions.Length;

            for (var i = 0; i < count; i++)
            {
                tangents[i] = SampleTangent(positions, i);
            }

            normals[0] = InitialNormal(tangents[0]);
            for (var i = 1; i < count; i++)
            {
                normals[i] = NormalFromPrevious(tangents[i], normals[i - 1]);
            }

            for (var i = 0; i < count; i++)
            {
                var bitangent = Vector3.Cross(tangents[i], normals[i]);
                bitangents[i] = bitangent.LengthSquared() > 1e-8f ? Vector3.Normalize(bitangent) : InitialNormal(tangents[i]);
            }
        }

        private static Vector3 SampleTangent(ReadOnlySpan<Vector3> positions, int i)
        {
            var count = positions.Length;

            Vector3 dir;
            if (i == 0)
            {
                dir = positions[1] - positions[0];
            }
            else if (i == count - 1)
            {
                dir = positions[count - 1] - positions[count - 2];
            }
            else
            {
                var a = Normalize(positions[i] - positions[i - 1]);
                var b = Normalize(positions[i + 1] - positions[i]);
                dir = a + b;
            }

            return dir.LengthSquared() > 1e-8f ? Vector3.Normalize(dir) : Vector3.UnitX;
        }

        private static Vector3 InitialNormal(Vector3 tangent)
        {
            var up = MathF.Abs(Vector3.Dot(tangent, Vector3.UnitZ)) > 0.98f ? Vector3.UnitX : Vector3.UnitZ;
            return Normalize(Vector3.Cross(tangent, up));
        }

        private static Vector3 NormalFromPrevious(Vector3 tangent, Vector3 previousNormal)
        {
            var projected = previousNormal - (tangent * Vector3.Dot(previousNormal, tangent));
            return projected.LengthSquared() > 1e-8f ? Vector3.Normalize(projected) : InitialNormal(tangent);
        }

        private static void BuildTubeGeometry(ReadOnlySpan<Vector3> positions, ReadOnlySpan<RopeSample> samples,
            float circumferenceRepeats,
            Vector3[] normals, Vector3[] bitangents, int sides, Span<Vertex> vertices, Span<uint> indices)
        {
            var ringCount = positions.Length;

            // Emit a duplicate seam vertex per ring (sides + 1): the extra vertex sits at the j == 0 position
            // but carries v == CircumferenceRepeats, so the closing quad interpolates the texture forward to
            // the full repeat instead of wrapping v back to 0.
            var vertsPerRing = sides + 1;
            var vertexCursor = 0;

            for (var i = 0; i < ringCount; i++)
            {
                var sample = samples[i];
                var center = positions[i];
                var color = Color32.FromVector4(new Vector4(sample.Color, 1.0f));

                for (var j = 0; j <= sides; j++)
                {
                    var angle = MathF.Tau * j / sides;
                    var radial = (normals[i] * MathF.Cos(angle)) + (bitangents[i] * MathF.Sin(angle));
                    var pos = center + (radial * sample.Radius);
                    var v = j / (float)sides * circumferenceRepeats;
                    vertices[vertexCursor++] = new Vertex(pos, Normalize(radial), new Vector2(sample.U, v), color);
                }
            }

            var segments = ringCount - 1;
            var indexCursor = 0;
            for (var i = 0; i < segments; i++)
            {
                var a = (uint)(i * vertsPerRing);
                var b = (uint)((i + 1) * vertsPerRing);
                for (var j = 0; j < sides; j++)
                {
                    var jn = j + 1;
                    // Outward-facing quad (CCW from outside); the tube is opaque, so backface culling hides
                    // the interior.
                    AddQuad(indices, ref indexCursor, a + (uint)j, a + (uint)jn, b + (uint)jn, b + (uint)j);
                }
            }
        }

        private static void AddQuad(Span<uint> indices, ref int cursor, uint a, uint b, uint c, uint d)
        {
            indices[cursor++] = a;
            indices[cursor++] = b;
            indices[cursor++] = c;
            indices[cursor++] = a;
            indices[cursor++] = c;
            indices[cursor++] = d;
        }

        // Approximates the arc length of a cubic Bezier by summing a fixed set of straight chords. Used to
        // pick the per-segment particle count (count = ceil(arcLength / spacing)).
        private static float BezierArcLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            const int subdivisions = 16;
            var length = 0f;
            var previous = p0;
            for (var i = 1; i <= subdivisions; i++)
            {
                var point = MathUtils.CubicBezier(p0, p1, p2, p3, i / (float)subdivisions);
                length += (point - previous).Length();
                previous = point;
            }

            return length;
        }

        private static Vector3 Normalize(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitZ;
    }
}
