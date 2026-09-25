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

    /// <summary>
    /// Gets the list of cloth proxy meshes (cloth "sheets") to be extracted as sub-DMX files. Built from
    /// the soft-body <see cref="FeModel"/> surface so a recompile regenerates the <c>$cloth_*</c> nodes.
    /// </summary>
    internal List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> ProxyMeshes { get; } = [];

    /// <summary>
    /// Gets the list of cloth sheet grids generated over neighbouring bone chains (skirts/capes whose
    /// original cloth is chain-only), extracted as sub-DMX files. The sheet simulates the surface between
    /// the chains and drives the render mesh directly, like hand-authored item proxies.
    /// </summary>
    internal List<(string FileName, string Name, FeModel.ChainGrid Grid)> ChainGrids { get; } = [];

    /// <summary>
    /// Gets the cloth control nodes that were authored as skeleton bones but culled from the compiled
    /// skeleton; re-declared as Bone nodes so cloth constructs can reference them.
    /// </summary>
    internal List<(int Node, string Name)> CulledBones { get; } = [];

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

    // Sheets EmitProxySheetClothPhase re-emits with flex_cloth_borders on; their pinned vertices
    // get freed by the flag, every other sheet's freed pins ride the per-vertex
    // cloth_anchor_free_rotate paint instead (see BuildClothProxyMeshDmx).
    private readonly HashSet<FeModel.ProxyMesh> flexedProxies = [];

    // Queues a cloth proxy-mesh DMX when the model carries a soft-body FeModel with a surface (quads/tris),
    // or generated sheet grids over the bone chains when the original cloth is chain-only.
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

        // Culled cloth-only bones get re-declared in the exported skeleton, so the cloth pipeline
        // treats their names as real from here on.
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

        // An imported PhysAuthFx cloth ships its own node/rod tables and is emitted as a single
        // ImportedCloth element, so neither a synthesised proxy sheet nor a chain grid has anything to
        // attach to and their DMX files would be written for nothing.
        if (feModel.IsImportedCloth)
        {
            return;
        }

        // The compiler assigns the $cloth_m<N> mesh index by ORDINAL STRING SORT of the proxy names rather
        // than by declaration order, so "cloth_proxy10" sorts before "cloth_proxy2". Zero-padding the
        // suffix to the model's own digit count keeps declaration order and sort order identical; a model
        // with up to 10 proxies keeps single-digit names.
        var proxyMeshes = feModel.BuildProxyMeshes().ToList();
        var suffixWidth = Math.Max(1, (proxyMeshes.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        var proxyIndex = 0;
        foreach (var proxyMesh in proxyMeshes)
        {
            // One proxy per island, like the originals (node names $cloth_mXpY encode the mesh index).
            var proxyName = proxyIndex > 0
                ? "cloth_proxy" + proxyIndex.ToString(CultureInfo.InvariantCulture).PadLeft(suffixWidth, '0')
                : "cloth_proxy";
            ProxyMeshes.Add((dmxFileName(proxyName), proxyName, proxyMesh));
            proxyIndex++;
        }

        // Regular sheet grids over the bone chains are generated in BOTH cases: as the only sheet for
        // chain-only cloth, and as an alternative clean editable grid next to a recovered surface.
        // They always ship disabled (see the vmdl emission) - purely a ready-made authoring asset.
        var gridIndex = 0;
        foreach (var grid in feModel.BuildChainGrids())
        {
            var name = "cloth_grid" + (gridIndex > 0 ? gridIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
            ChainGrids.Add((dmxFileName(name), name, grid));
            gridIndex++;
        }

        BuildClothChainBoneOrigins(feModel);
    }

    /// <summary>
    /// The bones an export declares in cloth, seeded with the collision-shape parents the compiler
    /// registers on its own. Each phase adds the bones its own constructs name.
    /// </summary>
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

    // Soft-body / cloth physics (m_pFeModel): reconstruct editable ModelDoc cloth source so the model
    // recompiles into a working FeModel PHYS block AND opens in ModelDoc (no binary transplant).
    // Phase 1 recovers bone-chain cloth as ClothChain nodes. Phase 2 recovers the cloth SHEET as a
    // ClothProxyMeshFile + proxy DMX.
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

    internal void AddCulledClothBones(KVObject skeletonChildren)
    {
        // Bones the compiled skeleton culled (unskinned cloth-only joints) but the cloth still
        // references. Re-declared WITHOUT do_not_discard so the compiler culls them again; the cloth
        // build resolves against the document skeleton, which is all these need to exist in.
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

    /// <summary>
    /// The pose of a cloth control node's bone relative to its parent's bone, both read off the control nodes' rest poses.
    /// </summary>
    /// <param name="feModel">The compiled cloth.</param>
    /// <param name="node">The bone's control node.</param>
    /// <param name="parent">The parent bone's control node.</param>
    internal static (Vector3 Origin, Quaternion Rotation) ClothBoneLocalPose(FeModel feModel, int node, int parent)
    {
        var parentRotation = parent < feModel.InitPoseRotations.Length ? feModel.InitPoseRotations[parent] : Quaternion.Identity;
        var rotation = node < feModel.InitPoseRotations.Length ? feModel.InitPoseRotations[node] : Quaternion.Identity;
        var inverse = Quaternion.Conjugate(parentRotation);
        return (Vector3.Transform(feModel.InitPosePositions[node] - feModel.InitPosePositions[parent], inverse),
            Quaternion.Normalize(inverse * rotation));
    }

    /// <summary>
    /// Gets the Bone <c>origin</c> of each ClothChain joint re-solved so that the compiler's own chain rest pose puts
    /// the joint on its recorded <c>m_InitPose</c> position bit for bit. Only the document skeleton reads these; mesh
    /// joints keep <see cref="RestBonePositions"/>.
    /// </summary>
    internal Dictionary<string, Vector3> ChainBoneOrigins { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the Bone <c>angles</c> of each ClothChain joint re-solved so that the compiler's chain rest pose gives the joint
    /// its recorded <c>m_InitPose</c> rotation bit for bit. Only the document skeleton reads these.
    /// </summary>
    internal Dictionary<string, Vector3> ChainBoneAngles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the ClothChain joints whose Bone origin or angles were re-solved onto the compiler's chain rest pose. Their rings
    /// are rebuilt from the recorded transform instead of a drifted one, so they are written without the node-base tie roll
    /// (<see cref="FeModel.BoneChainJoint.ExtrudeTwistTieNudge"/>) that was chosen against the drift.
    /// </summary>
    private HashSet<string> RelandedJoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the rest-pose bone positions written into the cloth PROXY mesh only. The cloth import
    /// takes the transforms it records in <c>m_InitPose</c> from the proxy mesh file's own joint
    /// list, so a model authored with a proxy posed differently from the render mesh is reproduced
    /// by correcting that joint list alone. Unlike <see cref="RestBonePositions"/> this one is
    /// not capped at <see cref="ClothRestBoneTolerance"/>, because nothing the render mesh is
    /// skinned to moves with it.
    /// </summary>
    internal Dictionary<string, Vector3> ProxyRestBonePositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the rest-pose bone rotations, parent-local, written into the cloth PROXY mesh only, beside
    /// <see cref="ProxyRestBonePositions"/>.
    /// </summary>
    internal Dictionary<string, Quaternion> ProxyRestBoneRotations { get; } = new(StringComparer.OrdinalIgnoreCase);
}
