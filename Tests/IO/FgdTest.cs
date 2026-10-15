using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace Tests.IO
{
    public class FgdTest
    {
        private static ForgeGameData LoadSample()
        {
            var fgd = new ForgeGameData();

            if (!fgd.Load(TestFixtures.Path("fgd", "sample.fgd")))
            {
                throw new InvalidDataException(string.Join('\n', fgd.Errors.Select(static error => $"{error.FileName}({error.Line}): {error.Message}")));
            }

            fgd.ResolveBaseClasses();
            return fgd;
        }

        private static ForgeGameData Parse(string text)
        {
            var fgd = new ForgeGameData();

            fgd.LoadText(text, "test.fgd");
            fgd.ResolveBaseClasses();
            return fgd;
        }

        [Test]
        public async Task ReadsAFileAndWhatItIncludes()
        {
            var fgd = LoadSample();

            await Assert.That(fgd.Errors).IsEmpty();
            await Assert.That(string.Join(' ', fgd.Classes.Select(static gameClass => gameClass.Name)))
                .IsEqualTo("Targetname Sized Uncolored sample_point sample_brush");
            await Assert.That(fgd.EntityGroups).IsEquivalentTo([new FgdEntityGroup("Samples", true)]);
            await Assert.That(fgd.GridNav).IsEqualTo((64, 0, 0, 128));
        }

        [Test]
        public async Task AClassNotDeclaredIsLookedUpInTheFallback()
        {
            var fgd = Parse("@PointClass color(1 2 3) = shared [] @PointClass = own []");
            fgd.Fallback = Parse("@PointClass color(4 5 6) = shared [] @PointClass = fallback_only []");

            await Assert.That(fgd.FindClass("SHARED")!.Color).IsEqualTo(new Color32(1, 2, 3));
            await Assert.That(fgd.FindClass("fallback_only")).IsNotNull();
            await Assert.That(fgd.FindClass("missing")).IsNull();
            await Assert.That(fgd.Classes.Select(static gameClass => gameClass.Name)).IsEquivalentTo(["shared", "own"]);
        }

        [Test]
        public async Task HelpersAreKeptInOrderAndNotTakenFromBaseClasses()
        {
            var gameClass = LoadSample().FindClass("SAMPLE_POINT")!;

            // The icon and the sphere of the class it is based on are not among them
            await Assert.That(string.Join(' ', gameClass.Helpers.Select(static helper => helper.Name)))
                .IsEqualTo("editormodel line sphere cubemap");

            // Every token but the commas is an argument
            await Assert.That(string.Join('|', gameClass.Helpers[1].Arguments)).IsEqualTo("255|0|255|targetname|target");

            var sphere = gameClass.Helpers[2];

            await Assert.That(sphere.Arguments).IsEmpty();
            await Assert.That(sphere.Metadata!.GetStringProperty("radius")).IsEqualTo("reach");
        }

        [Test]
        public async Task AClassTakesTheRestFromTheClassesItIsBasedOn()
        {
            var fgd = LoadSample();
            var gameClass = fgd.FindClass("sample_point")!;

            await Assert.That(gameClass.Type).IsEqualTo(FgdClassType.PointClass);
            await Assert.That(gameClass.HalfGridSnap).IsTrue();
            await Assert.That(gameClass.Description).IsEqualTo("A point entity that shows everything.");
            await Assert.That(gameClass.Metadata.GetStringProperty("entity_tool_name")).IsEqualTo("Sample");

            // The size is the one of the base class, as the class declares none
            await Assert.That(gameClass.HasSize).IsTrue();
            await Assert.That(gameClass.Mins).IsEqualTo(new Vector3(-4f, -4f, 0f));
            await Assert.That(gameClass.Maxs).IsEqualTo(new Vector3(4f, 4f, 16f));

            // Tags are told apart without regard to case, and the kind of class adds one
            await Assert.That(string.Join(' ', gameClass.Tags)).IsEqualTo("Sample base point");

            await Assert.That(string.Join(' ', gameClass.Variables.Select(static variable => variable.Name)))
                .IsEqualTo("targetname radius kind target model texture items reach hinge important spawnflags quoted added");
            await Assert.That(string.Join(' ', gameClass.Inputs.Select(static input => input.Name))).IsEqualTo("Kill Enable SetReach");
            await Assert.That(string.Join(' ', gameClass.Outputs.Select(static output => output.Name))).IsEqualTo("OnUsed OnScript");

            // A class that declares no color takes the one of the first class it is based on, even where
            // that one never declared any
            var brush = fgd.FindClass("sample_brush")!;

            await Assert.That(brush.HasColor).IsTrue();
            await Assert.That(brush.Color).IsEqualTo(FgdClass.DefaultColor);
            await Assert.That(brush.Description).IsEqualTo("A description over\ntwo lines.");
            await Assert.That(string.Join(' ', brush.Tags)).IsEqualTo("solid");
        }

        [Test]
        public async Task AnOverrideAddsToTheClassAndReplacesWhatItDeclaresAgain()
        {
            var gameClass = LoadSample().FindClass("sample_point")!;

            // The helper of the same name takes the arguments of the override, in its old place
            await Assert.That(string.Join('|', gameClass.Helpers[0].Arguments)).IsEqualTo("models/editor/other");

            // Its color is lost again to the first class the class is based on, as the class itself declared none
            await Assert.That(gameClass.Color).IsEqualTo(FgdClass.DefaultColor);
            await Assert.That(Parse("@PointClass = thing [] @OverrideClass color(10 20 30) = thing []").FindClass("thing")!.Color)
                .IsEqualTo(new Color32(10, 20, 30));

            var target = gameClass.FindVariable("target")!;

            await Assert.That(target.Type).IsEqualTo(FgdVariableType.String);
            await Assert.That(target.LongName).IsEqualTo("Target Name");
            await Assert.That(gameClass.FindVariable("plain")).IsNull();
            await Assert.That(gameClass.FindVariable("added")!.DefaultString).IsEqualTo("1");
        }

        [Test]
        public async Task VariablesAreReadWithTheirTypesAndDefaults()
        {
            var gameClass = LoadSample().FindClass("sample_point")!;

            var model = gameClass.FindVariable("model")!;

            await Assert.That(model.TypeName).IsEqualTo("studio");
            await Assert.That(model.Type).IsEqualTo(FgdVariableType.Resource);
            await Assert.That(model.SubType).IsEqualTo("vmdl");
            await Assert.That(model.DefaultString).IsEqualTo("models/sample.vmdl");
            await Assert.That(model.Description).IsEqualTo("The model to draw.");

            await Assert.That(gameClass.FindVariable("texture")!.SubType).IsEqualTo("vtex");

            var items = gameClass.FindVariable("items")!;

            await Assert.That(items.DeclaredType).IsEqualTo(FgdVariableType.Array);
            await Assert.That(items.Type).IsEqualTo(FgdVariableType.Struct);
            await Assert.That(items.SubType).IsEqualTo("sample_item");

            var reach = gameClass.FindVariable("reach")!;

            await Assert.That(reach.Metadata.GetStringProperty("group")).IsEqualTo("Size");
            await Assert.That(reach.Metadata.GetStringProperty("min")).IsEqualTo("0");
            await Assert.That(reach.Metadata.GetBooleanProperty("readonly")).IsTrue();

            var important = gameClass.FindVariable("important")!;

            await Assert.That(important.Metadata.GetBooleanProperty("report")).IsTrue();
            await Assert.That(important.DefaultInteger).IsEqualTo(3);

            await Assert.That(gameClass.FindVariable("hinge")!.Metadata.GetBooleanProperty("editor_only")).IsTrue();
            await Assert.That(gameClass.FindVariable("quoted")!.DefaultString).IsEqualTo("say \"hi\"\tnow");

            // Two colons in a row leave the default out
            var name = gameClass.FindVariable("targetname")!;

            await Assert.That(name.HasDefault).IsFalse();
            await Assert.That(name.Description).IsEqualTo("The name other entities refer to this entity by.");
        }

        [Test]
        public async Task ListsOfValuesGiveTheVariableItsDefault()
        {
            var gameClass = LoadSample().FindClass("sample_point")!;

            // The flags that are on
            var flags = gameClass.FindVariable("spawnflags")!;

            await Assert.That(string.Join(' ', flags.Choices.Select(static choice => $"{choice.Value}:{choice.Caption}:{choice.IsDefault}")))
                .IsEqualTo("1:First:True 2:Second:False 4:Third:True");
            await Assert.That(flags.DefaultString).IsEqualTo("1|4");

            // A default that names a value by what it is shown as becomes that value
            var kind = gameClass.FindVariable("kind")!;

            await Assert.That(kind.DefaultString).IsEqualTo("2");
            await Assert.That(kind.Choices[0].Description).IsEqualTo("Even more of it.");
        }

        [Test]
        public async Task InputsAndOutputsAreReadWithTheirTypes()
        {
            var gameClass = LoadSample().FindClass("sample_point")!;

            var setReach = gameClass.Inputs.Single(static input => input.Name == "SetReach");

            await Assert.That(setReach.Type).IsEqualTo(FgdInputOutputType.Float);
            await Assert.That(setReach.Metadata.GetBooleanProperty("is_scripted")).IsTrue();
            await Assert.That(setReach.Metadata.GetStringProperty("originating_classname")).IsEqualTo("sample_point");
            await Assert.That(gameClass.Inputs[1].Description).IsEqualTo("Turns it on.");
            await Assert.That(gameClass.Outputs.Single(static output => output.Name == "OnScript").IsApi).IsTrue();
        }

        [Test]
        public async Task AVariableWithoutALongNameIsDescribedByItsName()
        {
            var fgd = Parse("@PointClass = thing [ plain(integer) ]");
            var plain = fgd.FindClass("thing")!.FindVariable("plain")!;

            await Assert.That(plain.LongName).IsEmpty();
            await Assert.That(plain.Description).IsEqualTo("plain");
        }

        [Test]
        public async Task ASizeOnItsOwnIsCenteredOnTheEntity()
        {
            var gameClass = Parse("@PointClass size(16 16 32) = thing []").FindClass("thing")!;

            await Assert.That(gameClass.Mins).IsEqualTo(new Vector3(-8f, -8f, -16f));
            await Assert.That(gameClass.Maxs).IsEqualTo(new Vector3(8f, 8f, 16f));
        }

        [Test]
        public async Task CommentsAreSkippedBetweenTokens()
        {
            var fgd = Parse("""
                // A comment that goes on \
                @PointClass = not_a_class []
                @PointClass /* in the middle */ = thing : "not // a comment" []
                """);

            await Assert.That(fgd.Errors).IsEmpty();
            await Assert.That(fgd.Classes.Select(static gameClass => gameClass.Name)).IsEquivalentTo(["thing"]);
            await Assert.That(fgd.Classes[0].Description).IsEqualTo("not // a comment");
        }

        [Test]
        public async Task WhatIsWrongIsReportedAndPassedOver()
        {
            var fgd = Parse("""
                @PointClass = first []
                @mapbounds( 1 )
                @PointClass base(Missing) = second []
                @PointClass = third [ value(not_a_type) : "Value" ]
                stray
                @VisGroupFilter { filter_type = "nothing" group = "Group" }
                @PointClass = fourth [ value(integer) = [ 1 ] ]
                @PointClass = fifth []
                """);

            await Assert.That(string.Join(' ', fgd.Classes.Select(static gameClass => gameClass.Name))).IsEqualTo("first second third fifth");
            await Assert.That(fgd.Errors[0].Message).IsEqualTo("Unrecognized section name mapbounds");
            await Assert.That(fgd.Errors[0].Line).IsEqualTo(2);
            await Assert.That(fgd.Errors.Select(static error => error.Message)).Contains("Expected '@'");

            var value = fgd.FindClass("third")!.FindVariable("value")!;

            await Assert.That(value.Type).IsEqualTo(FgdVariableType.Invalid);
            await Assert.That(value.TypeName).IsEqualTo("not_a_type");
        }

        [Test]
        public async Task ClassesCannotBeDeclaredTwiceOrOverriddenBeforeTheyExist()
        {
            await Assert.That(Parse("@PointClass = thing [] @PointClass = THING []").Errors.Single().Message)
                .IsEqualTo("Error, duplicate class 'THING'. Use @OverrideClass to add keys to an existing class.");

            await Assert.That(Parse("@OverrideClass = thing []").Errors.Single().Message)
                .IsEqualTo("'OverrideClass' was specified but class 'thing' has not been previously defined.");
        }

        [Test]
        public async Task WhatALaterFileAddsToABaseClassReachesTheClassesBasedOnIt()
        {
            var fgd = new ForgeGameData();

            fgd.LoadText("@BaseClass = base [ kept(string) ] @PointClass base(base) = thing []", "first.fgd");
            fgd.ResolveBaseClasses();
            fgd.LoadText("@OverrideClass = base [ added(string) kept(integer) ]", "second.fgd");
            fgd.ResolveBaseClasses();

            var thing = fgd.FindClass("thing")!;

            await Assert.That(fgd.Errors).IsEmpty();
            await Assert.That(thing.FindVariable("added")).IsNotNull();

            // What the class took before stays as it was
            await Assert.That(thing.FindVariable("kept")!.Type).IsEqualTo(FgdVariableType.String);
        }

        [Test]
        public async Task AMetaKeyThatIsNotANameIsReported()
        {
            var fgd = Parse("""@PointClass = thing [ value(string) [ "quoted" ] : "Value" ]""");

            await Assert.That(fgd.Errors.Select(static error => error.Message)).Contains("Unrecognized meta key: ");
        }

        [Test]
        public async Task ATagIsListedOnce()
        {
            var tags = Parse("""
                @PointClass = thing
                [
                    tags(tag_list) : "Tags" : "" =
                    [
                        "first" : "First" : 0
                        "FIRST" : "Again" : 1
                        "" : "Empty" : 0
                        "second" : "Second" : 1
                    ]
                ]
                """).FindClass("thing")!.FindVariable("tags")!;

            await Assert.That(tags.Choices.Select(static choice => choice.ValueText)).IsEquivalentTo(["first", "second"]);
        }

        [Test]
        public async Task OldSectionsAreReadAndFilterTypesAreChecked()
        {
            var fgd = Parse("@mapsize(-16384, 16384) @PointClass = thing []");

            await Assert.That(fgd.Errors).IsEmpty();
            await Assert.That(fgd.MapSize).IsEqualTo((-16384, 16384));
            await Assert.That(fgd.FindClass("thing")).IsNotNull();

            await Assert.That(Parse("""@VisGroupFilter { filter_type = "nothing" group = "Group" } @PointClass = thing []""").Errors.Single().Message)
                .IsEqualTo("Error: unknown vis group filter type: 'nothing' in @VisGroupFilter specification");
            await Assert.That(Parse("""@VisGroupFilter { filter_type = "EntityTag" group = "Group" } @PointClass = thing []""").Errors).IsEmpty();
        }

        [Test]
        public async Task AGameNamesItsFilesInItsGameInfoAndFindsThemInWhatItMounts()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                Directory.CreateDirectory(Path.Combine(root, "game"));
                Directory.CreateDirectory(Path.Combine(root, "core"));

                // The file the game names is its own, the one that file includes is shared by every game
                File.Copy(TestFixtures.Path("fgd", "sample.fgd"), Path.Combine(root, "game", "sample.fgd"));
                File.Copy(TestFixtures.Path("fgd", "sample_base.fgd"), Path.Combine(root, "core", "sample_base.fgd"));
                await File.WriteAllTextAsync(Path.Combine(root, "game", "gameinfo.gi"), """
                    "GameInfo"
                    {
                        game "Sample"
                        FileSystem
                        {
                            SearchPaths
                            {
                                Game game
                                Game core
                            }
                        }
                        Hammer
                        {
                            fgd_files
                            {
                                "sample.fgd" "1"
                                "not_shipped.fgd" "1"
                            }
                        }
                    }
                    """);

                var gameInfo = Path.Combine(root, "game", "gameinfo.gi");

                using (var fileLoader = new GameFileLoader(null, gameInfo))
                {
                    var fgd = fileLoader.LoadFgd()!;

                    await Assert.That(fgd.Errors).IsEmpty();
                    await Assert.That(fgd.FindClass("sample_point")!.Variables).IsNotEmpty();
                }

                // A game that names no file that is on disk has no FGD files
                File.Delete(Path.Combine(root, "game", "sample.fgd"));

                using (var fileLoader = new GameFileLoader(null, gameInfo))
                {
                    await Assert.That(fileLoader.LoadFgd()).IsNull();
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
