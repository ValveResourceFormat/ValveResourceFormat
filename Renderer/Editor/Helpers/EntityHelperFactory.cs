using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// How much of what entities are drawn with a map creates. Each helper is registered with how much it
/// matters, and a map leaves out the ones below its level.
/// </summary>
public enum HelperLevel
{
    /// <summary>No helpers.</summary>
    None,

    /// <summary>Only the helpers a map cannot be built without.</summary>
    Essential,

    /// <summary>Those, and the helpers that show what the game draws.</summary>
    Game,

    /// <summary>Every helper.</summary>
    All,
}

/// <summary>
/// Creates the helpers an entity is drawn with, by the names its class declares them under.
/// </summary>
public static class EntityHelperFactory
{
    private delegate EntityHelper? Creator(IHelperOwner owner, FgdHelper declaration);

    // How much a helper matters to a map, which decides the levels it is created at
    private enum Importance
    {
        Editor,
        Game,
        Essential,
    }

    private sealed record Registration(Importance Importance)
    {
        public Creator? Create { get; set; }
    }

    private const string RequiredParentClassKey = "required_parent_class";

    // Material of the icon an entity of a class no game declares is given
    private const string ObsoleteIcon = "materials/editor/obsolete.vmat";

    private static readonly Vector3 DefaultBoxMins = new(-8f);
    private static readonly Vector3 DefaultBoxMaxs = new(8f);

    private const int MaxRememberedUnknownNames = 256;

    private static readonly Dictionary<string, Registration> Registrations = CreateRegistrations();
    private static readonly HashSet<string> UnknownNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the helper a declaration names.</summary>
    /// <param name="owner">The entity the helper is for.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <param name="level">How much the map creates.</param>
    /// <returns>
    /// The helper, or <see langword="null"/> when the level leaves it out, nothing creates helpers of that name,
    /// or no helper goes by it.
    /// </returns>
    public static EntityHelper? Create(IHelperOwner owner, FgdHelper declaration, HelperLevel level)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(declaration);

        if (level == HelperLevel.None)
        {
            return null;
        }

        if (!Registrations.TryGetValue(declaration.Name, out var registration))
        {
            if (level == HelperLevel.All && RememberUnknownName(declaration.Name))
            {
                owner.Scene.RendererContext.Logger.LogWarning("Can't find helper '{Helper}' - fgd error?", declaration.Name);
            }

            return null;
        }

        var leftOut = level switch
        {
            HelperLevel.Game => registration.Importance == Importance.Editor,
            HelperLevel.Essential => registration.Importance != Importance.Essential,
            _ => false,
        };

