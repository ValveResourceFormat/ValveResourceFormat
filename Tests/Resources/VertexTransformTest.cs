using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;

namespace Tests.Resources
{
    public class VertexTransformTest
    {
        private static readonly Vector3[] Directions =
        [
            Vector3.UnitX,
            -Vector3.UnitY,
            Vector3.UnitZ,
            -Vector3.UnitZ,
            Vector3.Normalize(new Vector3(1f, 2f, 3f)),
            Vector3.Normalize(new Vector3(-3f, 1f, -2f)),
            Vector3.Normalize(new Vector3(0.2f, -0.7f, -0.1f)),
        ];

        private static VBIB.OnDiskBufferData CreateBuffer<T>(T[] elements, params VBIB.RenderInputLayoutField[] fields) where T : unmanaged => new()
        {
            ElementCount = (uint)elements.Length,
            ElementSizeInBytes = (uint)Marshal.SizeOf<T>(),
            InputLayoutFields = fields,
            Data = MemoryMarshal.AsBytes(elements.AsSpan()).ToArray(),
        };

        private static Vector3 AnyPerpendicular(Vector3 normal)
            => Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));

        [Test]
        public async Task CompressNormalRoundTrips()
        {
            foreach (var direction in Directions)
            {
                foreach (var upperHalf in (bool[])[false, true])
                {
                    var (x, y) = VBIB.CompressNormal(direction, upperHalf);
                    var buffer = CreateBuffer([(uint)(x | y << 8 | x << 16 | y << 24)], new VBIB.RenderInputLayoutField("NORMAL", DXGI_FORMAT.R8G8B8A8_UNORM, 0));
                    var (normals, tangents) = VBIB.GetNormalTangentArray(buffer, buffer.InputLayoutFields[0]);

                    await Assert.That(Vector3.Dot(normals[0], direction)).IsGreaterThan(0.999f);
                    await Assert.That(tangents[0].W).IsEqualTo(upperHalf ? 1f : -1f);
                }
            }
        }

        [Test]
        public async Task CompressNormalTangent2RoundTrips()
        {
            foreach (var normal in Directions)
            {
                foreach (var sign in (float[])[-1f, 1f])
                {
                    var tangent = Vector3.Normalize(Vector3.Lerp(AnyPerpendicular(normal), Vector3.Cross(normal, AnyPerpendicular(normal)), 0.3f));
                    var packed = VBIB.CompressNormalTangent2(normal, new Vector4(tangent, sign));
                    var buffer = CreateBuffer([packed], new VBIB.RenderInputLayoutField("NORMAL", DXGI_FORMAT.R32_UINT, 0));
                    var (normals, tangents) = VBIB.GetNormalTangentArray(buffer, buffer.InputLayoutFields[0]);

                    await Assert.That(Vector3.Dot(normals[0], normal)).IsGreaterThan(0.9999f);
                    await Assert.That(Vector3.Dot(new Vector3(tangents[0].X, tangents[0].Y, tangents[0].Z), tangent)).IsGreaterThan(0.999f);
                    await Assert.That(tangents[0].W).IsEqualTo(sign);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector4 Tangent;
            public Vector2 TexCoord;
        }

        private static readonly VBIB.RenderInputLayoutField[] VertexFields =
        [
            new("POSITION", DXGI_FORMAT.R32G32B32_FLOAT, 0),
            new("NORMAL", DXGI_FORMAT.R32G32B32_FLOAT, 12),
            new("TANGENT", DXGI_FORMAT.R32G32B32A32_FLOAT, 24),
            new("TEXCOORD", DXGI_FORMAT.R32G32_FLOAT, 40),
        ];

        [Test]
        public async Task TransformVertexAttributesMirrorsAndKeepsOnlyTransformedFields()
        {
            var normal = Vector3.Normalize(new Vector3(1f, 1f, 0f));
            var buffer = CreateBuffer(
            [
                new Vertex { Position = new(1f, 2f, 3f), Normal = normal, Tangent = new(0f, 0f, 1f, 1f), TexCoord = new(0.5f, 0.5f) },
                new Vertex { Position = new(-4f, 5f, 6f), Normal = normal, Tangent = new(0f, 0f, 1f, -1f) },
            ], VertexFields);

            var mirror = Matrix4x4.CreateScale(-2f, 1f, 1f);
            var map = VertexAttributeMap.FromMatrix(mirror * Matrix4x4.CreateTranslation(Vector3.UnitZ));
            var transformed = VBIB.TransformVertexAttributes([buffer], map);

            await Assert.That(transformed).IsNotNull();

            var fields = transformed!.Value.InputLayoutFields;

            await Assert.That(map.Mirrored).IsTrue();
            await Assert.That(fields.Select(static f => f.SemanticName).ToArray()).IsEquivalentTo(["POSITION", "NORMAL", "TANGENT"]);
            await Assert.That(transformed.Value.ElementSizeInBytes).IsEqualTo(40u);

            var positions = VBIB.GetVector3AttributeArray(transformed.Value, fields[0]);
            var normals = VBIB.GetVector3AttributeArray(transformed.Value, fields[1]);
            var tangents = VBIB.GetVector4AttributeArray(transformed.Value, fields[2]);
            var expectedNormal = Vector3.Normalize(new Vector3(-0.5f, 1f, 0f));

            await Assert.That(positions[0]).IsEqualTo(new Vector3(-2f, 2f, 4f));
            await Assert.That(positions[1]).IsEqualTo(new Vector3(8f, 5f, 7f));
            await Assert.That(Vector3.Distance(normals[0], expectedNormal)).IsLessThan(1e-5f);
            await Assert.That(tangents[0]).IsEqualTo(new Vector4(0f, 0f, 1f, -1f));
            await Assert.That(tangents[1]).IsEqualTo(new Vector4(0f, 0f, 1f, 1f));
        }

        [Test]
        public async Task TransformVertexAttributesReadsDirectionsFromOtherBuffers()
        {
            var positions = CreateBuffer([Vector3.Zero, Vector3.One], new VBIB.RenderInputLayoutField("POSITION", DXGI_FORMAT.R32G32B32_FLOAT, 0));
            var normals = CreateBuffer([Vector3.UnitX, Vector3.UnitY], new VBIB.RenderInputLayoutField("NORMAL", DXGI_FORMAT.R32G32B32_FLOAT, 0));
            var otherCount = CreateBuffer([new Vector4(1f, 0f, 0f, 1f)], new VBIB.RenderInputLayoutField("TANGENT", DXGI_FORMAT.R32G32B32A32_FLOAT, 0));

            var rotation = Matrix4x4.CreateRotationZ(MathF.PI / 2f);
            var map = VertexAttributeMap.FromMatrix(rotation);
            var result = VBIB.TransformVertexAttributes([normals, positions, otherCount], map);

            await Assert.That(result).IsNotNull();

            var transformed = result!.Value;
            var fields = transformed.InputLayoutFields;
            var transformedNormals = VBIB.GetVector3AttributeArray(transformed, fields[1]);

            await Assert.That(map.Mirrored).IsFalse();
            await Assert.That(fields.Select(static f => f.SemanticName).ToArray()).IsEquivalentTo(["POSITION", "NORMAL"]);
            await Assert.That(Vector3.Distance(transformedNormals[0], Vector3.UnitY)).IsLessThan(1e-5f);
            await Assert.That(Vector3.Distance(transformedNormals[1], -Vector3.UnitX)).IsLessThan(1e-5f);
        }

        [Test]
        public async Task TransformVertexAttributesNeedsAPosition()
        {
            var normals = CreateBuffer([Vector3.UnitX], new VBIB.RenderInputLayoutField("NORMAL", DXGI_FORMAT.R32G32B32_FLOAT, 0));

            await Assert.That(VBIB.TransformVertexAttributes([normals], VertexAttributeMap.FromMatrix(Matrix4x4.Identity))).IsNull();
        }
    }
}
