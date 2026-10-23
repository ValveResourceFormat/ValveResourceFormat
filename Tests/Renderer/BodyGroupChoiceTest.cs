using System.Threading.Tasks;
using ValveResourceFormat.Renderer.SceneNodes;

namespace Tests.Renderer
{
    public class BodyGroupChoiceTest
    {
        [Test]
        public async Task MeshGroupsNameTheChoicesOfBodyGroups()
        {
            using (Assert.Multiple())
            {
                await Assert.That(BodyGroupChoice.Parse("helmet_@1")).IsEqualTo(new BodyGroupChoice("helmet_@1", "helmet", 1, null));
                await Assert.That(BodyGroupChoice.Parse("hat_@2_#&cap")).IsEqualTo(new BodyGroupChoice("hat_@2_#&cap", "hat", 2, "cap"));
                await Assert.That(BodyGroupChoice.Parse("default")).IsNull();
                await Assert.That(BodyGroupChoice.Parse("_@1")).IsNull();
                await Assert.That(BodyGroupChoice.Parse("body_@x")).IsNull();
            }
        }
    }
}
