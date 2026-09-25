using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Separates tied blend weights on one proxy vertex by <see cref="TiedInfluenceSeparation"/>, keeping their order, so
    /// the importer's weight sort picks the same primary anchor bone.
    /// </summary>
    private static (string Bone, float Weight)[] SeparateTiedInfluenceWeights((string Bone, float Weight)[] influences)
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

    /// <summary>Relative gap forced between two tied proxy blend weights, far below the 1/255 paint quantum.</summary>
    private const float TiedInfluenceSeparation = 1e-6f;

    /// <summary>
    /// The sheet's faces, with all-pinned filler triangles appended until every vertex slot has a face corner of its own:
    /// the compiler reads the rest normals by corner ordinal, and an all-static face adds nothing else.
    /// </summary>
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
    /// The <c>cloth_anchor_free_rotate</c> paint a sheet needs, or null where it states nothing. A sheet with a simulated,
    /// rotation-locked vertex paints every vertex its recorded class; otherwise a sheet without
    /// <c>flex_cloth_borders</c> paints the pins recorded rotation-free.
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
    /// The per-vertex collision-layer paints of a sheet, one for every layer some vertex's compiled mask clears.
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
    /// Builds the proxy-sheet DMX: the sheet's rest positions and faces, every cloth paint the compiled nodes state, and
    /// its skinning to the skeleton the DMX joint list carries.
    /// </summary>
    internal byte[] BuildClothProxyMeshDmx(FeModel.ProxyMesh proxy, string name)
    {
        Debug.Assert(model is not null, "model required for cloth proxy mesh");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeModel = ModelExtract.BuildDmeDagSkeleton(skeleton, out _, bonePositions: ProxyRestBonePositions,
            boneRotations: ProxyRestBoneRotations);
        dmeModel.Name = name;
        RespellJointsAsClothControlNodes(dmeModel, physAggregateData?.FeModel);

        var (dag, vertexData) = DmxScaffolding.CreateDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = proxy.Positions.Length;

        // Streams are indexed per face corner, and per vertex on a sheet without faces.
        var emittedFaces = PadSheetCornersToSlotCount(proxy, vertexCount);
        var cornerVertices = emittedFaces.SelectMany(static face => face).ToArray();
        var identity = Enumerable.Range(0, vertexCount).ToArray();
        var vertexIndices = cornerVertices.Length > 0 ? cornerVertices : identity;

        vertexData.AddIndexedStream("position$0", proxy.Positions, vertexIndices);

        // The importer reads vertex v's rest normal from corner ordinal v.
        var restNormals = physAggregateData?.FeModel?.RecoverRestNormals(proxy)
            ?? [.. Enumerable.Repeat(Vector3.UnitZ, vertexCount)];
        var cornerNormals = new Vector3[vertexIndices.Length];
        for (var corner = 0; corner < cornerNormals.Length; corner++)
        {
            cornerNormals[corner] = restNormals[corner < vertexCount ? corner : vertexIndices[corner]];
        }

        vertexData.AddIndexedStream("normal$0", cornerNormals, Enumerable.Range(0, cornerNormals.Length).ToArray());

        // A sheet needs texcoords to import; a bounding-box projection is enough.
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

        vertexData.AddIndexedStream("cloth_enable$0", proxy.ClothEnable, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_strength_v2$0", proxy.GoalStrength, vertexIndices);
        vertexData.AddIndexedStream("cloth_goal_damping$0", proxy.GoalDamping, vertexIndices);

        if (Array.Exists(proxy.AnimationForceAttract, static value => value != 0f)
            || Array.Exists(proxy.AnimationAttract, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_animation_force_attract$0", proxy.AnimationForceAttract, vertexIndices);
            vertexData.AddIndexedStream("cloth_animation_attract$0", proxy.AnimationAttract, vertexIndices);
        }

        vertexData.AddIndexedStream("cloth_collision_radius$0", proxy.CollisionRadius, vertexIndices);
        vertexData.AddIndexedStream("cloth_ground_collision$0", proxy.GroundCollision, vertexIndices);
        vertexData.AddIndexedStream("cloth_drag$0", proxy.Drag, vertexIndices);

        if (Array.Exists(proxy.GroundFriction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_ground_friction$0", proxy.GroundFriction, vertexIndices);
        }

        if (Array.Exists(proxy.Friction, static value => value != 0f))
        {
            vertexData.AddIndexedStream("cloth_friction$0", proxy.Friction, vertexIndices);
        }

        vertexData.AddIndexedStream("cloth_gravity$0", proxy.Gravity, vertexIndices);

        if (physAggregateData?.FeModel is { } feLayers)
        {
            foreach (var (layer, painted) in ClothCollisionLayerPaints(feLayers, proxy.NodeIndices, vertexCount))
            {
                vertexData.AddIndexedStream($"cloth_collision_layer_{layer}$0", painted, vertexIndices);
            }
        }

        if (physAggregateData?.FeModel is { } feRotate
            && ClothAnchorFreeRotatePaint(feRotate, proxy, flexedProxies.Contains(proxy)) is { } freeRotate)
        {
            vertexData.AddIndexedStream("cloth_anchor_free_rotate$0", freeRotate, vertexIndices);
        }

        if (physAggregateData?.FeModel?.RecoverMassPaint(proxy) is { } mass)
        {
            vertexData.AddIndexedStream("cloth_mass$0", mass, vertexIndices);
        }

        // The selections this sheet's ClothVertexMap container stands for are not painted.
        IReadOnlyList<string> containerMaps = physAggregateData?.FeModel is { } proxyFeModel
            && proxyFeModel.GetProxyVertexMapName(proxy, ProxyMeshes.ConvertAll(static entry => entry.Proxy))
                is { } containerMap
            ? proxyFeModel.VertexMapAliases(containerMap)
            : [];
        var selectionWeights = new Dictionary<string, float[]>(proxy.VertexMaps.Length, StringComparer.Ordinal);
        foreach (var (mapName, weights) in proxy.VertexMaps)
        {
            selectionWeights[mapName] = weights;
        }

        var selectionOrder = physAggregateData?.FeModel is { } orderFeModel
            ? orderFeModel.VertexSetStreamOrder(proxy)
            : proxy.VertexMaps.Select(static map => map.Name).ToArray();
        // A selection the original does not register as a vertex set is declared as a container instead.
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

        // A selection registered over no vertex is an all-zero stream on the first sheet.
        if (physAggregateData?.FeModel is { } ghostFeModel
            && ProxyMeshes.Count > 0 && ProxyMeshes[0].Proxy == proxy)
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

        if (physAggregateData?.FeModel?.RecoverStrayRadiusPaint(proxy) is { } strayRadius)
        {
            vertexData.AddIndexedStream("cloth_stray_radius$0", strayRadius, vertexIndices);
        }

        if (physAggregateData?.FeModel?.RecoverStrayStretchinessPaint(proxy) is { } strayStretchiness)
        {
            vertexData.AddIndexedStream("cloth_stray_radius_stretchiness$0", strayStretchiness, vertexIndices);
        }

        if (ClothSuspenderPaint(proxy) is { } suspenders)
        {
            vertexData.AddIndexedStream("cloth_suspenders$0", suspenders, vertexIndices);
        }

        if (ClothBendStiffnessPaint(proxy) is { } bendStiffness)
        {
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", bendStiffness, vertexIndices);
        }

        if (physAggregateData?.FeModel?.RecoverAntishrinkPaint(proxy) is { } antishrink)
        {
            vertexData.AddIndexedStream("cloth_antishrink$0", antishrink, vertexIndices);
        }

        if (physAggregateData?.FeModel?.RecoverShearResistancePaint(proxy) is { } shearResistance)
        {
            vertexData.AddIndexedStream("cloth_shear_resistance$0", shearResistance, vertexIndices);
        }

        if (physAggregateData?.FeModel?.RecoverStretchPaint(proxy) is { } stretch)
        {
            vertexData.AddIndexedStream("cloth_stretch$0", stretch, vertexIndices);
        }

        // A sheet with a rod region paints the split; a synthesised sheet of a model with surface elements keeps every
        // face out of the rod path.
        if (proxy.RodsDriven.Length == vertexCount)
        {
            vertexData.AddIndexedStream("cloth_make_rods$0", proxy.RodsDriven, vertexIndices);
        }
        else if (!proxy.UsesAuthoredFaces && physAggregateData?.FeModel is { HasSurfaceElements: true })
        {
            vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), vertexIndices);
            vertexData.AddIndexedStream("cloth_make_rods$0",
                Enumerable.Repeat(ClothSuppressedMakeRods, vertexCount).ToArray(), vertexIndices);

            if (ClothFaceKeptBendStiffness(physAggregateData.FeModel, ProxyMeshes) is { } faceKeptBend)
            {
                vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(faceKeptBend, vertexCount).ToArray(), vertexIndices);
            }
        }

        // Bone names resolve case-insensitively, as the compiler matches them.
        var clothCompaction = ModelExtract.BuildClothBoneCompaction(skeleton);
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            if (ModelExtract.IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            var emitted = clothCompaction[bone.Index];
            boneIndexByName.TryAdd(bone.Name, emitted);
            boneIndexByName.TryAdd(ModelExtract.GetExportBoneName(bone), emitted);
        }

        AppendCulledClothBoneJoints(dmeModel, boneIndexByName);

        // A sheet no real bone drives ships unskinned.
        if (!proxy.IsFreeFloating)
        {
            // Widened past the default slot count to hold every recovered influence.
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

    /// <summary>Re-emits the sheet's <c>m_MorphLayers</c> as sparse DMX delta states.</summary>
    private static void AddClothProxyMorphLayers(DmeMesh dmeMesh, FeModel.ProxyMesh proxy, FeModel? feModel)
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
    /// Builds the DMX of a sheet grid generated over a group of bone chains (see <see cref="FeModel.BuildChainGrids"/>).
    /// </summary>
    internal byte[] BuildClothChainGridDmx(FeModel.ChainGrid grid, string name)
    {
        Debug.Assert(model is not null, "model required for cloth grid");

        var skeleton = model.Skeleton;

        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeModel = ModelExtract.BuildDmeDagSkeleton(skeleton, out _, bonePositions: ProxyRestBonePositions,
            boneRotations: ProxyRestBoneRotations);
        dmeModel.Name = name;

        var (dag, vertexData) = DmxScaffolding.CreateDagVertexData(dmeModel, name);
        dag.Shape!.Name = name;

        var vertexCount = grid.Positions.Length;
        var identity = Enumerable.Range(0, vertexCount).ToArray();

        vertexData.AddIndexedStream("position$0", grid.Positions, identity);
        vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(Vector3.UnitZ, vertexCount).ToArray(), identity);
        vertexData.AddIndexedStream("texcoord$0", grid.Texcoords, identity);

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

        if (physAggregateData?.FeModel is { HasSurfaceElements: true })
        {
            vertexData.AddIndexedStream("cloth_use_rods$0", Enumerable.Repeat(1f, vertexCount).ToArray(), identity);
            vertexData.AddIndexedStream("cloth_make_rods$0", Enumerable.Repeat(0.4f, vertexCount).ToArray(), identity);
            vertexData.AddIndexedStream("cloth_bend_stiffness$0", Enumerable.Repeat(0.2f, vertexCount).ToArray(), identity);
        }

        var clothCompaction = ModelExtract.BuildClothBoneCompaction(skeleton);
        var boneIndexByName = new Dictionary<string, int>(skeleton.Bones.Length * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Bones)
        {
            if (ModelExtract.IsGeneratedClothProxyBone(bone))
            {
                continue;
            }

            var emitted = clothCompaction[bone.Index];
            boneIndexByName.TryAdd(bone.Name, emitted);
            boneIndexByName.TryAdd(ModelExtract.GetExportBoneName(bone), emitted);
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
