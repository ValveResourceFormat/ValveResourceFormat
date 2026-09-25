using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// Separates equal blend weights on one proxy vertex by the smallest amount that survives the
    /// float stream, keeping the recovered order. The cloth proxy importer sorts a vertex's
    /// influences by descending weight to choose its primary anchor bone, which becomes the
    /// vertex's <c>m_CtrlOffsets</c> parent while the rest become its <c>m_CtrlSoftOffsets</c>
    /// parents; the sort breaks a tie on something the emitted skinning does not control, so an
    /// exactly tied pair can promote the wrong bone. The nudge is several orders of magnitude below
    /// the weight resolution the compiled data carries.
    /// </summary>
    static (string Bone, float Weight)[] SeparateTiedInfluenceWeights((string Bone, float Weight)[] influences)
    {
        static bool IsTied(float a, float b)
            => MathF.Abs(a - b) <= TiedInfluenceSeparation * MathF.Max(MathF.Abs(a), MathF.Abs(b));

        var tied = false;
        for (var i = 1; i < influences.Length && !tied; i++)
        {
            tied = IsTied(influences[i].Weight, influences[i - 1].Weight);
        }

        if (!tied)
        {
            return influences;
        }

        var separated = new (string Bone, float Weight)[influences.Length];
        influences.CopyTo(separated, 0);
        for (var i = 1; i < separated.Length; i++)
        {
            if (IsTied(influences[i].Weight, influences[i - 1].Weight))
            {
                separated[i] = (separated[i].Bone, separated[i - 1].Weight * (1f - TiedInfluenceSeparation));
            }
        }

        return separated;
    }

    /// <summary>
    /// Relative gap forced between two proxy blend weights that would otherwise be a tie. It is
    /// three orders below the 1/255 quantum a painted weight is authored at and survives the
    /// per-vertex renormalization the importer applies before it sorts.
    /// </summary>
    const float TiedInfluenceSeparation = 1e-6f;

    // A selection solved as a volume carries its strength and the node it takes its scale from. Both are
    // authored on the container, and the volumetric strength also decides the covered nodes' masses.
    static void AddClothVertexMapAttributes(KVObject mapNode, FeModel feModel, string mapName,
        IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        var map = feModel.VertexMaps.FirstOrDefault(m => m.Name == mapName);
        if (map.Name != mapName || map.VolumetricSolveStrength <= 0f)
        {
            return;
        }

        mapNode.Add("volumetric_solve", map.VolumetricSolveStrength);

        if (ResolveAntiTunnelNodeName(feModel, map.ScaleSourceNode, proxyNodeNames) is { } scaleSource)
        {
            mapNode.Add("scale_source_node", scaleSource);
        }
    }

    /// <summary>
    /// Declares a <c>ClothVertexMap</c> for every selection solved as a volume over chain joints. The compiler
    /// reads a container's <c>volumetric_solve</c> and <c>scale_source_node</c> only through the <c>data.nodes</c>
    /// table naming its members, never through a joint's own <c>vertex_map</c>, so the table lists every covered
    /// joint at its membership weight. A selection that also covers a sheet vertex, a free cloth node or any
    /// other named node is left to the containers those phases declare.
    /// </summary>
    internal static void AddClothChainVolumetricMaps(KVObject softbodyChildren, FeModel feModel,
        IEnumerable<FeModel.BoneChain> chains)
    {
        var joints = chains.SelectMany(static chain => chain.Joints).ToList();
        var jointNames = joints.Select(static joint => joint.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var map in feModel.VertexMaps)
        {
            if (map.VolumetricSolveStrength <= 0f || CoversNodeOutsideChains(feModel, map, jointNames))
            {
                continue;
            }

            var members = KVObject.Collection();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var joint in joints)
            {
                var weight = map.WeightOf(joint.Node);
                if (weight <= 0f || !listed.Add(joint.Name))
                {
                    continue;
                }

                if (weight >= 1f)
                {
                    members.Add(joint.Name, true);
                }
                else
                {
                    var member = KVObject.Collection();
                    member.Add("weight", weight);
                    members.Add(joint.Name, member);
                }
            }

            if (listed.Count == 0)
            {
                continue;
            }

            var (mapNode, _) = MakeListNode("ClothVertexMap");
            mapNode.Add("name", map.Name);
            AddClothVertexMapAttributes(mapNode, feModel, map.Name, proxyNodeNames: null);
            var data = KVObject.Collection();
            data.Add("nodes", members);
            mapNode.Add("data", data);
            softbodyChildren.Add(mapNode);
        }
    }

    static bool CoversNodeOutsideChains(FeModel feModel, FeModel.VertexMap map, HashSet<string> jointNames)
    {
        for (var node = map.VertexBase; node < map.VertexBase + map.VertexCount && node < feModel.CtrlNames.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (map.WeightOf(node) > 0f && (name.StartsWith("$cloth_", StringComparison.Ordinal)
                || (!name.StartsWith('$') && !jointNames.Contains(name))))
            {
                return true;
            }
        }

        return false;
    }

    // Puts a free cloth node into the ClothVertexMap containers of every selection covering it, and
    // returns where the node itself goes. Each container lists its members in the data.nodes table the
    // ClothNodeListEditor keeps, which is membership on its own (with a partial weight where the
    // selection has one) and the only route on which the compiler reads the container's
    // volumetric_solve and scale_source_node. A node covered by exactly one selection is also parented
    // under that container, the grouping the "Add Cloth Vertex Map" wizard builds, unless the caller
    // keeps it flat; a node in several selections stays flat, a child having one parent.
    static Func<int, bool, KVObject> ClothVertexMapFolders(FeModel feModel, KVObject clothFolderChildren)
    {
        var groups = new Dictionary<string, (KVObject Children, KVObject Members)>(StringComparer.Ordinal);

        (KVObject Children, KVObject Members) GroupFor(string mapName)
        {
            if (!groups.TryGetValue(mapName, out var group))
            {
                var (mapNode, mapChildren) = MakeListNode("ClothVertexMap");
                mapNode.Add("name", mapName);
                AddClothVertexMapAttributes(mapNode, feModel, mapName, proxyNodeNames: null);
                var members = KVObject.Collection();
                var data = KVObject.Collection();
                data.Add("nodes", members);
                mapNode.Add("data", data);
                clothFolderChildren.Add(mapNode);
                groups[mapName] = group = (mapChildren, members);
            }

            return group;
        }

        return (node, parentUnderMap) =>
        {
            var maps = feModel.GetVertexMapNames(node);
            if (maps is null)
            {
                return clothFolderChildren;
            }

            var memberName = ResolveAntiTunnelNodeName(feModel, node, proxyNodeNames: null);
            if (memberName is null || memberName.StartsWith('$'))
            {
                return parentUnderMap && !maps.Contains(',', StringComparison.Ordinal)
                    ? GroupFor(FeModel.VertexMapName(maps)).Children
                    : clothFolderChildren;
            }

            KVObject? home = null;
            foreach (var entry in maps.Split(','))
            {
                var mapName = FeModel.VertexMapName(entry);
                var group = GroupFor(mapName);
                var weight = feModel.VertexMapWeight(mapName, node);
                if (weight >= 1f)
                {
                    group.Members.Add(memberName, true);
                }
                else
                {
                    var member = KVObject.Collection();
                    member.Add("weight", weight);
                    group.Members.Add(memberName, member);
                }

                home = home is null ? group.Children : clothFolderChildren;
            }

            return parentUnderMap && home is not null ? home : clothFolderChildren;
        };
    }

    // A compiled model can carry two spellings of one bone: m_modelSkeleton's m_boneName and, for cloth
    // control nodes, the FeModel's m_CtrlName. Both are authored, and the compiler records each verbatim
    // because every bone lookup it does is case-insensitive. This export has one name per bone, so a bone
    // the compiler registers as a control node through a blend INDEX rather than a KV name string comes
    // back under the skeleton's spelling instead of the cloth data's.
    //
    // Re-spelling the joints of THIS sheet alone leaves everything else in place: the compiler still binds
    // each joint to the same bone case-insensitively, the model skeleton and every other DMX keep the
    // spelling they were compiled with, and the control node lands under the cloth data's name.
    static void RespellJointsAsClothControlNodes(DmeModel dmeModel, FeModel? feModel)
    {
        if (feModel is null || feModel.CtrlNames.Length == 0)
        {
            return;
        }

        var clothSpelling = new Dictionary<string, string>(feModel.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var ctrlName in feModel.CtrlNames)
        {
            clothSpelling.TryAdd(ctrlName, ctrlName);
        }

        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint
                && clothSpelling.TryGetValue(joint.Name, out var spelling) && spelling != joint.Name)
            {
                joint.Name = spelling;
                joint.Transform.Name = spelling;
            }
        }
    }

    /// <summary>
    /// Adds the culled cloth bones the vmdl re-declares (<see cref="AddCulledClothBones"/>) to a cloth
    /// DMX's joint list at their control node's rest transform, and registers them in
    /// <paramref name="boneIndexByName"/> so the sheet's skin weights can reference them.
    /// </summary>
    /// <remarks>
    /// The compiler parents a proxy joint's node to the nearest DAG ancestor joint of the same file that
    /// has a node, so a culled bone whose compiled parent is a joint of this DMX is nested under that
    /// joint; the rest stay root joints.
    /// </remarks>
    void AppendCulledClothBoneJoints(DmeModel dmeModel, Dictionary<string, int> boneIndexByName)
    {
        if (physAggregateData?.FeModel is { } feModel)
        {
            AppendCulledClothBoneJoints(dmeModel, boneIndexByName, feModel, CulledClothBones);
            NestProxyJointsUnderCompiledParents(dmeModel, feModel);
        }
    }

    /// <summary>
    /// Moves every joint of a cloth DMX whose control node has a compiled parent that is another joint of the same
    /// DMX, but not one of its DAG ancestors, under that parent's joint at the local transform that keeps its
    /// model-space transform. A joint whose compiled parent sits in its own subtree is left where it is.
    /// </summary>
    /// <remarks>
    /// The compiler parents a proxy joint's node to the nearest DAG ancestor joint of the same file that has a node,
    /// so a joint the skeleton hangs elsewhere compiles with the wrong parent or none.
    /// </remarks>
    internal static void NestProxyJointsUnderCompiledParents(DmeModel dmeModel, FeModel feModel)
    {
        if (!feModel.HasCompiledSkelParents)
        {
            return;
        }

        var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var node = 0; node < feModel.CtrlNames.Length; node++)
        {
            nodeByName.TryAdd(feModel.CtrlNames[node], node);
        }

        var jointByName = new Dictionary<string, DmeJoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint)
            {
                jointByName.TryAdd(joint.Name, joint);
            }
        }

        var parentOf = new Dictionary<DmeJoint, object>();
        var pending = new Stack<object>();
        pending.Push(dmeModel);
        while (pending.Count > 0)
        {
            var dag = pending.Pop();
            var children = dag is DmeModel model ? model.Children : ((DmeDag)dag).Children;
            foreach (var child in children)
            {
                if (child is DmeJoint childJoint && parentOf.TryAdd(childJoint, dag))
                {
                    pending.Push(childJoint);
                }
            }
        }

        bool IsAncestor(DmeJoint candidate, DmeJoint joint)
        {
            for (var at = parentOf.GetValueOrDefault(joint); at is DmeJoint up; at = parentOf.GetValueOrDefault(up))
            {
                if (up == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        var moves = new List<(DmeJoint Joint, DmeJoint Parent)>();
        foreach (var (name, joint) in jointByName)
        {
            if (!nodeByName.TryGetValue(name, out var node) || node >= feModel.SkelParents.Length)
            {
                continue;
            }

            var parent = feModel.SkelParents[node];
            if (parent < 0 || parent >= feModel.CtrlNames.Length
                || !jointByName.TryGetValue(feModel.CtrlNames[parent], out var parentJoint)
                || parentJoint == joint || !parentOf.ContainsKey(joint) || !parentOf.ContainsKey(parentJoint)
                || IsAncestor(parentJoint, joint) || IsAncestor(joint, parentJoint))
            {
                continue;
            }

            moves.Add((joint, parentJoint));
        }

        if (moves.Count == 0)
        {
            return;
        }

        var world = DmeJointWorldTransforms(dmeModel);
        foreach (var (joint, parentJoint) in moves)
        {
            if (IsAncestor(joint, parentJoint))
            {
                continue;
            }

            var oldParent = parentOf[joint];
            var siblings = oldParent is DmeModel model ? model.Children : ((DmeDag)oldParent).Children;
            siblings.Remove(joint);

            var (position, rotation) = world[joint];
            var (parentPosition, parentRotation) = world[parentJoint];
            var inverse = Quaternion.Conjugate(parentRotation);
            joint.Transform.Position = Vector3.Transform(position - parentPosition, inverse);
            joint.Transform.Orientation = Quaternion.Normalize(inverse * rotation);
            parentJoint.Children.Add(joint);
            parentOf[joint] = parentJoint;
        }
    }

    /// <inheritdoc cref="AppendCulledClothBoneJoints(DmeModel, Dictionary{string, int})"/>
    internal static void AppendCulledClothBoneJoints(DmeModel dmeModel, Dictionary<string, int> boneIndexByName,
        FeModel feModel, IEnumerable<(int Node, string Name)> culledClothBones)
    {
        var appended = new List<(int Node, DmeJoint Joint)>();
        foreach (var (node, culledName) in culledClothBones)
        {
            if (node >= feModel.InitPosePositions.Length || boneIndexByName.ContainsKey(culledName))
            {
                continue;
            }

            var joint = new DmeJoint { Name = culledName };
            joint.Transform.Name = culledName;
            joint.Transform.Position = feModel.InitPosePositions[node];
            joint.Transform.Orientation = node < feModel.InitPoseRotations.Length
                ? feModel.InitPoseRotations[node]
                : Quaternion.Identity;
            boneIndexByName[culledName] = dmeModel.JointList.Count;
            dmeModel.JointList.Add(joint);
            appended.Add((node, joint));
        }

        if (appended.Count == 0)
        {
            return;
        }

        var world = feModel.HasCompiledSkelParents ? DmeJointWorldTransforms(dmeModel) : [];
        var jointByName = new Dictionary<string, DmeJoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint)
            {
                jointByName.TryAdd(joint.Name, joint);
            }
        }

        foreach (var (_, joint) in appended)
        {
            world[joint] = (joint.Transform.Position, joint.Transform.Orientation);
        }

        foreach (var (node, joint) in appended)
        {
            var parent = feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length ? feModel.SkelParents[node] : -1;
            if (parent < 0 || parent >= feModel.CtrlNames.Length
                || !jointByName.TryGetValue(feModel.CtrlNames[parent], out var parentJoint)
                || parentJoint == joint || !world.TryGetValue(parentJoint, out var parentWorld))
            {
                dmeModel.Children.Add(joint);
                continue;
            }

            var inverse = Quaternion.Conjugate(parentWorld.Rotation);
            joint.Transform.Position = Vector3.Transform(world[joint].Position - parentWorld.Position, inverse);
            joint.Transform.Orientation = Quaternion.Normalize(inverse * world[joint].Rotation);
            parentJoint.Children.Add(joint);
        }
    }

    /// <summary>
    /// The model-space transform of every joint reachable from <paramref name="dmeModel"/>'s DAG roots,
    /// composed from the joints' local transforms.
    /// </summary>
    static Dictionary<DmeJoint, (Vector3 Position, Quaternion Rotation)> DmeJointWorldTransforms(DmeModel dmeModel)
    {
        var world = new Dictionary<DmeJoint, (Vector3 Position, Quaternion Rotation)>();
        var pending = new Stack<(DmeDag Dag, Vector3 Position, Quaternion Rotation)>();
        foreach (var child in dmeModel.Children)
        {
            if (child is DmeJoint joint)
            {
                pending.Push((joint, Vector3.Zero, Quaternion.Identity));
            }
        }

        while (pending.Count > 0)
        {
            var (dag, parentPosition, parentRotation) = pending.Pop();
            var position = parentPosition + Vector3.Transform(dag.Transform.Position, parentRotation);
            var rotation = Quaternion.Normalize(parentRotation * dag.Transform.Orientation);
            if (dag is not DmeJoint joint || !world.TryAdd(joint, (position, rotation)))
            {
                continue;
            }

            foreach (var child in dag.Children)
            {
                if (child is DmeJoint childJoint)
                {
                    pending.Push((childJoint, position, rotation));
                }
            }
        }

        return world;
    }

    /// <summary>
    /// The sheet's faces, with enough all-pinned filler triangles appended to give every vertex slot a
    /// face corner of its own.
    /// </summary>
    /// <remarks>
    /// The compiler fills a sheet's per-vertex rest-normal array by ORDINAL out of the flattened
    /// per-corner stream, so a sheet whose slot range is wider than its face-corner count leaves its last
    /// vertices on the array's zero fill and their rest frames come back degenerate. A sheet reaches that
    /// state through <c>PadToAuthoredSlots</c>, which restores the authored slot range while only the
    /// rod-generating faces are recoverable.
    /// <para>
    /// The filler is a triangle over PINNED vertices the sheet already faces: a face whose corners are all
    /// static belongs to a fully-static region the solver discards, so it adds no rod, no element and no
    /// node, and the original's own face set is left exactly as recovered. That is also why the compiled
    /// file carries no trace of such a face and the shortfall cannot be closed by reading one back.
    /// </para>
    /// </remarks>
    internal static List<int[]> PadSheetCornersToSlotCount(FeModel.ProxyMesh proxy, int vertexCount)
    {
        var corners = 0;
        foreach (var face in proxy.Faces)
        {
            corners += face.Length;
        }

        if (corners == 0 || corners >= vertexCount)
        {
            return proxy.Faces;
        }

        var pinned = new List<int>();
        foreach (var corner in proxy.Faces.SelectMany(static face => face).Distinct())
        {
            if (proxy.ClothEnable[corner] == 0f)
            {
                pinned.Add(corner);
            }
        }

        if (pinned.Count < 3)
        {
            return proxy.Faces;
        }

        var padded = new List<int[]>(proxy.Faces);
        for (var at = 0; corners < vertexCount; at += 3, corners += 3)
        {
            padded.Add([pinned[at % pinned.Count], pinned[(at + 1) % pinned.Count], pinned[(at + 2) % pinned.Count]]);
        }

        return padded;
    }

    /// <summary>
    /// The <c>cloth_anchor_free_rotate</c> paint a sheet needs, or null where it states nothing.
    /// <para>
    /// The paint is the per-vertex rot-lock release. On a sheet the export re-emits
    /// <c>flex_cloth_borders</c> for, the flag frees the border pins and the paint has nothing to add -
    /// EXCEPT where the original compiled a vertex SIMULATED and rotation-locked, which no flag can
    /// state: the vertex node creator gives a proxy vertex free rotation from the same flag as its
    /// simulated bit, so only this paint can lock one. Such a sheet paints every vertex its recorded
    /// class. Every other sheet paints only the pins the original records rotation-free, which is what
    /// the flag it does not carry would otherwise have done.
    /// </para>
    /// </summary>
    internal static float[]? ClothAnchorFreeRotatePaint(FeModel feModel, FeModel.ProxyMesh proxy, bool sheetFlexes)
    {
        var vertexCount = Math.Min(proxy.Positions.Length, proxy.NodeIndices.Length);
        var lockedSimulated = false;
        for (var v = 0; v < vertexCount; v++)
        {
            var node = proxy.NodeIndices[v];
            lockedSimulated |= proxy.ClothEnable[v] != 0f && node < feModel.StaticNodeCount
                && !feModel.AllowsRotation(node);
        }

        if (!lockedSimulated && sheetFlexes)
        {
            return null;
        }

        var freeRotate = new float[proxy.Positions.Length];
        var anyFreed = false;
        for (var v = 0; v < vertexCount; v++)
        {
            var node = proxy.NodeIndices[v];
            var free = lockedSimulated
                ? node >= feModel.StaticNodeCount || feModel.AllowsRotation(node)
                : proxy.ClothEnable[v] == 0f && node < feModel.StaticNodeCount && feModel.AllowsRotation(node);
            if (free)
            {
                freeRotate[v] = 1f;
                anyFreed = true;
            }
        }

        return anyFreed ? freeRotate : null;
    }

    /// <summary>
    /// The per-vertex collision-layer paints a sheet's compiled masks state, keyed by layer. Each layer is
    /// its own paint and a vertex painted 0 on layer k compiles with bit k CLEARED in its tree collision
    /// mask, so a layer is stated only where some vertex of the sheet clears it: without the stream the
    /// compiler leaves every layer set, which is what an unpainted sheet means.
    /// </summary>
    internal static IEnumerable<(int Layer, float[] Painted)> ClothCollisionLayerPaints(FeModel feModel,
        int[] nodeIndices, int vertexCount)
    {
        for (var layer = 0; layer < ClothCollisionLayers; layer++)
        {
            var bit = 1 << layer;
            var painted = new float[vertexCount];
            var anyCleared = false;
            for (var v = 0; v < vertexCount; v++)
            {
                var set = v >= nodeIndices.Length || (feModel.GetNodeCollisionMask(nodeIndices[v]) & bit) != 0;
                painted[v] = set ? 1f : 0f;
                anyCleared |= !set;
            }

            if (anyCleared)
            {
                yield return (layer, painted);
            }
        }
    }

    /// <summary>
    /// Builds the cloth proxy-mesh DMX (the cloth "sheet") from the soft-body <see cref="FeModel"/>.
    /// Vertices are the FeModel surface control nodes (positions = their rest pose), faces come from the
    /// quad/tri surface, each vertex carries a <c>cloth_enable$0</c> paint value (1 = simulated, 0 = pinned)
    /// and is skinned to the real skeleton bone it is anchored to. A recompile turns this back into the
    /// <c>$cloth_*</c> FeModel nodes (one per enabled vertex). The skeleton is emitted into the DMX joint
    /// list so the skinning resolves, exactly like a render mesh.
    /// </summary>
    internal byte[] BuildClothProxyMeshDmx(FeModel.ProxyMesh proxy, string name)
    {
        Debug.Assert(model is not null, "model required for cloth proxy mesh");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        // Joint list = the full skeleton, so BLENDINDICES resolve (mirrors ConvertMeshToDatamodelMesh).
        var dmeModel = BuildDmeDagSkeleton(skeleton, out _, bonePositions: ClothProxyRestBonePositions,
            boneRotations: ClothProxyRestBoneRotations);
        dmeModel.Name = name;
        RespellJointsAsClothControlNodes(dmeModel, physAggregateData?.FeModel);

        var (dag, vertexData) = DmxScaffolding.CreateDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = proxy.Positions.Length;

        // Indexed one face corner at a time, the way authored proxies are: the face set names corner
        // ordinals and every stream's index array maps corner -> vertex. A sheet with no faces has no
        // corners and stays indexed per vertex.
        var emittedFaces = PadSheetCornersToSlotCount(proxy, vertexCount);
        var cornerVertices = emittedFaces.SelectMany(static face => face).ToArray();
        var identity = Enumerable.Range(0, vertexCount).ToArray();
        var vertexIndices = cornerVertices.Length > 0 ? cornerVertices : identity;

        vertexData.AddIndexedStream("position$0", proxy.Positions, vertexIndices);

        // The sheet's normals are its rest orientations (see FeModel.RecoverRestNormals). The importer
        // reads proxy vertex v's normal from the flattened per-corner stream at ordinal v, not at one of
        // v's own corners, so slot v carries vertex v's; the corners past the vertex count carry theirs.
        var restNormals = physAggregateData?.FeModel?.RecoverRestNormals(proxy)
            ?? [.. Enumerable.Repeat(Vector3.UnitZ, vertexCount)];
        var cornerNormals = new Vector3[vertexIndices.Length];
        for (var corner = 0; corner < cornerNormals.Length; corner++)
        {
            cornerNormals[corner] = restNormals[corner < vertexCount ? corner : vertexIndices[corner]];
        }

        vertexData.AddIndexedStream("normal$0", cornerNormals, Enumerable.Range(0, cornerNormals.Length).ToArray());

        // The cloth importer needs texcoords on the proxy (authored proxies always carry them; without
        // UVs the surface is not accepted as a sheet). A bounding-box projection along the two largest
        // extents is enough - the UVs only need to vary smoothly across the sheet.
        var boundsMin = proxy.Positions.Aggregate(Vector3.Min);
        var boundsMax = proxy.Positions.Aggregate(Vector3.Max);
        var extent = boundsMax - boundsMin;
        Span<int> axes = [0, 1, 2];
        axes.Sort((a, b) => extent[b].CompareTo(extent[a]));
        var (axisU, axisV) = (axes[0], axes[1]);
        var texcoords = new Vector2[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            texcoords[v] = new Vector2(
                extent[axisU] > 1e-6f ? (proxy.Positions[v][axisU] - boundsMin[axisU]) / extent[axisU] : 0f,
                extent[axisV] > 1e-6f ? (proxy.Positions[v][axisV] - boundsMin[axisV]) / extent[axisV] : 0f);
        }

        vertexData.AddIndexedStream("texcoord$0", texcoords, vertexIndices);

        // Per-vertex cloth paint layers, named and ordered as a current authored cloth proxy carries them.
        // cloth_goal_strength_v2 is the attribute the ModelDoc cloth editor paints; the legacy
        // cloth_goal_strength reads as 0 there. All values are 0..1 paint values rather than raw compiled
        // solver numbers.
        vertexData.AddIndexedStream("cloth_enable$0", proxy.ClothEnable, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", proxy.GoalStrength, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_damping$0", proxy.GoalDamping, vertexIndices);

        // The raw goal pair drives the same two integrator fields at 30x without the goal-damped solve,
        // and per vertex keeps that node on the raw integrator (see FeModel.RawGoalPaintNodes). A sheet
        // whose nodes all compiled goal-damped ships neither stream.
        if (Array.Exists(proxy.AnimationForceAttract, static value => value != 0f)
            || Array.Exists(proxy.AnimationAttract, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_animation_force_attract$0", proxy.AnimationForceAttract, vertexIndices);
            vertexData.AddIndexedStream("cloth_animation_attract$0", proxy.AnimationAttract, vertexIndices);
        }

        vertexData.AddIndexedStream("cloth_collision_radius$0", proxy.CollisionRadius, vertexIndices);
        vertexData.AddIndexedStream("cloth_ground_collision$0", proxy.GroundCollision, vertexIndices);
        vertexData.AddIndexedStream("cloth_drag$0", proxy.Drag, vertexIndices);

        // World-collision ground friction paint. The importer has no "cloth_world_friction" counterpart:
        // world friction rides the ground-collision paint instead, see ProxyVertexData's GroundCollision.
        if (Array.Exists(proxy.GroundFriction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_ground_friction$0", proxy.GroundFriction, vertexIndices);
        }

        // Friction is painted only where the cloth carries any: an all-zero stream is not the same input
        // as no stream at all.
        if (Array.Exists(proxy.Friction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_friction$0", proxy.Friction, vertexIndices);
        }

        // Per-vertex gravity, painted VERBATIM: cloth_gravity$0 compiles into flGravity with no scaling.
        // Without the stream the compiler gives every vertex 360.
        vertexData.AddIndexedStream("cloth_gravity$0", proxy.Gravity, vertexIndices);

        if (physAggregateData?.FeModel is { } feLayers)
        {
            foreach (var (layer, painted) in ClothCollisionLayerPaints(feLayers, proxy.NodeIndices, vertexCount))
            {
                vertexData.AddIndexedStream($"cloth_collision_layer_{layer}$0", painted, vertexIndices);
            }
        }

        // The per-vertex rot-lock release: a pinned vertex compiles rotation-locked unless this
        // paint (or the sheet-level flex_cloth_borders, which frees every pin at once) releases
        // it, so each pin the original records as rotation-free is painted 1.0 on sheets the
        // flag is not re-emitted for.
        if (physAggregateData?.FeModel is { } feRotate
            && ClothAnchorFreeRotatePaint(feRotate, proxy, clothProxiesFlexed.Contains(proxy)) is { } freeRotate)
        {
            vertexData.AddIndexedStream("cloth_anchor_free_rotate$0", freeRotate, vertexIndices);
        }

        // Per-vertex mass paint. The compiler adds expf(cloth_mass * cloth_mass_scale) on top of the mass
        // it derives from the sheet's own geometry, and only when the mesh ships this stream - so a sheet
        // exported without it comes back lighter than the original wherever the mass was painted, while an
        // all-zero stream is a real authoring choice (e^0 = 1) and not the same as no stream at all.
        if (physAggregateData?.FeModel?.RecoverMassPaint(proxy) is { } mass)
        {
            vertexData.AddIndexedStream("cloth_mass$0", mass, vertexIndices);
        }

        // Named vertex selections are painted per vertex, one stream per selection. A cloth effect or a
        // chain joint then names the selection, and the compiler collects every vertex the paint reaches.
        // The one selection this sheet is parented under as a ClothVertexMap is left unpainted: the
        // container recreates the same m_VertexMaps entry without the dynamic vertex set the paint also
        // registers (and which then gives back_solve a sheet-sized set to fit against). Every other
        // selection keeps its paint - an effect naming one the compile cannot find is a hard failure
        // ("refers to non-existent vertex map/set"). The container's aliases are its selections too.
        IReadOnlyList<string> containerMaps = physAggregateData?.FeModel is { } proxyFeModel
            && proxyFeModel.GetProxyVertexMapName(proxy, ClothProxyMeshesToExtract.ConvertAll(static entry => entry.Proxy))
                is { } containerMap
            ? proxyFeModel.VertexMapAliases(containerMap)
            : [];
        // The streams go out in the order the original's own per-node winners imply (see
        // FeModel.VertexSetStreamOrder), since the compiler gives a node to the first stream painting it at its
        // maximum weight and the compiled m_VertexMaps array is hash-sorted rather than authored-ordered.
        var selectionWeights = new Dictionary<string, float[]>(proxy.VertexMaps.Length, StringComparer.Ordinal);
        foreach (var (mapName, weights) in proxy.VertexMaps)
        {
            selectionWeights[mapName] = weights;
        }

        var selectionOrder = physAggregateData?.FeModel is { } orderFeModel
            ? orderFeModel.VertexSetStreamOrder(proxy)
            : proxy.VertexMaps.Select(static map => map.Name).ToArray();
        // A selection the original does NOT register as a vertex set is declared as its own container
        // instead (see the unregistered-selection containers in EmitProxySheetClothPhase): painting it here
        // would register the name and hand the recompile a dynamic vertex set the original never had.
        foreach (var mapName in selectionOrder)
        {
            if (containerMaps.Contains(mapName) || !selectionWeights.TryGetValue(mapName, out var weights))
            {
                continue;
            }

            if (physAggregateData?.FeModel is { } setFeModel
                && setFeModel.VertexMaps.FirstOrDefault(map => map.Name == mapName) is { } selection
                && selection.Name == mapName && !setFeModel.RegistersVertexSet(selection.NameHash))
            {
                continue;
            }

            vertexData.AddIndexedStream("cloth_vertex_set_" + mapName + "$0", weights, vertexIndices);
        }

        // A selection the original registers over NO vertex at all still ships as an m_VertexMaps record carrying
        // its name. The compiler registers the name of every cloth_vertex_set stream it reads and gives the
        // selection only the vertices whose weight clears its epsilon floor, so an all-zero stream reproduces the
        // record. It goes on the first exported sheet alone: one stream is what registers the name.
        if (physAggregateData?.FeModel is { } ghostFeModel
            && ClothProxyMeshesToExtract.Count > 0 && ClothProxyMeshesToExtract[0].Proxy == proxy)
        {
            var painted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (mapName, _weights) in proxy.VertexMaps)
            {
                painted.Add(mapName);
            }

            foreach (var mapName in ghostFeModel.ZeroVertexSelectionNames)
            {
                if (!painted.Contains(mapName) && !containerMaps.Contains(mapName))
                {
                    vertexData.AddIndexedStream("cloth_vertex_set_" + mapName + "$0",
                        new float[vertexCount], vertexIndices);
                }
            }
        }

        // Per-vertex stray radius: how far a simulated vertex may leave its animated position
        // (m_AnimStrayRadii). Without the stream the whole array compiles away.
        if (physAggregateData?.FeModel?.RecoverStrayRadiusPaint(proxy) is { } strayRadius)
        {
            vertexData.AddIndexedStream("cloth_stray_radius$0", strayRadius, vertexIndices);
        }

        // How far that radius relaxes under load, as its own stream: a sheet without it compiles every
        // stray-constrained vertex back at the compiler's rigid default and loses the authored give.
        if (physAggregateData?.FeModel?.RecoverStrayStretchinessPaint(proxy) is { } strayStretchiness)
        {
            vertexData.AddIndexedStream("cloth_stray_radius_stretchiness$0", strayStretchiness, vertexIndices);
        }

        // Suspender rods, which the compiler regenerates from this paint. Declaring them as explicit
        // springs instead costs a source element per pair, which leaves every vertex they touch heavier
        // than the original and re-picks its node basis (see ClothSuspenderCurvature).
        if (ClothSuspenderPaint(proxy) is { } suspenders)
        {
            vertexData.AddIndexedStream("cloth_suspenders$0", suspenders, vertexIndices);
        }

        // How far each hinge of the sheet folds, on top of the model-wide add_curvature: the compiler
        // takes the mean of this paint over a hinge's own two vertices, times pi, and adds it. A sheet
        // whose suspender rods lie flat has to keep add_curvature at zero (that pass reads the model-wide
        // angle alone), so its fold comes back here instead (see ClothBendStiffnessPaint).
        if (ClothBendStiffnessPaint(proxy) is { } bendStiffness)
        {
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", bendStiffness, vertexIndices);
        }

        // How far a face rod may contract: its flMinDist is flMaxDist times the mean of its two
        // endpoints' paint. The importer gives a sheet vertex 0.75 without a stream, so a sheet whose
        // face rods are rigid needs the stream at 1 (see FeModel.RecoverAntishrinkPaint).
        if (physAggregateData?.FeModel?.RecoverAntishrinkPaint(proxy) is { } antishrink)
        {
            vertexData.AddIndexedStream("cloth_antishrink$0", antishrink, vertexIndices);
        }

        // How far a face DIAGONAL relaxes: the cube of the mean of its two endpoints' paint, on top of
        // the sheet-wide additional_shear_stretch MakeClothParams emits. Only a sheet whose diagonals
        // disagree needs the stream (see FeModel.RecoverShearResistancePaint).
        if (physAggregateData?.FeModel?.RecoverShearResistancePaint(proxy) is { } shearResistance)
        {
            vertexData.AddIndexedStream("cloth_shear_resistance$0", shearResistance, vertexIndices);
        }

        // How far every face rod relaxes, edges and diagonals alike: the cube of one minus the mean of its two
        // endpoints' paint (see FeModel.RecoverStretchPaint).
        if (physAggregateData?.FeModel?.RecoverStretchPaint(proxy) is { } stretch)
        {
            vertexData.AddIndexedStream("cloth_stretch$0", stretch, vertexIndices);
        }

        // cloth_drag_v2 and cloth_mass have no measurable effect on the compiled flPointDamping/
        // m_NodeInvMasses - cloth_drag (no suffix, unlike goal_strength) is already the attribute the
        // compiler reads, so they are intentionally omitted.

        // cloth_make_rods is the per-face paint gating whether the mesh importer turns a face into rods or
        // keeps it as a solve element; cloth_use_rods does not move that split. Painted under the ~0.5
        // threshold the whole sheet stays faces, which is only right for cloth that ships a surface of its
        // own: a rod-network cloth then compiles to invented m_Tris and loses every rod. So the paints go
        // on only when the original itself carries faces, and the sheet is otherwise left for the compiler
        // to rebuild rods from.
        //
        // A sheet that ships BOTH kinds paints the split itself: 1 over the rod region, 0 over the surface
        // (see ProxyMesh.RodsDriven). A sheet exported with its AUTHORED faces and no rod region skips the
        // paints entirely, as hand-authored proxies do.
        if (proxy.RodsDriven.Length == vertexCount)
        {
            vertexData.AddIndexedStream("cloth_make_rods$0", proxy.RodsDriven, vertexIndices);
        }
        else if (!proxy.UsesAuthoredFaces && physAggregateData?.FeModel is { HasSurfaceElements: true })
        {
            vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), vertexIndices);
            vertexData.AddIndexedStream("cloth_make_rods$0",
                Enumerable.Repeat(ClothSuppressedMakeRods, vertexCount).ToArray(), vertexIndices);
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), vertexIndices);
        }

        // Skin the proxy vertices. Pinned (cloth_enable 0) vertices follow their anchor bone with weight 1;
        // simulated vertices carry smooth two-joint chain weights (see FeModel.ProxyMesh.SkinInfluences) so
        // the compiler back-solves each chain joint with a proper fit matrix instead of a point rope.
        //
        // Bone names are matched case-INSENSITIVELY, the way Source itself matches them: a model's
        // compiled FeModel m_CtrlName array does not always agree in case with its skeleton, and an
        // Ordinal lookup drops every influence on a bone whose two spellings differ, leaving the affected
        // simulated vertices with all-zero blend weights.
        var clothCompaction = BuildClothBoneCompaction(skeleton);
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            if (IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            var emitted = clothCompaction[bone.Index];
            boneIndexByName.TryAdd(bone.Name, emitted);
            boneIndexByName.TryAdd(GetExportBoneName(bone), emitted);
        }

        AppendCulledClothBoneJoints(dmeModel, boneIndexByName);

        // A sheet no real bone drives ships UNSKINNED, like its hand-authored counterpart: the compiler
        // then anchors the whole sheet to a static root node it generates itself and records every vertex
        // as an m_CtrlOffsets entry hanging off that root. Skinning it to the synthetic per-vertex bones
        // binds each node directly instead, which costs both the root node and the entire offsets array.
        if (!proxy.IsFreeFloating)
        {
            // Four slots cover everything BuildChainSkinInfluences synthesises; weights recovered
            // verbatim from a model's own offset network run wider, and an influence with no slot is
            // dropped before the compiler sees it. The count is widened to hold every recovered
            // influence, whose bone is always a control node the original already carries.
            var jointCount = FeModel.ClothProxyInfluenceSlots;
            if (physAggregateData?.FeModel is { } feModel)
            {
                for (var v = 0; v < vertexCount; v++)
                {
                    if (v < proxy.NodeIndices.Length && feModel.RecoveredSkinWeights.ContainsKey(proxy.NodeIndices[v]))
                    {
                        jointCount = Math.Max(jointCount, proxy.SkinInfluences[v].Count(i => boneIndexByName.ContainsKey(i.Bone)));
                    }
                }
            }

            var blendIndices = new int[vertexCount * jointCount];
            var blendWeights = new float[vertexCount * jointCount];
            for (var v = 0; v < vertexCount; v++)
            {
                var slot = 0;
                foreach (var (boneName, weight) in SeparateTiedInfluenceWeights(proxy.SkinInfluences[v]))
                {
                    if (slot >= jointCount || !boneIndexByName.TryGetValue(boneName, out var bi))
                    {
                        continue;
                    }

                    blendIndices[v * jointCount + slot] = bi;
                    blendWeights[v * jointCount + slot] = weight;
                    slot++;
                }
            }

            vertexData.JointCount = jointCount;
            vertexData.AddStream("blendindices$0", blendIndices);
            vertexData.AddStream("blendweights$0", blendWeights);
        }

        var faceSet = new DmeFaceSet { Name = "cloth" };
        faceSet.Material.MaterialName = "cloth";
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        var cornerOrdinal = 0;
        foreach (var face in emittedFaces)
        {
            foreach (var _ in face)
            {
                faceSet.Faces.Add(cornerOrdinal++);
            }

            faceSet.Faces.Add(-1);
        }

        if (dag.Shape is DmeMesh morphTarget)
        {
            AddClothProxyMorphLayers(morphTarget, proxy, physAggregateData?.FeModel);
        }

        DmxScaffolding.TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.SaveDeterministic(stream, "binary", 9);
        return stream.ToArray();
    }

    /// <summary>
    /// Re-emits a sheet's cloth morph layers (<c>m_MorphLayers</c>) as DMX delta states, sparse per
    /// vertex like any flex. The compiler reads them off the proxy mesh itself - no vmdl node carries
    /// the deltas, so a sheet exported without them loses the layer entirely.
    /// </summary>
    static void AddClothProxyMorphLayers(DmeMesh dmeMesh, FeModel.ProxyMesh proxy, FeModel? feModel)
    {
        if (feModel is null || feModel.MorphLayers.Length == 0)
        {
            return;
        }

        var localOfNode = new Dictionary<int, int>(proxy.NodeIndices.Length);
        for (var v = 0; v < proxy.NodeIndices.Length; v++)
        {
            localOfNode.TryAdd(proxy.NodeIndices[v], v);
        }

        foreach (var layer in feModel.MorphLayers)
        {
            var indices = new List<int>(layer.Nodes.Length);
            var values = new List<Vector3>(layer.Nodes.Length);
            for (var i = 0; i < layer.Nodes.Length && i < layer.InitPos.Length; i++)
            {
                if (localOfNode.TryGetValue(layer.Nodes[i], out var local))
                {
                    indices.Add(local);
                    values.Add(layer.InitPos[i]);
                }
            }

            if (values.Count == 0)
            {
                continue;
            }

            var deltaState = new DmeVertexDeltaData { Name = layer.Name };
            deltaState.AddIndexedStream("position$0", values.ToArray(), indices.ToArray());
            dmeMesh.DeltaStates.Add(deltaState);
            dmeMesh.DeltaStateWeights.Add(Vector2.Zero);
            dmeMesh.DeltaStateWeightsLagged.Add(Vector2.Zero);
        }
    }

    /// <summary>
    /// Builds a generated cloth sheet grid DMX over a group of bone chains (see
    /// <see cref="FeModel.BuildChainGrids"/>). Mirrors hand-authored item proxies: rows/columns of
    /// vertices spanning the chains, bilinear chain-joint skinning, recovered cloth paints, quad faces.
    /// </summary>
    internal byte[] BuildClothChainGridDmx(FeModel.ChainGrid grid, string name)
    {
        Debug.Assert(model is not null, "model required for cloth grid");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeModel = BuildDmeDagSkeleton(skeleton, out _, bonePositions: ClothProxyRestBonePositions,
            boneRotations: ClothProxyRestBoneRotations);
        dmeModel.Name = name;

        var (dag, vertexData) = DmxScaffolding.CreateDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = grid.Positions.Length;
        var identity = Enumerable.Range(0, vertexCount).ToArray();

        vertexData.AddIndexedStream("position$0", grid.Positions, identity);
        vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("texcoord$0", grid.Texcoords, identity);

        // Full paint set, matching BuildClothProxyMeshDmx: friction and drag are what damp the grid's fall
        // once goal_strength lets go.
        vertexData.AddIndexedStream("cloth_enable$0", grid.ClothEnable, identity);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", grid.GoalStrength, identity);
        vertexData.AddIndexedStream("cloth_goal_damping$0", grid.GoalDamping, identity);
        vertexData.AddIndexedStream("cloth_collision_radius$0", grid.CollisionRadius, identity);
        vertexData.AddIndexedStream("cloth_ground_collision$0", Enumerable.Repeat(0f, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("cloth_drag$0", grid.Drag, identity);

        if (Array.Exists(grid.Friction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_friction$0", grid.Friction, identity);
        }

        // See BuildClothProxyMeshDmx: keeping the sheet as faces is only right for cloth that ships faces.
        if (physAggregateData?.FeModel is { HasSurfaceElements: true })
        {
            vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), identity);
            vertexData.AddIndexedStream("cloth_make_rods$0", Enumerable.Repeat(0.4f, vertexCount).ToArray(), identity);
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), identity);
        }

        // Case-insensitive bone-name resolution - see BuildClothProxyMeshDmx for why (compiled cloth control
        // node names do not always agree in case with the skeleton; an Ordinal miss silently drops the skin).
        var clothCompaction = BuildClothBoneCompaction(skeleton);
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            if (IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            var emitted = clothCompaction[bone.Index];
            boneIndexByName.TryAdd(bone.Name, emitted);
            boneIndexByName.TryAdd(GetExportBoneName(bone), emitted);
        }

        AppendCulledClothBoneJoints(dmeModel, boneIndexByName);

        const int JointCount = 4;
        var blendIndices = new int[vertexCount * JointCount];
        var blendWeights = new float[vertexCount * JointCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var slot = 0;
            foreach (var (boneName, weight) in grid.SkinInfluences[v])
            {
                if (slot >= JointCount || !boneIndexByName.TryGetValue(boneName, out var bi))
                {
                    continue;
                }

                blendIndices[v * JointCount + slot] = bi;
                blendWeights[v * JointCount + slot] = weight;
                slot++;
            }
        }

        vertexData.JointCount = JointCount;
        vertexData.AddStream("blendindices$0", blendIndices);
        vertexData.AddStream("blendweights$0", blendWeights);

        var faceSet = new DmeFaceSet { Name = "cloth" };
        faceSet.Material.MaterialName = "cloth";
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        foreach (var face in grid.Faces)
        {
            foreach (var index in face)
            {
                faceSet.Faces.Add(index);
            }

            faceSet.Faces.Add(-1);
        }

        DmxScaffolding.TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.SaveDeterministic(stream, "binary", 9);
        return stream.ToArray();
    }
}
