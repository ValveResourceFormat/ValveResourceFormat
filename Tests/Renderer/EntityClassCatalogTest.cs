using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Editor.Entities;

namespace Tests.Renderer
{
    public class EntityClassCatalogTest
    {
        private const int CounterStrike2 = 730;
        private const int Dota2 = 570;
        private const int SteamVRHome = 250820;

        [Test]
        public async Task ReadsWhatHelpersAreDeclaredWith()
        {
            var light = EntityClassCatalog.GetTable(CounterStrike2).FindClass("LIGHT_OMNI2")!;

            await Assert.That(light.Kind).IsEqualTo(EntityClassKind.Point);
            await Assert.That(light.Helpers.Select(static helper => helper.Name)).IsEquivalentTo(["omnilight", "iconsprite"]);
            await Assert.That(light.Helpers[1].Metadata!.ContainsKey("image")).IsTrue();
            await Assert.That(light.FindKey("Color")?.Default).IsEqualTo("255 255 255");

            // A default is kept as the text the class declares, numbers included
            await Assert.That(light.FindKey("range")?.Default).IsEqualTo("256.0");
            await Assert.That(light.DerivesFrom("targetname")).IsTrue();
        }

        [Test]
        public async Task KeepsWhatEachGameDeclares()
        {
            await Assert.That(EntityClassCatalog.GetTable(Dota2).GridNav?.EdgeSize).IsEqualTo(64);
            await Assert.That(EntityClassCatalog.GetTable(CounterStrike2).GridNav).IsNull();

            // A class two games declare differently is the one of each game
            var games = new[] { CounterStrike2, Dota2, SteamVRHome }.Select(static appId => EntityClassCatalog.GetTable(appId).FindClass("env_beam"));

            await Assert.That(games.Distinct().Count()).IsGreaterThan(1);
        }

        [Test]
        public async Task ShowsAnyOtherGameWithTheClassesOfEveryGame()
        {
            var merged = EntityClassCatalog.GetTable(0);

            // Classes only one game declares are all there
            await Assert.That(merged.FindClass("ent_dota_tree")).IsSameReferenceAs(EntityClassCatalog.GetTable(Dota2).FindClass("ent_dota_tree"));
            await Assert.That(merged.FindClass("cs_minimap_boundary")).IsSameReferenceAs(EntityClassCatalog.GetTable(CounterStrike2).FindClass("cs_minimap_boundary"));
            await Assert.That(merged.GridNav).IsNotNull();

            // The classes kept by hand are only used where no game has a table of its own
            await Assert.That(merged.FindClass("ai_reaction")).IsNotNull();
            await Assert.That(EntityClassCatalog.GetTable(CounterStrike2).FindClass("ai_reaction")).IsNull();
        }
    }
}
