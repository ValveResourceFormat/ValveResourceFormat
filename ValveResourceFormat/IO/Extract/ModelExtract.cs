using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 models to editable vmdl/dmx format.
/// </summary>
public partial class ModelExtract
{
    private readonly Resource? modelResource;
    private readonly Model? model;
    private readonly PhysAggregateData? physAggregateData;
    private readonly IFileLoader? fileLoader;
    private readonly string fileName;
    private ClothExtract? cloth;

    /// <summary>
    /// Specifies the type of model extraction.
    /// </summary>
    public enum ModelExtractType
    {
#pragma warning disable CS1591
        Default,
        Map_PhysicsToRenderMesh,
        Map_AggregateSplit,
#pragma warning restore CS1591
    }

    /// <summary>Gets the extraction type to apply when generating assets.</summary>
    public ModelExtractType Type { get; init; } = ModelExtractType.Default;

    /// <summary>Optional sink for non-fatal progress and warning messages.</summary>
    public IProgress<string>? ProgressReporter { get; init; }

    /// <summary>
    /// Gets whether to reconstruct the model's soft-body (cloth) physics. When false, the model is extracted as if it
    /// had no cloth.
    /// </summary>
    public bool ReconstructSoftbody { get; init; } = true;

    /// <summary>
    /// Gets whether animations are left out: no animation DMX files, no animation graph clips and no <c>AnimationList</c>.
    /// </summary>
    public bool SkipAnimations { get; init; }

    /// <summary>
    /// Gets whether physics is left out: no physics shapes, bodies, joints or their DMX files.
    /// </summary>
    public bool SkipPhysics { get; init; }

    /// <summary>
    /// Gets whether only the meshes of the most detailed level of detail are written, without LOD groups.
    /// </summary>
    public bool HighestLodOnly { get; init; }

    /// <summary>Gets the options of decompiled models.</summary>
    public static IReadOnlyList<ContentExportOption> ExportOptions { get; } =
    [
        ContentExportOption.Toggle("model_reconstruct_softbody", "Reconstruct cloth", "Models",
            "Rebuild soft-body (cloth) physics of models and of the models in maps.", ["vmdl_c", "vmap_c"],
            static settings => settings.ExtractOptions.ReconstructSoftbody,
            static (settings, value) => settings.ExtractOptions = settings.ExtractOptions with { ReconstructSoftbody = value }),
        ContentExportOption.Toggle("model_skip_animations", "Skip animations", "Models",
            "Leave out animation files, animation graph clips and the animation list.", ["vmdl_c"],
            static settings => settings.ExtractOptions.ModelSkipAnimations,
            static (settings, value) => settings.ExtractOptions = settings.ExtractOptions with { ModelSkipAnimations = value }),
        ContentExportOption.Toggle("model_skip_physics", "Skip physics", "Models",
            "Leave out physics shapes, bodies and joints.", ["vmdl_c"],
            static settings => settings.ExtractOptions.ModelSkipPhysics,
            static (settings, value) => settings.ExtractOptions = settings.ExtractOptions with { ModelSkipPhysics = value }),
        ContentExportOption.Toggle("model_highest_lod_only", "Highest LOD only", "Models",
            "Only write the meshes of the most detailed level of detail, without LOD groups.", ["vmdl_c"],
            static settings => settings.ExtractOptions.ModelHighestLodOnly,
            static (settings, value) => settings.ExtractOptions = settings.ExtractOptions with { ModelHighestLodOnly = value }),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelExtract"/> class.
    /// </summary>
    public ModelExtract(Resource modelResource, IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(modelResource);

        if (modelResource.DataBlock is not Model modelData)
        {
            throw new InvalidDataException("Resource data block is not a Model");
        }

        this.fileLoader = fileLoader;
        this.modelResource = modelResource;
        model = modelData;

        var refPhysics = model.GetReferencedPhysNames()?.FirstOrDefault();
        if (refPhysics != null)
        {
            using var physResource = fileLoader.LoadFileCompiled(refPhysics);

            if (physResource?.DataBlock is PhysAggregateData physData)
            {
                physAggregateData = physData;
            }
        }
        else
        {
            physAggregateData = model.GetEmbeddedPhys();
        }

        fileName = modelResource.FileName != null ? ContentPaths.ChangeExtension(modelResource.FileName, ".vmdl") : modelData.Name;
        EnqueueMeshes();
        EnqueueAnimations();
        morphSetForControlNodes = new(FindMorphSetForControlNodes);
    }

    /// <inheritdoc cref="ModelExtract(Resource, IFileLoader)"/>
    /// <summary>
    /// Extract a single mesh to vmdl+dmx.
    /// </summary>
    /// <param name="mesh">Mesh data</param>
    /// <param name="meshFileName">File name of the mesh e.g. "models/my_mesh.vmesh"</param>
    public ModelExtract(Mesh mesh, string meshFileName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(meshFileName);

        RenderMeshesToExtract.Add(new(mesh, "unnamed", 0, GetDmxFileName_ForReferenceMesh(meshFileName)));
        fileName = Path.ChangeExtension(meshFileName, ".vmdl");
    }

    /// <inheritdoc cref="ModelExtract(Resource, IFileLoader)"/>
    public ModelExtract(PhysAggregateData physAggregateData, string physFileName)
    {
        ArgumentNullException.ThrowIfNull(physAggregateData);
        ArgumentNullException.ThrowIfNull(physFileName);

        this.physAggregateData = physAggregateData;
        fileName = physFileName;
        EnqueueMeshes();
    }

    /// <summary>
    /// Converts the model to a content file with associated meshes and animations.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var vmdl = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveModel()),
            FileName = ModelName,
        };

