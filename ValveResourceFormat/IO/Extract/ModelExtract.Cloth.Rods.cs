using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // Explicitly declares a two-node distance constraint (a "rod") by NODE NAME: the ClothSpring node, the
    // analogue of ClothQuad for edges instead of faces. is_length_explicit=false, the default, pins
    // min_length = max_length = the rest distance, a fully rigid edge. Both is_length_explicit and
    // enable_advanced_parameters are needed together for min_length/max_length to take effect.
    //
    // weight0 and relaxation_factor are not ClothSpring inputs: it registers no attribute for either, so
    // an authored weight0 compiles to the builder's default of 0.5 while min_length/max_length stay exact
    // (see FeModel.Rod.Weight0). "stiffness" is the attribute a rod's flRelaxationFactor comes back on.
    static KVObject MakeClothSpring(string name, string n0, string n1, float minLength, float maxLength,
        float stiffness, int extraIterations = 0)
    {
        var kv = MakeNode("ClothSpring",
            ("name", name),
            ("cloth_node_0", n0),
            ("cloth_node_1", n1),
            ("stiffness", stiffness),
            ("enable_advanced_parameters", true),
            ("is_length_explicit", true),
            ("min_length", minLength),
            ("max_length", maxLength));

        if (extraIterations != 0)
        {
            kv.Add("extra_iterations", extraIterations);
        }

        return kv;
    }

    // A ClothSelfCollisionCluster's member pair compiles to exactly one m_Rods entry (flMinDist/flMaxDist
    // the summed member radii, flWeight0 the builder's own default) and leaves no other trace:
    // m_SelfCollisionLayers, m_NodeCollisionRadii and m_AnimStrayRadii are all unaffected. Unlike a
    // ClothSpring it registers no m_SourceElems entry, so it is the node to re-emit for a rod between two
    // chain joints that a chain does not itself regenerate. The per-member radius split the compiled rod
    // does not preserve (only the sum reaches m_Rods) is recovered as an even split.
    internal static KVObject MakeClothSelfCollisionCluster(string name, List<string> members, float radius,
        float strayRadius, float[]? stiffness = null, float[]? radii = null, float[]? strayRadii = null)
    {
        KVObject MakeJoint(string jointName, float jointStiffness, float jointRadius, float jointStrayRadius)
        {
            var joint = KVObject.Collection();
            joint.Add("joint_name", jointName);
            joint.Add("collision_radius", jointRadius);
            joint.Add("stray_radius", jointStrayRadius);
            joint.Add("stiffness", jointStiffness);
            return joint;
        }

        var joints = KVObject.Array();
        for (var i = 0; i < members.Count; i++)
        {
            joints.Add(MakeJoint(members[i], stiffness is not null && i < stiffness.Length ? stiffness[i] : 1.0f,
                radii is not null && i < radii.Length ? radii[i] : radius,
                strayRadii is not null && i < strayRadii.Length ? strayRadii[i] : strayRadius));
        }

        // The member table's own schema, which the compiler falls back to for any member row that omits
        // a key, exactly as a ClothChain's attrs table does. Its four defaults are the dense-KV3
        // schema's own (CAuthClothDataTable::ctor_dtor_1).
        var attrs = KVObject.Collection();

        KVObject Attr(string key, string display, int uiOrder)
        {
            var attr = KVObject.Collection();
            attr.Add("display", display);
            attr.Add("show", true);
            attr.Add("ui_order", uiOrder);
            attrs.Add(key, attr);
            return attr;
        }

        Attr("joint_name", "Joint Name", 0).Add("default", string.Empty);
        Attr("stiffness", "Stiffness", 4).Add("default", 1f);
        Attr("stray_radius", "Max Range/Radius", 20).Add("default", 2f);
        Attr("collision_radius", "Collision Radius", 27).Add("default", 2f);

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", attrs);
        chainData.Add("selection", KVObject.Array());
        chainData.Add("version", 0);

        return MakeNode("ClothSelfCollisionCluster",
            ("name", name),
            ("algorithm", 0),
            ("chain", chainData));
    }

    // m_Rods is not derivable from the surface: a shipped rod matches neither a Quads/Tris edge nor a quad
    // diagonal. It is read directly off the FeModel and re-declared as explicit ClothSpring nodes by NAME.
    //
    // Every "$cloth_*" endpoint resolves through the export's own global-node-index to
    // "$cloth_m{proxy}p{local}" map (built from proxy.NodeIndices, the same one proxy.Faces uses) rather
    // than through the original's literal CtrlNames string: the re-exported proxy DMX re-sorts vertices
    // (FeModel.BuildProxyMesh sorts referenced nodes ascending), so the original's local index names a
    // different vertex here. Real bone names are not proxy-mesh-local and need no translation.
    /// <summary>
    /// The rods the compiler rebuilds from the exported surface on its own, which must therefore not also
    /// be declared as explicit springs. Every face edge and diagonal is one. When the sheet's compiled rods
    /// reach further than that, the extra bend network was authored on (see <c>add_stiffness_rods</c> in
    /// <see cref="MakeClothParams"/>) and regenerates the remaining pairs of that sheet too.
    /// </summary>
    internal static HashSet<(int, int)> ClothRodsFromSurface(FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, out bool generatesBendRods,
        out bool generatesBendOnlyRods, out float addCurvature, out HashSet<int> suspenderNodes,
        out float bendStiffness, out Dictionary<int, float>? bendStiffnessByNode)
    {
        suspenderNodes = [];
        bendStiffness = 0f;
        bendStiffnessByNode = null;
        var surfaceNodes = new HashSet<int>();
        var derived = new HashSet<(int, int)>();
        var surfaceFaces = new List<int[]>();
        foreach (var (_, _, proxyMesh) in proxies)
        {
            if (!proxyMesh.UsesAuthoredFaces)
            {
                continue;
            }

            var nodeOf = proxyMesh.NodeIndices;
            surfaceNodes.UnionWith(nodeOf);
            var globalFaces = proxyMesh.Faces.Select(face => face.Select(local => nodeOf[local]).ToArray()).ToList();
            surfaceFaces.AddRange(globalFaces);
            derived.UnionWith(FeModel.DeriveRodsFromFaces(globalFaces));
        }

        var beyondSurface = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (surfaceNodes.Contains(edge.Item1) && surfaceNodes.Contains(edge.Item2) && !derived.Contains(edge))
            {
                beyondSurface.Add(edge);
            }
        }

        // The bend network spans the pairs two steps apart across the surface. Only when every rod
        // reaching past the faces has that shape is the switch able to account for all of them - otherwise
        // enabling it would drop the rods it cannot reproduce, so those keep their explicit springs.
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var (a, b) in derived)
        {
            (neighbours.TryGetValue(a, out var na) ? na : neighbours[a] = []).Add(b);
            (neighbours.TryGetValue(b, out var nb) ? nb : neighbours[b] = []).Add(a);
        }

        var regenerable = beyondSurface.Count > 0 && beyondSurface.All(edge =>
            neighbours.TryGetValue(edge.Item1, out var near)
            && near.Any(step => neighbours.TryGetValue(step, out var beyond) && beyond.Contains(edge.Item2)));

        var boundedBeyondSurface = feModel.Rods.Any(rod => rod.MaxDist < ClothBendOnlyRodMaxDistance
            && beyondSurface.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)));

        // Both switches span the same pairs; only the bend-only network leaves their maximum length
        // unbounded, so the lengths are what tells the two apart.
        generatesBendOnlyRods = regenerable && !boundedBeyondSurface;
        generatesBendRods = regenerable && boundedBeyondSurface;

        // Only a regenerated network carries the curvature: where the rods are re-declared as explicit
        // springs instead they already ship their own minimum, and the compiler builds nothing to bend.
        addCurvature = regenerable ? ClothCurvatureFromSurface(feModel, surfaceFaces, beyondSurface) : 0f;

        // The pairs the exporter is asking the compiler to fold for itself, which are the ones whose hinges
        // can carry a bend-stiffness paint. A sheet keeping its explicit springs hands the compiler no fan.
        var bendNetwork = new HashSet<(int, int)>();

        if (regenerable)
        {
            derived.UnionWith(beyondSurface);
            bendNetwork.UnionWith(beyondSurface);
        }
        else if (ClothMixedSurfaceRods(feModel, surfaceFaces, beyondSurface) is { Bend.Count: > 0 } mixed)
        {
            generatesBendRods = mixed.Bounded;
            generatesBendOnlyRods = !mixed.Bounded;
            addCurvature = mixed.AddCurvature;
            bendStiffness = mixed.BendStiffness;
            suspenderNodes.UnionWith(mixed.Suspenders.SelectMany(static edge => new[] { edge.Item1, edge.Item2 }));
            derived.UnionWith(mixed.Bend);
            derived.UnionWith(mixed.Suspenders);
            bendNetwork.UnionWith(mixed.Bend);
        }
        else if (ClothSuspenders(feModel, beyondSurface) is var (suspenders, suspenderCurvature, _)
            && suspenders.Count > 0)
        {
            addCurvature = suspenderCurvature;
            suspenderNodes.UnionWith(suspenders.SelectMany(static edge => new[] { edge.Item1, edge.Item2 }));
            derived.UnionWith(suspenders);
        }
        else
        {
            // Last resort, for a sheet none of the readings above accounts for: the compiler's own bend
            // network is one rod per edge two faces share, joining the far corners of the two, and where
            // every pair it would build is a rod the original carries it can be turned on to cover that
            // part of the sheet. What it does not name keeps its explicit springs. The subset test is what
            // keeps it from inventing a constraint, and it is only reached once the whole-surface, mixed
            // and suspender readings have each declined the sheet. These rods are folded by the model's own
            // curvature like any other, so the network is read for it - but only where every rod of it
            // supports one value, since nothing else here cross-checks the answer.
            var bend = FeModel.BendRodsFromSurface(surfaceFaces, feModel.IsStatic);
            bend.ExceptWith(derived);
            if (bend.Count > 0 && bend.IsSubsetOf(beyondSurface))
            {
                var boundedBend = feModel.Rods.Any(rod => rod.MaxDist < ClothBendOnlyRodMaxDistance
                    && bend.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)));
                generatesBendRods = boundedBend;
                generatesBendOnlyRods = !boundedBend;
                addCurvature = ClothCurvatureFromBendNetwork(feModel, surfaceFaces, bend);
                derived.UnionWith(bend);
                bendNetwork.UnionWith(bend);
            }
        }

        if (!generatesBendRods && !generatesBendOnlyRods && feModel.HasSurfaceFolds)
        {
            var keptFaces = new List<int[]>();
            foreach (var (_, _, proxyMesh) in proxies)
            {
                if (!proxyMesh.UsesAuthoredFaces)
                {
                    var nodeOf = proxyMesh.NodeIndices;
                    keptFaces.AddRange(proxyMesh.Faces.Select(face => face.Select(local => nodeOf[local]).ToArray()));
                }
            }

            var shipped = feModel.Rods.Select(static rod => rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA))
                .ToHashSet();
            var folds = FeModel.BendRodsFromDeclaredFaces(keptFaces, feModel.IsStatic);
            folds.ExceptWith(derived);
            if (folds.Count > 0 && folds.All(fold => shipped.Contains(fold)
                || feModel.IsStatic(fold.Item1) || feModel.IsStatic(fold.Item2)))
            {
                var boundedFolds = feModel.Rods.Any(rod => rod.MaxDist < ClothBendOnlyRodMaxDistance
                    && folds.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)));
                generatesBendRods = boundedFolds;
                generatesBendOnlyRods = !boundedFolds;
                addCurvature = ClothCurvatureFromBendNetwork(feModel, keptFaces, folds);
                derived.UnionWith(folds);
            }
        }

        // A uniform reading the solver's own band cannot tell from zero is no reading: it would be
        // written out as a flat stream that folds nothing while standing in the way of the per-vertex
        // solve below, which is the only thing that can still account for the sheet.
        if (bendStiffness > 0f && bendStiffness <= ClothBendStiffnessAgreement / 2f)
        {
            bendStiffness = 0f;
        }

        // Whatever single value the arms above settled on, the fold each hinge actually carries is that
        // value plus its own two vertices' paint. Where one value already accounts for the sheet the
        // residual solves to nothing and no stream is written; where it cannot, the paint carries the
        // rest. A rigid-edge sheet is left alone: there the compiler folds nothing and the same per-vertex
        // number drives the Kelager ring bends instead.
        if (bendStiffness <= 0f && bendNetwork.Count > 0 && !feModel.HasAxialEdges
            && (generatesBendRods || generatesBendOnlyRods))
        {
            (bendStiffnessByNode, addCurvature) = ClothBendStiffnessOverFold(feModel, surfaceFaces, bendNetwork,
                addCurvature, keepsCurvature: suspenderNodes.Count > 0);
        }

        // Cloth that ships no surface of its own exports its synthesised sheets without the rod-suppressing
        // paint (see BuildClothProxyMeshDmx), so the compiler rebuilds rods from that triangulation as
        // well - declaring those same edges as explicit springs would ship each of them twice.
        if (!feModel.HasSurfaceElements)
        {
            foreach (var (_, _, proxyMesh) in proxies)
            {
                var nodeOf = proxyMesh.NodeIndices;
                derived.UnionWith(FeModel.DeriveRodsFromFaces(
                    proxyMesh.Faces.Select(face => face.Select(local => nodeOf[local]).ToArray())));
            }
        }

        // A sheet kept out of the rod path by the cloth_make_rods paint (see BuildClothProxyMeshDmx) still
        // hands the compiler its faces as solve elements, and the quad-split pass gives every bent one of
        // them a rod of its own across the diagonal it discards. Re-declaring those pairs as explicit
        // springs ships each of them twice.
        foreach (var (_, _, proxyMesh) in proxies)
        {
            var nodeOf = proxyMesh.NodeIndices;
            derived.UnionWith(FeModel.BentQuadRodsFromFaces(
                proxyMesh.Faces
                    .Where(face => !ClothFaceMakesRods(feModel, proxyMesh, face))
                    .Select(face => face.Select(local => nodeOf[local]).ToArray()),
                feModel.InitPosePositions, feModel.IsStatic, feModel.QuadBendTolerance));
        }

        return derived;
    }

    // The cloth_make_rods paint BuildClothProxyMeshDmx writes over a sheet it keeps out of the rod path,
    // which is under the importer's own 0.5 threshold on the mean over a face's corners.
    const float ClothSuppressedMakeRods = 0.4f;

    /// <summary>
    /// Whether the compiler turns <paramref name="face"/> into rods rather than into a solve element: the
    /// mean <c>cloth_make_rods</c> paint over its corners against the importer's threshold of one half,
    /// over the paint <see cref="BuildClothProxyMeshDmx"/> writes for this sheet.
    /// </summary>
    static bool ClothFaceMakesRods(FeModel feModel, FeModel.ProxyMesh proxy, int[] face)
    {
        var vertexCount = proxy.Positions.Length;
        var driven = proxy.RodsDriven.Length == vertexCount;
        if (!driven && (proxy.UsesAuthoredFaces || !feModel.HasSurfaceElements))
        {
            return true;
        }

        var painted = 0f;
        foreach (var local in face)
        {
            painted += driven && local >= 0 && local < vertexCount
                ? proxy.RodsDriven[local]
                : ClothSuppressedMakeRods;
        }

        return painted >= 0.5f * face.Length;
    }

    // The maximum length a bend-only rod is given, which is no limit at all.
    const float ClothBendOnlyRodMaxDistance = FeModel.UnboundedRodDistance;

    /// <summary>
    /// A sheet whose rods beyond its own faces are a MIXTURE of the <c>add_stiffness_rods</c> bend network
    /// and suspender rods, split into those two classes so each can be emitted through its own route
    /// rather than every rod of the sheet becoming an explicit <c>ClothSpring</c>.
    /// <para>
    /// The bend network is derived from the exported surface the way the compiler derives it
    /// (<see cref="FeModel.BendRodsFromSurface"/>), and taken only when no rod it would build is one the
    /// model has not got - a network reaching past the compiled data would add constraints the original
    /// lacks. The rods it does not account for all have to be suspender rods agreeing on the same
    /// <c>add_curvature</c> the network was folded by: the two passes share that one value, and a leftover
    /// is the signal that the surface being exported is not the one the network was built from, so such a
    /// sheet keeps every spring it has.
    /// </para>
    /// <para>
    /// The two passes do NOT share the whole angle. A bend rod is folded by
    /// <c>clamp(mean cloth_bend_stiffness over its hinge's own two vertices * pi + add_curvature, 0, pi)</c>
    /// while a suspender rod takes <c>add_curvature</c> alone, so a sheet whose suspenders lie flat states
    /// <c>add_curvature = 0</c> and any fold its network still shows is that paint. Such a sheet reports the
    /// fold as <c>BendStiffness</c> and keeps a curvature of zero, which is the only reading that satisfies
    /// both passes at once.
    /// </para>
    /// </summary>
    static (HashSet<(int, int)> Bend, HashSet<(int, int)> Suspenders, float AddCurvature, float BendStiffness,
        bool Bounded)?
        ClothMixedSurfaceRods(FeModel feModel, List<int[]> surfaceFaces, HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || feModel.HasAxialEdges)
        {
            return null;
        }

        var invMasses = feModel.NodeInvMasses;
        bool IsStatic(int node) => node >= 0 && node < invMasses.Length && invMasses[node] == 0f;

        var network = FeModel.BendRodsFromSurface(surfaceFaces, IsStatic);
        var shipped = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            shipped.Add(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA));
        }

        if (network.Count == 0 || !network.IsSubsetOf(shipped))
        {
            return null;
        }

        var bend = new HashSet<(int, int)>();
        var rest = new HashSet<(int, int)>();
        foreach (var edge in beyondSurface)
        {
            (network.Contains(edge) ? bend : rest).Add(edge);
        }

        var (suspenders, suspenderCurvature, saturated) = ClothSuspenders(feModel, rest);
        if (bend.Count == 0 || suspenders.Count != rest.Count)
        {
            return null;
        }

        var curvature = ClothCurvatureFromSurface(feModel, surfaceFaces, bend);
        var bendStiffness = 0f;
        if (suspenders.Count > 0 && saturated)
        {
            bendStiffness = curvature;
            curvature = 0f;
        }
        else if (suspenders.Count > 0)
        {
            if (curvature > 0f && MathF.Abs(curvature - suspenderCurvature)
                > FeModel.ChainRingCurvatureAgreement * MathF.Max(curvature, suspenderCurvature))
            {
                return null;
            }

            curvature = suspenderCurvature;
        }

        var bounded = false;
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (bend.Contains(edge) && rod.MaxDist < ClothBendOnlyRodMaxDistance)
            {
                bounded = true;
                break;
            }
        }

        return (bend, suspenders, curvature, bendStiffness, bounded);
    }

    /// <summary>
    /// The <c>cloth_bend_stiffness</c> paint of a proxy sheet, or null when the sheet needs none. The
    /// compiler folds each bend rod by the mean of this paint over its hinge's own two vertices, added to
    /// the model-wide <c>add_curvature</c>, so a sheet that has to keep a curvature of zero for its
    /// suspender rods carries the fold here instead. It is emitted only on a sheet exported with its own
    /// faces, which is the surface the fold was read off.
    /// <para>
    /// One value covers a sheet whose hinges all fold alike. Where they do not - part of the sheet at its
    /// rest cap and part barely folded, which one <c>add_curvature</c> cannot produce - the paint is solved
    /// per vertex out of the hinges themselves (see <see cref="ClothBendStiffnessFromHinges(FeModel, List{int[]}, HashSet{ValueTuple{int, int}}, float)"/>).
    /// </para>
    /// </summary>
    /// <summary>
    /// The uniform <c>cloth_bend_stiffness</c> a face-kept sheet's proxy states, or null where the compiler folds rods across
    /// the model's sheets (<c>add_stiffness_rods</c> or <c>add_bend_only_rods</c> without <c>rigid_edge_hinges</c>): every
    /// such fold opens by the paint over its hinge on top of <c>add_curvature</c>, so the sheet's folds carry the model-wide
    /// angle alone.
    /// </summary>
    internal static float? ClothFaceKeptBendStiffness(FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies)
    {
        if (!feModel.HasAxialEdges && !feModel.HasChainRingBends)
        {
            ClothRodsFromSurface(feModel, proxies, out var bendRods, out var bendOnlyRods, out _, out _, out _, out _);
            if (bendRods || bendOnlyRods)
            {
                return null;
            }
        }

        return ClothFaceKeptBendStiffnessDefault;
    }

    // The bend paint a face-kept sheet states where nothing folds its fans.
    const float ClothFaceKeptBendStiffnessDefault = 0.2f;

    float[]? ClothBendStiffnessPaint(FeModel.ProxyMesh proxy)
    {
        if (physAggregateData?.FeModel is not { } feModel || !proxy.UsesAuthoredFaces)
        {
            return null;
        }

        if (feModel.HasAxialEdges)
        {
            return feModel.RecoverRigidHingeBendPaint(proxy);
        }

        ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract, out _, out _, out _, out _,
            out var bendStiffness, out var bendStiffnessByNode);
        if (bendStiffness > 0f)
        {
            var uniform = new float[proxy.NodeIndices.Length];
            Array.Fill(uniform, bendStiffness);
            return uniform;
        }

        if (bendStiffnessByNode is null)
        {
            return null;
        }

        var paint = new float[proxy.NodeIndices.Length];
        var painted = 0;
        for (var v = 0; v < paint.Length; v++)
        {
            paint[v] = bendStiffnessByNode.GetValueOrDefault(proxy.NodeIndices[v]);
            if (paint[v] > 0f)
            {
                painted++;
            }
        }

        return painted > 0 ? paint : null;
    }

    /// <summary>
    /// The SUSPENDER rods among the ones a sheet has beyond its own faces, and the
    /// <c>add_curvature</c> they were authored with. A suspender rod ties a static sheet vertex to a
    /// simulated one over their rest span, and the compiler gives it <c>flMaxDist</c> = that span and
    /// <c>flMinDist</c> = <c>flMaxDist * sin(add_curvature * pi)</c>, so one such rod pins the curvature
    /// down. The rest of the set keeps its explicit springs: a rod the paint does not rebuild has to,
    /// or the model comes back short of it.
    /// <para>
    /// The paint that builds them has to reach BOTH ends of each rod (see
    /// <see cref="ClothSuspenderPaint"/>), and the compiler pairs each painted simulated vertex with its
    /// nearest painted static one. <c>add_curvature</c> is one model-wide value with three readers, so
    /// the answer is taken only where the readings cannot contradict each other: every suspender rod has
    /// to agree with every other to
    /// <see cref="FeModel.ChainRingCurvatureAgreement"/>, a chain ring reading of its own has to agree
    /// too, and a sheet with axial edges is left alone entirely because <c>rigid_edge_hinges</c> gives
    /// the same value a second, independent job.
    /// </para>
    /// <para>
    /// A set whose rods all sit at <c>flMinDist</c> zero is reported SATURATED. It is the one shape the
    /// paint reproduces without the sheet having to carry a curvature at all, so the caller takes it only
    /// where the bend network reads zero as well and the two cannot contradict each other; a chain ring
    /// reading of its own has to be zero for the same reason.
    /// </para>
    /// </summary>
    static (HashSet<(int, int)> Suspenders, float AddCurvature, bool Saturated) ClothSuspenders(
        FeModel feModel, HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || feModel.HasAxialEdges)
        {
            return ([], 0f, false);
        }

        var positions = feModel.InitPosePositions;
        var invMasses = feModel.NodeInvMasses;
        var shaped = new List<((int, int) Edge, float Reading)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!beyondSurface.Contains(edge) || edge.Item1 < 0
                || edge.Item2 >= positions.Length || edge.Item2 >= invMasses.Length)
            {
                continue;
            }

            if ((invMasses[rod.NodeA] == 0f) == (invMasses[rod.NodeB] == 0f)
                || MathF.Abs(rod.RelaxationFactor - 1f) > 1e-4f || rod.MaxDist <= 0f)
            {
                continue;
            }

            var rest = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
            if (rest <= 0f || MathF.Abs(rod.MaxDist - rest) > 1e-3f * rest)
            {
                continue;
            }

            shaped.Add((edge, MathF.Asin(Math.Clamp(rod.MinDist / rod.MaxDist, 0f, 1f)) / MathF.PI));
        }

        // The answer is the value the largest set of them shares, as everywhere else a curvature is read
        // back, and a set that reads zero throughout is the saturated one the summary describes.
        //
        // The whole set has to agree AND account for every rod reaching past the faces: the compiler's own
        // pass walks the authored proxy vertices while this recovers only the ones that became nodes, so
        // where the two differ the pass pairs the sheet up differently and rebuilds only part of what it
        // shipped. A set with leftovers is exactly that case, and it keeps every spring it has.
        var curvature = DominantReading(shaped.Select(static s => s.Reading), out var agreeing);
        if (shaped.Count == 0 || agreeing != shaped.Count || shaped.Count != beyondSurface.Count)
        {
            return ([], 0f, false);
        }

        var saturated = curvature <= 0f;
        var ring = feModel.ChainRingCurvature;
        if (saturated
            ? ring > FeModel.ChainRingCurvatureAgreement
            : ring > 0f && MathF.Abs(ring - curvature)
                > FeModel.ChainRingCurvatureAgreement * MathF.Max(ring, curvature))
        {
            return ([], 0f, false);
        }

        var suspenders = new HashSet<(int, int)>();
        foreach (var (edge, reading) in shaped)
        {
            if (MathF.Abs(reading - curvature) <= FeModel.ChainRingCurvatureAgreement * MathF.Max(reading, curvature))
            {
                suspenders.Add(edge);
            }
        }

        return (suspenders, curvature, saturated);
    }

    // The value the largest subset of `readings` agrees on to ChainRingCurvatureAgreement, taking the
    // largest such value on a tie, with the size of that subset. Zero when there are none.
    static float DominantReading(IEnumerable<float> readings, out int agreeing)
    {
        var sorted = readings.ToArray();
        Array.Sort(sorted);
        var best = 0f;
        agreeing = 0;
        var low = 0;
        for (var high = 0; high < sorted.Length; high++)
        {
            while (sorted[high] - sorted[low] > FeModel.ChainRingCurvatureAgreement * sorted[high])
            {
                low++;
            }

            if (high - low + 1 >= agreeing)
            {
                agreeing = high - low + 1;
                best = sorted[high];
            }
        }

        return best;
    }

    /// <summary>
    /// The <c>cloth_suspenders</c> paint of a proxy sheet, or null when the sheet has none. The compiler
    /// builds a suspender rod only when the paint reaches both of its ends, so both the static vertex and
    /// the simulated one it holds up carry it.
    /// </summary>
    float[]? ClothSuspenderPaint(FeModel.ProxyMesh proxy)
    {
        if (physAggregateData?.FeModel is not { } feModel)
        {
            return null;
        }

        ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract, out _, out _, out _, out var suspenderNodes,
            out _, out _);
        if (suspenderNodes.Count == 0)
        {
            return null;
        }

        var paint = new float[proxy.NodeIndices.Length];
        var painted = 0;
        for (var v = 0; v < paint.Length; v++)
        {
            if (suspenderNodes.Contains(proxy.NodeIndices[v]))
            {
                paint[v] = 1f;
                painted++;
            }
        }

        return painted > 0 ? paint : null;
    }

    /// <summary>
    /// The <c>add_curvature</c> the sheet was authored with, read back out of the bend network it
    /// generates. Such a rod joins the far corners of two faces that share an edge; the compiler gives it
    /// the span those corners have with the two faces coplanar as <c>flMaxDist</c>, and the span they have
    /// folded about that shared edge through a dihedral angle of <c>add_curvature * pi</c> as
    /// <c>flMinDist</c> - capped at the rod's own rest span, which a curved sheet reaches before the fold
    /// opens all the way. One uncapped rod plus the rest positions therefore pin the value down, and every
    /// rod of the network agrees on it to the print quantum, so the answer is the value the largest set of
    /// them shares - which also discards the pairs some other rule shaped. A capped rod only says the
    /// value is at least enough to have reached its rest span, so a network that is capped throughout
    /// yields the greatest of those bounds. Values at or above 1.0 all open the fold fully and compile
    /// identically, which is the one distinction the compiled data cannot make.
    /// </summary>
    static float ClothCurvatureFromSurface(FeModel feModel, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var (opened, capped) = ClothCurvatureReadings(feModel, faces, beyondSurface);

        // The half-angle sine squared is what the minimum length is linear in, so the rods are clustered
        // in that before the value is read off - the angle itself is arbitrarily sensitive near either end.
        opened.Sort();
        var agreed = 0;
        var consensus = 0f;
        for (var i = 0; i < opened.Count; i++)
        {
            var j = i;
            while (j < opened.Count && opened[j] <= opened[i] + ClothCurvatureAgreement)
            {
                j++;
            }

            if (j - i > agreed)
            {
                agreed = j - i;
                consensus = opened[(i + j - 1) / 2];
            }
        }

        if (agreed < 3 || agreed * 4 < opened.Count)
        {
            if (capped.Count == 0)
            {
                return 0f;
            }

            consensus = capped.Max();
        }

        return 2f / MathF.PI * MathF.Asin(MathF.Sqrt(consensus));
    }

    // How far two rods' readings may sit apart, in the sin^2(half-angle) the minimum length is linear in,
    // and still count as the same authored value.
    const float ClothCurvatureAgreement = 1e-3f;

    /// <summary>
    /// The <c>add_curvature</c> of the bend network the compiler is being asked to regenerate, taken only
    /// where the network states ONE value: every rod that still has room to open has to read the same
    /// fraction, and every rod already pinned at its own rest span - which only says the value is at least
    /// enough to have reached it - has to sit at or below that. A network with no room left anywhere states
    /// a lower bound alone, and the greatest of those bounds is the answer. Anything else recovers 0 and
    /// the sheet keeps the exporter's default, because unlike the whole-surface and suspender readings
    /// nothing else here can contradict a wrong answer.
    /// </summary>
    static float ClothCurvatureFromBendNetwork(FeModel feModel, List<int[]> faces, HashSet<(int, int)> bend)
    {
        var (opened, capped) = ClothCurvatureReadings(feModel, faces, bend);
        float consensus;
        if (opened.Count == 0)
        {
            if (capped.Count == 0)
            {
                return 0f;
            }

            consensus = capped.Max();
        }
        else
        {
            opened.Sort();
            if (opened[^1] - opened[0] > ClothCurvatureAgreement
                || (capped.Count > 0 && capped.Max() > opened[^1] + ClothCurvatureAgreement))
            {
                return 0f;
            }

            consensus = opened[^1];
        }

        return consensus > ClothCurvatureAgreement ? 2f / MathF.PI * MathF.Asin(MathF.Sqrt(consensus)) : 0f;
    }

    // Per rod of `network`, the fraction of its own fold the compiled minimum length sits at, split into the
    // rods that still had room to open (an exact reading) and the ones pinned at their rest span (a lower
    // bound). See ClothCurvatureFromSurface for the geometry.
    static (List<float> Opened, List<float> Capped) ClothCurvatureReadings(FeModel feModel, List<int[]> faces,
        HashSet<(int, int)> beyondSurface)
    {
        var opened = new List<float>();
        var capped = new List<float>();
        foreach (var (_, fraction, isCapped, _, _) in ClothHingeReadings(feModel, faces, beyondSurface))
        {
            (isCapped ? capped : opened).Add(fraction);
        }

        return (opened, capped);
    }

    /// <summary>
    /// The per-vertex <c>cloth_bend_stiffness</c> of a regenerated bend network, keyed by control node, and the
    /// <c>add_curvature</c> that goes with it. The paint is solved first as the residual on top of the model-wide
    /// value. That value is read off the hinges most of the sheet folds by, so where some hinges fold less their
    /// residual goes negative and the solve recovers nothing; the paint then carries every fold with the model-wide
    /// value at zero.
    /// <para>
    /// That answer is taken only where the model-wide value demonstrably folds some hinge further than its rods
    /// allow, where the hinges do not all fold alike, where the paint reproduces every rod of the network, and where it
    /// reproduces them more closely than the model-wide value does by more than the agreement (see
    /// <c>ClothBendStiffnessFromHinges</c>); a sheet whose suspenders or chain rings read the value keeps it.
    /// </para>
    /// <para>
    /// A sheet that reads no model-wide value has none to keep, so where its first solve recovers nothing the paint is
    /// solved over every hinge that generates each rod, and taken wherever that reproduces every rod of the network.
    /// </para>
    /// <para>
    /// A sheet whose model-wide value no hinge over-folds keeps it, and where that value alone leaves rods short of their
    /// minimum by more than the agreement the residual is solved over every generating hinge on top of it instead. Each
    /// of these solves holds every hinge to its largest reading first and, only where that recovers nothing, holds the
    /// hinges no rod needs as its sole setter to that reading from below.
    /// </para>
    /// </summary>
    internal static (Dictionary<int, float>? Paint, float AddCurvature) ClothBendStiffnessOverFold(FeModel feModel,
        List<int[]> faces, HashSet<(int, int)> network, float addCurvature, bool keepsCurvature)
        => ClothPaintCoveringEveryRod(feModel, faces, network, ClothCurvatureMeetsItsCappedRods(feModel, faces, network,
            ClothBendStiffnessRead(feModel, faces, network, addCurvature, keepsCurvature), keepsCurvature), keepsCurvature);

    /// <summary>
    /// The covering-hinge solve is exact by construction, so where the paint an earlier solve settled on leaves network rods
    /// short of or past their compiled minimum, the covering solve's paint is taken instead when it rebuilds strictly more of
    /// them: first on top of the settled <c>add_curvature</c>, then at zero where neither the sheet's suspenders nor a chain ring
    /// keep its value. A tie
    /// keeps the settled answer, and the earlier of the two covering answers wins a tie between them.
    /// </summary>
    static (Dictionary<int, float>? Paint, float AddCurvature) ClothPaintCoveringEveryRod(FeModel feModel,
        List<int[]> faces, HashSet<(int, int)> network, (Dictionary<int, float>? Paint, float AddCurvature) settled,
        bool keepsCurvature)
    {
        var best = settled;
        var bestMisses = ClothPaintMisses(feModel, faces, network, settled.Paint, settled.AddCurvature);
        if (bestMisses == 0)
        {
            return settled;
        }

        float[] curvatures = keepsCurvature || feModel.ChainRingCurvature > 0f || settled.AddCurvature == 0f
            ? [settled.AddCurvature]
            : [settled.AddCurvature, 0f];
        foreach (var curvature in curvatures)
        {
            if (ClothBendStiffnessCoveringHinges(feModel, faces, network, curvature) is not { } covered)
            {
                continue;
            }

            var misses = ClothPaintMisses(feModel, faces, network, covered, curvature);
            if (misses < bestMisses)
            {
                (best, bestMisses) = ((covered, curvature), misses);
            }
        }

        return best;
    }

    // The network rods whose minimum the compiler would not rebuild from the paint and add_curvature: each generating hinge
    // folds by clamp((paint[u] + paint[v]) * pi / 2 + add_curvature * pi, 0, pi), and the rod takes the smallest span any of
    // them folds it to, never more than its own rest span.
    static int ClothPaintMisses(FeModel feModel, List<int[]> faces, HashSet<(int, int)> network,
        Dictionary<int, float>? paint, float addCurvature)
    {
        var positions = feModel.InitPosePositions;
        var generators = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var (hinge, nodeA, nodeB) in FeModel.BendRodGenerators(faces))
        {
            var pair = nodeA < nodeB ? (nodeA, nodeB) : (nodeB, nodeA);
            (generators.TryGetValue(pair, out var known) ? known : generators[pair] = []).Add(hinge);
        }

        var misses = 0;
        foreach (var rod in feModel.Rods)
        {
            var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!network.Contains(pair) || pair.Item2 >= positions.Length)
            {
                continue;
            }

            var span = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
            foreach (var hinge in generators.GetValueOrDefault(pair) ?? [])
            {
                var axis = positions[hinge.Item2] - positions[hinge.Item1];
                if (axis.LengthSquared() < 1e-12f)
                {
                    continue;
                }

                axis = Vector3.Normalize(axis);
                var toA = positions[rod.NodeA] - positions[hinge.Item1];
                var toB = positions[rod.NodeB] - positions[hinge.Item1];
                var alongA = Vector3.Dot(toA, axis);
                var alongB = Vector3.Dot(toB, axis);
                var riseA = (toA - (alongA * axis)).Length();
                var riseB = (toB - (alongB * axis)).Length();
                var sum = (paint?.GetValueOrDefault(hinge.Item1) ?? 0f) + (paint?.GetValueOrDefault(hinge.Item2) ?? 0f);
                var fold = Math.Clamp((sum * MathF.PI / 2f) + (addCurvature * MathF.PI), 0f, MathF.PI);
                var folded = MathF.Sqrt(MathF.Max(0f, ((alongA - alongB) * (alongA - alongB)) + (riseA * riseA) + (riseB * riseB)
                    - (2f * riseA * riseB * MathF.Cos(fold))));
                span = MathF.Min(span, folded);
            }

            if (MathF.Abs(span - rod.MinDist) > MathF.Max(1e-3f, 1e-4f * rod.MinDist))
            {
                misses++;
            }
        }

        return misses;
    }

    /// <summary>
    /// A bend rod held at its own rest span states a lower bound on the fold at every hinge that generates it, and
    /// that fold is the model-wide <c>add_curvature</c> plus the hinge's own paint. Where a paint is recovered it
    /// carries the bound itself, and where a suspender or chain-ring reading pins the model-wide value the sheet
    /// does not own it - but where every paint solve declines and nothing else states the value, the model-wide
    /// value is all there is to meet the bound with, and a sheet that states less than its own capped rods allow
    /// asks the compiler to fold them past their compiled minimum.
    /// </summary>
    static (Dictionary<int, float>? Paint, float AddCurvature) ClothCurvatureMeetsItsCappedRods(FeModel feModel,
        List<int[]> faces, HashSet<(int, int)> network,
        (Dictionary<int, float>? Paint, float AddCurvature) read, bool keepsCurvature)
    {
        if (read.Paint is not null || keepsCurvature || feModel.ChainRingCurvature > 0f)
        {
            return read;
        }

        var (_, capped) = ClothCurvatureReadings(feModel, faces, network);
        if (capped.Count == 0)
        {
            return read;
        }

        // Both sides in the sin^2(half angle) the minimum length is linear in, which is the unit the readings
        // are taken in and the only one the agreement is calibrated for.
        var stated = MathF.Sin(MathF.PI * read.AddCurvature / 2f);
        var bound = capped.Max();
        return bound > (stated * stated) + ClothCurvatureAgreement
            ? (read.Paint, 2f / MathF.PI * MathF.Asin(MathF.Sqrt(bound)))
            : read;
    }

    static (Dictionary<int, float>? Paint, float AddCurvature) ClothBendStiffnessRead(FeModel feModel,
        List<int[]> faces, HashSet<(int, int)> network, float addCurvature, bool keepsCurvature)
    {
        var paint = ClothBendStiffnessFromHinges(feModel, faces, network,
            addCurvature > 0f ? addCurvature : feModel.ChainRingCurvature);
        if (paint is not null || keepsCurvature || feModel.ChainRingCurvature > 0f)
        {
            return (paint, addCurvature);
        }

        if (addCurvature <= 0f)
        {
            return (ClothBendStiffnessFromHinges(feModel, faces, network, 0f, generatorBound: true, out _, out _, out _)
                ?? ClothBendStiffnessFromHinges(feModel, faces, network, 0f, generatorBound: true, out _, out _, out _,
                    relaxSetters: true)
                ?? ClothBendStiffnessCoveringHinges(feModel, faces, network, 0f), addCurvature);
        }

        var residual = ClothBendStiffnessFromHinges(feModel, faces, network, addCurvature, generatorBound: true,
            out var residuals, out var sharedSlack, out var residualSlack);
        var overFolds = residuals.Values.Any(static sum => sum < -ClothBendStiffnessAgreement);
        (Dictionary<int, float>? Paint, float Slack, float Spread) RetryAtZero(bool relaxSetters)
        {
            var retried = ClothBendStiffnessFromHinges(feModel, faces, network, 0f, generatorBound: true, out var folds,
                out _, out var paintedSlack, relaxSetters);
            return (retried, paintedSlack, folds.Count > 0 ? folds.Values.Max() - folds.Values.Min() : 0f);
        }

        if (overFolds && RetryAtZero(relaxSetters: false) is ({ Count: > 0 } whole, var slack, var spread)
            && spread > ClothBendStiffnessAgreement && sharedSlack > slack + ClothBendStiffnessAgreement)
        {
            return (whole, 0f);
        }

        residual ??= ClothBendStiffnessFromHinges(feModel, faces, network, addCurvature, generatorBound: true, out _, out _,
            out residualSlack, relaxSetters: true);
        if (residual is null && !overFolds && sharedSlack > ClothBendStiffnessAgreement
            && ClothBendStiffnessCoveringHinges(feModel, faces, network, addCurvature) is { } coveredResidual)
        {
            return (coveredResidual, addCurvature);
        }
        if (residual is { Count: > 0 } && sharedSlack > residualSlack + ClothBendStiffnessAgreement)
        {
            return (residual, addCurvature);
        }

        if (overFolds && RetryAtZero(relaxSetters: true) is ({ Count: > 0 } relaxed, var relaxedSlack, var relaxedSpread)
            && relaxedSpread > ClothBendStiffnessAgreement && sharedSlack > relaxedSlack + ClothBendStiffnessAgreement)
        {
            return (relaxed, 0f);
        }

        if (overFolds && sharedSlack > ClothBendStiffnessAgreement
            && ClothBendStiffnessCoveringHinges(feModel, faces, network, 0f) is { } covered)
        {
            return (covered, 0f);
        }

        return (paint, addCurvature);
    }

    // The same readings keyed by the HINGE each rod was folded about, which is what the per-vertex paint is
    // solved over: the compiler's angle is per hinge, not per rod, so two rods across one hinge state one
    // value and rods across different hinges state different ones.
    static List<((int, int) Hinge, float Fraction, bool Capped, float Error, ((int, int) Hinge, float Fraction)[] Candidates)> ClothHingeReadings(
        FeModel feModel, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var positions = feModel.InitPosePositions;
        var generators = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var (hinge, nodeA, nodeB) in FeModel.BendRodGenerators(faces))
        {
            if (nodeA == nodeB)
            {
                continue;
            }

            var generated = nodeA < nodeB ? (nodeA, nodeB) : (nodeB, nodeA);
            var about = generators.TryGetValue(generated, out var known) ? known : generators[generated] = [];
            if (!about.Contains(hinge))
            {
                about.Add(hinge);
            }
        }

        var readings = new List<((int, int) Hinge, float Fraction, bool Capped, float Error, ((int, int) Hinge, float Fraction)[] Candidates)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!beyondSurface.Contains(edge) || edge.Item2 >= positions.Length)
            {
                continue;
            }

            // A bend-only rod has no length of its own to identify its hinge by, so its rest span stands in.
            var rest = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
            var coplanar = rod.MaxDist < ClothBendOnlyRodMaxDistance ? rod.MaxDist : rest;
            var closest = float.MaxValue;
            var flat = 0f;
            var folded = 0f;
            var about = (0, 0);
            var fits = new List<((int, int) Hinge, float Error, float Open, float Shut)>();
            foreach (var hinge in generators.TryGetValue(edge, out var generating) ? generating : [])
            {
                var axis = positions[hinge.Item2] - positions[hinge.Item1];
                var axisLength = axis.Length();
                if (axisLength < 1e-6f)
                {
                    continue;
                }

                axis /= axisLength;
                var toA = positions[rod.NodeA] - positions[hinge.Item1];
                var toB = positions[rod.NodeB] - positions[hinge.Item1];
                var alongA = Vector3.Dot(toA, axis);
                var alongB = Vector3.Dot(toB, axis);
                var riseA = (toA - alongA * axis).Length();
                var riseB = (toB - alongB * axis).Length();
                var slide = (alongA - alongB) * (alongA - alongB);
                var open = MathF.Sqrt(slide + ((riseA + riseB) * (riseA + riseB)));
                var shut = MathF.Sqrt(slide + ((riseA - riseB) * (riseA - riseB)));
                var error = MathF.Abs(open - coplanar);
                if (open - shut >= 0.02f * open)
                {
                    fits.Add((hinge, error, open, shut));
                }

                if (error < closest && open - shut >= 0.02f * open)
                {
                    closest = error;
                    flat = open;
                    folded = shut;
                    about = hinge;
                }
            }

            if (closest > 0.005f * MathF.Max(1f, coplanar))
            {
                continue;
            }

            var reach = (flat * flat) - (folded * folded);
            var span = rod.MinDist >= rest - (2e-4f * MathF.Max(1f, rest)) ? rest : rod.MinDist;
            var fraction = Math.Clamp(((span * span) - (folded * folded)) / reach, 0f, 1f);
            // The compiler builds the rod once per hinge its own element pairing generates it from, and each of
            // those reads the rod's one minimum through its own geometry.
            var candidates = fits
                .Select(fit => (fit.Hinge, Math.Clamp(((span * span) - (fit.Shut * fit.Shut))
                    / ((fit.Open * fit.Open) - (fit.Shut * fit.Shut)), 0f, 1f)))
                .ToArray();
            readings.Add((about, fraction, span == rest, closest, candidates));
        }

        return readings;
    }

    /// <summary>
    /// The per-vertex <c>cloth_bend_stiffness</c> paint the sheet's own bend rods state, keyed by control
    /// node, or null where they state none. The compiler folds the rod across a hinge by
    /// <c>clamp((paint[u] + paint[v]) * pi/2 + add_curvature * pi, 0, pi)</c>, so each hinge is one
    /// equation in its two vertices, and the paint recovered here is the RESIDUAL on top of the
    /// <c>add_curvature</c> the sheet already emits: a sheet the model-wide value alone explains recovers
    /// nothing and keeps its output unchanged.
    /// <para>
    /// A hinge whose rod still has room to open states its sum exactly; one already pinned at its own rest
    /// span states only a lower bound. A sum of zero pins both of its vertices to zero, and a bound of two
    /// pins both to one, the paint being a 0..1 channel and every reader of it clamping its own angle at
    /// pi - so no compiled rod can distinguish a sum above two from two. Those pins propagate through the
    /// exact equations, an alternating chain per connected component; a component no pin reaches keeps the
    /// smallest assignment its own bounds allow, which is the compiler's own default of zero wherever the
    /// interval admits it, and the even split where the two directions tie. A vertex no hinge reaches
    /// keeps zero, which is also what leaving the stream out would give it.
    /// </para>
    /// <para>
    /// Two rods across one hinge that disagree, two pins that contradict, or an assignment that fails to
    /// reproduce a hinge it was solved from all recover nothing: the sheet being exported is then not the
    /// one the compiler folded, and it keeps the default.
    /// </para>
    /// </summary>
    static Dictionary<int, float>? ClothBendStiffnessFromHinges(FeModel feModel, List<int[]> faces,
        HashSet<(int, int)> network, float addCurvature)
        => ClothBendStiffnessFromHinges(feModel, faces, network, addCurvature, generatorBound: false, out _, out _, out _);

    /// <summary>
    /// <see cref="ClothBendStiffnessFromHinges(FeModel, List{int[]}, HashSet{ValueTuple{int, int}}, float)"/>, with
    /// <paramref name="statedSums"/> set to the pair sum each hinge states on top of <paramref name="addCurvature"/>.
    /// With <paramref name="generatorBound"/> every hinge that generates a rod is read through its own geometry and bounded
    /// from below by every rod it generates, the compiler keeping each rod's shortest minimum, and solved at that bound; with
    /// <paramref name="relaxSetters"/> only a hinge some rod has as its sole candidate is solved exactly and every other hinge
    /// only has to reach its bound. The answer is kept only where it reproduces every rod: across each rod's hinges
    /// the smallest assigned sum above the one that hinge states is zero, so no hinge folds further than the rod allows
    /// and one of them folds as far, and a rod already at its rest span has no hinge assigned less than the sum it states
    /// there. <paramref name="unpaintedSlack"/> and <paramref name="solvedSlack"/> are the largest amount any
    /// rod misses that by, with no paint and with the answer; both are zero without <paramref name="generatorBound"/>.
    /// </summary>
    static Dictionary<int, float>? ClothBendStiffnessFromHinges(FeModel feModel, List<int[]> faces,
        HashSet<(int, int)> network, float addCurvature, bool generatorBound,
        out Dictionary<(int, int), float> statedSums, out float unpaintedSlack, out float solvedSlack, bool relaxSetters = false)
    {
        unpaintedSlack = 0f;
        solvedSlack = 0f;
        var readings = ClothHingeReadings(feModel, faces, network);
        float StatedSum(float fraction)
            => (4f / MathF.PI * MathF.Asin(MathF.Sqrt(fraction))) - (2f * addCurvature);

        float RodSlack(Func<(int, int), float> assigned)
        {
            var worst = 0f;
            foreach (var (_, _, capped, _, candidates) in readings)
            {
                if (candidates.Length == 0)
                {
                    continue;
                }

                worst = MathF.Max(worst, capped
                    ? candidates.Max(candidate => StatedSum(candidate.Fraction) - assigned(candidate.Hinge))
                    : MathF.Abs(candidates.Min(candidate => assigned(candidate.Hinge) - StatedSum(candidate.Fraction))));
            }

            return worst;
        }

        // Two rods across one hinge were folded through one angle, so where they read differently the
        // hinge one of them was matched to is not the hinge the compiler folded it about. The better fit
        // is the reading whose flat span reproduces its rod's own maximum length more closely.
        var best = new Dictionary<(int, int), (float Fraction, bool Capped, float Error)>();
        foreach (var (hinge, fraction, capped, error, _) in readings)
        {
            if (!best.TryGetValue(hinge, out var stated) || error < stated.Error)
            {
                best[hinge] = (fraction, capped, error);
            }
        }

        var exact = new Dictionary<(int, int), float>();
        var bounds = new Dictionary<(int, int), float>();
        statedSums = exact;
        foreach (var (hinge, reading) in best)
        {
            (reading.Capped ? bounds : exact)[hinge] = StatedSum(reading.Fraction);
        }

        // The compiler keeps one record per rod and gives it the SHORTEST minimum any hinge generating it builds,
        // so every such hinge is bounded from below by the rod and the one that set it states its fold. Each hinge
        // therefore takes the largest bound its rods give it. Only a hinge some rod has no other candidate for has
        // to sit exactly there; with relaxSetters the others only have to reach it.
        if (generatorBound)
        {
            exact.Clear();
            foreach (var (_, _, capped, _, candidates) in readings)
            {
                if (capped)
                {
                    continue;
                }

                foreach (var (hinge, candidateFraction) in candidates)
                {
                    var sum = StatedSum(candidateFraction);
                    exact[hinge] = exact.TryGetValue(hinge, out var known) ? MathF.Max(known, sum) : sum;
                }
            }

            statedSums = new Dictionary<(int, int), float>(exact);
            if (relaxSetters)
            {
                var setting = new HashSet<(int, int)>();
                foreach (var (_, _, capped, _, candidates) in readings)
                {
                    if (capped)
                    {
                        continue;
                    }

                    var setters = candidates
                        .Where(candidate => StatedSum(candidate.Fraction) >= exact[candidate.Hinge] - ClothBendStiffnessAgreement)
                        .Select(static candidate => candidate.Hinge)
                        .Distinct()
                        .ToList();
                    if (setters.Count == 1)
                    {
                        setting.Add(setters[0]);
                    }
                }

                foreach (var hinge in exact.Keys.Where(hinge => !setting.Contains(hinge)).ToList())
                {
                    bounds[hinge] = bounds.TryGetValue(hinge, out var least) ? MathF.Max(least, exact[hinge]) : exact[hinge];
                    exact.Remove(hinge);
                }
            }

            unpaintedSlack = RodSlack(static _ => 0f);
        }

        // A rod already at its own rest span states a bound through every hinge that generates it, and each
        // hinge has to satisfy the greatest of them or that rod comes back short of its cap.
        foreach (var (_, _, capped, _, candidates) in readings)
        {
            if (!capped)
            {
                continue;
            }

            foreach (var (hinge, candidateFraction) in candidates)
            {
                var least = StatedSum(candidateFraction);
                if (!bounds.TryGetValue(hinge, out var known) || least > known)
                {
                    bounds[hinge] = least;
                }
            }
        }

        if (exact.Count == 0 && bounds.Count == 0)
        {
            return null;
        }

        var pinned = new Dictionary<int, float>();
        var equations = new List<(int U, int V, float Sum)>();
        var checks = new List<(int U, int V, float Least)>();

        bool Pin(int node, float value)
        {
            if (!pinned.TryGetValue(node, out var stated))
            {
                pinned[node] = value;
                return true;
            }

            return MathF.Abs(stated - value) <= ClothBendStiffnessAgreement;
        }

        foreach (var (hinge, sum) in exact.OrderBy(static entry => entry.Key.Item1)
            .ThenBy(static entry => entry.Key.Item2))
        {
            if (sum < -ClothBendStiffnessAgreement)
            {
                return null;
            }

            if (sum <= ClothBendStiffnessAgreement)
            {
                if (!Pin(hinge.Item1, 0f) || !Pin(hinge.Item2, 0f))
                {
                    return null;
                }
            }
            else if (sum >= 2f - ClothBendStiffnessAgreement)
            {
                if (!Pin(hinge.Item1, 1f) || !Pin(hinge.Item2, 1f))
                {
                    return null;
                }
            }
            else
            {
                equations.Add((hinge.Item1, hinge.Item2, sum));
            }
        }

        foreach (var (hinge, least) in bounds.OrderBy(static entry => entry.Key.Item1)
            .ThenBy(static entry => entry.Key.Item2))
        {
            if (exact.ContainsKey(hinge))
            {
                checks.Add((hinge.Item1, hinge.Item2, least));
            }
            else if (least >= 2f - ClothBendStiffnessAgreement)
            {
                if (!Pin(hinge.Item1, 1f) || !Pin(hinge.Item2, 1f))
                {
                    return null;
                }
            }
            else if (least > ClothBendStiffnessAgreement)
            {
                checks.Add((hinge.Item1, hinge.Item2, least));
            }
        }

        var solved = ClothBendStiffnessComponents(pinned, equations, checks);
        if (solved is null)
        {
            return null;
        }

        // A vertex the equations already decided keeps that value; only a vertex no equation and no pin
        // reaches is free to be raised by a hinge that states a bound alone.
        var determined = new HashSet<int>(solved.Keys);
        foreach (var (u, v, _) in checks)
        {
            solved.TryAdd(u, 0f);
            solved.TryAdd(v, 0f);
        }

        for (var pass = 0; pass < ClothBendStiffnessRepairPasses; pass++)
        {
            var raised = false;
            foreach (var (u, v, least) in checks)
            {
                var have = solved[u] + solved[v];
                if (have >= least - ClothBendStiffnessAgreement)
                {
                    continue;
                }

                var movesU = !determined.Contains(u) && solved[u] < 1f;
                var movesV = !determined.Contains(v) && solved[v] < 1f;
                var movable = (movesU ? 1 : 0) + (movesV ? 1 : 0);
                if (movable == 0)
                {
                    return null;
                }

                var share = (least - have) / movable;
                if (movesU)
                {
                    solved[u] = MathF.Min(1f, solved[u] + share);
                }

                if (movesV)
                {
                    solved[v] = MathF.Min(1f, solved[v] + share);
                }

                raised = true;
            }

            if (!raised)
            {
                break;
            }
        }

        foreach (var (hinge, sum) in exact)
        {
            if (MathF.Abs(solved.GetValueOrDefault(hinge.Item1) + solved.GetValueOrDefault(hinge.Item2) - sum)
                > ClothBendStiffnessAgreement)
            {
                return null;
            }
        }

        foreach (var (hinge, least) in bounds)
        {
            if (solved.GetValueOrDefault(hinge.Item1) + solved.GetValueOrDefault(hinge.Item2)
                < least - ClothBendStiffnessAgreement)
            {
                return null;
            }
        }

        if (generatorBound)
        {
            solvedSlack = RodSlack(hinge => solved.GetValueOrDefault(hinge.Item1) + solved.GetValueOrDefault(hinge.Item2));
            if (solvedSlack > ClothBendStiffnessAgreement)
            {
                return null;
            }
        }

        return solved.Values.Any(static value => value > ClothBendStiffnessAgreement) ? solved : null;
    }

    // The equation half of ClothBendStiffnessFromHinges: every hinge stating an exact sum joins its two
    // vertices into a chain on which the values alternate, b(x) = sign * p + offset, so one pin decides the
    // whole chain and a chain that closes on itself either checks out or decides p by itself.
    static Dictionary<int, float>? ClothBendStiffnessComponents(Dictionary<int, float> pinned,
        List<(int U, int V, float Sum)> equations, List<(int U, int V, float Least)> checks)
    {
        var adjacency = new Dictionary<int, List<(int Node, float Sum)>>();
        foreach (var (u, v, sum) in equations)
        {
            (adjacency.TryGetValue(u, out var fromU) ? fromU : adjacency[u] = []).Add((v, sum));
            (adjacency.TryGetValue(v, out var fromV) ? fromV : adjacency[v] = []).Add((u, sum));
        }

        var sign = new Dictionary<int, float>();
        var offset = new Dictionary<int, float>();
        var component = new Dictionary<int, int>();
        var members = new List<List<int>>();
        var forced = new List<List<float>>();

        foreach (var root in adjacency.Keys.Concat(pinned.Keys).Distinct().Order())
        {
            if (component.ContainsKey(root))
            {
                continue;
            }

            var index = members.Count;
            members.Add([root]);
            forced.Add([]);
            sign[root] = 1f;
            offset[root] = 0f;
            component[root] = index;
            var walk = new Queue<int>();
            walk.Enqueue(root);
            while (walk.Count > 0)
            {
                var here = walk.Dequeue();
                foreach (var (there, sum) in adjacency.GetValueOrDefault(here) ?? [])
                {
                    var thereSign = -sign[here];
                    var thereOffset = sum - offset[here];
                    if (component.ContainsKey(there))
                    {
                        if (thereSign == sign[there])
                        {
                            if (MathF.Abs(thereOffset - offset[there]) > ClothBendStiffnessAgreement)
                            {
                                return null;
                            }
                        }
                        else
                        {
                            forced[index].Add((thereOffset - offset[there]) / (2f * sign[there]));
                        }

                        continue;
                    }

                    sign[there] = thereSign;
                    offset[there] = thereOffset;
                    component[there] = index;
                    members[index].Add(there);
                    walk.Enqueue(there);
                }
            }
        }

        foreach (var (node, value) in pinned)
        {
            forced[component[node]].Add((value - offset[node]) / sign[node]);
        }

        var solved = new Dictionary<int, float>();
        for (var index = 0; index < members.Count; index++)
        {
            float parameter;
            if (forced[index].Count > 0)
            {
                if (forced[index].Max() - forced[index].Min() > ClothBendStiffnessAgreement)
                {
                    return null;
                }

                parameter = forced[index].Average();
            }
            else
            {
                var least = float.MinValue;
                var most = float.MaxValue;
                foreach (var node in members[index])
                {
                    var end = (1f - offset[node]) / sign[node];
                    var start = -offset[node] / sign[node];
                    least = MathF.Max(least, MathF.Min(start, end));
                    most = MathF.Min(most, MathF.Max(start, end));
                }

                foreach (var (u, v, need) in checks)
                {
                    if (component.GetValueOrDefault(u, -1) != index
                        || component.GetValueOrDefault(v, -1) != index
                        || sign[u] + sign[v] == 0f)
                    {
                        continue;
                    }

                    var edge = (need - offset[u] - offset[v]) / (sign[u] + sign[v]);
                    if (sign[u] > 0f)
                    {
                        least = MathF.Max(least, edge);
                    }
                    else
                    {
                        most = MathF.Min(most, edge);
                    }
                }

                if (least > most + ClothBendStiffnessAgreement)
                {
                    return null;
                }

                var direction = members[index].Sum(node => sign[node]);
                parameter = direction > 0f ? least : direction < 0f ? most : 0.5f * (least + most);
            }

            foreach (var node in members[index])
            {
                var value = (sign[node] * parameter) + offset[node];
                if (value < -ClothBendStiffnessAgreement || value > 1f + ClothBendStiffnessAgreement)
                {
                    return null;
                }

                solved[node] = Math.Clamp(value, 0f, 1f);
            }
        }

        return solved;
    }

    /// <summary>
    /// The per-vertex <c>cloth_bend_stiffness</c> that folds a bend network on top of <paramref name="addCurvature"/>, where
    /// the rods do not say which of their hinges built them. Each rod reaches its minimum through ONE hinge and sits at or
    /// above it through the rest, so each hinge's sum is at least the largest any of its rods states, and every rod needs
    /// one hinge held exactly there. The rods only one hinge can set hold it; the rest are covered by the fewest further
    /// hinges, the one covering most first and the lowest pair on a tie. A reading pinned at the fully shut span, where
    /// another hinge of the same rod reads it open, is the rod sitting below that hinge's reach: that hinge cannot have
    /// built it and bounds nothing.
    /// </summary>
    /// <remarks>
    /// Every constraint is a sum or difference of at most two paints, so the system is solved exactly as a shortest-path
    /// problem over each paint and its negation, from the tightest tolerance up; null where no tolerance up to the
    /// agreement admits one.
    /// </remarks>
    static Dictionary<int, float>? ClothBendStiffnessCoveringHinges(FeModel feModel, List<int[]> faces,
        HashSet<(int, int)> network, float addCurvature)
    {
        var readings = ClothHingeReadings(feModel, faces, network);
        float Sum(float fraction) => (4f / MathF.PI * MathF.Asin(MathF.Sqrt(fraction))) - (2f * addCurvature);

        static ((int, int) Hinge, float Fraction)[] Usable(bool capped, ((int, int) Hinge, float Fraction)[] candidates)
            => capped || !Array.Exists(candidates, static candidate => candidate.Fraction > 0f)
                ? candidates
                : Array.FindAll(candidates, static candidate => candidate.Fraction > 0f);

        var least = new Dictionary<(int, int), float>();
        foreach (var (_, _, capped, _, candidates) in readings)
        {
            foreach (var (hinge, fraction) in Usable(capped, candidates))
            {
                least[hinge] = MathF.Max(least.GetValueOrDefault(hinge, float.MinValue), Sum(fraction));
            }
        }

        foreach (var tolerance in ClothBendStiffnessCoverTolerances)
        {
            var held = new HashSet<(int, int)>();
            var open = new List<List<(int, int)>>();
            var explained = true;
            foreach (var (_, _, capped, _, candidates) in readings)
            {
                if (capped || candidates.Length == 0)
                {
                    continue;
                }

                var setters = Usable(capped, candidates)
                    .Where(candidate => Sum(candidate.Fraction) >= least[candidate.Hinge] - tolerance)
                    .Select(static candidate => candidate.Hinge)
                    .Distinct()
                    .Order()
                    .ToList();
                if (setters.Count == 0)
                {
                    explained = false;
                    break;
                }

                if (setters.Count == 1)
                {
                    held.Add(setters[0]);
                }
                else
                {
                    open.Add(setters);
                }
            }

            if (!explained)
            {
                continue;
            }

            open.RemoveAll(setters => setters.Exists(held.Contains));
            while (open.Count > 0)
            {
                var covers = new Dictionary<(int, int), int>();
                foreach (var setters in open)
                {
                    foreach (var hinge in setters)
                    {
                        covers[hinge] = covers.GetValueOrDefault(hinge) + 1;
                    }
                }

                var next = covers.OrderByDescending(static entry => entry.Value).ThenBy(static entry => entry.Key).First().Key;
                held.Add(next);
                open.RemoveAll(setters => setters.Contains(next));
            }

            var constraints = new List<(int U, float SignU, int V, float SignV, float Most)>();
            foreach (var (hinge, sum) in least)
            {
                constraints.Add((hinge.Item1, -1f, hinge.Item2, -1f, tolerance - sum));
                if (held.Contains(hinge))
                {
                    constraints.Add((hinge.Item1, 1f, hinge.Item2, 1f, sum + tolerance));
                }
            }

            if (SolvePairwiseBounds(constraints) is { } paint)
            {
                return paint.Values.Any(static value => value > ClothBendStiffnessAgreement) ? paint : null;
            }
        }

        return null;
    }

    static readonly float[] ClothBendStiffnessCoverTolerances = [1e-5f, 1e-4f, 1e-3f, ClothBendStiffnessAgreement];

    /// <summary>
    /// Values in [0, 1] for every node the constraints name, each constraint <c>SignU * x[U] + SignV * x[V] &lt;= Most</c>
    /// with unit signs, or null where none exist. Every node stands as itself and as its negation, a constraint becomes two
    /// shortest-path edges between them, and the values are half the distance between the two; a negative cycle means the
    /// system has no solution.
    /// </summary>
    static Dictionary<int, float>? SolvePairwiseBounds(List<(int U, float SignU, int V, float SignV, float Most)> constraints)
    {
        var nodes = constraints.SelectMany(static c => new[] { c.U, c.V }).Distinct().Order().ToList();
        var index = new Dictionary<int, int>();
        for (var i = 0; i < nodes.Count; i++)
        {
            index[nodes[i]] = i;
        }

        // Vertex 2i stands for x[i] and 2i+1 for -x[i]; an edge a -> b of weight w states value(b) - value(a) <= w.
        static int Term(int node, float sign) => (2 * node) + (sign > 0f ? 0 : 1);
        static int Negated(int term) => term ^ 1;
        var edges = new List<(int From, int To, double Weight)>();
        void Bound(int u, float signU, int v, float signV, double most)
        {
            var a = Term(u, signU);
            var b = Term(v, signV);
            edges.Add((Negated(b), a, most));
            edges.Add((Negated(a), b, most));
        }

        foreach (var (u, signU, v, signV, most) in constraints)
        {
            Bound(index[u], signU, index[v], signV, most);
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            Bound(i, 1f, i, 1f, 2.0);
            Bound(i, -1f, i, -1f, 0.0);
        }

        var distance = new double[2 * nodes.Count];
        for (var pass = 0; pass <= distance.Length; pass++)
        {
            var changed = false;
            foreach (var (from, to, weight) in edges)
            {
                if (distance[from] + weight < distance[to] - 1e-12)
                {
                    distance[to] = distance[from] + weight;
                    changed = true;
                }
            }

            if (!changed)
            {
                var solved = new Dictionary<int, float>();
                for (var i = 0; i < nodes.Count; i++)
                {
                    solved[nodes[i]] = (float)Math.Clamp((distance[2 * i] - distance[(2 * i) + 1]) / 2.0, 0.0, 1.0);
                }

                return solved;
            }
        }

        return null;
    }

    // How far two hinges' stated sums may sit apart and still count as the same paint, in the
    // paint's own units: a hundredth of the half turn a full sum of two folds a hinge through.
    const float ClothBendStiffnessAgreement = 0.02f;

    // How many times a hinge stating only a lower bound may raise its own two vertices before the
    // solve gives up. Each pass satisfies every bound it can, so a chain of them settles in a few.
    const int ClothBendStiffnessRepairPasses = 8;

    // TODO: some models re-export more rods than the original, from overlap between the springs emitted
    // here, the chains, and the proxy sheet all re-declaring the same span.
    static void AddClothProxySprings(KVObject softbodyChildren, FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies, HashSet<int> chainJointNodes,
        HashSet<int> authoredClothNodes, Dictionary<int, string> freeClothNodeNames,
        HashSet<(int, int)> derivedRods, Dictionary<int, string> proxyNodeNames)
    {
        // Islands the cloth importer is expected to prune vertices from (see FeModel.ComputeDropRisk):
        // emitting explicit rods into them would orphan a ClothSpring on a vertex the compiler never creates
        // ("Cannot find node $cloth_mXpY", a hard failure). Skip their explicit rods entirely and let the
        // importer auto-derive the network from the surface instead - guaranteed to compile, at the cost of
        // exact rod topology for that one island. Clean islands keep their exact reconstructed rods.
        var riskyNodes = new HashSet<int>();

        foreach (var (_, _, proxyMesh) in proxies)
        {
            if (proxyMesh.IsDropRisk)
            {
                foreach (var node in proxyMesh.NodeIndices)
                {
                    riskyNodes.Add(node);
                }
            }
        }

        // A real bone anchors a spring only when this export also declares it as a ClothNode. A bone the
        // compile knows solely through a chain's joint list or a proxy back-solve is not a valid endpoint,
        // and naming one fails the whole compile with "Cannot find Fx Bone"/"Cannot find node". A
        // "$cloth_node_" ctrl re-authored as a free ClothNode is named by its element name instead.
        string? ResolveName(int node)
            => FeModel.IsProxyNodeName(feModel.CtrlNames[node])
                ? proxyNodeNames.GetValueOrDefault(node) ?? freeClothNodeNames.GetValueOrDefault(node)
                : authoredClothNodes.Contains(node) ? feModel.CtrlNames[node] : null;

        var seen = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            var edge = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!seen.Add(edge))
            {
                continue;
            }

            // A rod inside a drop-risk island is skipped (the whole island falls back to compiler-derived
            // rods) - see the riskyNodes remarks above.
            if (riskyNodes.Contains(edge.Item1) || riskyNodes.Contains(edge.Item2))
            {
                continue;
            }

            if (derivedRods.Contains(edge))
            {
                continue;
            }

            // A ClothChain's own joint hierarchy compiles to a fully-connected local rod mesh among ITS
            // OWN joints, not just parent-child pairs, so re-declaring one of these as an explicit
            // ClothSpring is redundant. It is also rejected: a bone that is only a ClothChain joint_name,
            // with no fit-matrix back-solve or ClothNode registration of its own, is not a valid
            // ClothSpring endpoint.
            if (chainJointNodes.Contains(edge.Item1) || chainJointNodes.Contains(edge.Item2))
            {
                continue;
            }

            var name0 = ResolveName(rod.NodeA);
            var name1 = ResolveName(rod.NodeB);
            if (name0 is null || name1 is null)
            {
                // A rod-only proxy node dropped by BuildProxyMeshesFromRodsOnly's 3-member minimum (see
                // its own remarks) has no corresponding exported vertex to reference at all - skip rather
                // than author a dangling reference the compiler would reject outright.
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1, rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor));
        }
    }

    // Rods the chains do not rebuild themselves (extra copies of a parent span) are re-declared here, and a
    // cluster's tie beside a chain span as its two-member cluster.
    internal static HashSet<(int, int)> AddClothChainSurplusRods(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        // The pairs declared here, so the free-node pass does not re-declare one of them and ship the
        // same constraint twice. A pair reaches both only where one endpoint is a chain joint the other
        // passes still declare a bare node for, which is what a sibling hub is.
        var declaredPairs = new HashSet<(int, int)>();
        var controlNames = feModel.CtrlNames;

        // Only a bone some emitted chain actually claims as a joint is registered as a cloth node, and so
        // only such a bone can anchor a spring. A cloth-flagged bone that no chain covers (a chain's own
        // parent one hop above its root, say) resolves to nothing and fails the whole compile with
        // "Cannot find Fx Bone".
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // Ring nodes the emitted chain regenerates, and which joint extruded each: a cluster tie may name
        // one, a spring may not, and a tie spans the rings of two DIFFERENT joints.
        var chainRingNodes = chains.SelectMany(static chain => chain.Joints)
            .SelectMany(static joint => joint.RingNodes)
            .ToHashSet();

        var ringOwner = new Dictionary<int, int>();
        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            foreach (var ring in joint.RingNodes)
            {
                ringOwner[ring] = joint.Node;
            }
        }

        bool Nameable(int node, bool tie)
            => chainJoints.Contains(node) || (tie && chainRingNodes.Contains(node));

        // One spring per surplus rod OCCURRENCE, numbered like AddFreeClothNodesAndSprings' copies.
        var occurrence = new Dictionary<(int, int), int>();
        var surplus = feModel.GetUngeneratedRods(chains, feModel.HasChainStiffnessRods(chains));
        var clusterTies = ClusterTiesBesideChainSpans(feModel, surplus);
        var ringTies = RingClusterTies(feModel, surplus, ringOwner);
        var cliquePairs = AddRingClusterCliques(softbodyChildren, feModel, ringOwner);
        declaredPairs.UnionWith(cliquePairs);
        foreach (var rod in surplus)
        {
            if (rod.NodeA < 0 || rod.NodeA >= controlNames.Length
            || rod.NodeB < 0 || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            var pair = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (cliquePairs.Contains(pair) && IsBandedRod(rod))
            {
                continue;
            }

            var tie = clusterTies.Contains(pair) || (ringTies.Contains(pair) && IsBandedRod(rod));
            if (!Nameable(rod.NodeA, tie) || !Nameable(rod.NodeB, tie))
            {
                continue;
            }

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if ((FeModel.IsProxyNodeName(name0) && !chainRingNodes.Contains(rod.NodeA))
                || (FeModel.IsProxyNodeName(name1) && !chainRingNodes.Contains(rod.NodeB)))
            {
                continue;
            }

            declaredPairs.Add(pair);
            if (tie)
            {
                softbodyChildren.Add(MakeClothSelfCollisionCluster(
                    NodeNameSafe($"cluster_{name0}_{name1}"), [name0, name1],
                    rod.MinDist / 2f, rod.MaxDist / 2f));
                continue;
            }

            // A two-member cluster compiles its rod with the members reversed, so the rod's second node is listed first.
            if (IsUnrecordedClusterRod(feModel, rod))
            {
                softbodyChildren.Add(MakeClothSelfCollisionCluster(
                    NodeNameSafe($"cluster_{name1}_{name0}"), [name1, name0],
                    rod.MinDist / 2f, rod.MaxDist / 2f));
                continue;
            }

            var copy = occurrence.GetValueOrDefault((rod.NodeA, rod.NodeB));
            occurrence[(rod.NodeA, rod.NodeB)] = copy + 1;
            var springLabel = NodeNameSafe(copy == 0
                ? $"rod_{name0}_{name1}" : $"rod_{name0}_{name1}_{copy}");
            softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                rod.RelaxationFactor));
        }

        return declaredPairs;
    }

    // The proxy-sheet phase's own AddClothProxySprings skips every rod touching an independent chain
    // joint (that pairing is a chain's job), but a chain's own generated spans (see
    // FeModel.ChainGeneratedSpans) only ever cover ITS OWN joints - a rod between two joints of two
    // DIFFERENT chains is never regenerated by anything in that phase and was dropped outright before
    // this. Unlike AddClothChainSurplusRods' plain ClothSpring, this emits a ClothSelfCollisionCluster
    // (see MakeClothSelfCollisionCluster), which adds no m_SourceElems entry.
    //
    // A cluster's compiled rod always carries the builder's own fixed relax and weight of 1.0 and 0.5,
    // neither an authorable cluster input (same as ClothSpring's, see MakeClothSpring). A rod without that
    // signature is left unemitted rather than re-declared as a ClothSpring, which would compile the
    // m_SourceElems entry a cluster-derived rod never has.
    static void AddClothChainSurplusClusters(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var controlNames = feModel.CtrlNames;
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // A pair with more than one raw entry is skipped unless it is a cluster tie beside a chain span.
        var rodCounts = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            rodCounts[key] = rodCounts.GetValueOrDefault(key) + 1;
        }

        var surplus = feModel.GetUngeneratedRods(chains);
        var clusterTies = ClusterTiesBesideChainSpans(feModel, surplus);
        var ringOwner = new Dictionary<int, int>();
        foreach (var joint in chains.SelectMany(static chain => chain.Joints))
        {
            foreach (var ring in joint.RingNodes)
            {
                ringOwner[ring] = joint.Node;
            }
        }

        AddRingClusterCliques(softbodyChildren, feModel, ringOwner);
        foreach (var rod in surplus)
        {
            if (rod.NodeA < 0 || rod.NodeA >= controlNames.Length
            || rod.NodeB < 0 || rod.NodeB >= controlNames.Length)
            {
                continue;
            }

            if (!chainJoints.Contains(rod.NodeA) || !chainJoints.Contains(rod.NodeB))
            {
                continue;
            }

            var pairKey = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (rodCounts.GetValueOrDefault(pairKey) > 1 && !clusterTies.Contains(pairKey))
            {
                continue;
            }

            if (rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f)
            {
                continue;
            }

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if (FeModel.IsProxyNodeName(name0) || FeModel.IsProxyNodeName(name1))
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSelfCollisionCluster(
                NodeNameSafe($"cluster_{name0}_{name1}"), [name0, name1],
                rod.MinDist / 2f, rod.MaxDist / 2f));
        }
    }

    /// <summary>
    /// A composed ModelDoc node name the compiler will keep. A softbody child whose <c>name</c> carries a
    /// <c>$</c> ANYWHERE is discarded silently - no error, and the compile still reports success - so a name
    /// built out of control names, every generated one of which carries one, has to drop it. The members a
    /// cluster or spring NAMES are unaffected: <c>joint_name</c> resolves a <c>$cc</c> ring node fine.
    /// <para>
    /// Measured on VRF's own emitted document for synth row <c>w37wt_probe_ring2_cluster_span</c>: five arms
    /// differing in this field alone, compiled into one namespace. The two with no <c>$</c> compiled the
    /// cluster's rod (16 rods, the banded 12/48 on the ring pair); the three with one, leading, middle or
    /// trailing, compiled 15 and dropped it. A 43-character name with no <c>$</c> compiled, so it is the
    /// character and not the length.
    /// </para>
    /// </summary>
    static string NodeNameSafe(string name)
        => name.Contains('$', StringComparison.Ordinal)
            ? name.Replace("$", string.Empty, StringComparison.Ordinal)
            : name;

    /// <summary>
    /// Whether a surplus rod is a two-member <c>ClothSelfCollisionCluster</c>'s own rod rather than a spring's: the only
    /// rod on its pair, at the cluster's fixed relaxation of 1.0 and weight of 0.5, with no two-corner source element
    /// on the pair in the original, and a length that is not the pair's rest distance (the summed member radii,
    /// banded or not). A <c>ClothSpring</c> always records that source element, so its absence rules the spring out.
    /// </summary>
    internal static bool IsUnrecordedClusterRod(FeModel feModel, FeModel.Rod rod)
    {
        if (rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f
            || Array.IndexOf(feModel.SourceSprings, (rod.NodeA, rod.NodeB)) >= 0
            || Array.IndexOf(feModel.SourceSprings, (rod.NodeB, rod.NodeA)) >= 0)
        {
            return false;
        }

        var onPair = 0;
        foreach (var other in feModel.Rods)
        {
            if ((other.NodeA == rod.NodeA && other.NodeB == rod.NodeB) || (other.NodeA == rod.NodeB && other.NodeB == rod.NodeA))
            {
                onPair++;
            }
        }

        var poses = feModel.InitPosePositions;
        if (onPair != 1 || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
        {
            return false;
        }

        var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
        return IsBandedRod(rod) || MathF.Abs(rod.MaxDist - rest) > MathF.Max(1e-3f, 1e-4f * rest);
    }

    /// <summary>
    /// Emits one <c>ClothSelfCollisionCluster</c> per clique of three or more extruded ring nodes, owned by at least two
    /// different chain joints, whose every pair carries exactly one banded rod at relaxation 1 and weight 0.5 off its rest
    /// length, where those bands solve as per-member radii: a cluster compiles a rod on every member pair, its minimum the
    /// two members' collision radii summed and its maximum their stray radii summed. No chain span is banded off its rest
    /// length, so the clique is read off every shipped rod. Returns the pairs the clusters cover.
    /// </summary>
    internal static HashSet<(int, int)> AddRingClusterCliques(KVObject softbodyChildren, FeModel feModel, Dictionary<int, int> ringOwner)
    {
        var covered = new HashSet<(int, int)>();
        var poses = feModel.InitPosePositions;
        var bandedOnPair = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            if (IsBandedRod(rod))
            {
                var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
                bandedOnPair[key] = bandedOnPair.GetValueOrDefault(key) + 1;
            }
        }

        var band = new Dictionary<(int, int), (float Min, float Max)>();
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (rod.NodeA == rod.NodeB || !IsBandedRod(rod) || rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f
                || !ringOwner.ContainsKey(rod.NodeA) || !ringOwner.ContainsKey(rod.NodeB)
                || bandedOnPair.GetValueOrDefault(key) != 1 || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length
                || MathF.Abs(rod.MaxDist - Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB])) <= ClusterRestTolerance)
            {
                continue;
            }

            band[key] = (rod.MinDist, rod.MaxDist);
            (neighbours.TryGetValue(key.Item1, out var na) ? na : neighbours[key.Item1] = []).Add(key.Item2);
            (neighbours.TryGetValue(key.Item2, out var nb) ? nb : neighbours[key.Item2] = []).Add(key.Item1);
        }

        foreach (var clique in MaximalCliques(neighbours))
        {
            if (clique.Count < 3 || clique.Select(node => ringOwner[node]).Distinct().Count() < 2
                || SolveMemberRadii(clique, band) is not var (radii, strayRadii))
            {
                continue;
            }

            var names = clique.Select(node => feModel.CtrlNames[node]).ToList();
            softbodyChildren.Add(MakeClothSelfCollisionCluster(NodeNameSafe($"cluster_{string.Join("_", names)}"), names,
                radii[0], strayRadii[0], radii: radii, strayRadii: strayRadii));
            foreach (var a in clique)
            {
                foreach (var b in clique)
                {
                    if (a < b)
                    {
                        covered.Add((a, b));
                    }
                }
            }
        }

        return covered;
    }

    // How far a cluster band's maximum has to sit from the pair's rest length before it is read as summed stray radii.
    const float ClusterRestTolerance = 1e-3f;

    // Maximal cliques of an undirected graph, each in ascending node order.
    static List<List<int>> MaximalCliques(Dictionary<int, HashSet<int>> neighbours)
    {
        var cliques = new List<List<int>>();

        void Extend(List<int> clique, HashSet<int> candidates, HashSet<int> excluded)
        {
            if (candidates.Count == 0 && excluded.Count == 0)
            {
                cliques.Add([.. clique.Order()]);
                return;
            }

            foreach (var node in candidates.Order().ToList())
            {
                var around = neighbours[node];
                Extend([.. clique, node], [.. candidates.Where(around.Contains)], [.. excluded.Where(around.Contains)]);
                candidates.Remove(node);
                excluded.Add(node);
            }
        }

        Extend([], [.. neighbours.Keys], []);
        return cliques;
    }

    // Per-member collision and stray radii that sum to every pair's band, read off one triangle and checked on every
    // pair; null where no such split exists.
    static (float[] Radii, float[] StrayRadii)? SolveMemberRadii(List<int> members,
        Dictionary<(int, int), (float Min, float Max)> band)
    {
        (float Min, float Max) Band(int a, int b) => band[a < b ? (a, b) : (b, a)];

        var radii = new float[members.Count];
        var strayRadii = new float[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var j = (i + 1) % members.Count;
            var k = (i + 2) % members.Count;
            var (minIj, maxIj) = Band(members[i], members[j]);
            var (minIk, maxIk) = Band(members[i], members[k]);
            var (minJk, maxJk) = Band(members[j], members[k]);
            radii[i] = (minIj + minIk - minJk) / 2f;
            strayRadii[i] = (maxIj + maxIk - maxJk) / 2f;
            if (radii[i] < 0f || strayRadii[i] < radii[i])
            {
                return null;
            }
        }

        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                var (min, max) = Band(members[i], members[j]);
                if (MathF.Abs(radii[i] + radii[j] - min) > ClusterRadiusTolerance * MathF.Max(1f, min)
                    || MathF.Abs(strayRadii[i] + strayRadii[j] - max) > ClusterRadiusTolerance * MathF.Max(1f, max))
                {
                    return null;
                }
            }
        }

        return (radii, strayRadii);
    }

    // How closely the solved radii have to reproduce every band of the clique, relative to the band.
    const float ClusterRadiusTolerance = 1e-4f;

    /// <summary>Whether a rod's length band is open: a cluster's separation constraint rather than a span.</summary>
    static bool IsBandedRod(FeModel.Rod rod)
        => MathF.Abs(rod.MinDist - rod.MaxDist) > 1e-4f * MathF.Max(1f, MathF.Abs(rod.MaxDist));

    /// <summary>
    /// Returns the node pairs whose one banded rod is a two-member <c>ClothSelfCollisionCluster</c> over the
    /// extruded ring nodes of two DIFFERENT chain joints, beside the chain's rigid span on the same pair.
    /// <para>
    /// <see cref="ClusterTiesBesideChainSpans"/> cannot see these: it requires the pair to hold exactly ONE
    /// surplus rod, which assumes <see cref="FeModel.GetUngeneratedRods"/> gave the chain the rigid entry,
    /// and a chain whose spans that model does not predict leaves BOTH on the pair. Ring-ring spans are the
    /// population where that happens, so two other conditions have to do the work instead.
    /// </para>
    /// <para>
    /// FIRST, the rings belong to different joints. A banded rod between two rings of ONE joint is that
    /// joint's own ring rod under an <c>antishrink</c> below one, which the emitted chain regenerates:
    /// declaring a cluster there duplicates it. Measured on the 30 dota documents a band test alone reached,
    /// 21 of which were EXACT or EQUIVALENT and every one of which gained <c>m_Rods</c> without this
    /// condition. SECOND, the band's maximum is not the pair's rest length, which is
    /// <see cref="FeModel.IsRadiusBandTriangle"/>'s test applied to a pair: a cluster's maximum is its
    /// members' summed stray radii and has nothing to do with how far apart they sit.
    /// </para>
    /// </summary>
    static HashSet<(int, int)> RingClusterTies(FeModel feModel, List<FeModel.Rod> surplus,
        Dictionary<int, int> ringOwner)
    {
        var ties = new HashSet<(int, int)>();
        if (ringOwner.Count == 0)
        {
            return ties;
        }

        var entries = new Dictionary<(int, int), int>();
        var banded = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            entries[key] = entries.GetValueOrDefault(key) + 1;
            if (IsBandedRod(rod))
            {
                banded[key] = banded.GetValueOrDefault(key) + 1;
            }
        }

        var poses = feModel.InitPosePositions;
        foreach (var rod in surplus)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            if (!ringOwner.TryGetValue(rod.NodeA, out var ownerA)
                || !ringOwner.TryGetValue(rod.NodeB, out var ownerB) || ownerA == ownerB
                || entries.GetValueOrDefault(key) < 2 || banded.GetValueOrDefault(key) != 1
                || !IsBandedRod(rod) || rod.RelaxationFactor != 1f || rod.Weight0 != 0.5f
                || rod.NodeA >= poses.Length || rod.NodeB >= poses.Length)
            {
                continue;
            }

            var rest = Vector3.Distance(poses[rod.NodeA], poses[rod.NodeB]);
            if (MathF.Abs(rod.MaxDist - rest) <= MathF.Max(1e-3f, 1e-4f * rest))
            {
                continue;
            }

            ties.Add(key);
        }

        return ties;
    }

    /// <summary>
    /// Returns the node pairs whose one surplus rod is a two-member self-collision cluster's tie beside the
    /// chain span on the same pair: the pair carries several entries, every one but that rod is rigid, and
    /// that rod is banded with the cluster's fixed relaxation of 1.0 and weight of 0.5.
    /// <see cref="FeModel.GetUngeneratedRods"/> gives the chain the rigid entry, so the banded one left over
    /// is the cluster's own and never the chain's.
    /// </summary>
    static HashSet<(int, int)> ClusterTiesBesideChainSpans(FeModel feModel, List<FeModel.Rod> surplus)
    {
        static (int, int) PairOf(FeModel.Rod rod)
            => rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);

        var entries = new Dictionary<(int, int), int>();
        var banded = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = PairOf(rod);
            entries[key] = entries.GetValueOrDefault(key) + 1;
            if (IsBandedRod(rod))
            {
                banded[key] = banded.GetValueOrDefault(key) + 1;
            }
        }

        var surplusCounts = new Dictionary<(int, int), int>();
        foreach (var rod in surplus)
        {
            var key = PairOf(rod);
            surplusCounts[key] = surplusCounts.GetValueOrDefault(key) + 1;
        }

        var ties = new HashSet<(int, int)>();
        foreach (var rod in surplus)
        {
            var key = PairOf(rod);
            if (entries.GetValueOrDefault(key) > 1 && banded.GetValueOrDefault(key) == 1
                && surplusCounts[key] == 1 && IsBandedRod(rod)
                && rod.RelaxationFactor == 1f && rod.Weight0 == 0.5f)
            {
                ties.Add(key);
            }
        }

        return ties;
    }

    /// <summary>
    /// Re-declares each recovered <see cref="FeModel.SelfCollisionCluster"/> as one
    /// <c>ClothSelfCollisionCluster</c> listing every member. The compiler rebuilds the whole pairwise rod
    /// set from it and records no source element for any of them, which one spring per pair would.
    /// <para>
    /// Returns the member nodes, which the caller must keep out of the lone-node and lone-chain emitters:
    /// the cluster registers them on its own.
    /// </para>
    /// </summary>
    static HashSet<int> AddClothSelfCollisionClusters(KVObject softbodyChildren, FeModel feModel,
        HashSet<string> clothBones)
    {
        var covered = new HashSet<int>();
        var names = feModel.CtrlNames;
        var index = 0;
        foreach (var cluster in feModel.SelfCollisionClusters)
        {
            var members = new List<string>(cluster.Nodes.Length);
            foreach (var node in cluster.Nodes)
            {
                if (node < 0 || node >= names.Length || feModel.IsGeneratedNodeName(names[node]))
                {
                    members.Clear();
                    break;
                }

                members.Add(names[node]);
            }

            if (members.Count != cluster.Nodes.Length)
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSelfCollisionCluster(
                $"cluster_{index.ToString(CultureInfo.InvariantCulture)}", members,
                cluster.MinDist / 2f, cluster.MaxDist / 2f, cluster.Stiffness));
            clothBones.UnionWith(members);
            covered.UnionWith(cluster.Nodes);
            index++;
        }

        return covered;
    }

    /// <summary>
    /// Re-declares the authored two-corner source elements (<see cref="FeModel.SourceSprings"/>) as
    /// explicit springs. Neither the surface nor a chain regenerates these, and the compiler records one
    /// source element per spring, so a model exported without them comes back short both a rod and a
    /// source element per pair. Endpoints are named verbatim, <c>$cc</c> proxies included - those are
    /// valid ClothSpring endpoints even though they are not chain joints.
    /// </summary>
    static HashSet<(int, int)> AddClothSourceSprings(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var emitted = new HashSet<(int, int)>();
        var names = feModel.CtrlNames;
        var rodByEdge = new Dictionary<(int, int), FeModel.Rod>();
        foreach (var rod in feModel.Rods)
        {
            rodByEdge.TryAdd(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA), rod);
        }

        foreach (var (a, b, copies) in feModel.GetAuthoredSourceSprings(chains))
        {
            if (a < 0 || a >= names.Length || b < 0 || b >= names.Length)
            {
                continue;
            }

            if (!rodByEdge.TryGetValue(a < b ? (a, b) : (b, a), out var rod))
            {
                continue;
            }

            // The source element keeps the two corners in the order the spring named them, which the rod's
            // own endpoint order does not.
            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[a], names[b], rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor, copies - 1));
            emitted.Add(a < b ? (a, b) : (b, a));
        }

        return emitted;
    }
}
