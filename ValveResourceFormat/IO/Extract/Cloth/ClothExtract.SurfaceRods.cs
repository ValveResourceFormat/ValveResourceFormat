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
    private ClothSurfaceRods SurfaceRods(ClothReconstruction cloth) => surfaceRodsCache ??= ClothRodsFromSurface(cloth, ProxyMeshes);

    /// <summary>Reads the rods the compiler rebuilds from the exported <paramref name="proxies"/> on its own.</summary>
    internal static ClothSurfaceRods ClothRodsFromSurface(ClothReconstruction cloth,
        List<ClothProxyFile> proxies)
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

            surfaceNodes.UnionWith(proxyMesh.NodeIndices);
            var globalFaces = GlobalFaces(proxyMesh, proxyMesh.Faces).ToList();
            surfaceFaces.AddRange(globalFaces);
            derived.UnionWith(ClothReconstruction.DeriveRodsFromFaces(globalFaces));
        }

        var beyondSurface = new HashSet<(int, int)>();
        foreach (var rod in cloth.Fe.Rods)
        {
            var edge = RodPair(rod);
            if (surfaceNodes.Contains(edge.Item1) && surfaceNodes.Contains(edge.Item2) && !derived.Contains(edge))
            {
                beyondSurface.Add(edge);
            }
        }

        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var (a, b) in derived)
        {
            ClothReconstruction.GetOrAdd(neighbours, a).Add(b);
            ClothReconstruction.GetOrAdd(neighbours, b).Add(a);
        }

        var regenerable = beyondSurface.Count > 0 && beyondSurface.All(edge =>
            neighbours.TryGetValue(edge.Item1, out var near)
            && near.Any(step => neighbours.TryGetValue(step, out var beyond) && beyond.Contains(edge.Item2)));

        var boundedBeyondSurface = HasBoundedRod(cloth, beyondSurface);

        var generatesBendOnlyRods = regenerable && !boundedBeyondSurface;
        var generatesBendRods = regenerable && boundedBeyondSurface;

        var addCurvature = regenerable ? ClothCurvatureFromSurface(cloth, surfaceFaces, beyondSurface) : 0f;

        var bendNetwork = new HashSet<(int, int)>();

        if (regenerable)
        {
            derived.UnionWith(beyondSurface);
            bendNetwork.UnionWith(beyondSurface);
        }
        else if (ClothMixedSurfaceRods(cloth, surfaceFaces, beyondSurface) is { Bend.Count: > 0 } mixed)
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
        else if (ClothSuspenders(cloth, beyondSurface) is var (suspenders, suspenderCurvature, _)
            && suspenders.Count > 0)
        {
            addCurvature = suspenderCurvature;
            suspenderNodes.UnionWith(suspenders.SelectMany(static edge => new[] { edge.Item1, edge.Item2 }));
            derived.UnionWith(suspenders);
        }
        else
        {
            var bend = ClothReconstruction.BendRodsFromSurface(surfaceFaces, cloth.Fe.IsStatic);
            bend.ExceptWith(derived);
            if (bend.Count > 0 && bend.IsSubsetOf(beyondSurface))
            {
                var boundedBend = HasBoundedRod(cloth, bend);
                generatesBendRods = boundedBend;
                generatesBendOnlyRods = !boundedBend;
                addCurvature = ClothCurvatureFromBendNetwork(cloth, surfaceFaces, bend);
                derived.UnionWith(bend);
                bendNetwork.UnionWith(bend);
            }
        }

        if (!generatesBendRods && !generatesBendOnlyRods && cloth.HasSurfaceFolds)
        {
            var keptFaces = new List<int[]>();
            foreach (var (_, _, proxyMesh) in proxies)
            {
                if (!proxyMesh.UsesAuthoredFaces)
                {
                    keptFaces.AddRange(GlobalFaces(proxyMesh, proxyMesh.Faces));
                }
            }

            var shipped = cloth.Fe.Rods.Select(RodPair)
                .ToHashSet();
            var folds = ClothReconstruction.BendRodsFromDeclaredFaces(keptFaces, cloth.Fe.IsStatic);
            folds.ExceptWith(derived);
            if (folds.Count > 0 && folds.All(fold => shipped.Contains(fold)
                || cloth.Fe.IsStatic(fold.Item1) || cloth.Fe.IsStatic(fold.Item2)))
            {
                var boundedFolds = HasBoundedRod(cloth, folds);
                generatesBendRods = boundedFolds;
                generatesBendOnlyRods = !boundedFolds;
                addCurvature = ClothCurvatureFromBendNetwork(cloth, keptFaces, folds);
                derived.UnionWith(folds);
            }
        }

        if (bendStiffness > 0f && bendStiffness <= ClothBendStiffnessAgreement / 2f)
        {
            bendStiffness = 0f;
        }

        if (bendStiffness <= 0f && bendNetwork.Count > 0 && !cloth.Fe.HasAxialEdges
            && (generatesBendRods || generatesBendOnlyRods))
        {
            (bendStiffnessByNode, addCurvature) = ClothBendStiffnessOverFold(cloth, surfaceFaces, bendNetwork,
                addCurvature, keepsCurvature: suspenderNodes.Count > 0);
        }

        AddRodsTheSheetsRebuild(derived, cloth, proxies);
        return new ClothSurfaceRods(derived, generatesBendRods, generatesBendOnlyRods, addCurvature, suspenderNodes,
            bendStiffness, bendStiffnessByNode);
    }

    /// <summary>
    /// Adds the rods the compiler builds from the exported sheets' faces whatever the bend switches: every face rod of a
    /// model with no surface of its own, and the discarded diagonal of every bent quad kept out of the rod path.
    /// </summary>
    private static void AddRodsTheSheetsRebuild(HashSet<(int, int)> derived, ClothReconstruction cloth,
        List<ClothProxyFile> proxies)
    {
        if (!cloth.Fe.HasSurfaceElements)
        {
            foreach (var (_, _, proxyMesh) in proxies)
            {
                derived.UnionWith(ClothReconstruction.DeriveRodsFromFaces(GlobalFaces(proxyMesh, proxyMesh.Faces)));
            }
        }

        foreach (var (_, _, proxyMesh) in proxies)
        {
            derived.UnionWith(ClothReconstruction.BentQuadRodsFromFaces(
                GlobalFaces(proxyMesh, proxyMesh.Faces.Where(face => !ClothFaceMakesRods(cloth, proxyMesh, face))),
                cloth.Fe.InitPosePositions, cloth.Fe.IsStatic, cloth.QuadBendTolerance));
        }
    }

    /// <summary><paramref name="faces"/> of <paramref name="proxy"/> with their corners as control nodes.</summary>
    private static IEnumerable<int[]> GlobalFaces(ProxyMesh proxy, IEnumerable<int[]> faces)
    {
        var nodeOf = proxy.NodeIndices;
        return faces.Select(face => face.Select(local => nodeOf[local]).ToArray());
    }

    /// <summary>
    /// The cloth_make_rods paint of a sheet kept out of the rod path, under the importer's 0.5 threshold.
    /// </summary>
    private const float ClothSuppressedMakeRods = 0.4f;

    /// <summary>
    /// Whether the compiler turns <paramref name="face"/> into rods rather than a solve element, by the mean
    /// <c>cloth_make_rods</c> paint <see cref="BuildClothProxyMeshDmx"/> writes over its corners.
    /// </summary>
    private static bool ClothFaceMakesRods(ClothReconstruction cloth, ProxyMesh proxy, int[] face)
    {
        var vertexCount = proxy.Positions.Length;
        var driven = proxy.RodsDriven.Length == vertexCount;
        if (!driven && (proxy.UsesAuthoredFaces || !cloth.Fe.HasSurfaceElements))
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

    /// <summary>A sheet's bend network and suspender rods sharing one <c>add_curvature</c>, and whether the network is bounded.</summary>
    private sealed record MixedSurfaceRods(HashSet<(int, int)> Bend, HashSet<(int, int)> Suspenders, float AddCurvature,
        float BendStiffness, bool Bounded);

    /// <summary>
    /// Splits a sheet's rods beyond its faces into the <c>add_stiffness_rods</c> bend network and suspender rods sharing
    /// one <c>add_curvature</c>, or null where they do not split that way. A saturated suspender set leaves the curvature
    /// at zero and reports the network's fold as <c>BendStiffness</c>.
    /// </summary>
    private static MixedSurfaceRods? ClothMixedSurfaceRods(ClothReconstruction cloth, List<int[]> surfaceFaces, HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || cloth.Fe.HasAxialEdges)
        {
            return null;
        }

        var network = ClothReconstruction.BendRodsFromSurface(surfaceFaces, cloth.Fe.IsStatic);
        var shipped = new HashSet<(int, int)>();
        foreach (var rod in cloth.Fe.Rods)
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

        var (suspenders, suspenderCurvature, saturated) = ClothSuspenders(cloth, rest);
        if (bend.Count == 0 || suspenders.Count != rest.Count)
        {
            return null;
        }

        var curvature = ClothCurvatureFromSurface(cloth, surfaceFaces, bend);
        var bendStiffness = 0f;
        if (suspenders.Count > 0 && saturated)
        {
            bendStiffness = curvature;
            curvature = 0f;
        }
        else if (suspenders.Count > 0)
        {
            if (curvature > 0f && MathF.Abs(curvature - suspenderCurvature)
                > ClothReconstruction.ChainRingCurvatureAgreement * MathF.Max(curvature, suspenderCurvature))
            {
                return null;
            }

            curvature = suspenderCurvature;
        }

        return new MixedSurfaceRods(bend, suspenders, curvature, bendStiffness, HasBoundedRod(cloth, bend));
    }

    /// <summary>Whether any rod on a pair of <paramref name="pairs"/> has a bounded maximum length.</summary>
    private static bool HasBoundedRod(ClothReconstruction cloth, HashSet<(int, int)> pairs)
        => cloth.Fe.Rods.Any(rod => rod.MaxDist < ClothReconstruction.UnboundedRodDistance && pairs.Contains(RodPair(rod)));

    /// <summary>
    /// The uniform <c>cloth_bend_stiffness</c> of a face-kept sheet, or null where the compiler folds rods across the
    /// model's sheets without <c>rigid_edge_hinges</c>.
    /// </summary>
    internal static float? ClothFaceKeptBendStiffness(ClothReconstruction cloth, ClothSurfaceRods surfaceRods)
    {
        if (!cloth.Fe.HasAxialEdges && !cloth.HasChainRingBends
            && (surfaceRods.GeneratesBendRods || surfaceRods.GeneratesBendOnlyRods))
        {
            return null;
        }

        return ClothFaceKeptBendStiffnessDefault;
    }

    /// <summary>The bend paint a face-kept sheet states where nothing folds its fans.</summary>
    private const float ClothFaceKeptBendStiffnessDefault = 0.2f;

    /// <summary>
    /// The <c>cloth_bend_stiffness</c> paint of a sheet exported with its own faces, or null when it needs none.
    /// </summary>
    private float[]? ClothBendStiffnessPaint(ClothReconstruction cloth, ProxyMesh proxy)
    {
        if (!proxy.UsesAuthoredFaces)
        {
            return null;
        }

        if (cloth.Fe.HasAxialEdges)
        {
            return cloth.RecoverRigidHingeBendPaint(proxy);
        }

        var rods = SurfaceRods(cloth);
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

        return ClothReconstruction.PaintPerVertex(proxy, bendStiffnessByNode.GetValueOrDefault, static value => value > 0f);
    }

    /// <summary>
    /// The suspender rods among a sheet's rods beyond its faces, and the <c>add_curvature</c> they read: a rigid rod from
    /// a static to a simulated vertex at its rest span, with <c>flMinDist = flMaxDist * sin(add_curvature * pi)</c>. Taken
    /// only where every such rod and the chain rings agree; a set at zero minimum is reported saturated.
    /// </summary>
    private static (HashSet<(int, int)> Suspenders, float AddCurvature, bool Saturated) ClothSuspenders(
        ClothReconstruction cloth, HashSet<(int, int)> beyondSurface)
    {
        if (beyondSurface.Count == 0 || cloth.Fe.HasAxialEdges)
        {
            return ([], 0f, false);
        }

        var positions = cloth.Fe.InitPosePositions;
        var invMasses = cloth.Fe.NodeInvMasses;
        var shaped = new List<((int, int) Edge, float Reading)>();
        foreach (var rod in cloth.Fe.Rods)
        {
            var edge = RodPair(rod);
            if (!beyondSurface.Contains(edge)
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

        var curvature = DominantReading(shaped.Select(static s => s.Reading), out var agreeing);
        if (shaped.Count == 0 || agreeing != shaped.Count || shaped.Count != beyondSurface.Count)
        {
            return ([], 0f, false);
        }

        var saturated = curvature <= 0f;
        var ring = cloth.ChainRingCurvature;
        if (saturated
            ? ring > ClothReconstruction.ChainRingCurvatureAgreement
            : ring > 0f && MathF.Abs(ring - curvature)
                > ClothReconstruction.ChainRingCurvatureAgreement * MathF.Max(ring, curvature))
        {
            return ([], 0f, false);
        }

        var suspenders = new HashSet<(int, int)>();
        foreach (var (edge, reading) in shaped)
        {
            if (MathF.Abs(reading - curvature) <= ClothReconstruction.ChainRingCurvatureAgreement * MathF.Max(reading, curvature))
            {
                suspenders.Add(edge);
            }
        }

        return (suspenders, curvature, saturated);
    }

    /// <summary>
    /// The value the largest subset of readings agrees on to ChainRingCurvatureAgreement, taking the largest such value
    /// on a tie, with the size of that subset. Zero when there are none.
    /// </summary>
    private static float DominantReading(IEnumerable<float> readings, out int agreeing)
    {
        var sorted = readings.ToArray();
        Array.Sort(sorted);
        var best = 0f;
        agreeing = 0;
        var low = 0;
        for (var high = 0; high < sorted.Length; high++)
        {
            while (sorted[high] - sorted[low] > ClothReconstruction.ChainRingCurvatureAgreement * sorted[high])
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
    private float[]? ClothSuspenderPaint(ClothReconstruction cloth, ProxyMesh proxy)
    {
        var suspenderNodes = SurfaceRods(cloth).SuspenderNodes;
        if (suspenderNodes.Count == 0)
        {
            return null;
        }

        return ClothReconstruction.PaintPerVertex(proxy, node => suspenderNodes.Contains(node) ? 1f : 0f, static value => value > 0f);
    }

    /// <summary>
    /// The <c>add_curvature</c> the bend network was folded by: the value most uncapped rods agree on, or the largest
    /// lower bound the capped rods give where too few agree.
    /// </summary>
    private static float ClothCurvatureFromSurface(ClothReconstruction cloth, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var (opened, capped) = ClothCurvatureReadings(cloth, faces, beyondSurface);

        opened.Sort();
        var agreed = 0;
        var consensus = 0f;
        var j = 0;
        for (var i = 0; i < opened.Count; i++)
        {
            j = Math.Max(j, i);
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

    /// <summary>
    /// How far two rods' readings may sit apart, in sin^2 of the half angle, and still count as one value.
    /// </summary>
    private const float ClothCurvatureAgreement = 1e-3f;

    /// <summary>
    /// The <c>add_curvature</c> of a regenerated bend network where it states one value: every uncapped rod reads the same
    /// and no capped rod reads more. Zero otherwise.
    /// </summary>
    private static float ClothCurvatureFromBendNetwork(ClothReconstruction cloth, List<int[]> faces, HashSet<(int, int)> bend)
    {
        var (opened, capped) = ClothCurvatureReadings(cloth, faces, bend);
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

    /// <summary>
    /// Per rod of the network, the fraction of its fold the compiled minimum sits at, split into exact readings and the
    /// lower bounds of rods capped at their rest span.
    /// </summary>
    private static (List<float> Opened, List<float> Capped) ClothCurvatureReadings(ClothReconstruction cloth, List<int[]> faces,
        HashSet<(int, int)> beyondSurface)
        => CurvatureFractions(ClothHingeReadings(cloth, faces, beyondSurface));

    /// <summary>The fractions of <paramref name="readings"/>, split into exact readings and capped lower bounds.</summary>
    private static (List<float> Opened, List<float> Capped) CurvatureFractions(List<HingeReading> readings)
    {
        var opened = new List<float>();
        var capped = new List<float>();
        foreach (var (_, fraction, isCapped, _, _) in readings)
        {
            (isCapped ? capped : opened).Add(fraction);
        }

        return (opened, capped);
    }
}