        return leftOut ? null : registration.Create?.Invoke(owner, declaration);
    }

    /// <summary>
    /// Creates everything an entity is drawn with: the box of a class that declares a size, the helpers its
    /// class declares, one for each of its variables that is drawn, and a box where none of those is seen,
    /// unless it is made of map geometry, a path, or a node of one.
    /// </summary>
    /// <param name="owner">The entity.</param>
    /// <param name="level">How much the map creates.</param>
    /// <returns>The helpers, in the order they were created.</returns>
    public static List<EntityHelper> CreateHelpers(IHelperOwner owner, HelperLevel level)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var helpers = new List<EntityHelper>();

        if (level == HelperLevel.None)
        {
            return helpers;
        }

        var gameClass = owner.FgdClass;
        var isSeen = false;

        if (gameClass is { HasSize: true })
        {
            helpers.Add(new AlignedBoxHelper(owner, gameClass.Mins, gameClass.Maxs));
            isSeen = true;
        }

        if (gameClass != null)
        {
            foreach (var declaration in gameClass.Helpers.Concat(GetVariableHelpers(gameClass)))
            {
                if (Create(owner, declaration, level) is { } helper)
                {
                    helpers.Add(helper);
                    isSeen |= helper.IsVisual;
                }
            }

            // Never what the entity is seen by, so it does not keep the box away
            if (gameClass.Metadata.ContainsKey(RequiredParentClassKey) && gameClass.Metadata.GetStringProperty(RequiredParentClassKey) is { Length: > 0 }
                && Create(owner, new FgdHelper { Name = "error_indicator" }, level) is { } errorIndicator)
            {
                helpers.Add(errorIndicator);
            }
        }

        // An entity made of map geometry is seen by that, and a node of a path by the path it is on
        if (gameClass is { Type: FgdClassType.SolidClass or FgdClassType.PathNodeClass })
        {
            return helpers;
        }

        if (gameClass == null && Create(owner, CreateDeclaration("iconsprite", ObsoleteIcon), level) is { } obsolete)
        {
            helpers.Add(obsolete);
            isSeen = true;
        }

        // A path or a cable is seen by its nodes
        if (!isSeen && level == HelperLevel.All && gameClass is not { Type: FgdClassType.PathClass or FgdClassType.CableClass })
        {
            helpers.Add(new AlignedBoxHelper(owner, DefaultBoxMins, DefaultBoxMaxs));
        }

        return helpers;
    }

    // Each unknown name is only said once, as long as there are few enough of them to remember
    private static bool RememberUnknownName(string name)
    {
        lock (UnknownNames)
        {
            if (UnknownNames.Contains(name))
            {
                return false;
            }

            if (UnknownNames.Count < MaxRememberedUnknownNames)
            {
                UnknownNames.Add(name);
            }

            return true;
        }
    }

    // A variable of one of these types is drawn by a helper of its own, which is given its name
    private static IEnumerable<FgdHelper> GetVariableHelpers(FgdClass gameClass)
    {
        foreach (var variable in gameClass.Variables)
        {
            var name = variable.DeclaredType switch
            {
                FgdVariableType.VecLine => "vecline",
                FgdVariableType.Axis => "axis",
                FgdVariableType.LocalAxis => "localaxis",
                _ => null,
            };

            if (name != null)
            {
                // A variable without a name gives the helper no key
                yield return variable.Name.Length > 0 ? CreateDeclaration(name, variable.Name) : new FgdHelper { Name = name };
            }
        }
    }

    private static FgdHelper CreateDeclaration(string name, string argument)
    {
        var declaration = new FgdHelper { Name = name };

        declaration.Arguments.Add(argument);
        return declaration;
    }

    private static Dictionary<string, Registration> CreateRegistrations()
    {
        var registrations = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);

        void Add(Importance importance, params string[] names)
        {
            foreach (var name in names)
            {
                registrations.Add(name, new Registration(importance));
            }
        }

        Add(Importance.Essential,
            "bakeambientocclusion", "bakeresource", "bakeskylight", "model", "skybox", "volumetric_fog_controller");

        Add(Importance.Game,
            "barnlight", "dotaskybox", "editormodel", "externalHelper", "fog", "global", "light", "omnilight",
            "overlay", "particle", "rectlight", "studio", "studioprop", "tonemap");

        Add(Importance.Editor,
            "accompany", "arc_range", "axis", "box_oriented", "box_world_aligned", "camera_path",
            "camera_path_node", "capsule", "centered_box_oriented", "cubemap", "cubemap_fog", "door_helper",
            "dotaGlobalLightHelper", "dotaLightInfoHelper", "dota_obstruction", "drawangles", "drawangles_local",
            "error_indicator", "frustum", "gameItemModel", "gameUnitModel", "gradientfog", "helper_particle_glow",
            "iconsprite", "leansphere", "lightcone", "lightortho", "line", "localaxis", "occluder",
            "orientedwidthheight", "particle_rope", "postprocessing", "selected_line", "sequenceList", "skybox_map",
            "sphere", "sweptplayerhull", "text_local", "vecline", "vecline_local", "viewer", "wirebox",
            "wirebox_local");

        void Creates(Creator creator, params string[] names)
        {
            foreach (var name in names)
            {
                registrations[name].Create = creator;
            }
        }

        Creates(static (owner, declaration) => new IconHelper(owner, declaration), "iconsprite");
        Creates(ModelHelper.Create, "editormodel", "model", "studio", "studioprop");
        Creates(static (owner, declaration) => new LightHelper(owner, declaration), "light");
        Creates(static (owner, declaration) => new BarnLightHelper(owner, declaration), "barnlight");
        Creates(static (owner, declaration) => new RectLightHelper(owner, declaration), "rectlight");
        Creates(static (owner, declaration) => new OmniLightHelper(owner, declaration), "omnilight");
        Creates(static (owner, declaration) => new BakeResourceHelper(owner, declaration), "bakeresource");
        Creates(static (owner, declaration) => new SkyboxHelper(owner, declaration), "skybox");
        Creates(static (owner, declaration) => new GradientFogHelper(owner, declaration), "gradientfog");
        Creates(static (owner, declaration) => new CubemapFogHelper(owner, declaration), "cubemap_fog");
        Creates(static (owner, declaration) => new ToneMappingHelper(owner, declaration), "tonemap");
        Creates(static (owner, declaration) => new PostProcessingVolumeHelper(owner, declaration), "postprocessing");
        Creates(static (owner, declaration) => new ParticleHelper(owner, declaration), "particle");
        Creates(static (owner, declaration) => new ParticleGlowHelper(owner, declaration), "helper_particle_glow");
        Creates(static (owner, declaration) => new ParticleRopeHelper(owner, declaration), "particle_rope");
        Creates(LineHelper.Create, "line", "selected_line");
        Creates(static (owner, declaration) => new SphereHelper(owner, declaration), "sphere", "leansphere");
        Creates(BoxHelper.Create, "box_oriented", "box_world_aligned", "centered_box_oriented");
        Creates(WireBoxHelper.Create, "wirebox", "wirebox_local");
        Creates(static (owner, declaration) => new AnglesHelper(owner, declaration), "drawangles", "drawangles_local");
        Creates(static (owner, declaration) => new VecLineHelper(owner, declaration), "vecline", "vecline_local");
        Creates(static (owner, declaration) => new LightConeHelper(owner, declaration), "lightcone");
        Creates(static (owner, declaration) => new VolumetricFogControllerHelper(owner, declaration), "volumetric_fog_controller");
        Creates(static (owner, declaration) => new ArcRangeHelper(owner, declaration), "arc_range");
        Creates(static (owner, declaration) => new AxisHelper(owner, declaration), "axis", "localaxis");
        Creates(OrientedWidthHeightHelper.Create, "orientedwidthheight");
        Creates(static (owner, declaration) => new LightOrthoHelper(owner, declaration), "lightortho");
        Creates(static (owner, declaration) => new TextHelper(owner, declaration), "text_local");
        Creates(static (owner, declaration) => new OverlayHelper(owner, declaration), "overlay");
        Creates(static (owner, declaration) => new ErrorIndicatorHelper(owner, declaration), "error_indicator");
        Creates(DotaObstructionHelper.Create, "dota_obstruction");
        Creates(GameUnitModelHelper.Create, "gameUnitModel", "gameItemModel");
        Creates(static (owner, declaration) => new FrustumHelper(owner, declaration), "frustum");
        Creates(static (owner, declaration) => new CapsuleHelper(owner, declaration), "capsule");
        Creates(static (owner, declaration) => new RadiusSphereHelper(owner, declaration), "occluder", "viewer");
        Creates(static (owner, declaration) => new SweptPlayerHullHelper(owner, declaration), "sweptplayerhull");
        Creates(static (owner, declaration) => new DoorHelper(owner, declaration), "door_helper");
        Creates(static (owner, declaration) => new AccompanyHelper(owner, declaration), "accompany");
        Creates(static (owner, declaration) => new CameraPathHelper(owner, declaration), "camera_path");
        Creates(static (owner, declaration) => new CameraPathNodeHelper(owner, declaration), "camera_path_node");
        Creates(static (owner, declaration) => new GlobalHelper(owner, declaration), "global");
        Creates(static (owner, declaration) => new FogHelper(owner, declaration), "fog");
        Creates(static (owner, declaration) => new ExternalHelper(owner, declaration), "externalHelper");
        Creates(static (owner, declaration) => new DotaLightInfoHelper(owner, declaration), "dotaLightInfoHelper");

        return registrations;
    }
}
