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
    /// Gets whether to reconstruct the model's cloth (soft-body) physics. When false, the model is extracted as if it
    /// had no cloth.
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
                ReconstructCloth = ReconstructsCloth,
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

    /// <summary>Gets the model's cloth reconstruction, empty when <see cref="ReconstructsCloth"/> is false.</summary>
    internal ClothExtract Cloth
    {
        get
        {
            EnsureClothAndAnimationsQueued();
            return cloth;
        }
    }

    /// <summary>
    /// Gets whether cloth is reconstructed: it was requested, the model has some, and building it did not fail.
    /// </summary>
    private bool ReconstructsCloth
    {
        get
        {
            EnsureClothAndAnimationsQueued();
            return cloth.Reconstruction is not null;
        }
    }

    /// <summary>
    /// On first use, builds the cloth and then queues the animations, so that animation file names avoid the cloth's.
    /// Cloth that fails to build is reported and left out.
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
            cloth = new ClothExtract(null, null);
        }

        EnqueueAnimations();
    }

    /// <summary>
    /// Gets the DMX file name for cloth file <paramref name="name"/>, suffixed when a mesh or earlier cloth file already
    /// uses it.
    /// </summary>
    private string GetDmxFileName_ForCloth(string name)
    {
        var dmxFileName = GetDmxFileName_ForEmbeddedMesh(name);
        for (var suffix = 1; IsDmxFileNameTaken(dmxFileName, includePhysics: true, includeAnimations: false); suffix++)
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
            || (includeAnimations && animationsToExtract.Exists(entry => Same(entry.FileName)));
    }
}
