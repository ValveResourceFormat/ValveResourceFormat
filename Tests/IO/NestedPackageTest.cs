using System.IO;
using System.Threading.Tasks;
using ValvePak;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    public class NestedPackageTest
    {
        [Test]
        public async Task ReadsPackagesInsidePackages()
        {
            var text = "inner"u8.ToArray();
            var outerData = CreatePackage("maps/middle.vpk", CreatePackage("maps/inner.vpk", CreatePackage("materials/a.txt", text)));

            using var outer = new Package();
            outer.SetFileName("outer.vpk");
            outer.Read(new MemoryStream(outerData));

            var middleEntry = outer.FindEntry("maps/middle.vpk")!;
            using var middle = NestedPackage.Open(outer, middleEntry);
            using var inner = NestedPackage.Open(middle, middle.FindEntry("maps/inner.vpk")!);
            using var otherMiddle = NestedPackage.Open(outer, middleEntry);

            inner.ReadEntry(inner.FindEntry("materials/a.txt")!, out var data);

            using var loader = new GameFileLoader(null, null);
            loader.AddPackageToSearch(inner);
            loader.AddPackageToSearch(otherMiddle);

            using (Assert.Multiple())
            {
                await Assert.That(data).IsEquivalentTo(text);
                await Assert.That(loader.TryMountMapPackage("maps/inner.vpk")).IsTrue();
                await Assert.That(loader.FindFile("materials/a.txt").Package).IsSameReferenceAs(inner);
            }
        }

        private static byte[] CreatePackage(string filePath, byte[] fileData)
        {
            using var package = new Package();
            package.AddFile(filePath, fileData);

            using var stream = new MemoryStream();
            package.Write(stream);

            return stream.ToArray();
        }
    }
}
