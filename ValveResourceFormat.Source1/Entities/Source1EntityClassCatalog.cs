using Microsoft.Extensions.Logging;
using ValveResourceFormat.GoldSrc;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.FileSystem;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Gives the entity classes a Source 1 or GoldSrc map is shown with, from the tables built into the library.
/// Source 1 games are not detected as a <see cref="ValveResourceFormat.IO.Game"/>, so their tables are found by the Steam app id of their
/// <c>gameinfo.txt</c>, and GoldSrc games by the folder of their mod.
/// </summary>
internal static class Source1EntityClassCatalog
{
    private const string ResourceName = "ValveResourceFormat.Source1.Entities.entity_classes.kv3";

    private const string IconSpriteHelper = "iconsprite";

    private const string EditorModelFolder = "models/editor/";

    private static readonly Lazy<BuiltInTables> BuiltIn = new(LoadBuiltIn);

    // The FGD files of the games declare helpers that have nothing to draw in a converted map, or that the
    // converter already turned into models
    private static readonly HashSet<string> UndrawnHelpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "sidelist", "animator", "sprite", "keyframe", "decal", "quadbounds", "overlay", "overlay_transition", "mapsize", "instance", "cylinder", "studio_keyed", "studio_cycle", "angles_direction",
    };

    /// <summary>
    /// Gets the entity classes of a Source 1 game.
    /// </summary>
    /// <param name="fileSystem">The file system of the game.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    /// <returns>The classes of the game, or the ones of every Source 1 game where it is not one whose classes are known.</returns>
    public static EntityClassTable Get(Source1FileSystem fileSystem, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        if (BuiltIn.Value.Source1Games.TryGetValue(fileSystem.SteamAppId, out var table))
        {
            logger.LogInformation("Entities are shown with the {Count} classes of Steam app {AppId}", table.Classes.Count, fileSystem.SteamAppId);
            return table;
        }

        logger.LogInformation("The entity classes of the game are not known, entities are shown with the classes of every Source 1 game");
        return BuiltIn.Value.Source1Merged;
    }

    /// <summary>
    /// Gets the entity classes of a GoldSrc game.
    /// </summary>
    /// <param name="gameDirectories">The folders of the game, from the mod down to the base game it runs on, such as <c>bshift</c> then <c>valve</c>. Empty when the game is not known.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    /// <returns>The classes of the first folder that has a table, or the ones of every GoldSrc game where none has.</returns>
    public static EntityClassTable GetGoldSrc(IReadOnlyList<string> gameDirectories, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(gameDirectories);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var directory in gameDirectories)
        {
            var appId = GoldSrcGameApps.GetAppId(directory);

            if (appId != 0 && BuiltIn.Value.GoldSrcGames.TryGetValue(appId, out var table))
            {
                logger.LogInformation("Entities are shown with the {Count} classes of Steam app {AppId}", table.Classes.Count, appId);
                return table;
            }
        }

        logger.LogInformation("The entity classes of the game are not known, entities are shown with the classes of every GoldSrc game");
        return BuiltIn.Value.GoldSrcMerged;
    }

    private sealed record BuiltInTables(
        Dictionary<int, EntityClassTable> Source1Games,
        EntityClassTable Source1Merged,
        Dictionary<int, EntityClassTable> GoldSrcGames,
        EntityClassTable GoldSrcMerged);

    private static BuiltInTables LoadBuiltIn()
    {
        var root = EntityClassReader.ReadResource(typeof(Source1EntityClassCatalog).Assembly, ResourceName);
        var source1Games = new Dictionary<int, EntityClassTable>();
        var goldSrcGames = new Dictionary<int, EntityClassTable>();
        var source1Classes = new List<EntityClass>();
        var goldSrcClasses = new List<EntityClass>();

        foreach (var game in EntityClassReader.ReadTables(root))
        {
            if (game.IsGoldSrc)
            {
                goldSrcGames.Add(game.AppId, game.Table);
                goldSrcClasses.AddRange(game.Table.Classes);
                continue;
            }

            source1Games.Add(game.AppId, PrepareSource1Table(game.Table));
            source1Classes.AddRange(game.Table.Classes);
        }

        var source1Merged = PrepareSource1Table(new EntityClassTable(EntityClassReader.MergeClasses(source1Classes), null));

        var goldSrcMerged = new EntityClassTable(EntityClassReader.MergeClasses(goldSrcClasses), null);

        return new BuiltInTables(source1Games, source1Merged, goldSrcGames, goldSrcMerged);
    }

    private static EntityClassTable PrepareSource1Table(EntityClassTable table) => RewriteHelpers(table, addIconVariants: true);

    internal static EntityClassTable WithoutUndrawnHelpers(EntityClassTable table) => RewriteHelpers(table, addIconVariants: false);

    private static EntityClassTable RewriteHelpers(EntityClassTable table, bool addIconVariants)
    {
        var classes = new List<EntityClass>(table.Classes.Count);
        var changed = false;

        foreach (var entityClass in table.Classes)
        {
            var rewritten = RewriteHelpers(entityClass, addIconVariants);

            changed |= !ReferenceEquals(rewritten, entityClass);
            classes.Add(rewritten);
        }

        if (!changed)
        {
            return table;
        }

        return new EntityClassTable(classes, table.GridNav);
    }

    private static EntityClass RewriteHelpers(EntityClass entityClass, bool addIconVariants)
    {
        var helpers = new List<HelperDeclaration>(entityClass.Helpers.Count);
        var changed = false;

        foreach (var helper in entityClass.Helpers)
        {
            if (UndrawnHelpers.Contains(helper.Name))
            {
                changed = true;
                continue;
            }

            var rewritten = WithModelHelperName(addIconVariants ? WithIconVariant(helper) : helper);

            changed |= !ReferenceEquals(rewritten, helper);
            helpers.Add(rewritten);
        }

        if (!changed)
        {
            return entityClass;
        }

        return new EntityClass
        {
            Name = entityClass.Name,
            Kind = entityClass.Kind,
            Size = entityClass.Size,
            Helpers = helpers,
            PreviewAnimationKeys = entityClass.PreviewAnimationKeys,
            BaseClasses = entityClass.BaseClasses,
            RequiredParentClass = entityClass.RequiredParentClass,
            AutoApplyMaterial = entityClass.AutoApplyMaterial,
        };
    }

    // A model the editor draws is declared by the helper of a model the game draws, which the editor folder of the model
    // tells apart. A light model is a model the game draws.
    private static HelperDeclaration WithModelHelperName(HelperDeclaration helper)
    {
        var isLightModel = helper.Name.Equals("lightprop", StringComparison.OrdinalIgnoreCase);
        var isModel = isLightModel
            || helper.Name.Equals("studio", StringComparison.OrdinalIgnoreCase)
            || helper.Name.Equals("studioprop", StringComparison.OrdinalIgnoreCase);

        if (!isModel)
        {
            return helper;
        }

        var model = helper.Arguments.Count > 0 ? helper.Arguments[0].Replace('\\', '/') : string.Empty;
        var name = helper.Name;

        if (model.StartsWith(EditorModelFolder, StringComparison.OrdinalIgnoreCase))
        {
            name = "editormodel";
        }
        else if (isLightModel)
        {
            name = "studio";
        }

        if (name == helper.Name)
        {
            return helper;
        }

        return new HelperDeclaration
        {
            Name = name,
            Arguments = helper.Arguments,
            Metadata = helper.Metadata,
        };
    }

    // The icon of a class is asked for as the icon variant of the material of the game that holds it, which is not drawn as the material
    private static HelperDeclaration WithIconVariant(HelperDeclaration helper)
    {
        if (!helper.Name.Equals(IconSpriteHelper, StringComparison.OrdinalIgnoreCase) || helper.Arguments.Count == 0 || string.IsNullOrEmpty(helper.Arguments[0]))
        {
            return helper;
        }

        var arguments = new List<string>(helper.Arguments);
        arguments[0] = Source1Paths.AddVariant(arguments[0], Source1ResourceProvider.IconVariant);

        return new HelperDeclaration
        {
            Name = helper.Name,
            Arguments = arguments,
            Metadata = helper.Metadata,
        };
    }
}
