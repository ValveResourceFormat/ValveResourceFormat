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
    static KVObject MakeClothSelfCollisionCluster(string name, string joint0, string joint1, float radius,
        float strayRadius)
    {
        KVObject MakeJoint(string jointName)
        {
            var joint = KVObject.Collection();
            joint.Add("joint_name", jointName);
            joint.Add("collision_radius", radius);
            joint.Add("stray_radius", strayRadius);
            joint.Add("stiffness", 1.0f);
            return joint;
        }

        var joints = KVObject.Array();
        joints.Add(MakeJoint(joint0));
        joints.Add(MakeJoint(joint1));

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
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
    static HashSet<(int, int)> ClothRodsFromSurface(FeModel feModel,
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
        else if (ClothMixedSurfaceRods(feModel, surfaceFaces, beyondSurface) is
            { Bend.Count: > 0 } mixed)
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

        // Whatever single value the arms above settled on, the fold each hinge actually carries is that
        // value plus its own two vertices' paint. Where one value already accounts for the sheet the
        // residual solves to nothing and no stream is written; where it cannot, the paint carries the
        // rest. A rigid-edge sheet is left alone: there the compiler folds nothing and the same per-vertex
        // number drives the Kelager ring bends instead.
        if (bendStiffness <= 0f && bendNetwork.Count > 0 && !feModel.HasAxialEdges
            && (generatesBendRods || generatesBendOnlyRods))
        {
            bendStiffnessByNode = ClothBendStiffnessFromHinges(feModel, surfaceFaces, bendNetwork,
                addCurvature > 0f ? addCurvature : feModel.ChainRingCurvature);
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
                feModel.InitPosePositions, feModel.IsStatic));
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
    /// per vertex out of the hinges themselves (see <see cref="ClothBendStiffnessFromHinges"/>).
    /// </para>
    /// </summary>
    float[]? ClothBendStiffnessPaint(FeModel.ProxyMesh proxy)
    {
        if (physAggregateData?.FeModel is not { } feModel || !proxy.UsesAuthoredFaces)
        {
            return null;
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
        foreach (var (_, fraction, isCapped, _) in ClothHingeReadings(feModel, faces, beyondSurface))
        {
            (isCapped ? capped : opened).Add(fraction);
        }

        return (opened, capped);
    }

    // The same readings keyed by the HINGE each rod was folded about, which is what the per-vertex paint is
    // solved over: the compiler's angle is per hinge, not per rod, so two rods across one hinge state one
    // value and rods across different hinges state different ones.
    static List<((int, int) Hinge, float Fraction, bool Capped, float Error)> ClothHingeReadings(
        FeModel feModel, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var positions = feModel.InitPosePositions;
        var hinges = new Dictionary<(int, int), List<int[]>>();
        var touching = new Dictionary<int, List<int[]>>();
        foreach (var face in faces)
        {
            for (var i = 0; i < face.Length; i++)
            {
                var a = face[i];
                var b = face[(i + 1) % face.Length];
                var hinge = a < b ? (a, b) : (b, a);
                (hinges.TryGetValue(hinge, out var sharing) ? sharing : hinges[hinge] = []).Add(face);
                (touching.TryGetValue(a, out var around) ? around : touching[a] = []).Add(face);
            }
        }

        var readings = new List<((int, int) Hinge, float Fraction, bool Capped, float Error)>();
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
            foreach (var hinge in HingesAround(touching, rod.NodeA))
            {
                if (hinge.Item1 == edge.Item1 || hinge.Item1 == edge.Item2
                    || hinge.Item2 == edge.Item1 || hinge.Item2 == edge.Item2
                    || !hinges[hinge].Any(face => face.Contains(rod.NodeB)))
                {
                    continue;
                }

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
            readings.Add((about, fraction, span == rest, closest));
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
    {
        var readings = ClothHingeReadings(feModel, faces, network);
        float StatedSum(float fraction)
            => (4f / MathF.PI * MathF.Asin(MathF.Sqrt(fraction))) - (2f * addCurvature);

        // Two rods across one hinge were folded through one angle, so where they read differently the
        // hinge one of them was matched to is not the hinge the compiler folded it about. The better fit
        // is the reading whose flat span reproduces its rod's own maximum length more closely.
        var best = new Dictionary<(int, int), (float Fraction, bool Capped, float Error)>();
        foreach (var (hinge, fraction, capped, error) in readings)
        {
            if (!best.TryGetValue(hinge, out var stated) || error < stated.Error)
            {
                best[hinge] = (fraction, capped, error);
            }
        }

        var exact = new Dictionary<(int, int), float>();
        var bounds = new Dictionary<(int, int), float>();
        foreach (var (hinge, reading) in best)
        {
            (reading.Capped ? bounds : exact)[hinge] = StatedSum(reading.Fraction);
        }

        // A rod already at its own rest span states a bound whichever hinge it was matched to, and the
        // hinge has to satisfy the greatest of them or that rod comes back short of its cap.
        foreach (var (hinge, fraction, capped, _) in readings)
        {
            var least = capped ? StatedSum(fraction) : 0f;
            if (capped && (!bounds.TryGetValue(hinge, out var known) || least > known))
            {
                bounds[hinge] = least;
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

    // How far two hinges' stated sums may sit apart and still count as the same paint, in the
    // paint's own units: a hundredth of the half turn a full sum of two folds a hinge through.
    const float ClothBendStiffnessAgreement = 0.02f;

    // How many times a hinge stating only a lower bound may raise its own two vertices before the
    // solve gives up. Each pass satisfies every bound it can, so a chain of them settles in a few.
    const int ClothBendStiffnessRepairPasses = 8;

    static IEnumerable<(int, int)> HingesAround(Dictionary<int, List<int[]>> touching, int node)
    {
        if (!touching.TryGetValue(node, out var around))
        {
            yield break;
        }

        foreach (var face in around)
        {
            for (var i = 0; i < face.Length; i++)
            {
                var a = face[i];
                var b = face[(i + 1) % face.Length];
                yield return a < b ? (a, b) : (b, a);
            }
        }
    }

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

    // Rods the chains do not rebuild themselves (extra copies of a parent span) are re-declared here.
    static void AddClothChainSurplusRods(KVObject softbodyChildren, FeModel feModel,
        List<FeModel.BoneChain> chains)
    {
        var controlNames = feModel.CtrlNames;

        // Only a bone some emitted chain actually claims as a joint is registered as a cloth node, and so
        // only such a bone can anchor a spring. A cloth-flagged bone that no chain covers (a chain's own
        // parent one hop above its root, say) resolves to nothing and fails the whole compile with
        // "Cannot find Fx Bone".
        var chainJoints = chains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();

        // One spring per surplus rod OCCURRENCE, numbered like AddFreeClothNodesAndSprings' copies.
        var occurrence = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.GetUngeneratedRods(chains))
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

            var name0 = controlNames[rod.NodeA];
            var name1 = controlNames[rod.NodeB];
            if (FeModel.IsProxyNodeName(name0) || FeModel.IsProxyNodeName(name1))
            {
                continue;
            }

            var copy = occurrence.GetValueOrDefault((rod.NodeA, rod.NodeB));
            occurrence[(rod.NodeA, rod.NodeB)] = copy + 1;
            var springLabel = copy == 0 ? $"rod_{name0}_{name1}" : $"rod_{name0}_{name1}_{copy}";
            softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                rod.RelaxationFactor));
        }
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

        // GetUngeneratedRods decides which of several same-pair rod entries counts as "generated" by
        // array order rather than by value, so a pair carrying both a chain-adjacent rod and a
        // separate cluster-pairwise rod can have the two attributed backwards. A pair with more than
        // one raw entry is that ambiguous case and is skipped.
        var rodCounts = new Dictionary<(int, int), int>();
        foreach (var rod in feModel.Rods)
        {
            var key = rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA);
            rodCounts[key] = rodCounts.GetValueOrDefault(key) + 1;
        }

        foreach (var rod in feModel.GetUngeneratedRods(chains))
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
            if (rodCounts.GetValueOrDefault(pairKey) > 1)
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

            softbodyChildren.Add(MakeClothSelfCollisionCluster($"cluster_{name0}_{name1}", name0, name1,
                rod.MinDist / 2f, rod.MaxDist / 2f));
        }
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

        foreach (var (a, b) in feModel.GetAuthoredSourceSprings(chains))
        {
            if (a < 0 || a >= names.Length || b < 0 || b >= names.Length)
            {
                continue;
            }

            if (!rodByEdge.TryGetValue(a < b ? (a, b) : (b, a), out var rod))
            {
                continue;
            }

            softbodyChildren.Add(MakeClothSpring($"spring_{a}_{b}", names[rod.NodeA], names[rod.NodeB], rod.MinDist,
                rod.MaxDist, rod.RelaxationFactor));
            emitted.Add(a < b ? (a, b) : (b, a));
        }

        return emitted;
    }
}
