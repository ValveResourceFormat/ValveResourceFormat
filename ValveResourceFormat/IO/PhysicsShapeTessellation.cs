using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

/// <summary>
/// Approximates physics spheres and capsules as closed convex polyhedra.
/// </summary>
internal static class PhysicsShapeTessellation
{
    /// <summary>
    /// Tessellates a sphere, see <see cref="Capsule"/>.
    /// </summary>
    public static (Vector3[] Positions, List<int[]> Faces) Sphere(Vector3 center, float radius, int segments, int hemisphereRings)
        => Capsule(center, center, radius, segments, hemisphereRings);

    /// <summary>
    /// Tessellates a capsule into latitude rings with their vertices on the surface, from the pole past
    /// <paramref name="start"/> to the pole past <paramref name="end"/>. Faces are planar quads, with
    /// triangle fans at the poles, wound counter-clockwise when seen from outside.
    /// </summary>
    /// <param name="start">Center of the first hemisphere.</param>
    /// <param name="end">Center of the second hemisphere, equal to <paramref name="start"/> for a sphere.</param>
    /// <param name="radius">Capsule radius.</param>
    /// <param name="segments">Vertices around each ring.</param>
    /// <param name="hemisphereRings">Rings in each hemisphere, the equator included.</param>
    public static (Vector3[] Positions, List<int[]> Faces) Capsule(Vector3 start, Vector3 end, float radius, int segments, int hemisphereRings)
    {
        var axis = MathUtils.SafeNormalize(end - start, Vector3.UnitZ, 1e-12f);
        var frame = EntityTransformHelper.ForwardDirectionToRotationMatrix(axis);
        var left = new Vector3(frame.M21, frame.M22, frame.M23);
        var up = new Vector3(frame.M31, frame.M32, frame.M33);

        var rings = new List<(Vector3 Center, float Radius)>();

        for (var ring = 1; ring <= hemisphereRings; ring++)
        {
            var polarAngle = ring * MathF.PI / (2 * hemisphereRings);
            rings.Add((start - axis * (radius * MathF.Cos(polarAngle)), radius * MathF.Sin(polarAngle)));
        }

        var firstEndRing = start == end ? hemisphereRings - 1 : hemisphereRings;

        for (var ring = firstEndRing; ring >= 1; ring--)
        {
            var polarAngle = ring * MathF.PI / (2 * hemisphereRings);
            rings.Add((end + axis * (radius * MathF.Cos(polarAngle)), radius * MathF.Sin(polarAngle)));
        }

        var positions = new Vector3[rings.Count * segments + 2];
        var startPole = positions.Length - 2;
        var endPole = positions.Length - 1;

        for (var ring = 0; ring < rings.Count; ring++)
        {
            var (ringCenter, ringRadius) = rings[ring];

            for (var segment = 0; segment < segments; segment++)
            {
                var angle = segment * MathF.Tau / segments;
                positions[ring * segments + segment] = ringCenter + (left * MathF.Cos(angle) + up * MathF.Sin(angle)) * ringRadius;
            }
        }

        positions[startPole] = start - axis * radius;
        positions[endPole] = end + axis * radius;

        var faces = new List<int[]>((rings.Count + 1) * segments);
        var lastRing = (rings.Count - 1) * segments;

        for (var segment = 0; segment < segments; segment++)
        {
            var next = (segment + 1) % segments;

            faces.Add([startPole, next, segment]);

            for (var ring = 0; ring < lastRing; ring += segments)
            {
                faces.Add([ring + segment, ring + next, ring + segments + next, ring + segments + segment]);
            }

            faces.Add([lastRing + segment, lastRing + next, endPole]);
        }

        return (positions, faces);
    }
}
