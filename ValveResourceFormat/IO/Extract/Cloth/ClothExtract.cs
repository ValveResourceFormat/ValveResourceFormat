using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Reconstructs editable ModelDoc cloth source from a compiled soft-body <see cref="FeModel"/>: the
/// <c>Softbody</c> node tree written into the vmdl, and the proxy-sheet and chain-grid DMX files it
/// references.
/// </summary>
internal sealed partial class ClothExtract
{
    private readonly Model? model;
    private readonly PhysAggregateData? physAggregateData;

    internal ClothExtract(Model? model, PhysAggregateData? physAggregateData)
    {
        this.model = model;
        this.physAggregateData = physAggregateData;
    }

    /// <summary>Gets the cloth proxy sheets to extract as DMX files, in declaration order.</summary>
    internal List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> ProxyMeshes { get; } = [];

    /// <summary>Gets the sheet grids generated over neighbouring bone chains, extracted as disabled DMX files.</summary>
    internal List<(string FileName, string Name, FeModel.ChainGrid Grid)> ChainGrids { get; } = [];

    /// <summary>Gets the cloth control nodes whose bones the compiled skeleton culled, which the vmdl re-declares.</summary>
    internal List<(int Node, string Name)> CulledBones { get; } = [];

    /// <summary>
    /// Gets the parent-space bone positions that put every cloth control node back on its <c>m_InitPose</c> position,
    /// keyed by bone name. Empty where the model has no cloth or the two already agree.
    /// </summary>
    internal Dictionary<string, Vector3> RestBonePositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the proxy-sheet and chain-grid DMX files to <paramref name="vmdl"/>, built when they are written.
    /// </summary>
    internal void AddSubFiles(ContentFile vmdl)
    {
        foreach (var clothProxy in ProxyMeshes)
        {
            var proxyMesh = clothProxy.Proxy;
            vmdl.AddSubFile(
                Path.GetFileName(clothProxy.FileName),
                () => BuildClothProxyMeshDmx(proxyMesh, Path.GetFileNameWithoutExtension(clothProxy.FileName))
            );
        }

        foreach (var clothGrid in ChainGrids)
        {
            var grid = clothGrid.Grid;
            vmdl.AddSubFile(
                Path.GetFileName(clothGrid.FileName),
                () => BuildClothChainGridDmx(grid, Path.GetFileNameWithoutExtension(clothGrid.FileName))
            );
        }
    }

    private readonly HashSet<FeModel.ProxyMesh> flexedProxies = [];

