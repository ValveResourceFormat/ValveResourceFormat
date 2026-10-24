using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.Renderer.Editor.Entities;

namespace Tests.Renderer
{
    public class EntityClassCatalogTest
    {
        private const int CounterStrike2 = 730;
        private const int Dota2 = 570;
        private const int SteamVRHome = 250820;
        private const int HalfLifeAlyx = 546560;

        [Test]
        public async Task ReadsWhatHelpersAreDeclaredWith()
        {
            var light = EntityClassCatalog.GetTable(CounterStrike2).FindClass("LIGHT_OMNI2")!;

            await Assert.That(light.Kind).IsEqualTo(EntityClassKind.Point);
            await Assert.That(light.Helpers.Select(static helper => helper.Name)).IsEquivalentTo(["omnilight", "iconsprite"], StringComparer.Ordinal);
            await Assert.That(light.Helpers[1].Metadata!.ContainsKey("image")).IsTrue();
            await Assert.That(light.PreviewAnimationKeys).IsEmpty();

            // Only the classes an entity can be required to be placed under are kept of what a class is based on
            await Assert.That(light.DerivesFrom("light_omni2")).IsTrue();
            await Assert.That(light.DerivesFrom("targetname")).IsFalse();
        }

        [Test]
        public async Task ReadsTheMaterialAClassDrawsItsMeshesWith()
        {
            var classes = EntityClassCatalog.GetTable(CounterStrike2);

            await Assert.That(classes.FindClass("post_processing_volume")?.AutoApplyMaterial).IsEqualTo("materials/tools_postprocess_volume.vmat");
            await Assert.That(classes.FindClass("trigger_multiple")?.AutoApplyMaterial).IsEqualTo("materials/tools/toolstrigger.vmat");
            await Assert.That(classes.FindClass("trigger_multiple")?.Kind).IsEqualTo(EntityClassKind.Solid);
            await Assert.That(classes.FindClass("func_brush")?.AutoApplyMaterial).IsNull();
        }

        [Test]
        public async Task ReadsTheKeysThatNameTheAnimationPlayed()
        {
            var prop = EntityClassCatalog.GetTable(CounterStrike2).FindClass("prop_dynamic")!;

            // Keys the class takes from its bases are there, the one that matters most first
            await Assert.That(prop.PreviewAnimationKeys).IsEquivalentTo(["StartingAnim", "IdleAnim"], StringComparer.Ordinal, CollectionOrdering.Matching);
        }

        [Test]
        public async Task KeepsWhatEachGameDeclares()
        {
            await Assert.That(EntityClassCatalog.GetTable(Dota2).GridNav?.EdgeSize).IsEqualTo(64);
            await Assert.That(EntityClassCatalog.GetTable(CounterStrike2).GridNav).IsNull();

            // A class two games declare differently is the one of each game
            var games = new[] { CounterStrike2, Dota2, SteamVRHome }.Select(static appId => EntityClassCatalog.GetTable(appId).FindClass("info_target"));

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

            // Where games declare a class differently, it is the version most of them declare
            var games = new[] { Dota2, CounterStrike2, SteamVRHome, HalfLifeAlyx }.Select(EntityClassCatalog.GetTable).ToList();

            var outnumbered = merged.Classes.Where(entityClass =>
            {
                var versions = games.Select(game => game.FindClass(entityClass.Name)).OfType<EntityClass>().ToList();

                return versions.Exists(version => versions.Count(other => other == version) > versions.Count(other => other == entityClass));
            });

            await Assert.That(outnumbered.Select(static entityClass => entityClass.Name)).IsEmpty();

            // The classes kept by hand are only used where no game has a table of its own
            await Assert.That(merged.FindClass("ai_reaction")).IsNotNull();
            await Assert.That(EntityClassCatalog.GetTable(CounterStrike2).FindClass("ai_reaction")).IsNull();
        }
    }
}
