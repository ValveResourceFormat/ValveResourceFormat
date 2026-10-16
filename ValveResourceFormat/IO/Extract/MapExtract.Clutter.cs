using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO;

public sealed partial class MapExtract
{
    /// <summary>
    /// The model a clutter object is placed with, and the skin or material override that gives it the materials the
    /// compiler baked into the clutter object's generated model.
    /// </summary>
    private readonly record struct ClutterModel(string Name, string? Skin, string? MaterialOverride);

    /// <summary>
    /// Matches the name the compiler gives the model it generates per clutter object, <c>{node}_lr{layer}_clutter{index}_{source model}</c>.
    /// </summary>
    [GeneratedRegex(@"_lr\d+_clutter\d+_(?<model>.+)$")]
    private static partial Regex ClutterModelNameRegex();

    private readonly Dictionary<(string GeneratedModel, string MaterialGroup), ClutterModel> ResolvedClutterModels = [];
    private readonly Dictionary<string, List<string>> ClutterSourceModelCandidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string Name, string[] Materials)>?> ClutterCandidateMaterialGroups = [];

    /// <summary>
    /// Places every instance of a clutter object as a <c>prop_static</c> clutter instance, which the compiler groups
    /// back into the same clutter object.
    /// </summary>
    private void AddClutter(WorldNode.ClutterSceneObject clutter, MapNode parent)
    {
        CompiledMapPieces.Add(clutter.RenderableModel);

        if (!ResolvedClutterModels.TryGetValue((clutter.RenderableModel, clutter.MaterialGroup), out var model))
        {
            model = ResolveClutterModel(clutter.RenderableModel, clutter.MaterialGroup);
            ResolvedClutterModels.Add((clutter.RenderableModel, clutter.MaterialGroup), model);
        }

        AssetReferences.Add(model.Name);

        if (model.Name == clutter.RenderableModel)
        {
            SceneObjectsToExtract.Add(model.Name);
            ProgressReporter?.Report($"Clutter model {clutter.RenderableModel} has no source model, its instances use the compiled clutter model.");
        }

        var group = new CMapGroup { Name = Path.GetFileNameWithoutExtension(model.Name) };
        parent.Children.Add(group);

        var fadeStartSize = clutter.BeginCullSize.ToString(CultureInfo.InvariantCulture);
        var fadeEndSize = clutter.EndCullSize.ToString(CultureInfo.InvariantCulture);
        var renderColors = new Dictionary<Vector3, string>();

        for (var i = 0; i < clutter.InstancePositions.Length; i++)
        {
            var prop = new CMapEntity()
                .WithClassName("prop_static")
                .WithProperty("model", model.Name)
                .WithProperty("clutterinstance", StringBool(true))
                .WithProperty("clutterFadeStartSize", fadeStartSize)
                .WithProperty("clutterFadeEndSize", fadeEndSize);

            SetPropertiesFromFlags(prop, clutter.Flags);

            if (model.Skin is not null)
            {
                prop.EntityProperties["skin"] = model.Skin;
            }

            if (model.MaterialOverride is not null)
            {
                prop.EntityProperties["materialoverride"] = model.MaterialOverride;
            }

            var tint = clutter.InstanceTints[i];
            if (tint != new Vector3(255f))
            {
                if (!renderColors.TryGetValue(tint, out var renderColor))
                {
                    // The instance tint is the sRGB encoding of the render color, stored as if it were linear
                    var color = Vector3.Round(ColorSpace.SrgbGammaToLinear(tint / 255f) * 255f);
                    renderColor = string.Create(CultureInfo.InvariantCulture, $"{color.X} {color.Y} {color.Z}");
                    renderColors.Add(tint, renderColor);
                }

                prop.EntityProperties["rendercolor"] = renderColor;
            }

            prop.Origin = clutter.InstancePositions[i];
            prop.Angles = EntityTransformHelper.ToEulerAngles(Quaternion.Normalize(clutter.InstanceOrientations[i]));
            prop.Scales = new Vector3(clutter.InstanceScales[i]);

            group.Children.Add(prop);
        }
    }

    /// <summary>
    /// Finds the model a clutter object was placed with from the name of the model the compiler generated for it,
    /// falling back to the generated model when no mounted model of that name has its materials.
    /// </summary>
    /// <param name="generatedModelName">The model the compiler generated for the clutter object.</param>
    /// <param name="materialGroup">The material group the clutter object draws with, empty for the default.</param>
    private ClutterModel ResolveClutterModel(string generatedModelName, string materialGroup)
    {
        var skinOverride = string.IsNullOrEmpty(materialGroup) ? null : materialGroup;
        var fallback = new ClutterModel(generatedModelName, skinOverride, null);
        var match = ClutterModelNameRegex().Match(Path.GetFileNameWithoutExtension(generatedModelName));

        if (!match.Success)
        {
            return fallback;
        }

        using var generatedResource = FileLoader.LoadFileCompiled(generatedModelName);

        if (generatedResource?.DataBlock is not Model generatedModel)
        {
            return fallback;
        }

        var materials = GetDrawCallMaterials(generatedModel).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sourceModelName = match.Groups["model"].Value;

        if (!ClutterSourceModelCandidates.TryGetValue(sourceModelName, out var candidates))
        {
            candidates = [.. FileLoader.FindFilesByName(sourceModelName + ".vmdl" + GameFileLoader.CompiledFileSuffix).Select(ContentPaths.TrimCompiledSuffix)];
            ClutterSourceModelCandidates.Add(sourceModelName, candidates);
        }

        foreach (var candidate in candidates)
        {
            if (GetClutterCandidateMaterialGroups(candidate) is not { } materialGroups)
            {
                continue;
            }

            var skin = materialGroups.FindIndex(group => group.Name == materialGroup && materials.IsSubsetOf(group.Materials));

            if (skin >= 0)
            {
                return new ClutterModel(candidate, skinOverride ?? (skin > 0 ? materialGroups[skin].Name : null), null);
            }

            skin = materialGroups.FindIndex(group => materials.IsSubsetOf(group.Materials));

            if (skin >= 0)
            {
                return new ClutterModel(candidate, skin > 0 ? materialGroups[skin].Name : null, null);
            }
        }

        // An overridden material replaces every material of the model
        if (candidates.Count == 1 && materials.Count == 1)
        {
            return new ClutterModel(candidates[0], skinOverride, materials.First());
        }

        return fallback;
    }

    /// <summary>
    /// Gets the material groups of a model a clutter object may have been placed with, or a single unnamed group
    /// of its draw call materials when it has none. Null when the model does not load.
    /// </summary>
    private List<(string Name, string[] Materials)>? GetClutterCandidateMaterialGroups(string candidate)
    {
        if (ClutterCandidateMaterialGroups.TryGetValue(candidate, out var materialGroups))
        {
            return materialGroups;
        }

        using var resource = FileLoader.LoadFileCompiled(candidate);

        if (resource?.DataBlock is Model model)
        {
            materialGroups = [.. model.GetMaterialGroups()];

            if (materialGroups.Count == 0)
            {
                materialGroups.Add((string.Empty, [.. GetDrawCallMaterials(model)]));
            }
        }

        ClutterCandidateMaterialGroups.Add(candidate, materialGroups);
        return materialGroups;
    }

    private IEnumerable<string> GetDrawCallMaterials(Model model)
        => EnumerateDrawCalls(model).Select(static drawCall => Mesh.GetMaterialName(drawCall.DrawCall)).OfType<string>();
}
