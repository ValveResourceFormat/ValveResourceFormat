using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class MorphTest
    {
        [Test]
        public async Task LoadsFlexDataFromAtlasTexture()
        {
            var vpkPath = Path.Combine(TestContext.TestDirectory!, "Files", "rubick_head_morph.vpk");

            using var package = new Package();
            package.Read(vpkPath);

            using var loader = new GameFileLoader(package, vpkPath);
            using var resource = loader.LoadFileCompiled("models/heroes/rubick/rubick_head_model.vmorf");
            var morph = (Morph)resource!.DataBlock!;

            morph.LoadFlexData(loader);

            using (Assert.Multiple())
            {
                await Assert.That(morph.TextureResource).IsNotNull();
                await Assert.That(morph.FlexControllers!.Select(c => c.Name)).IsEquivalentTo(["rubick_head_blink_L", "rubick_head_blink_R"]);
                await Assert.That(morph.FlexControllers!.Select(c => (c.Min, c.Max))).All(range => range == (0f, 1f));
                await Assert.That(morph.FlexRules!.Length).IsEqualTo(2);
            }

            var flexVertexData = morph.GetFlexVertexData();

            using (Assert.Multiple())
            {
                await Assert.That(flexVertexData.Keys).IsEquivalentTo(["rubick_head_blink_L", "rubick_head_blink_R"]);

                foreach (var (name, vertices) in flexVertexData)
                {
                    await Assert.That(vertices.Length).IsEqualTo(672);
                    await Assert.That(vertices.Count(v => v != Vector3.Zero)).IsEqualTo(112);
                }
            }
        }
    }
}
