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
/// <c>Softbody</c> node tree written into the vmdl, and the proxy-sheet DMX files it references.
/// </summary>
internal sealed partial class ClothExtract(Model? model, PhysAggregateData? physAggregateData)
{
    private readonly ClothRestPose noRestPose = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
        new(StringComparer.OrdinalIgnoreCase));

    // Sheets declared with flex_cloth_borders, recorded while the vmdl is emitted and read when their DMX is built
    private readonly HashSet<ProxyMesh> flexedProxies = [];

    // The vmdl and each sheet's DMX both ask which selection a sheet stands for
    private readonly Dictionary<ProxyMesh, string?> proxyVertexMapNames = [];
    private List<ProxyMesh>? proxyGroup;

    private readonly List<KVObject> culledBoneNodes = [];
    private readonly KVObject rootNodes = KVObject.Array();
    private readonly List<(string FileName, byte[] Data)> subFiles = [];
    private ClothReconstruction? reconstruction;
    private KVObject? jiggleBoneList;

    /// <summary>A proxy sheet exported as its own DMX.</summary>
    internal readonly record struct ClothProxyFile(string FileName, string Name, ProxyMesh Proxy);

    /// <summary>A control node and the name it is declared under.</summary>
    internal readonly record struct NodeRef(string Name, int Node);

    /// <summary>The root bone, bone-local origin and angles a free <c>$cloth_node_</c> control node is re-authored at.</summary>
    internal readonly record struct ClothNodeAnchor(string RootBone, Vector3 Origin, Vector3 Angles);

    /// <summary>Gets the cloth proxy sheets to extract as DMX files, in declaration order.</summary>
    internal List<ClothProxyFile> ProxyMeshes { get; } = [];

    /// <summary>Gets the reconstruction of the model's cloth, or null where it has none.</summary>
    internal ClothReconstruction? Reconstruction
        => reconstruction ??= physAggregateData?.FeModel is { } fe ? new ClothReconstruction(fe) : null;

    /// <summary>Gets the cloth control nodes whose bones the compiled skeleton culled, which the vmdl re-declares.</summary>
    internal IReadOnlyList<CulledBone> CulledBones => reconstruction?.Context?.CulledBones ?? [];

    private ClothRestPose RestPose => reconstruction?.RestPose ?? noRestPose;

    /// <summary>
    /// Gets the parent-space bone positions that put every cloth control node back on its <c>m_InitPose</c> position,
    /// keyed by bone name. Empty where the model has no cloth or the two already agree.
    /// </summary>
    internal Dictionary<string, Vector3> RestBonePositions => RestPose.BonePositions;

    /// <summary>
    /// Gets the parent-space bone positions written into the proxy DMX joint lists, which the compiler takes
    /// <c>m_InitPose</c> from. Unlike <see cref="RestBonePositions"/> these are not capped by distance.
    /// </summary>
    internal Dictionary<string, Vector3> ProxyRestBonePositions => RestPose.ProxyBonePositions;

    /// <summary>Gets the parent-space bone rotations written beside <see cref="ProxyRestBonePositions"/>.</summary>
    private Dictionary<string, Quaternion> ProxyRestBoneRotations => RestPose.ProxyBoneRotations;

    /// <summary>
    /// Reconstructs the model's cloth and builds everything it writes: the vmdl nodes, the proxy-sheet DMX files and the
    /// render binding surface. Throws where the cloth cannot be reconstructed.
    /// </summary>
    internal void Build(string fileName, Func<string, string> dmxFileName)
    {
        if (physAggregateData?.FeModel is { } fe && !ClothReconstruction.HasConsistentLayout(fe))
        {
            throw new InvalidDataException("The cloth node counts or parents lie outside its nodes.");
        }

        EnqueueClothProxyMesh(fileName, dmxFileName);

        if (Reconstruction is not { } cloth)
        {
            return;
        }

        if (CulledBones.Count > 0)
        {
            AddCulledClothBones(cloth);
        }

        jiggleBoneList = ExtractJiggleBones(cloth);
        EmitCloth(cloth, rootNodes);

        if (model is not null)
        {
            foreach (var (proxyFileName, _, proxy) in ProxyMeshes)
            {
                subFiles.Add((proxyFileName,
                    BuildClothProxyMeshDmx(model.Skeleton, cloth, proxy, Path.GetFileNameWithoutExtension(proxyFileName))));
            }
        }

        RenderBindingSurface = BuildRenderBindingSurface();
    }

    /// <summary>Adds the re-declared culled cloth bones to the skeleton list's <paramref name="skeletonChildren"/>.</summary>
    internal void AddCulledBonesTo(KVObject skeletonChildren)
    {
        foreach (var bone in culledBoneNodes)
        {
            skeletonChildren.Add(bone);
        }
    }

    /// <summary>Adds the jiggle bone list and the cloth nodes to the vmdl root's <paramref name="rootChildren"/>.</summary>
    internal void AddToValveModel(KVObject rootChildren)
    {
        if (jiggleBoneList is not null)
        {
            rootChildren.Add(jiggleBoneList);
        }

        foreach (var (_, node) in rootNodes)
        {
            rootChildren.Add(node);
        }
    }

    /// <summary>Adds the proxy-sheet DMX files to <paramref name="vmdl"/>.</summary>
    internal void AddSubFiles(ContentFile vmdl)
    {
        foreach (var (fileName, data) in subFiles)
        {
            vmdl.AddSubFile(Path.GetFileName(fileName), () => data);
        }
    }

    /// <summary>
    /// Reconstructs the model's <see cref="FeModel"/> against its skeleton and queues the proxy sheets to extract.
    /// </summary>
    private void EnqueueClothProxyMesh(string fileName, Func<string, string> dmxFileName)
    {
        if (model is null || physAggregateData?.FeModel is not { } fe)
        {
            return;
        }

        var cloth = ClothReconstruction.ForModel(fe, model.Skeleton, Path.GetFileNameWithoutExtension(fileName));
        reconstruction = cloth;

        if (cloth.IsImportedCloth)
        {
            return;
        }

        var proxyMeshes = cloth.BuildProxyMeshes().ToList();
        var suffixWidth = Math.Max(1, (proxyMeshes.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        var proxyIndex = 0;
        foreach (var proxyMesh in proxyMeshes)
        {
            var proxyName = proxyIndex > 0
                ? "cloth_proxy" + proxyIndex.ToString(CultureInfo.InvariantCulture).PadLeft(suffixWidth, '0')
                : "cloth_proxy";
            ProxyMeshes.Add(new ClothProxyFile(dmxFileName(proxyName), proxyName, proxyMesh));
            proxyIndex++;
        }
    }

    /// <summary>The collision-shape parent bones, which every phase declares in cloth before adding its own.</summary>
    private static HashSet<string> ClothBoneNames(ClothReconstruction cloth)
        => new(cloth.CollisionShapes.ParentBones, StringComparer.OrdinalIgnoreCase);

    private void EmitCloth(ClothReconstruction cloth, KVObject rootChildren)
    {
        var boneChains = cloth.BuildDeclaredBoneChains();

        if (cloth.IsImportedCloth)
        {
            EmitImportedClothPhase(cloth, boneChains, rootChildren);
        }
        else if (ProxyMeshes.Count > 0)
        {
            EmitProxySheetClothPhase(cloth, boneChains, rootChildren);
        }
        else if (boneChains.Count > 0)
        {
            EmitChainClothPhase(cloth, boneChains, rootChildren);
        }
        else
        {
            EmitFreeNodeClothPhase(cloth, boneChains, rootChildren);
        }
    }

    /// <summary>A <c>Softbody</c> node carrying its own attributes, and its children list.</summary>
    private (KVObject Softbody, KVObject Children) MakeSoftbody(ClothReconstruction cloth)
    {
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, cloth);
        return (softbody, softbodyChildren);
    }

    /// <summary>Adds the <c>cloth</c> folder to a Softbody's children and returns the folder's children.</summary>
    private static KVObject AddClothFolder(KVObject softbodyChildren)
    {
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);
        return clothFolderChildren;
    }

    /// <summary>Declares the model's imported PhysAuthFx strip, if it has one, and returns its nodes.</summary>
    private static IReadOnlySet<int> AddImportedStrip(KVObject clothFolderChildren, ClothReconstruction cloth)
    {
        var strip = cloth.ImportedStripNodes;
        if (strip.Count > 0)
        {
            clothFolderChildren.Add(MakeImportedCloth(cloth, strip));
        }

        return strip;
    }

    /// <summary>
    /// Ends every phase: follow bones, joint locks where given, collision shapes, the anti-tunnel group where cloth is
    /// given, effects and shape-parent nodes, then the Softbody itself and the anti-tunnel probes beside it.
    /// </summary>
    private void AddClothPhaseTail(ClothReconstruction cloth, KVObject rootChildren, KVObject softbody, KVObject softbodyChildren,
        HashSet<string> clothBones, List<BoneChain> effectChains, IEnumerable<string>? antiTunnelCloth = null,
        Func<int, string, bool>? jointLocks = null, IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        AddClothFollowBones(softbodyChildren, cloth, clothBones);
        if (jointLocks is not null)
        {
            AddClothJointLocks(softbodyChildren, cloth, jointLocks);
        }

        var shapeNames = AddClothCollisionShapes(softbodyChildren, cloth);
        if (antiTunnelCloth is not null)
        {
            AddClothAntiTunnelGroup(softbodyChildren, cloth, shapeNames, [.. antiTunnelCloth]);
        }

        AddClothEffects(softbodyChildren, cloth, AvailableVertexMaps(cloth, effectChains));
        AddShapeParentDefaultClothNodes(softbodyChildren, cloth);
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, cloth, proxyNodeNames);
    }

    /// <summary>
    /// Re-declares the <see cref="CulledBones"/> without <c>do_not_discard</c>, so the compiler culls them again.
    /// </summary>
    private void AddCulledClothBones(ClothReconstruction cloth)
    {
        var nestByClothParent = model is not null && model.Skeleton.Roots.Length == 0 && cloth.HasCompiledSkelParents;
        var emitted = CulledBones.Where(bone => bone.Node < cloth.Index.InitPosePositions.Length)
            .Select(static bone => bone.Node).ToHashSet();

        var parentOf = new Dictionary<int, int>();
        foreach (var (node, _) in CulledBones)
        {
            if (emitted.Contains(node))
            {
                parentOf[node] = nestByClothParent && node < cloth.SkelParents.Length
                    && emitted.Contains(cloth.SkelParents[node])
                        ? cloth.SkelParents[node]
                        : -1;
            }
        }

        // Break parent cycles by turning a bone on each into a root
        foreach (var node in parentOf.Keys.ToList())
        {
            var ancestor = parentOf[node];
            for (var steps = 0; ancestor >= 0 && ancestor != node && steps < parentOf.Count; steps++)
            {
                ancestor = parentOf[ancestor];
            }

            if (ancestor == node)
            {
                parentOf[node] = -1;
            }
        }

        var bones = new List<(int Node, int Parent, KVObject Bone)>();
        foreach (var (node, name) in CulledBones)
        {
            if (!parentOf.TryGetValue(node, out var parent))
            {
                continue;
            }

            var (origin, rotation) = parent >= 0
                ? ClothBoneLocalPose(cloth, node, parent)
                : (cloth.Index.InitPosePositions[node], cloth.Index.InitPoseRotations[node]);
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
                culledBoneNodes.Add(bone);
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
    internal static (Vector3 Origin, Quaternion Rotation) ClothBoneLocalPose(ClothReconstruction cloth, int node, int parent)
    {
        return RelativePose(cloth.Index.InitPosePositions[node], cloth.Index.InitPoseRotations[node],
            cloth.Index.InitPosePositions[parent], cloth.Index.InitPoseRotations[parent]);
    }

    /// <summary>A pose relative to its parent's pose.</summary>
    private static (Vector3 Position, Quaternion Rotation) RelativePose(Vector3 position, Quaternion rotation,
        Vector3 parentPosition, Quaternion parentRotation)
    {
        var inverse = Quaternion.Conjugate(parentRotation);
        return (Vector3.Transform(position - parentPosition, inverse), Quaternion.Normalize(inverse * rotation));
    }
}
