using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // How far two hinges' stated sums may sit apart and still count as the same paint.
    private const float ClothBendStiffnessAgreement = 0.02f;

    // How many times the bound repair may raise vertices before the solve gives up.
    private const int ClothBendStiffnessRepairPasses = 8;

    // The fraction of its open span a hinge has to fold a rod by to count as generating it.
    private const float ClothHingeMinimumFold = 0.02f;

    // How far a rod's flat span may sit from its closest hinge's open span, relative to the span.
    private const float ClothHingeFitTolerance = 0.005f;

    // How close to its rest span, relative to the span, a rod's minimum reads as capped there.
    private const float ClothRodCapTolerance = 2e-4f;

    private static readonly float[] ClothBendStiffnessCoverTolerances = [1e-5f, 1e-4f, 1e-3f, ClothBendStiffnessAgreement];

    /// <summary>A per-vertex <c>cloth_bend_stiffness</c> keyed by control node, or null for none, and its <c>add_curvature</c>.</summary>
    internal readonly record struct BendPaintSolution(Dictionary<int, float>? Paint, float AddCurvature);

    /// <summary>
    /// A network rod's curvature reading: the hinge it was folded about, the fraction of the fold, whether its minimum is
    /// capped at its rest span, the fit error, and every generating hinge's own reading.
    /// </summary>
    private readonly record struct HingeReading((int, int) Hinge, float Fraction, bool Capped, float Error,
        ((int, int) Hinge, float Fraction)[] Candidates);

    /// <summary>
    /// A hinge-sum solve: the per-vertex paint or null, the sum each hinge states, and the largest rod miss without and
    /// with the paint.
    /// </summary>
    private readonly record struct HingeSolve(Dictionary<int, float>? Paint, Dictionary<(int, int), float> StatedSums,
        float UnpaintedSlack, float SolvedSlack);

    /// <summary>
    /// The per-vertex <c>cloth_bend_stiffness</c> of a regenerated bend network, keyed by control node, and the
    /// <c>add_curvature</c> that goes with it. Each hinge folds by
    /// <c>clamp((paint[u] + paint[v]) * pi / 2 + add_curvature * pi, 0, pi)</c>.
    /// </summary>
    internal static BendPaintSolution ClothBendStiffnessOverFold(ClothReconstruction cloth,
        List<int[]> faces, HashSet<(int, int)> network, float addCurvature, bool keepsCurvature)
    {
        return ClothBendStiffnessOverFold(cloth, faces, network, ClothHingeReadings(cloth, faces, network), addCurvature,
            keepsCurvature);
    }

    /// <summary>
    /// The per-vertex <c>cloth_bend_stiffness</c> of a regenerated bend network and its <c>add_curvature</c>, from the
    /// network's hinge <paramref name="readings"/>.
    /// </summary>
    private static BendPaintSolution ClothBendStiffnessOverFold(ClothReconstruction cloth, List<int[]> faces,
        HashSet<(int, int)> network, List<HingeReading> readings, float addCurvature, bool keepsCurvature)
    {
        // A curvature stated by suspenders or the chain rings is kept rather than solved for
        var curvatureStated = keepsCurvature || cloth.ChainRingCurvature > 0f;
        return ClothPaintCoveringEveryRod(cloth, faces, network, readings, ClothCurvatureMeetsItsCappedRods(readings,
            ClothBendStiffnessRead(cloth, readings, addCurvature, curvatureStated), curvatureStated), curvatureStated);
    }

    /// <summary>
    /// Replaces the <paramref name="settled"/> answer with a covering-hinge solve, at its <c>add_curvature</c> or at zero,
    /// where that rebuilds strictly more network rods.
    /// </summary>
    private static BendPaintSolution ClothPaintCoveringEveryRod(ClothReconstruction cloth,
        List<int[]> faces, HashSet<(int, int)> network, List<HingeReading> readings, BendPaintSolution settled,
        bool curvatureStated)
    {
        var generators = HingeGenerators(faces);
        var best = settled;
        var bestMisses = ClothPaintMisses(cloth, generators, network, settled.Paint, settled.AddCurvature);
        if (bestMisses == 0)
        {
            return settled;
        }

        float[] curvatures = curvatureStated || settled.AddCurvature == 0f
            ? [settled.AddCurvature]
            : [settled.AddCurvature, 0f];
        foreach (var curvature in curvatures)
        {
            if (ClothBendStiffnessCoveringHinges(cloth, readings, curvature) is not { } covered)
            {
                continue;
            }

            var misses = ClothPaintMisses(cloth, generators, network, covered, curvature);
            if (misses < bestMisses)
            {
                (best, bestMisses) = (new BendPaintSolution(covered, curvature), misses);
            }
        }

        return best;
    }

    /// <summary>
    /// The hinges that generate each bend rod of <paramref name="faces"/>, keyed by the rod's node pair, each hinge once
    /// in first-generated order.
    /// </summary>
    private static Dictionary<(int, int), List<(int, int)>> HingeGenerators(List<int[]> faces)
    {
        var generators = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var (hinge, nodeA, nodeB) in ClothReconstruction.BendRodGenerators(faces))
        {
            var about = ClothReconstruction.GetOrAdd(generators, ClothReconstruction.UnorderedPair(nodeA, nodeB));
            if (!about.Contains(hinge))
            {
                about.Add(hinge);
            }
        }

        return generators;
    }

    /// <summary>
    /// Where a rod's ends sit about a hinge: how far along the unit <paramref name="axis"/> from the hinge's first node
    /// each lies, and how far off it.
    /// </summary>
    private static (float AlongA, float AlongB, float RiseA, float RiseB) HingeOffsets(Vector3[] positions, FeModel.Rod rod,
        (int, int) hinge, Vector3 axis)
    {
        var toA = positions[rod.NodeA] - positions[hinge.Item1];
        var toB = positions[rod.NodeB] - positions[hinge.Item1];
        return (Vector3.Dot(toA, axis), Vector3.Dot(toB, axis),
            MathUtils.ProjectOntoPlane(toA, axis).Length(), MathUtils.ProjectOntoPlane(toB, axis).Length());
    }

    /// <summary>
    /// Counts the network rods whose compiled minimum the paint and <c>add_curvature</c> would not rebuild. Each rod takes
    /// the shortest span any of its generating hinges folds it to, capped at its rest span.
    /// </summary>
    private static int ClothPaintMisses(ClothReconstruction cloth, Dictionary<(int, int), List<(int, int)>> generators,
        HashSet<(int, int)> network, Dictionary<int, float>? paint, float addCurvature)
    {
        var positions = cloth.Fe.InitPosePositions;
        var misses = 0;
        foreach (var rod in cloth.Fe.Rods)
        {
            var pair = RodPair(rod);
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

                var (alongA, alongB, riseA, riseB) = HingeOffsets(positions, rod, hinge, Vector3.Normalize(axis));
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
    /// Raises a model-wide <c>add_curvature</c> that no paint, suspender or chain ring states to the bound its capped rods
    /// give.
    /// </summary>
    private static BendPaintSolution ClothCurvatureMeetsItsCappedRods(List<HingeReading> readings, BendPaintSolution read,
        bool curvatureStated)
    {
        if (read.Paint is not null || curvatureStated)
        {
            return read;
        }

        var (_, capped) = CurvatureFractions(readings);
        if (capped.Count == 0)
        {
            return read;
        }

        var stated = MathF.Sin(MathF.PI * read.AddCurvature / 2f);
        var bound = capped.Max();
        return bound > (stated * stated) + ClothCurvatureAgreement
            ? read with { AddCurvature = AddCurvatureFromFold(bound) }
            : read;
    }

    /// <summary>
    /// The bend-stiffness paint and <c>add_curvature</c> read off the hinge <paramref name="readings"/>, before repairing
    /// the rods it leaves unbuilt.
    /// </summary>
    private static BendPaintSolution ClothBendStiffnessRead(ClothReconstruction cloth,
        List<HingeReading> readings, float addCurvature, bool curvatureStated)
    {
        var paint = ClothBendStiffnessFromHinges(cloth, readings,
            addCurvature > 0f ? addCurvature : cloth.ChainRingCurvature, generatorBound: false).Paint;
        if (paint is not null || curvatureStated)
        {
            return new(paint, addCurvature);
        }

        if (addCurvature <= 0f)
        {
            return new(ClothBendStiffnessFromHinges(cloth, readings, 0f, generatorBound: true).Paint
                ?? ClothBendStiffnessFromHinges(cloth, readings, 0f, generatorBound: true, relaxSetters: true).Paint
                ?? ClothBendStiffnessCoveringHinges(cloth, readings, 0f), addCurvature);
        }

        var (residual, residuals, sharedSlack, residualSlack) = ClothBendStiffnessFromHinges(cloth, readings, addCurvature,
            generatorBound: true);
        var overFolds = residuals.Values.Any(static sum => sum < -ClothBendStiffnessAgreement);

        // The paint at zero curvature, where it spreads its hinges apart and leaves less slack than the shared one
        Dictionary<int, float>? RetryAtZero(bool relaxSetters)
        {
            var (retried, folds, _, paintedSlack) = ClothBendStiffnessFromHinges(cloth, readings, 0f, generatorBound: true,
                relaxSetters);
            var spread = folds.Count > 0 ? folds.Values.Max() - folds.Values.Min() : 0f;
            return retried is { Count: > 0 } && spread > ClothBendStiffnessAgreement
                && sharedSlack > paintedSlack + ClothBendStiffnessAgreement
                    ? retried
                    : null;
        }

        if (overFolds && RetryAtZero(relaxSetters: false) is { } whole)
        {
            return new(whole, 0f);
        }

        if (residual is null)
        {
            (residual, _, _, residualSlack) = ClothBendStiffnessFromHinges(cloth, readings, addCurvature, generatorBound: true,
                relaxSetters: true);
        }

        if (residual is null && !overFolds && sharedSlack > ClothBendStiffnessAgreement
            && ClothBendStiffnessCoveringHinges(cloth, readings, addCurvature) is { } coveredResidual)
        {
            return new(coveredResidual, addCurvature);
        }

        if (residual is { Count: > 0 } && sharedSlack > residualSlack + ClothBendStiffnessAgreement)
        {
            return new(residual, addCurvature);
        }

        if (overFolds && RetryAtZero(relaxSetters: true) is { } relaxed)
        {
            return new(relaxed, 0f);
        }

        if (overFolds && sharedSlack > ClothBendStiffnessAgreement
            && ClothBendStiffnessCoveringHinges(cloth, readings, 0f) is { } covered)
        {
            return new(covered, 0f);
        }

        return new(paint, addCurvature);
    }

    /// <summary>The curvature readings of the network rods beyond the faces, one per rod.</summary>
    private static List<HingeReading> ClothHingeReadings(
        ClothReconstruction cloth, List<int[]> faces, HashSet<(int, int)> beyondSurface)
    {
        var positions = cloth.Fe.InitPosePositions;
        var generators = HingeGenerators(faces);
        var readings = new List<HingeReading>();
        foreach (var rod in cloth.Fe.Rods)
        {
            var edge = RodPair(rod);
            if (!beyondSurface.Contains(edge) || edge.Item2 >= positions.Length)
            {
                continue;
            }

            var rest = Vector3.Distance(positions[rod.NodeA], positions[rod.NodeB]);
            var coplanar = rod.MaxDist < ClothReconstruction.UnboundedRodDistance ? rod.MaxDist : rest;
            var closest = float.MaxValue;
            var flat = 0f;
            var folded = 0f;
            var about = (0, 0);
            var fits = new List<((int, int) Hinge, float Error, float Open, float Shut)>();
            // The map keeps degenerate one-node pairs for the miss count, but a rod on one node reads no hinge
            var generating = edge.Item1 != edge.Item2 ? generators.GetValueOrDefault(edge) : null;
            foreach (var hinge in generating ?? [])
            {
                var axis = positions[hinge.Item2] - positions[hinge.Item1];
                var axisLength = axis.Length();
                if (axisLength < 1e-6f)
                {
                    continue;
                }

                var (alongA, alongB, riseA, riseB) = HingeOffsets(positions, rod, hinge, axis / axisLength);
                var slide = (alongA - alongB) * (alongA - alongB);
                var open = MathF.Sqrt(slide + ((riseA + riseB) * (riseA + riseB)));
                var shut = MathF.Sqrt(slide + ((riseA - riseB) * (riseA - riseB)));
                var error = MathF.Abs(open - coplanar);
                var generates = open - shut >= ClothHingeMinimumFold * open;
                if (!generates)
                {
                    continue;
                }

                fits.Add((hinge, error, open, shut));
                if (error < closest)
                {
                    closest = error;
                    flat = open;
                    folded = shut;
                    about = hinge;
                }
            }

            if (closest > ClothHingeFitTolerance * MathF.Max(1f, coplanar))
            {
                continue;
            }

            var reach = (flat * flat) - (folded * folded);
            var span = rod.MinDist >= rest - (ClothRodCapTolerance * MathF.Max(1f, rest)) ? rest : rod.MinDist;
            var fraction = MathUtils.Saturate(((span * span) - (folded * folded)) / reach);
            var candidates = fits
                .Select(fit => (fit.Hinge, MathUtils.Saturate(((span * span) - (fit.Shut * fit.Shut))
                    / ((fit.Open * fit.Open) - (fit.Shut * fit.Shut)))))
                .ToArray();
            readings.Add(new HingeReading(about, fraction, span == rest, closest, candidates));
        }

        return readings;
    }

    /// <summary>
    /// The per-vertex paint the sheet's bend rods state on top of <paramref name="addCurvature"/>, keyed by control node,
    /// or null where they state none or contradict each other, and the sum each hinge states. With
    /// <paramref name="generatorBound"/> every generating hinge is bounded by every rod it generates, and the slacks are
    /// the largest miss without and with the paint; with <paramref name="relaxSetters"/> only a rod's sole setter is solved
    /// exactly.
    /// </summary>
    private static HingeSolve ClothBendStiffnessFromHinges(ClothReconstruction cloth, List<HingeReading> readings,
        float addCurvature, bool generatorBound, bool relaxSetters = false)
    {
        var unpaintedSlack = 0f;
        var solvedSlack = 0f;

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
                    ? candidates.Max(candidate => HingeSum(candidate.Fraction, addCurvature) - assigned(candidate.Hinge))
                    : MathF.Abs(candidates.Min(candidate
                        => assigned(candidate.Hinge) - HingeSum(candidate.Fraction, addCurvature))));
            }

            return worst;
        }

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
        var statedSums = exact;
        foreach (var (hinge, reading) in best)
        {
            (reading.Capped ? bounds : exact)[hinge] = HingeSum(reading.Fraction, addCurvature);
        }

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
                    var sum = HingeSum(candidateFraction, addCurvature);
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
                        .Where(candidate => HingeSum(candidate.Fraction, addCurvature)
                            >= exact[candidate.Hinge] - ClothBendStiffnessAgreement)
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

        foreach (var (_, _, capped, _, candidates) in readings)
        {
            if (!capped)
            {
                continue;
            }

            foreach (var (hinge, candidateFraction) in candidates)
            {
                var least = HingeSum(candidateFraction, addCurvature);
                if (!bounds.TryGetValue(hinge, out var known) || least > known)
                {
                    bounds[hinge] = least;
                }
            }
        }

        if (exact.Count == 0 && bounds.Count == 0)
        {
            return new(null, statedSums, unpaintedSlack, solvedSlack);
        }

        var solved = SolveHingeSums(exact, bounds);
        if (solved is null)
        {
            return new(null, statedSums, unpaintedSlack, solvedSlack);
        }

        if (generatorBound)
        {
            solvedSlack = RodSlack(hinge => solved.GetValueOrDefault(hinge.Item1) + solved.GetValueOrDefault(hinge.Item2));
            if (solvedSlack > ClothBendStiffnessAgreement)
            {
                return new(null, statedSums, unpaintedSlack, solvedSlack);
            }
        }

        var paint = solved.Values.Any(static value => value > ClothBendStiffnessAgreement) ? solved : null;
        return new(paint, statedSums, unpaintedSlack, solvedSlack);
    }

    /// <summary>
    /// Per-vertex paints whose pair sums meet every <paramref name="exact"/> hinge sum and reach every
    /// <paramref name="bounds"/> lower bound, or null where none do.
    /// </summary>
    private static Dictionary<int, float>? SolveHingeSums(Dictionary<(int, int), float> exact,
        Dictionary<(int, int), float> bounds)
    {
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

        return solved;
    }

    /// <summary>
    /// Solves the exact hinge sums: each connected component alternates as <c>sign * p + offset</c>, with <c>p</c> fixed by
    /// a pin or a closed cycle.
    /// </summary>
    private static Dictionary<int, float>? ClothBendStiffnessComponents(Dictionary<int, float> pinned,
        List<(int U, int V, float Sum)> equations, List<(int U, int V, float Least)> checks)
    {
        var adjacency = new Dictionary<int, List<(int Node, float Sum)>>();
        foreach (var (u, v, sum) in equations)
        {
            ClothReconstruction.GetOrAdd(adjacency, u).Add((v, sum));
            ClothReconstruction.GetOrAdd(adjacency, v).Add((u, sum));
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

                solved[node] = MathUtils.Saturate(value);
            }
        }

        return solved;
    }

    /// <summary>
    /// The per-vertex paint that folds a bend network on top of <paramref name="addCurvature"/> where the rods do not say
    /// which hinge built them. Each rod's sole setter is held exact, the rest are covered greedily, and the system is
    /// solved as pairwise bounds from the tightest tolerance up.
    /// </summary>
    private static Dictionary<int, float>? ClothBendStiffnessCoveringHinges(ClothReconstruction cloth, List<HingeReading> readings,
        float addCurvature)
    {
        static ((int, int) Hinge, float Fraction)[] Usable(bool capped, ((int, int) Hinge, float Fraction)[] candidates)
            => capped || !Array.Exists(candidates, static candidate => candidate.Fraction > 0f)
                ? candidates
                : Array.FindAll(candidates, static candidate => candidate.Fraction > 0f);

        var least = new Dictionary<(int, int), float>();
        foreach (var (_, _, capped, _, candidates) in readings)
        {
            foreach (var (hinge, fraction) in Usable(capped, candidates))
            {
                least[hinge] = MathF.Max(least.GetValueOrDefault(hinge, float.MinValue), HingeSum(fraction, addCurvature));
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
                    .Where(candidate => HingeSum(candidate.Fraction, addCurvature) >= least[candidate.Hinge] - tolerance)
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

    /// <summary>
    /// Values in [0, 1] for every node the constraints name, each constraint <c>SignU * x[U] + SignV * x[V] &lt;= Most</c>,
    /// solved as shortest paths over each value and its negation; null on a negative cycle.
    /// </summary>
    private static Dictionary<int, float>? SolvePairwiseBounds(List<(int U, float SignU, int V, float SignV, float Most)> constraints)
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
}
