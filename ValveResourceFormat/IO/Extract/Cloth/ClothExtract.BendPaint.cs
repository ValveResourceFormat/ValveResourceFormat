using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
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
    private static (Dictionary<int, float>? Paint, float AddCurvature) ClothPaintCoveringEveryRod(FeModel feModel,
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
    private static int ClothPaintMisses(FeModel feModel, List<int[]> faces, HashSet<(int, int)> network,
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
    private static (Dictionary<int, float>? Paint, float AddCurvature) ClothCurvatureMeetsItsCappedRods(FeModel feModel,
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

    private static (Dictionary<int, float>? Paint, float AddCurvature) ClothBendStiffnessRead(FeModel feModel,
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
    private static List<((int, int) Hinge, float Fraction, bool Capped, float Error, ((int, int) Hinge, float Fraction)[] Candidates)> ClothHingeReadings(
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
    private static Dictionary<int, float>? ClothBendStiffnessFromHinges(FeModel feModel, List<int[]> faces,
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
    private static Dictionary<int, float>? ClothBendStiffnessFromHinges(FeModel feModel, List<int[]> faces,
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
    private static Dictionary<int, float>? ClothBendStiffnessComponents(Dictionary<int, float> pinned,
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
    private static Dictionary<int, float>? ClothBendStiffnessCoveringHinges(FeModel feModel, List<int[]> faces,
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

    private static readonly float[] ClothBendStiffnessCoverTolerances = [1e-5f, 1e-4f, 1e-3f, ClothBendStiffnessAgreement];

    /// <summary>
    /// Values in [0, 1] for every node the constraints name, each constraint <c>SignU * x[U] + SignV * x[V] &lt;= Most</c>
    /// with unit signs, or null where none exist. Every node stands as itself and as its negation, a constraint becomes two
    /// shortest-path edges between them, and the values are half the distance between the two; a negative cycle means the
    /// system has no solution.
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

    // How far two hinges' stated sums may sit apart and still count as the same paint, in the
    // paint's own units: a hundredth of the half turn a full sum of two folds a hinge through.
    private const float ClothBendStiffnessAgreement = 0.02f;

    // How many times a hinge stating only a lower bound may raise its own two vertices before the
    // solve gives up. Each pass satisfies every bound it can, so a chain of them settles in a few.
    private const int ClothBendStiffnessRepairPasses = 8;
}
