using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;

namespace Tests.IO
{
    public class EntityClassCatalogTest
    {
        [Test]
        public async Task ReadsWhatHelpersAreDeclaredWith()
        {
            var light = EntityClassCatalog.GetTable(Game.Csgo).FindClass("LIGHT_OMNI2")!;

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
            var classes = EntityClassCatalog.GetTable(Game.Csgo);

            await Assert.That(classes.FindClass("post_processing_volume")?.AutoApplyMaterial).IsEqualTo("materials/tools_postprocess_volume.vmat");
            await Assert.That(classes.FindClass("trigger_multiple")?.AutoApplyMaterial).IsEqualTo("materials/tools/toolstrigger.vmat");
            await Assert.That(classes.FindClass("trigger_multiple")?.Kind).IsEqualTo(EntityClassKind.Solid);
            await Assert.That(classes.FindClass("func_brush")?.AutoApplyMaterial).IsNull();
        }

        [Test]
        public async Task ReadsTheKeysThatNameTheAnimationPlayed()
        {
            var prop = EntityClassCatalog.GetTable(Game.Csgo).FindClass("prop_dynamic")!;

            // Keys the class takes from its bases are there, the one that matters most first
            await Assert.That(prop.PreviewAnimationKeys).IsEquivalentTo(["StartingAnim", "IdleAnim"], StringComparer.Ordinal, CollectionOrdering.Matching);
        }

        [Test]
        public async Task KeepsWhatEachGameDeclares()
        {
            await Assert.That(EntityClassCatalog.GetTable(Game.Dota).GridNav?.EdgeSize).IsEqualTo(64);
            await Assert.That(EntityClassCatalog.GetTable(Game.Csgo).GridNav).IsNull();

            // A class two games declare differently is the one of each game
            var games = new[] { Game.Csgo, Game.Dota, Game.Steamtours }.Select(static game => EntityClassCatalog.GetTable(game).FindClass("info_target"));

            await Assert.That(games.Distinct().Count()).IsGreaterThan(1);
        }

        [Test]
        public async Task ShowsAnyOtherGameWithTheClassesOfEveryGame()
        {
            var merged = EntityClassCatalog.GetTable(Game.Unknown);

            // Classes only one game declares are all there
            await Assert.That(merged.FindClass("ent_dota_tree")).IsSameReferenceAs(EntityClassCatalog.GetTable(Game.Dota).FindClass("ent_dota_tree"));
            await Assert.That(merged.FindClass("cs_minimap_boundary")).IsSameReferenceAs(EntityClassCatalog.GetTable(Game.Csgo).FindClass("cs_minimap_boundary"));
            await Assert.That(merged.GridNav).IsNotNull();

            // Where games declare a class differently, it is the version most of them declare
            var games = new[] { Game.Dota, Game.Csgo, Game.Steamtours, Game.Hlvr }.Select(EntityClassCatalog.GetTable).ToList();

            var outnumbered = merged.Classes.Where(entityClass =>
            {
                var versions = games.Select(game => game.FindClass(entityClass.Name)).OfType<EntityClass>().ToList();

                return versions.Exists(version => versions.Count(other => other == version) > versions.Count(other => other == entityClass));
            });

            await Assert.That(outnumbered.Select(static entityClass => entityClass.Name)).IsEmpty();

            // The classes kept by hand are only used where no game has a table of its own
            await Assert.That(merged.FindClass("ai_reaction")).IsNotNull();
            await Assert.That(EntityClassCatalog.GetTable(Game.Csgo).FindClass("ai_reaction")).IsNull();
        }
    }
}
