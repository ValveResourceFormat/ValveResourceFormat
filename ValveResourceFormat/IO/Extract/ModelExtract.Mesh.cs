using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Datamodel;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{

    /// <summary>
    /// Gets the list of render meshes to be extracted.
    /// </summary>
    public List<RenderMeshExtractConfiguration> RenderMeshesToExtract { get; } = [];

    /// <summary>
    /// Gets the list of cloth proxy meshes (cloth "sheets") to be extracted as sub-DMX files. Built from
    /// the soft-body <see cref="FeModel"/> surface so a recompile regenerates the <c>$cloth_*</c> nodes.
    /// </summary>
    public List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> ClothProxyMeshesToExtract { get; } = [];

    /// <summary>
    /// Gets the list of cloth sheet grids generated over neighbouring bone chains (skirts/capes whose
    /// original cloth is chain-only), extracted as sub-DMX files. The sheet simulates the surface between
    /// the chains and drives the render mesh directly, like hand-authored item proxies.
    /// </summary>
    public List<(string FileName, string Name, FeModel.ChainGrid Grid)> ClothChainGridsToExtract { get; } = [];

    /// <summary>
    /// Gets the cloth control nodes that were authored as skeleton bones but culled from the compiled
    /// skeleton; re-declared as Bone nodes so cloth constructs can reference them.
    /// </summary>
    public List<(int Node, string Name)> CulledClothBones { get; } = [];

    /// <summary>
    /// Gets the parent-space bone positions that put every cloth control node back on the rest position
    /// the FeModel records for it, keyed by bone name. Empty where the model has no cloth or the two
    /// already agree.
    /// </summary>
    /// <remarks>
    /// <c>m_modelSkeleton</c> is a lossy re-expression of the authored bone transforms - re-composing it
    /// walks away from the authored world pose as the hierarchy deepens (prof_dynamo's coat chain ends
    /// 4.8e-3 units out, archer's fingers 1.3e-2, and the error grows strictly with depth) - while
    /// <c>m_InitPose</c> keeps the authored world position of every control node to float32.
    /// Emitting the skeleton straight from the compiled bone data therefore hands the compiler a rest
    /// pose the original was never built from, and every ctrl offset measured against those bones, plus
    /// every chain ring extruded off them, inherits the error.
    /// </remarks>
    public Dictionary<string, Vector3> ClothRestBonePositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the material input signatures for mapping DirectX semantic names.
    /// </summary>
    public Dictionary<string, Material.VsInputSignature> MaterialInputSignatures { get; } = [];

    /// <summary>
    /// Gets or sets the translation offset for the model.
    /// </summary>
    public Vector3 Translation { get; set; }

    /// <summary>
    /// Options for extracting a render mesh to datamodel format.
    /// </summary>
    public readonly struct DatamodelRenderMeshExtractOptions
    {
        /// <summary>
        /// Split draw calls into sub-meshes named draw0, draw1, draw2...
        /// </summary>
        public bool SplitDrawCallsIntoSeparateSubmeshes { get; init; }

        /// <summary>
        /// When set together with <see cref="SplitDrawCallsIntoSeparateSubmeshes"/>, receives each sub-mesh
        /// paired with the draw call it was made from, in draw call order.
        /// </summary>
        public List<(DmeDag Dag, KVObject DrawCall)>? SubmeshDrawCalls { get; init; }

        /// <summary>
        /// Pre-parsed input signatures used to map DirectX semantic names to engine semantic names.
        /// </summary>
        public Dictionary<string, Material.VsInputSignature> MaterialInputSignatures { get; init; }

        /// <summary>
        /// Remap table for the mesh bone indices.
        /// </summary>
        public int[]? BoneRemapTable { get; init; }

        /// <summary>
        /// Skeleton whose bones the mesh's BLENDINDICES reference (post-remap, in <see cref="Bone.Index"/> order).
        /// When provided, bones are emitted into the DMX <c>jointList</c> so ModelDoc can resolve indices.
        /// </summary>
        public Skeleton? Skeleton { get; init; }

        /// <summary>
        /// Parent-space bone positions to emit in place of the skeleton's own, keyed by bone name
        /// (see <see cref="ClothRestBonePositions"/>).
        /// </summary>
        public IReadOnlyDictionary<string, Vector3>? BonePositions { get; init; }
    }

    /// <summary>
    /// Configuration for extracting a render mesh.
    /// </summary>
    public record struct RenderMeshExtractConfiguration(
        Mesh Mesh,
        string Name,
        int Index,
        string FileName,
        int[]? BoneRemapTable = null,
        Skeleton? Skeleton = null,
        ImportFilter ImportFilter = default
    );

    string GetDmxFileName_ForEmbeddedMesh(string subString, int number = 0)
    {
        var fileName = ModelName;
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName)
            + "_"
            + subString
            + (number > 0 ? number : string.Empty)
            + ".dmx")
            .Replace('\\', '/');
    }

    static string GetDmxFileName_ForReferenceMesh(string fileName)
        => Path.ChangeExtension(fileName, ".dmx").Replace('\\', '/');

    private void EnqueueMeshes()
    {
        if (fileLoader is not null) // May be null for mesh-only constructor
        {
            FileExtract.EnsurePopulatedStringToken(fileLoader);
        }
        EnqueueRenderMeshes();
        EnqueuePhysMeshes();
    }

    /// <summary>
    /// The parent-space position to emit for a bone: its cloth rest correction where there is one, and
    /// its compiled transform otherwise.
    /// </summary>
    internal static Vector3 BonePosition(Bone bone, IReadOnlyDictionary<string, Vector3>? overrides)
        => overrides is not null && overrides.TryGetValue(bone.Name, out var position) ? position : bone.Position;

    private void EnqueueRenderMeshes()
    {
        if (model == null)
        {
            return;
        }

        GrabMaterialInputSignatures(modelResource);

        var i = 0;
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            var remapTable = model.GetRemapTable(embedded.MeshIndex);
            RenderMeshesToExtract.Add(new(embedded.Mesh, embedded.Name, embedded.MeshIndex, GetDmxFileName_ForEmbeddedMesh(embedded.Name, i++), remapTable, model.Skeleton));
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            Debug.Assert(fileLoader is not null, "fileLoader should not be null when loading reference meshes");

            using var resource = fileLoader.LoadFileCompiled(reference.MeshName);

            if (resource is null)
            {
                continue;
            }

            GrabMaterialInputSignatures(resource);

            if (resource.DataBlock is not Mesh mesh)
            {
                continue;
            }

            model.SetExternalMeshData(mesh);

            var remapTable = model.GetRemapTable(reference.MeshIndex);
            var meshKey = Path.GetFileNameWithoutExtension(reference.MeshName);

            RenderMeshesToExtract.Add(new(mesh, meshKey, reference.MeshIndex, GetDmxFileName_ForReferenceMesh(reference.MeshName), remapTable, model.Skeleton));
        }
    }

    internal void GrabMaterialInputSignatures(Resource? resource)
    {
        Debug.Assert(fileLoader is not null, "fileLoader should not be null when grabbing material signatures");

        var materialReferences = resource?.ExternalReferences?.ResourceRefInfoList.Where(static r => r.Name[^4..] == "vmat");
        foreach (var material in materialReferences ?? [])
        {
            MaterialInputSignatures[material.Name] = Material.LoadInputSignature(fileLoader, material.Name);
        }
    }

    /// <summary>
    /// Extracts content files from an aggregate model resource, splitting by draw calls.
    /// </summary>
    public static IEnumerable<ContentFile> GetContentFiles_DrawCallSplit(Resource aggregateModelResource, IFileLoader fileLoader, Vector3[] drawOrigins, int drawCallCount)
    {
        var extract = new ModelExtract(aggregateModelResource, fileLoader) { Type = ModelExtractType.Map_AggregateSplit };
        Debug.Assert(extract.RenderMeshesToExtract.Count == 1);

        if (extract.RenderMeshesToExtract.Count == 0)
        {
            yield break;
        }

        var (mesh, name, index, fileName, boneRemapTable, skeleton, _) = extract.RenderMeshesToExtract[0];

        var options = new DatamodelRenderMeshExtractOptions
        {
            MaterialInputSignatures = extract.MaterialInputSignatures,
            SplitDrawCallsIntoSeparateSubmeshes = true,
            BoneRemapTable = boneRemapTable,
            Skeleton = skeleton,
            BonePositions = extract.ClothRestBonePositions,
        };

        byte[] sharedDmxExtractMethod() => ToDmxMesh(
            mesh,
            Path.GetFileNameWithoutExtension(fileName),
            options
        );

        var sharedMeshExtractConfiguration = new RenderMeshExtractConfiguration(mesh, name, index, fileName, boneRemapTable, skeleton, new(true, new(1)));
        extract.RenderMeshesToExtract.Clear();
        extract.RenderMeshesToExtract.Add(sharedMeshExtractConfiguration);

        for (var i = 0; i < drawCallCount; i++)
        {
            sharedMeshExtractConfiguration.ImportFilter.Filter.Clear();
            sharedMeshExtractConfiguration.ImportFilter.Filter.Add("draw" + i);

            extract.Translation = drawOrigins.Length > i
                ? -1 * drawOrigins[i]
                : Vector3.Zero;

            var vmdl = new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(extract.ToValveModel()),
                FileName = GetFragmentModelName(extract.ModelName, i),
            };

            if (i == 0)
            {
                vmdl.AddSubFile(Path.GetFileName(fileName), sharedDmxExtractMethod);
            }

            yield return vmdl;
        }
    }

    /// <summary>
    /// Gets the fragment model name for a draw call index.
    /// </summary>
    public static string GetFragmentModelName(string aggModelName, int drawCallIndex)
    {
        const string vmdlExt = ".vmdl";
        return aggModelName[..^vmdlExt.Length] + "_draw" + drawCallIndex + vmdlExt;
    }

    /// <summary>
    /// Converts a mesh to DMX format.
    /// </summary>
    public static byte[] ToDmxMesh(Mesh mesh, string name, DatamodelRenderMeshExtractOptions options = default)
    {
        using var dmx = ConvertMeshToDatamodelMesh(mesh, name, options);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    /// <summary>
    /// Converts a mesh to a datamodel mesh representation.
    /// </summary>
    public static Datamodel.Datamodel ConvertMeshToDatamodelMesh(Mesh mesh, string name, DatamodelRenderMeshExtractOptions options)
    {
        DmeModel? skeletonRoot = null;

        if (options.Skeleton is { Bones.Length: > 0 } skeleton)
        {
            skeletonRoot = BuildDmeDagSkeleton(skeleton, out _, bonePositions: options.BonePositions);
        }

        return DmxMeshBuilder.Build(mesh, name, new DmxMeshBuildOptions
        {
            SplitDrawCallsIntoSeparateSubmeshes = options.SplitDrawCallsIntoSeparateSubmeshes,
            SubmeshDrawCalls = options.SubmeshDrawCalls,
            MaterialInputSignatures = options.MaterialInputSignatures,
            BoneRemapTable = options.BoneRemapTable,
            SkeletonRoot = skeletonRoot,
        });
    }

    /// <summary>
    /// Filter configuration for import operations.
    /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only
    public record struct ImportFilter(bool ExcludeByDefault, HashSet<string> Filter);
#pragma warning restore CA2227 // Collection properties should be read only
}
