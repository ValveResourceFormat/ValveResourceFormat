using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Editor.Entities;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Creates the helpers an entity is drawn with, by the names its class declares them under.
/// </summary>
public static class EntityHelperFactory
{
    private delegate EntityHelper? Creator(IHelperOwner owner, HelperDeclaration declaration);

    // Material of the icon an entity of a class no game declares is given
    private const string ObsoleteIcon = "materials/editor/obsolete.vmat";

    private static readonly Vector3 DefaultBoxMins = new(-8f);
    private static readonly Vector3 DefaultBoxMaxs = new(8f);

    private const int MaxRememberedUnknownNames = 256;

    // A helper without a creator is one the editor has that draws nothing here
    private static readonly Dictionary<string, Creator?> Creators = CreateCreators();
    private static readonly HashSet<string> UnknownNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the helper a declaration names.</summary>
    /// <param name="owner">The entity the helper is for.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <returns>
    /// The helper, or <see langword="null"/> when nothing creates helpers of that name, or no helper goes by it.
    /// </returns>
    public static EntityHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(declaration);

        if (!Creators.TryGetValue(declaration.Name, out var create))
        {
            if (RememberUnknownName(declaration.Name))
            {
                owner.Scene.RendererContext.Logger.LogWarning("Can't find helper '{Helper}' - fgd error?", declaration.Name);
            }

            return null;
        }

