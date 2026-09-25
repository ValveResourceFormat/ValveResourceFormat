using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// The rods the compiler rebuilds from the exported sheets on its own, which are not declared as springs, with the bend
    /// switches, curvature, suspender nodes and bend-stiffness paint that rebuild them.
    /// </summary>
    internal sealed record ClothSurfaceRods(HashSet<(int, int)> Derived, bool GeneratesBendRods, bool GeneratesBendOnlyRods,
        float AddCurvature, HashSet<int> SuspenderNodes, float BendStiffness, Dictionary<int, float>? BendStiffnessByNode);

    private ClothSurfaceRods? surfaceRodsCache;

    /// <summary>The <see cref="ClothRodsFromSurface"/> reading of <see cref="ProxyMeshes"/>, taken once.</summary>
    private ClothSurfaceRods SurfaceRods(FeModel feModel) => surfaceRodsCache ??= ClothRodsFromSurface(feModel, ProxyMeshes);

    /// <summary>Reads the rods the compiler rebuilds from the exported <paramref name="proxies"/> on its own.</summary>
    internal static ClothSurfaceRods ClothRodsFromSurface(FeModel feModel,
        List<(string FileName, string Name, FeModel.ProxyMesh Proxy)> proxies)
    {
        var suspenderNodes = new HashSet<int>();
        var bendStiffness = 0f;
        Dictionary<int, float>? bendStiffnessByNode = null;
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
            var edge = RodPair(rod);
            if (surfaceNodes.Contains(edge.Item1) && surfaceNodes.Contains(edge.Item2) && !derived.Contains(edge))
            {
                beyondSurface.Add(edge);
            }
        }

        // The bend switches regenerate the rods beyond the faces only where every one of them spans two surface steps.
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var (a, b) in derived)
        {
            GetOrAdd(neighbours, a).Add(b);
            GetOrAdd(neighbours, b).Add(a);
        }

        var regenerable = beyondSurface.Count > 0 && beyondSurface.All(edge =>
            neighbours.TryGetValue(edge.Item1, out var near)
            && near.Any(step => neighbours.TryGetValue(step, out var beyond) && beyond.Contains(edge.Item2)));

        var boundedBeyondSurface = HasBoundedRod(feModel, beyondSurface);

        // Only the bend-only network leaves the maximum length unbounded.
        var generatesBendOnlyRods = regenerable && !boundedBeyondSurface;
        var generatesBendRods = regenerable && boundedBeyondSurface;

        var addCurvature = regenerable ? ClothCurvatureFromSurface(feModel, surfaceFaces, beyondSurface) : 0f;

        // The pairs the compiler is asked to fold itself, whose hinges can carry a bend-stiffness paint.
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
            // The compiler's own bend network, taken where every rod it builds is one the original carries.
            var bend = FeModel.BendRodsFromSurface(surfaceFaces, feModel.IsStatic);
            bend.ExceptWith(derived);
            if (bend.Count > 0 && bend.IsSubsetOf(beyondSurface))
            {
                var boundedBend = HasBoundedRod(feModel, bend);
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

            var shipped = feModel.Rods.Select(RodPair)
                .ToHashSet();
            var folds = FeModel.BendRodsFromDeclaredFaces(keptFaces, feModel.IsStatic);
            folds.ExceptWith(derived);
            if (folds.Count > 0 && folds.All(fold => shipped.Contains(fold)
                || feModel.IsStatic(fold.Item1) || feModel.IsStatic(fold.Item2)))
            {
                var boundedFolds = HasBoundedRod(feModel, folds);
                generatesBendRods = boundedFolds;
                generatesBendOnlyRods = !boundedFolds;
                addCurvature = ClothCurvatureFromBendNetwork(feModel, keptFaces, folds);
                derived.UnionWith(folds);
            }
        }

        // A uniform reading within half the solver band is no reading.
        if (bendStiffness > 0f && bendStiffness <= ClothBendStiffnessAgreement / 2f)
        {
            bendStiffness = 0f;
        }

        // The fold a single value cannot account for is carried by a per-vertex paint, except on a rigid-edge sheet.
        if (bendStiffness <= 0f && bendNetwork.Count > 0 && !feModel.HasAxialEdges
            && (generatesBendRods || generatesBendOnlyRods))
        {
            (bendStiffnessByNode, addCurvature) = ClothBendStiffnessOverFold(feModel, surfaceFaces, bendNetwork,
                addCurvature, keepsCurvature: suspenderNodes.Count > 0);
        }

        // A model with no surface of its own lets the compiler rebuild rods from its synthesised sheets.
        if (!feModel.HasSurfaceElements)
        {
            foreach (var (_, _, proxyMesh) in proxies)
            {
                var nodeOf = proxyMesh.NodeIndices;
                derived.UnionWith(FeModel.DeriveRodsFromFaces(
                    proxyMesh.Faces.Select(face => face.Select(local => nodeOf[local]).ToArray())));
            }
        }

        // A face kept out of the rod path still gets a rod across the diagonal a bent quad discards.
        foreach (var (_, _, proxyMesh) in proxies)
        {
            var nodeOf = proxyMesh.NodeIndices;
            derived.UnionWith(FeModel.BentQuadRodsFromFaces(
                proxyMesh.Faces
                    .Where(face => !ClothFaceMakesRods(feModel, proxyMesh, face))
                    .Select(face => face.Select(local => nodeOf[local]).ToArray()),
                feModel.InitPosePositions, feModel.IsStatic, feModel.QuadBendTolerance));
        }

        return new ClothSurfaceRods(derived, generatesBendRods, generatesBendOnlyRods, addCurvature, suspenderNodes,
            bendStiffness, bendStiffnessByNode);
    }

    // The cloth_make_rods paint of a sheet kept out of the rod path, under the importer's 0.5 threshold.
    private const float ClothSuppressedMakeRods = 0.4f;

    /// <summary>
    /// Whether the compiler turns <paramref name="face"/> into rods rather than a solve element, by the mean
    /// <c>cloth_make_rods</c> paint <see cref="BuildClothProxyMeshDmx"/> writes over its corners.
    /// </summary>
    private static bool ClothFaceMakesRods(FeModel feModel, FeModel.ProxyMesh proxy, int[] face)
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

    /// <summary>
    /// Splits a sheet's rods beyond its faces into the <c>add_stiffness_rods</c> bend network and suspender rods sharing
    /// one <c>add_curvature</c>, or null where they do not split that way. A saturated suspender set leaves the curvature
    /// at zero and reports the network's fold as <c>BendStiffness</c>.
    /// </summary>
    private static (HashSet<(int, int)> Bend, HashSet<(int, int)> Suspenders, float AddCurvature, float BendStiffness,
        bool Bounded)?
        ClothMixedSurfaceRods(FeModel feModel, List<int[]> surfaceFaces, HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || feModel.HasAxialEdges)
        {
            return null;
        }

        var network = FeModel.BendRodsFromSurface(surfaceFaces, feModel.IsStatic);
        var shipped = new HashSet<(int, int)>();
        foreach (var rod in feModel.Rods)
        {
            shipped.Add(RodPair(rod));
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

        return (bend, suspenders, curvature, bendStiffness, HasBoundedRod(feModel, bend));
    }

    /// <summary>Whether any rod on a pair of <paramref name="pairs"/> has a bounded maximum length.</summary>
    private static bool HasBoundedRod(FeModel feModel, HashSet<(int, int)> pairs)
        => feModel.Rods.Any(rod => rod.MaxDist < FeModel.UnboundedRodDistance && pairs.Contains(RodPair(rod)));

    /// <summary>
    /// The uniform <c>cloth_bend_stiffness</c> of a face-kept sheet, or null where the compiler folds rods across the
    /// model's sheets without <c>rigid_edge_hinges</c>.
    /// </summary>
    internal static float? ClothFaceKeptBendStiffness(FeModel feModel, ClothSurfaceRods surfaceRods)
    {
        if (!feModel.HasAxialEdges && !feModel.HasChainRingBends
            && (surfaceRods.GeneratesBendRods || surfaceRods.GeneratesBendOnlyRods))
        {
            return null;
        }

        return ClothFaceKeptBendStiffnessDefault;
    }

    // The bend paint a face-kept sheet states where nothing folds its fans.
    private const float ClothFaceKeptBendStiffnessDefault = 0.2f;

    /// <summary>
    /// The <c>cloth_bend_stiffness</c> paint of a sheet exported with its own faces, or null when it needs none.
    /// </summary>
    private float[]? ClothBendStiffnessPaint(FeModel.ProxyMesh proxy)
    {
        if (physAggregateData?.FeModel is not { } feModel || !proxy.UsesAuthoredFaces)
        {
            return null;
        }

        if (feModel.HasAxialEdges)
        {
            return feModel.RecoverRigidHingeBendPaint(proxy);
        }

        var rods = SurfaceRods(feModel);
        var bendStiffness = rods.BendStiffness;
        var bendStiffnessByNode = rods.BendStiffnessByNode;
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
    /// The suspender rods among a sheet's rods beyond its faces, and the <c>add_curvature</c> they read: a rigid rod from
    /// a static to a simulated vertex at its rest span, with <c>flMinDist = flMaxDist * sin(add_curvature * pi)</c>. Taken
    /// only where every such rod and the chain rings agree; a set at zero minimum is reported saturated.
    /// </summary>
    private static (HashSet<(int, int)> Suspenders, float AddCurvature, bool Saturated) ClothSuspenders(
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
            var edge = RodPair(rod);
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

        // The whole set has to agree and account for every rod beyond the faces.
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

    // The value the largest subset of readings agrees on to ChainRingCurvatureAgreement, taking the largest such
    // value on a tie, with the size of that subset. Zero when there are none.
    private static float DominantReading(IEnumerable<float> readings, out int agreeing)
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
    /// The <c>cloth_suspenders</c> paint of a proxy sheet, on both ends of every suspender rod, or null where it has none.
    /// </summary>
    private float[]? ClothSuspenderPaint(FeModel.ProxyMesh proxy)
    {
        if (physAggregateData?.FeModel is not { } feModel)
        {
            return null;
        }

        var suspenderNodes = SurfaceRods(feModel).SuspenderNodes;
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
    /// The <c>add_curvature</c> the bend network was folded by: the value most uncapped rods agree on, or the largest
    /// lower bound the capped rods give where too few agree.
    /// </summary>
    private static float ClothCurvatureFromSurface(FeModel feModel, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var (opened, capped) = ClothCurvatureReadings(feModel, faces, beyondSurface);

        // Readings are clustered in sin^2 of the half angle, which the minimum length is linear in.
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

    // How far two rods' readings may sit apart, in sin^2 of the half angle, and still count as one value.
    private const float ClothCurvatureAgreement = 1e-3f;

    /// <summary>
    /// The <c>add_curvature</c> of a regenerated bend network where it states one value: every uncapped rod reads the same
    /// and no capped rod reads more. Zero otherwise.
    /// </summary>
    private static float ClothCurvatureFromBendNetwork(FeModel feModel, List<int[]> faces, HashSet<(int, int)> bend)
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

    // Per rod of the network, the fraction of its fold the compiled minimum sits at, split into exact readings and the
    // lower bounds of rods capped at their rest span.
    private static (List<float> Opened, List<float> Capped) ClothCurvatureReadings(FeModel feModel, List<int[]> faces,
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
}
