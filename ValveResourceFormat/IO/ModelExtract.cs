using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 models to editable vmdl/dmx format.
/// </summary>
public partial class ModelExtract
{
    private readonly Resource modelResource;
    private readonly Model model;
    private readonly PhysAggregateData physAggregateData;
    private readonly IFileLoader fileLoader;
    private readonly string fileName;

    /// <summary>
    /// Filter configuration for import operations.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only
    public record struct ImportFilter(bool ExcludeByDefault, HashSet<string> Filter);
#pragma warning restore CA2227 // Collection properties should be read only

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelExtract"/> class.
    /// </summary>
    public ModelExtract(Resource modelResource, IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);

        this.fileLoader = fileLoader;
        this.modelResource = modelResource;
        model = (Model)modelResource.DataBlock;

        var refPhysics = model.GetReferencedPhysNames().FirstOrDefault();
        if (refPhysics != null)
        {
            using var physResource = fileLoader.LoadFileCompiled(refPhysics);

            if (physResource != null)
            {
                physAggregateData = (PhysAggregateData)physResource.DataBlock;
            }
        }
        else
        {
            physAggregateData = model.GetEmbeddedPhys();
        }

        EnqueueMeshes();
        EnqueueAnimations();
    }

    /// <inheritdoc cref="ModelExtract(Resource, IFileLoader)"/>
    /// <summary>
    /// Extract a single mesh to vmdl+dmx.
    /// </summary>
    /// <param name="mesh">Mesh data</param>
    /// <param name="fileName">File name of the mesh e.g "models/my_mesh.vmesh"</param>
    public ModelExtract(Mesh mesh, string fileName)
    {
        RenderMeshesToExtract.Add(new(mesh, "unnamed", 0, GetDmxFileName_ForReferenceMesh(fileName)));
        this.fileName = Path.ChangeExtension(fileName, ".vmdl");
    }

    /// <inheritdoc cref="ModelExtract(Resource, IFileLoader)"/>
    public ModelExtract(PhysAggregateData physAggregateData, string fileName)
    {
        this.physAggregateData = physAggregateData;
        this.fileName = fileName;
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
            var options = new DatamodelRenderMeshExtractOptions
            {
                MaterialInputSignatures = MaterialInputSignatures,
                BoneRemapTable = renderMesh.BoneRemapTable,
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
                () => ToDmxMesh(physHull.Hull)
            );
        }

        foreach (var physMesh in PhysMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physMesh.FileName),
                () => ToDmxMesh(physMesh.Mesh)
            );
        }

        foreach (var anim in AnimationsToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(anim.FileName),
                () => ToDmxAnim(model, anim.Anim)
            );
        }

        return vmdl;
    }

    /// <summary>
    /// Gets the model name from either the model resource or the file name.
    /// </summary>
    public string ModelName => model?.Name ?? fileName;
}