    /// <summary>
    /// Registers the model's skeleton with its <see cref="FeModel"/>, recovers the rest poses and queues the proxy
    /// sheets and chain grids to extract.
    /// </summary>
    internal void EnqueueClothProxyMesh(string fileName, Func<string, string> dmxFileName)
    {
        if (model is null || physAggregateData?.FeModel is not { } feModel)
        {
            return;
        }

        var skeletonBoneNames = model.Skeleton.Bones
            .Select(static bone => bone.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        feModel.SkeletonBoneNames = skeletonBoneNames;

        CulledBones.AddRange(feModel.GetCulledBoneCtrls());
        feModel.CulledBoneCtrlNodes = CulledBones.Select(static c => c.Node).ToHashSet();
        foreach (var (_, culledName) in CulledBones)
        {
            skeletonBoneNames.Add(culledName);
        }

        var boneParents = model.Skeleton.Bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().Parent?.Name, StringComparer.OrdinalIgnoreCase);
        feModel.SkeletonBoneParents = boneParents;
        feModel.SetSkeletonParents(boneParents);
        feModel.DropModelNameVertexSet(Path.GetFileNameWithoutExtension(fileName));
        feModel.DropUnnamedVertexSet();

        BuildClothRestBonePositions(feModel);

        if (feModel.IsImportedCloth)
        {
            return;
        }

        // The compiler numbers $cloth_m<N> by ordinal sort of the proxy names, so the suffix is zero-padded.
        var proxyMeshes = feModel.BuildProxyMeshes().ToList();
        var suffixWidth = Math.Max(1, (proxyMeshes.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        var proxyIndex = 0;
        foreach (var proxyMesh in proxyMeshes)
        {
            var proxyName = proxyIndex > 0
                ? "cloth_proxy" + proxyIndex.ToString(CultureInfo.InvariantCulture).PadLeft(suffixWidth, '0')
                : "cloth_proxy";
            ProxyMeshes.Add((dmxFileName(proxyName), proxyName, proxyMesh));
            proxyIndex++;
        }

        var gridIndex = 0;
        foreach (var grid in feModel.BuildChainGrids())
        {
            var name = "cloth_grid" + (gridIndex > 0 ? gridIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
            ChainGrids.Add((dmxFileName(name), name, grid));
            gridIndex++;
        }

        BuildClothChainBoneOrigins(feModel);
    }

    /// <summary>The collision-shape parent bones, which every phase declares in cloth before adding its own.</summary>
    private static HashSet<string> ClothBoneNames(FeModel feModel)
    {
        var bones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parentBone in CollisionShapeParentBones(feModel))
        {
            if (parentBone is not null)
            {
                bones.Add(parentBone);
            }
        }

        return bones;
    }

    /// <summary>
    /// Adds the cloth source of <paramref name="feModel"/> to <paramref name="rootChildren"/>, and returns whether any
    /// was emitted.
    /// </summary>
    internal bool EmitCloth(FeModel feModel, KVObject rootChildren)
    {
        var boneChains = feModel.BuildBoneChains((chain, hasOtherChains) => ClothChainVersion(feModel, chain, hasOtherChains));

        if (feModel.IsImportedCloth)
        {
            return EmitImportedClothPhase(feModel, boneChains, rootChildren);
        }

        if (ProxyMeshes.Count > 0)
        {
            return EmitProxySheetClothPhase(feModel, boneChains, rootChildren);
        }

        if (boneChains.Count > 0)
        {
            return EmitChainClothPhase(feModel, boneChains, rootChildren);
        }

        return feModel.HasData && EmitFreeNodeClothPhase(feModel, boneChains, rootChildren);
    }

    /// <summary>
    /// Re-declares the <see cref="CulledBones"/> without <c>do_not_discard</c>, so the compiler culls them again.
    /// </summary>
    internal void AddCulledClothBones(KVObject skeletonChildren)
    {
        var culledSource = physAggregateData?.FeModel;
        if (culledSource is null)
        {
            return;
        }

        // A model with no compiled bones at all keeps its hierarchy only in m_SkelParents.
        var nestByClothParent = model is not null && model.Skeleton.Roots.Length == 0 && culledSource.HasCompiledSkelParents;
        var emitted = CulledBones.Where(bone => bone.Node < culledSource.InitPosePositions.Length)
            .Select(static bone => bone.Node).ToHashSet();

        var bones = new List<(int Node, int Parent, KVObject Bone)>();
        foreach (var (node, name) in CulledBones)
        {
            if (!emitted.Contains(node))
            {
                continue;
            }

            var parent = nestByClothParent && node < culledSource.SkelParents.Length && emitted.Contains(culledSource.SkelParents[node])
                ? culledSource.SkelParents[node]
                : -1;
            var (origin, rotation) = parent >= 0
                ? ClothBoneLocalPose(culledSource, node, parent)
                : (culledSource.InitPosePositions[node],
                    node < culledSource.InitPoseRotations.Length ? culledSource.InitPoseRotations[node] : Quaternion.Identity);
            bones.Add((node, parent, MakeNode("Bone",
                ("name", name),
                ("origin", ToKVArray(origin)),
                ("angles", ToKVArray(EntityTransformHelper.ToEulerAngles(rotation))))));
        }

        var boneByNode = bones.ToDictionary(static bone => bone.Node, static bone => bone.Bone);
        foreach (var (_, parent, bone) in bones)
        {
            if (parent < 0)
            {
                skeletonChildren.Add(bone);
                continue;
            }

            var parentBone = boneByNode[parent];
            if (!parentBone.TryGetValue("children", out var childBones))
            {
                childBones = KVObject.Array();
                parentBone.Add("children", childBones);
            }

            childBones.Add(bone);
        }
    }

    /// <summary>The rest pose of control node <paramref name="node"/> relative to control node <paramref name="parent"/>.</summary>
    internal static (Vector3 Origin, Quaternion Rotation) ClothBoneLocalPose(FeModel feModel, int node, int parent)
    {
        var parentRotation = parent < feModel.InitPoseRotations.Length ? feModel.InitPoseRotations[parent] : Quaternion.Identity;
        var rotation = node < feModel.InitPoseRotations.Length ? feModel.InitPoseRotations[node] : Quaternion.Identity;
        var inverse = Quaternion.Conjugate(parentRotation);
        return (Vector3.Transform(feModel.InitPosePositions[node] - feModel.InitPosePositions[parent], inverse),
            Quaternion.Normalize(inverse * rotation));
    }

    /// <summary>
    /// Gets the Bone <c>origin</c> of each ClothChain joint, re-solved so the compiler's chain rest pose lands it on its
    /// <c>m_InitPose</c> position exactly. Only the document skeleton reads these.
    /// </summary>
    internal Dictionary<string, Vector3> ChainBoneOrigins { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the Bone <c>angles</c> of each ClothChain joint, re-solved so the compiler's chain rest pose gives it its
    /// <c>m_InitPose</c> rotation exactly. Only the document skeleton reads these.
    /// </summary>
    internal Dictionary<string, Vector3> ChainBoneAngles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the ClothChain joints whose origin or angles were re-solved, which are written without
    /// <see cref="FeModel.BoneChainJoint.ExtrudeTwistTieNudge"/>.
    /// </summary>
    private HashSet<string> RelandedJoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the parent-space bone positions written into the proxy and grid DMX joint lists, which the compiler takes
    /// <c>m_InitPose</c> from. Unlike <see cref="RestBonePositions"/> these are not capped by distance.
    /// </summary>
    internal Dictionary<string, Vector3> ProxyRestBonePositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the parent-space bone rotations written beside <see cref="ProxyRestBonePositions"/>.</summary>
    internal Dictionary<string, Quaternion> ProxyRestBoneRotations { get; } = new(StringComparer.OrdinalIgnoreCase);
}
