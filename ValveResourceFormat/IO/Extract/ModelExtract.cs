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
    /// Gets whether to reconstruct the model's cloth (soft-body) physics. When false, the soft-body data is not read and
    /// the model is extracted as if it had none: no cloth nodes, proxy DMX files or cloth bone handling.
    /// </summary>
    public bool ExtractCloth { get; init; } = true;

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

        fileName = Path.ChangeExtension(modelResource.FileName ?? "model", ".vmdl");
        EnqueueMeshes();
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
                SkipCloth = !ReconstructsCloth,
            };

            vmdl.AddSubFile(
                Path.GetFileName(renderMesh.FileName),
                () => ToDmxMesh(renderMesh.Mesh, Path.GetFileNameWithoutExtension(renderMesh.FileName), options)
            );
        }

        foreach (var physHull in PhysHullsToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physHull.FileName),
                () => ToDmxMesh(physHull.Hull, physHull.BindPose)
            );
        }

        foreach (var physMesh in PhysMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physMesh.FileName),
                () => ToDmxMesh(physMesh.Mesh, physMesh.BindPose)
            );
        }

        Cloth.AddSubFiles(vmdl);

        foreach (var anim in AnimationsToExtract)
        {
            if (!WritesOwnAnimation(anim.Anim))
            {
                continue;
            }

            vmdl.AddSubFile(
                Path.GetFileName(anim.FileName),
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
    /// Gets the model name from either the model resource or the file name.
    /// </summary>
    public string ModelName => model?.Name ?? fileName;

    private ClothExtract? cloth;
    private bool clothFailed;

    /// <summary>
    /// Gets the cloth reconstruction of the model's soft-body physics, empty when <see cref="ReconstructsCloth"/> is false.
    /// </summary>
    internal ClothExtract Cloth
    {
        get
        {
            EnsureClothAndAnimationsQueued();
            return cloth;
        }
    }

    /// <summary>Gets whether the cloth is reconstructed: <see cref="ExtractCloth"/>, unless reconstructing it failed.</summary>
    private bool ReconstructsCloth
    {
        get
        {
            EnsureClothAndAnimationsQueued();
            return ExtractCloth && !clothFailed;
        }
    }

    /// <summary>
    /// Builds the cloth and then queues the animations, whose file names avoid the cloth's, on first use. A cloth that
    /// fails to build is reported and left out, as if <see cref="ExtractCloth"/> were false.
    /// </summary>
    [MemberNotNull(nameof(cloth))]
    private void EnsureClothAndAnimationsQueued()
    {
        if (cloth is not null)
        {
            return;
        }

        cloth = ExtractCloth ? new ClothExtract(model, physAggregateData) : new ClothExtract(null, null);

        try
        {
            cloth.Build(fileName, GetDmxFileName_ForCloth);
        }
        catch (Exception e)
        {
            ProgressReporter?.Report($"Skipping cloth of {ModelName}: {e.Message}");
            clothFailed = true;
            cloth = new ClothExtract(null, null);
        }

        EnqueueAnimations();
    }

    /// <summary>
    /// The DMX file name of the cloth file <paramref name="name"/>, suffixed where a render or physics mesh or an earlier
    /// cloth file already has it.
    /// </summary>
    private string GetDmxFileName_ForCloth(string name)
    {
        bool Taken(string candidate)
            => RenderMeshesToExtract.Exists(mesh => string.Equals(mesh.FileName, candidate, StringComparison.OrdinalIgnoreCase))
            || PhysHullsToExtract.Exists(hull => string.Equals(hull.FileName, candidate, StringComparison.OrdinalIgnoreCase))
            || PhysMeshesToExtract.Exists(mesh => string.Equals(mesh.FileName, candidate, StringComparison.OrdinalIgnoreCase))
            || (cloth is not null
                && cloth.ProxyMeshes.Exists(proxy => string.Equals(proxy.FileName, candidate, StringComparison.OrdinalIgnoreCase)));

        var dmxFileName = GetDmxFileName_ForEmbeddedMesh(name);
        for (var suffix = 1; Taken(dmxFileName); suffix++)
        {
            dmxFileName = GetDmxFileName_ForEmbeddedMesh(FormattableString.Invariant($"{name}_{suffix}"));
        }

        return dmxFileName;
    }
}
