using System.Numerics;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer;

namespace Tests.Renderer
{
    public class ProjectedDecalTest
    {
        private const float Tolerance = 1e-4f;

        // What the decal shader computes before discarding: box units with the origin in a corner,
        // and the texture V flipped so that the box's +Y edge is the top of the texture.
        private static Vector3 GetDecalCoords(Matrix4x4 boxTransform, Vector3 positionWs)
        {
            Matrix4x4.Invert(boxTransform, out var worldToBox);
            var box = Vector3.Transform(positionWs, worldToBox) + new Vector3(0.5f);
            return new Vector3(box.X, 1f - box.Y, box.Z);
        }

        [Test]
        public async Task BoxTransformOrientsTextureOnSurface()
        {
            // A wall facing +X, so a viewer in front of it looks down -X with +Y on their right
            var center = new Vector3(100f, 20f, 50f);
            var normal = Vector3.UnitX;
            var up = Vector3.UnitZ;
            var size = new Vector3(4f, 6f, 12f);

            var transform = ProjectedDecalSystem.CreateBoxTransform(center, normal, up, size);
            var viewerRight = Vector3.UnitY;

            var atCenter = GetDecalCoords(transform, center);
            var atTop = GetDecalCoords(transform, center + up * (size.Y / 2f));
            var atRight = GetDecalCoords(transform, center + viewerRight * (size.X / 2f));
            var atOuterFace = GetDecalCoords(transform, center + normal * (size.Z / 2f));

            using (Assert.Multiple())
            {
                await Assert.That(Vector3.Distance(atCenter, new Vector3(0.5f))).IsLessThan(Tolerance);

                await Assert.That(atTop.Y).IsEqualTo(0f).Within(Tolerance);
                await Assert.That(atRight.X).IsEqualTo(1f).Within(Tolerance);
                await Assert.That(atOuterFace.Z).IsEqualTo(1f).Within(Tolerance);
            }
        }

        [Test]
        public async Task BoxTransformFlattensUpOntoSurface()
        {
            var normal = Vector3.Normalize(new Vector3(0f, 1f, 1f));
            var transform = ProjectedDecalSystem.CreateBoxTransform(Vector3.Zero, normal, Vector3.UnitZ, Vector3.One);

            var axisY = Vector3.Normalize(new Vector3(transform.M21, transform.M22, transform.M23));
            var axisZ = Vector3.Normalize(new Vector3(transform.M31, transform.M32, transform.M33));

            using (Assert.Multiple())
            {
                await Assert.That(Vector3.Dot(axisY, axisZ)).IsEqualTo(0f).Within(Tolerance);
                await Assert.That(Vector3.Dot(axisZ, normal)).IsEqualTo(1f).Within(Tolerance);
                await Assert.That(axisY.Z).IsGreaterThan(0f);
            }
        }
    }
}
