using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Renderer
{
    public class PolygonMeshRenderDataTest
    {
        private static Material.VsInputSignature MakeSignature(params (string Semantic, string D3DSemanticName, int D3DSemanticIndex)[] inputs)
        {
            var elements = KVObject.Array();

            foreach (var (semantic, d3dSemanticName, d3dSemanticIndex) in inputs)
            {
                var element = new KVObject();
                element.Add("m_pName", $"v{semantic}");
                element.Add("m_pSemantic", semantic);
                element.Add("m_pD3DSemanticName", d3dSemanticName);
                element.Add("m_nD3DSemanticIndex", d3dSemanticIndex);
                elements.Add(element);
            }

            var data = new KVObject();
            data.Add("m_elems", elements);

            return new Material.VsInputSignature(data);
        }

        private static PolygonMesh.BakedSubmesh MakePaintedQuad()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            var stored = builder.GenerateMesh();
            var lighting = new Datamodel.Vector4Array();
            lighting.AddRange(Enumerable.Repeat(new Vector4(0.5f, 0.25f, 0.125f, 1f), stored.FaceVertexData.Size));

            stored.FaceVertexData.Streams.Add(new CDmePolygonMeshDataStream
            {
                Name = "PerVertexLighting:0",
                SemanticName = "PerVertexLighting",
                VertexBufferLocation = 1,
                DataStateFlags = 1,
                Data = lighting,
            });

            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);

            return mesh.Triangulate().Single();
        }

        private static string Describe(VBIB.OnDiskBufferData buffer)
            => string.Join(' ', buffer.InputLayoutFields.Select(static field => $"{field.SemanticName}{field.SemanticIndex}@{field.Offset}"));

        /// <summary>
        /// The streams of a mesh go into one vertex buffer per location they name, under the semantic the material
        /// has an input for. A stream the material has no input for is left out.
        /// </summary>
        [Test]
        public async Task PacksTheStreamsTheMaterialReadsIntoTheBufferTheyName()
        {
            var submesh = MakePaintedQuad();

            var blend = MakeSignature(
                ("PosXyz", "POSITION", 0),
                ("LowPrecisionUv", "TEXCOORD", 0),
                ("VertexPaintBlendParams", "TEXCOORD", 4),
                ("VertexPaintTintColor", "COLOR", 0),
                ("PerVertexLighting", "COLOR", 1));

            var (buffers, bounds) = PolygonMeshRenderData.BuildVertexAndIndexBuffers(submesh, blend);
            var lighting = MemoryMarshal.Cast<byte, float>(buffers.VertexBuffers[1].Data).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(buffers.VertexBuffers.Count).IsEqualTo(2);
                await Assert.That(Describe(buffers.VertexBuffers[0])).IsEqualTo("POSITION0@0 NORMAL0@12 TANGENT0@24 TEXCOORD0@40");
                await Assert.That(Describe(buffers.VertexBuffers[1])).IsEqualTo("TEXCOORD4@0 COLOR0@16 COLOR1@32");
                await Assert.That(buffers.VertexBuffers[0].ElementSizeInBytes).IsEqualTo(48u);
                await Assert.That(buffers.VertexBuffers.All(static buffer => buffer.ElementCount == 4)).IsTrue();
                await Assert.That(buffers.IndexBuffers.Single().ElementCount).IsEqualTo(6u);
                await Assert.That(bounds.Max).IsEqualTo(new Vector3(4, 4, 0));

                // Paint that was never applied reads as no tint, the lighting is what the mesh stores
                await Assert.That(lighting[4]).IsEqualTo(1f);
                await Assert.That(lighting[8]).IsEqualTo(0.5f);
            }

            var plain = MakeSignature(("PosXyz", "POSITION", 0), ("LowPrecisionUv", "TEXCOORD", 0));
            var (plainBuffers, _) = PolygonMeshRenderData.BuildVertexAndIndexBuffers(submesh, plain);

            using (Assert.Multiple())
            {
                await Assert.That(plainBuffers.VertexBuffers.Count).IsEqualTo(1);
                await Assert.That(Describe(plainBuffers.VertexBuffers[0])).IsEqualTo("POSITION0@0 NORMAL0@12 TANGENT0@24 TEXCOORD0@40");
            }
        }
    }
}
