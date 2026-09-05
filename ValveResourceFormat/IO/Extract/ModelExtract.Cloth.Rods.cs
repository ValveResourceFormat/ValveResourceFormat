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
        out bool generatesBendOnlyRods, out float addCurvature, out HashSet<int> suspenderNodes)
    {
        suspenderNodes = [];
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

        if (regenerable)
        {
            derived.UnionWith(beyondSurface);
        }
        else if (ClothMixedSurfaceRods(feModel, surfaceFaces, beyondSurface) is
            { Bend.Count: > 0 } mixed)
        {
            generatesBendRods = mixed.Bounded;
            generatesBendOnlyRods = !mixed.Bounded;
            addCurvature = mixed.AddCurvature;
            suspenderNodes.UnionWith(mixed.Suspenders.SelectMany(static edge => new[] { edge.Item1, edge.Item2 }));
            derived.UnionWith(mixed.Bend);
            derived.UnionWith(mixed.Suspenders);
        }
        else if (ClothSuspenders(feModel, beyondSurface) is var (suspenders, suspenderCurvature)
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
            // and suspender readings have each declined the sheet. The curvature is left alone: the rods
            // this network builds take a fixed bend angle, not the cloth's own curvature.
            var bend = FeModel.BendRodsFromSurface(surfaceFaces, feModel.IsStatic);
            bend.ExceptWith(derived);
            if (bend.Count > 0 && bend.IsSubsetOf(beyondSurface))
            {
                var boundedBend = feModel.Rods.Any(rod => rod.MaxDist < ClothBendOnlyRodMaxDistance
                    && bend.Contains(rod.NodeA < rod.NodeB ? (rod.NodeA, rod.NodeB) : (rod.NodeB, rod.NodeA)));
                generatesBendRods = boundedBend;
                generatesBendOnlyRods = !boundedBend;
                derived.UnionWith(bend);
            }
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

        return derived;
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
    /// </summary>
    static (HashSet<(int, int)> Bend, HashSet<(int, int)> Suspenders, float AddCurvature, bool Bounded)?
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

        var (suspenders, suspenderCurvature) = ClothSuspenders(feModel, rest);
        if (bend.Count == 0 || suspenders.Count != rest.Count)
        {
            return null;
        }

        var curvature = ClothCurvatureFromSurface(feModel, surfaceFaces, bend);
        if (suspenders.Count > 0)
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

        return (bend, suspenders, curvature, bounded);
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
    /// </summary>
    static (HashSet<(int, int)> Suspenders, float AddCurvature) ClothSuspenders(FeModel feModel,
        HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || feModel.HasAxialEdges)
        {
            return ([], 0f);
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
        // back. A curvature of zero is the one value the paint alone already reproduces, and taking the
        // branch for it would replace the sheet's own chain curvature with nothing.
        //
        // The whole set has to agree AND account for every rod reaching past the faces: the compiler's own
        // pass walks the authored proxy vertices while this recovers only the ones that became nodes, so
        // where the two differ the pass pairs the sheet up differently and rebuilds only part of what it
        // shipped. A set with leftovers is exactly that case, and it keeps every spring it has.
        var curvature = DominantReading(shaped.Select(static s => s.Reading), out var agreeing);
        if (curvature <= 0f || agreeing != shaped.Count || shaped.Count != beyondSurface.Count)
        {
            return ([], 0f);
        }

        var ring = feModel.ChainRingCurvature;
        if (ring > 0f && MathF.Abs(ring - curvature) > FeModel.ChainRingCurvatureAgreement * MathF.Max(ring, curvature))
        {
            return ([], 0f);
        }

        var suspenders = new HashSet<(int, int)>();
        foreach (var (edge, reading) in shaped)
        {
            if (MathF.Abs(reading - curvature) <= FeModel.ChainRingCurvatureAgreement * MathF.Max(reading, curvature))
            {
                suspenders.Add(edge);
            }
        }

        return (suspenders, curvature);
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

        ClothRodsFromSurface(feModel, ClothProxyMeshesToExtract, out _, out _, out _, out var suspenderNodes);
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

        var opened = new List<float>();
        var capped = new List<float>();
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
                }
            }

            if (closest > 0.005f * MathF.Max(1f, coplanar))
            {
                continue;
            }

            var reach = (flat * flat) - (folded * folded);
            var span = rod.MinDist >= rest - (2e-4f * MathF.Max(1f, rest)) ? rest : rod.MinDist;
            var fraction = Math.Clamp(((span * span) - (folded * folded)) / reach, 0f, 1f);
            (span == rest ? capped : opened).Add(fraction);
        }

        // The half-angle sine squared is what the minimum length is linear in, so the rods are clustered
        // in that before the value is read off - the angle itself is arbitrarily sensitive near either end.
        opened.Sort();
        var agreed = 0;
        var consensus = 0f;
        for (var i = 0; i < opened.Count; i++)
        {
            var j = i;
            while (j < opened.Count && opened[j] <= opened[i] + 1e-3f)
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