        var skipEmptyMorphSets = MorphSetForControlNodes != null;

        foreach (var renderMesh in RenderMeshesToExtract)
        {
            if (fileLoader != null)
            {
                renderMesh.Mesh.LoadExternalMorphData(fileLoader);
            }

            if (renderMesh.Mesh.MorphData is { HasMissingAtlas: true } morphData)
            {
                ProgressReporter?.Report(
                    $"Morph atlas '{morphData.AtlasPath}' was not found, the {morphData.GetMorphCount()} morph target(s) of "
                    + $"{Path.GetFileName(renderMesh.FileName)} will be written with no deltas.");
            }

            var options = new DatamodelRenderMeshExtractOptions
            {
                MaterialInputSignatures = MaterialInputSignatures,
                BoneRemapTable = renderMesh.BoneRemapTable,
                Skeleton = renderMesh.Skeleton,
                BonePositions = Cloth.RestBonePositions,
                ClothSurface = Cloth.RenderBindingSurface,
                ReconstructCloth = ReconstructsCloth,
                SkipEmptyMorphSet = skipEmptyMorphSets,
            };

            vmdl.AddSubFile(
                renderMesh.FileName,
                () => ToDmxMesh(renderMesh.Mesh, Path.GetFileNameWithoutExtension(renderMesh.FileName), options)
            );
        }

        if (!SkipPhysics)
        {
            foreach (var physHull in PhysHullsToExtract)
            {
                vmdl.AddSubFile(
                    physHull.FileName,
                    () => ToDmxMesh(physHull.Hull, physHull.BindPose)
                );
            }

            foreach (var physMesh in PhysMeshesToExtract)
            {
                vmdl.AddSubFile(
                    physMesh.FileName,
                    () => ToDmxMesh(physMesh.Mesh, physMesh.BindPose)
                );
            }
        }

        Cloth.AddSubFiles(vmdl);

        if (SkipAnimations)
        {
            return vmdl;
        }

        foreach (var anim in AnimationsToExtract)
        {
            if (!WritesOwnAnimation(anim.Anim))
            {
                continue;
            }

            vmdl.AddSubFile(
                anim.FileName,
                () =>
                {
                    Debug.Assert(model is not null, "model should not be null when AnimationsToExtract has items");
                    return ToDmxAnim(model.Skeleton, model.FlexControllers, anim.Anim, [], nmSkelAxisFixup: false, keepClothProxyBones: !ReconstructsCloth);
                }
            );
        }

        AddAnimationGraphClips(vmdl);

        return vmdl;
    }

    /// <summary>
    /// Gets the content path the model is written at: the path of its resource, or else the name the model was compiled with.
    /// </summary>
    public string ModelName => fileName;

    /// <summary>Gets the model's cloth reconstruction, empty when <see cref="ReconstructsCloth"/> is false.</summary>
    internal ClothExtract Cloth
    {
        get
        {
            EnsureCloth();
            return cloth;
        }
    }

    /// <summary>
    /// Gets whether cloth is reconstructed: it was requested and the model has some.
    /// </summary>
    private bool ReconstructsCloth
    {
        get
        {
            EnsureCloth();
            return cloth.Reconstruction is not null;
        }
    }

    /// <summary>
    /// Builds the cloth on first use.
    /// </summary>
    [MemberNotNull(nameof(cloth))]
    private void EnsureCloth()
    {
        if (cloth is not null)
        {
            return;
        }

        cloth = ReconstructSoftbody ? new ClothExtract(model, physAggregateData) : new ClothExtract(null, null);
        cloth.Build(fileName, GetDmxFileName_ForCloth);
    }

    /// <summary>
    /// Gets the DMX file name for cloth file <paramref name="name"/>, suffixed when a mesh, animation or earlier cloth
    /// file already uses it.
    /// </summary>
    private string GetDmxFileName_ForCloth(string name)
    {
        var dmxFileName = GetDmxFileName_ForEmbeddedMesh(name);
        for (var suffix = 1; IsDmxFileNameTaken(dmxFileName, includePhysics: true, includeAnimations: true); suffix++)
        {
            dmxFileName = GetDmxFileName_ForEmbeddedMesh(FormattableString.Invariant($"{name}_{suffix}"));
        }

        return dmxFileName;
    }

    /// <summary>
    /// Whether a queued render mesh or cloth file already uses <paramref name="name"/>, and optionally a queued physics or
    /// animation file.
    /// </summary>
    private bool IsDmxFileNameTaken(string name, bool includePhysics, bool includeAnimations)
    {
        bool Same(string fileName) => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase);

        return RenderMeshesToExtract.Exists(mesh => Same(mesh.FileName))
            || (includePhysics && (PhysHullsToExtract.Exists(hull => Same(hull.FileName))
                || PhysMeshesToExtract.Exists(mesh => Same(mesh.FileName))))
            || (cloth is not null && cloth.ProxyMeshes.Exists(proxy => Same(proxy.FileName)))
            || (includeAnimations && animationFileNames.Contains(name));
    }
}
