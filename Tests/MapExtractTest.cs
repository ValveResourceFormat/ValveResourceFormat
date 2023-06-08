using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace Tests
{
    [TestFixture]
    public class MapExtractTest
    {
        public class NullFileLoader : IFileLoader
        {
            public Resource LoadFile(string file) => null;
            public ShaderCollection LoadShader(string shaderName) => null;
        }

        [Test]
        public void TestMapExtractVmapInit()
        {
            using var vmapResource = new Resource();
            vmapResource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "dota.vmap_c"));

            var exception = Assert.Throws<InvalidDataException>(() => new MapExtract(vmapResource, new NullFileLoader()));
            Assert.That(exception.Message, Contains.Substring("filename does not match"));
            Assert.That(exception.Message, Contains.Substring("RERL-derived lump folder"));

            //var extract = new MapExtract(vmapResource, null);
            //Assert.AreEqual(extract.LumpFolder, Path.Combine("maps", "dota"));
        }

        [Test]
        public void TestMapExtractVwrldInit()
        {
            using var worldResource = new Resource();
            var worldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "world.vwrld_c");
            worldResource.Read(worldPath);

            var exception = Assert.Throws<ArgumentNullException>(() => new MapExtract(worldResource, null));
            Assert.That(exception.Message, Contains.Substring("file loader must be provided to load the map's lumps"));

            var extract = new MapExtract(worldResource, new NullFileLoader());
            Assert.AreEqual(extract.LumpFolder, Path.GetDirectoryName(worldPath));

            extract.ToValveMap();

            //var contentFile = extract.ToContentFile();
            //Assert.That(contentFile, Is.Not.Null);
            //Assert.That(contentFile.Data, Is.Not.Null);
            //Assert.That(contentFile.Data.Length, Is.GreaterThan(0));
        }

        [Test]
        public void TestHalfEdgeGenerator()
        {
            HammerMeshBuilder builder = new();
            builder.AddFace("materials/dev/reflectivity_30.vmat",
                builder.AddVertex(new Vector3(160, 0, 0)),
                builder.AddVertex(new Vector3(224, 0, 0)),
                builder.AddVertex(new Vector3(224, 256, 128)),
                builder.AddVertex(new Vector3(160, 256, 128))
            );

            var mesh = builder.GenerateMesh();

            Assert.That(mesh.VertexEdgeIndices, Is.EquivalentTo(new[] { 0, 2, 4, 6 }));
            Assert.That(mesh.VertexDataIndices, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));

            Assert.That(mesh.EdgeVertexIndices, Is.EquivalentTo(new[] { 1, 0, 2, 1, 3, 2, 0, 3 }));
            Assert.That(mesh.EdgeOppositeIndices, Is.EquivalentTo(new[] { 1, 0, 3, 2, 5, 4, 7, 6 }));
            Assert.That(mesh.EdgeNextIndices, Is.EquivalentTo(new[] { 2, 7, 4, 1, 6, 3, 0, 5 }));
            Assert.That(mesh.EdgeFaceIndices, Is.EquivalentTo(new[] { 0, -1, 0, -1, 0, -1, 0, -1 }));
            Assert.That(mesh.EdgeDataIndices, Is.EquivalentTo(new[] { 0, 0, 1, 1, 2, 2, 3, 3 }));
            Assert.That(mesh.EdgeVertexDataIndices, Is.EquivalentTo(new[] { 1, 4, 2, 5, 3, 6, 0, 7 }));

            Assert.That(mesh.FaceEdgeIndices, Is.EquivalentTo(new[] { 6 }));
            Assert.That(mesh.FaceDataIndices, Is.EquivalentTo(new[] { 0 }));

        }
    }
}