        return create?.Invoke(owner, declaration);
    }

    /// <summary>
    /// Creates everything an entity is drawn with: the box of a class that declares a size, the helpers its
    /// class declares, one for each of its keys that is drawn, and a box where none of those is seen, unless it
    /// is made of map geometry, a path, or a node of one.
    /// </summary>
    /// <param name="owner">The entity.</param>
    /// <returns>The helpers, in the order they were created.</returns>
    public static List<EntityHelper> CreateHelpers(IHelperOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var helpers = new List<EntityHelper>();
        var entityClass = owner.EntityClass;
        var isSeen = false;

        if (entityClass?.Size is var (mins, maxs))
        {
            helpers.Add(new AlignedBoxHelper(owner, mins, maxs));
            isSeen = true;
        }

        if (entityClass != null)
        {
            foreach (var declaration in entityClass.Helpers)
            {
                if (Create(owner, declaration) is { } helper)
                {
                    helpers.Add(helper);
                    isSeen |= helper.IsVisual;
                }
            }

            // Never what the entity is seen by, so it does not keep the box away
            if (entityClass.RequiredParentClass is { Length: > 0 } && Create(owner, new HelperDeclaration { Name = "error_indicator" }) is { } errorIndicator)
            {
                helpers.Add(errorIndicator);
            }
        }

        // An entity made of map geometry is seen by that, and a node of a path by the path it is on
        if (entityClass is { Kind: EntityClassKind.Solid or EntityClassKind.PathNode })
        {
            return helpers;
        }

        if (entityClass == null && Create(owner, new HelperDeclaration { Name = "iconsprite", Arguments = [ObsoleteIcon] }) is { } obsolete)
        {
            helpers.Add(obsolete);
            isSeen = true;
        }

        // A path or a cable is seen by its nodes
        if (!isSeen && entityClass is not { Kind: EntityClassKind.Path or EntityClassKind.Cable })
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

    private static Dictionary<string, Creator?> CreateCreators()
    {
        var creators = new Dictionary<string, Creator?>(StringComparer.OrdinalIgnoreCase);

        void Add(Creator? create, params string[] names)
        {
            foreach (var name in names)
            {
                creators.Add(name, create);
            }
        }

        Add(null, "bakeambientocclusion", "bakeskylight");
        Add(static (owner, declaration) => new BakeResourceHelper(owner, declaration), "bakeresource");
        Add(ModelHelper.Create, "model");
        Add(static (owner, declaration) => new SkyboxHelper(owner, declaration), "skybox");
        Add(static (owner, declaration) => new VolumetricFogControllerHelper(owner, declaration), "volumetric_fog_controller");

        Add(null, "dotaskybox");
        Add(ModelHelper.Create, "editormodel", "studio", "studioprop");
        Add(static (owner, declaration) => new LightHelper(owner, declaration), "light");
        Add(static (owner, declaration) => new BarnLightHelper(owner, declaration), "barnlight");
        Add(static (owner, declaration) => new RectLightHelper(owner, declaration), "rectlight");
        Add(static (owner, declaration) => new OmniLightHelper(owner, declaration), "omnilight");
        Add(static (owner, declaration) => new ToneMappingHelper(owner, declaration), "tonemap");
        Add(static (owner, declaration) => new ParticleHelper(owner, declaration), "particle");
        Add(static (owner, declaration) => new OverlayHelper(owner, declaration), "overlay");
        Add(static (owner, declaration) => new GlobalHelper(owner, declaration), "global");
        Add(static (owner, declaration) => new FogHelper(owner, declaration), "fog");
        Add(static (owner, declaration) => new ExternalHelper(owner, declaration), "externalHelper");

        Add(null, "cubemap", "dotaGlobalLightHelper", "sequenceList", "skybox_map");
        Add(static (owner, declaration) => new IconHelper(owner, declaration), "iconsprite");
        Add(static (owner, declaration) => new GradientFogHelper(owner, declaration), "gradientfog");
        Add(static (owner, declaration) => new CubemapFogHelper(owner, declaration), "cubemap_fog");
        Add(static (owner, declaration) => new PostProcessingVolumeHelper(owner, declaration), "postprocessing");
        Add(static (owner, declaration) => new ParticleGlowHelper(owner, declaration), "helper_particle_glow");
        Add(static (owner, declaration) => new ParticleRopeHelper(owner, declaration), "particle_rope");
        Add(LineHelper.Create, "line", "selected_line");
        Add(static (owner, declaration) => new SphereHelper(owner, declaration), "sphere", "leansphere");
        Add(BoxHelper.Create, "box_oriented", "box_world_aligned", "centered_box_oriented");
        Add(WireBoxHelper.Create, "wirebox", "wirebox_local");
        Add(static (owner, declaration) => new AnglesHelper(owner, declaration), "drawangles", "drawangles_local");
        Add(static (owner, declaration) => new VecLineHelper(owner, declaration), "vecline", "vecline_local");
        Add(static (owner, declaration) => new LightConeHelper(owner, declaration), "lightcone");
        Add(static (owner, declaration) => new ArcRangeHelper(owner, declaration), "arc_range");
        Add(static (owner, declaration) => new AxisHelper(owner, declaration), "axis", "localaxis");
        Add(OrientedWidthHeightHelper.Create, "orientedwidthheight");
        Add(static (owner, declaration) => new LightOrthoHelper(owner, declaration), "lightortho");
        Add(static (owner, declaration) => new TextHelper(owner, declaration), "text_local");
        Add(static (owner, declaration) => new ErrorIndicatorHelper(owner, declaration), "error_indicator");
        Add(DotaObstructionHelper.Create, "dota_obstruction");
        Add(GameUnitModelHelper.Create, "gameUnitModel", "gameItemModel");
        Add(static (owner, declaration) => new FrustumHelper(owner, declaration), "frustum");
        Add(static (owner, declaration) => new CapsuleHelper(owner, declaration), "capsule");
        Add(static (owner, declaration) => new RadiusSphereHelper(owner, declaration), "occluder", "viewer");
        Add(static (owner, declaration) => new SweptPlayerHullHelper(owner, declaration), "sweptplayerhull");
        Add(static (owner, declaration) => new DoorHelper(owner, declaration), "door_helper");
        Add(static (owner, declaration) => new AccompanyHelper(owner, declaration), "accompany");
        Add(static (owner, declaration) => new CameraPathHelper(owner, declaration), "camera_path");
        Add(static (owner, declaration) => new CameraPathNodeHelper(owner, declaration), "camera_path_node");
        Add(static (owner, declaration) => new DotaLightInfoHelper(owner, declaration), "dotaLightInfoHelper");

        return creators;
    }
}
