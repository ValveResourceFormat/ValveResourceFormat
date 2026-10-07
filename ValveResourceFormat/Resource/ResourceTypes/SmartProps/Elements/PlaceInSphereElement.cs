using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_PlaceInSphere: evaluates the children at points in a sphere or circle shell, placed randomly
    /// or in regular rings. All points are generated before the first child runs.
    /// </summary>
    internal sealed class PlaceInSphereElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private const float TwoPi = 6.2831855f;

        private readonly SmartPropAttribute placementMode = Attr.Enum(data, "m_PlacementMode", 0);
        private readonly SmartPropAttribute distributionMode = Attr.Enum(data, "m_DistributionMode", 0);
        private readonly SmartPropAttribute randomness = Attr.Float(data, "m_flRandomness", 0f);
        private readonly SmartPropAttribute planeUpDirection = Attr.Vector3(data, "m_vPlaneUpDirection", Vector3.UnitZ);
        private readonly SmartPropAttribute countMin = Attr.Int(data, "m_nCountMin", 1);
        private readonly SmartPropAttribute countMax = Attr.Int(data, "m_nCountMax", 1);
        private readonly SmartPropAttribute radiusInner = Attr.Float(data, "m_flPositionRadiusInner", 0f);
        private readonly SmartPropAttribute radiusOuter = Attr.Float(data, "m_flPositionRadiusOuter", 0f);
        private readonly SmartPropAttribute alignOrientation = Attr.Bool(data, "m_bAlignOrientation", false);
        private readonly SmartPropAttribute alignDirection = Attr.Vector3(data, "m_vAlignDirection", Vector3.UnitZ);

        public override void Evaluate(SmartPropContext ctx)
        {
            var min = countMin.EvaluateInt(ctx);
            var max = countMax.EvaluateInt(ctx);
            var count = min < max ? ctx.GetRandomStream().RandomInt(min, max) : min;
            var isCircle = placementMode.EvaluateEnum(ctx, SmartPropEnums.RadiusPlacementMode) == 1;
            var up = isCircle ? MathUtils.SafeNormalize(planeUpDirection.EvaluateVector3(ctx)) : Vector3.UnitZ;
            var planeRotation = SmartPropMath.QuaternionFromTwoVectors(Vector3.UnitZ, up);
            var positions = GeneratePositions(ctx, Math.Max(count, 0), isCircle, planeRotation);

            var align = alignOrientation.EvaluateBool(ctx);
            var alignRotation = Quaternion.Identity;

            if (align)
            {
                var direction = MathUtils.SafeNormalize(alignDirection.EvaluateVector3(ctx));
                alignRotation = Vector3.Dot(direction, Vector3.UnitX) >= -0.99999988f
                    ? SmartPropMath.QuaternionFromTwoVectors(direction, Vector3.UnitX)
                    : new Quaternion(0f, 0f, 1f, 0f);
            }

            var parent = ctx.Transform;
            var saved = ctx.SaveInstanceValues();

            for (var i = 0; i < positions.Count; i++)
            {
                var rotation = Quaternion.Identity;

                if (align)
                {
                    var facing = SmartPropMath.QuaternionFromForwardUp(MathUtils.SafeNormalize(positions[i]), up);
                    var sign = Quaternion.Dot(facing, alignRotation) < 0f ? -1f : 1f;
                    rotation = facing * (alignRotation * sign);
                }

                ctx.PushPath(i);
                ctx.InstanceCount = positions.Count;
                ctx.InstanceIndex = i;
                ctx.Transform = parent.Concat(new SmartPropTransform(positions[i], 1f, rotation));
                ctx.EvaluateChildren(Children);
                ctx.PopPath();
            }

            ctx.Transform = parent;
            ctx.RestoreInstanceValues(saved);
        }

        private List<Vector3> GeneratePositions(SmartPropContext ctx, int count, bool isCircle, Quaternion planeRotation)
        {
            var inner = radiusInner.EvaluateFloat(ctx);
            var outer = radiusOuter.EvaluateFloat(ctx);
            var isRegular = distributionMode.EvaluateEnum(ctx, SmartPropEnums.DistributionMode) == 1;
            var positions = new List<Vector3>(count);

            if (isRegular)
            {
                var jitter = randomness.EvaluateFloat(ctx);
                var r0 = MathF.Min(inner, outer);
                var r1 = MathF.Max(inner, outer);

                if (isCircle)
                {
                    RegularCircle(ctx, count, r0, r1, jitter, planeRotation, positions);
                }
                else
                {
                    RegularSphere(ctx, count, r0, r1, jitter, positions);
                }

                return positions;
            }

            var rMin = MathF.Max(MathF.Min(inner, outer), 0f);
            var rMax = MathF.Max(inner, outer);

            for (var i = 0; i < count; i++)
            {
                if (rMax <= 0f)
                {
                    positions.Add(Vector3.Zero);
                    continue;
                }

                var stream = ctx.GetRandomStream();
                var t = stream.RandomFloat(0f, 1f);

                if (isCircle)
                {
                    var a = ctx.GetRandomStream().RandomFloat(0f, 1f);
                    var radius = MathF.Pow(t, rMin / rMax * 0.5f + 0.5f) * (rMax - rMin) + rMin;
                    var (sin, cos) = MathF.SinCos(a * TwoPi);
                    positions.Add(Vector3.Transform(new Vector3(cos * radius, sin * radius, 0f), planeRotation));
                }
                else
                {
                    var radius = MathF.Pow(t, rMin / rMax * 0.6666667f + 0.33333334f) * (rMax - rMin) + rMin;
                    positions.Add(RandomUnitVector(ctx) * radius);
                }
            }

            return positions;
        }

        private static Vector3 RandomUnitVector(SmartPropContext ctx)
        {
            var z = 2f * ctx.GetRandomStream().RandomFloat(0f, 1f) - 1f;
            var phi = ctx.GetRandomStream().RandomFloat(0f, 1f) * TwoPi;
            var r = MathF.Sqrt(1f - z * z);
            var (sin, cos) = MathF.SinCos(phi);
            return new Vector3(cos * r, sin * r, z);
        }

        private static float Jitter(SmartPropContext ctx, float low, float high) => low < high ? ctx.GetRandomStream().RandomFloat(low, high) : low;

        private static int[] DistributeCountOverRings(int total, float[] radii)
        {
            var count = radii.Length;
            var circumference = 0f;

            foreach (var radius in radii)
            {
                circumference += TwoPi * radius;
            }

            var perPoint = circumference / total;
            var remaining = total - count;
            var counts = new int[count];

            for (var i = 0; i < count; i++)
            {
                var extra = (int)(TwoPi * radii[i] / perPoint) - 1;
                extra = Math.Max(Math.Min(extra, remaining), 0);
                remaining -= extra;
                counts[i] = extra + 1;
            }

            if (remaining > 0)
            {
                var order = Enumerable.Range(0, count).ToArray();
                Array.Sort(order, (a, b) => radii[a] > radii[b] ? -1 : 1);

                for (var i = 0; remaining > 0; i = (i + 1) % count)
                {
                    counts[order[i]]++;
                    remaining--;
                }
            }

            return counts;
        }

        private static float[] RingRadii(int rings, float r0, float r1)
        {
            var radii = new float[rings];

            for (var j = 0; j < rings; j++)
            {
                radii[j] = rings == 1 ? (r1 + r0) * 0.5f : j * ((r1 - r0) / (rings - 1)) + r0;
            }

            return radii;
        }

        private static void RegularCircle(SmartPropContext ctx, int count, float r0, float r1, float jitter, Quaternion planeRotation, List<Vector3> positions)
        {
            if (count <= 0)
            {
                return;
            }

            var spacing = 2f * MathF.Sqrt((r1 * r1 - r0 * r0) * 3.1415927f / count / 3.1415927f);
            var rings = spacing <= 0f ? 1 : Math.Min(count, (int)((r1 - r0) / spacing + 0.5f) + 1);
            var radialSpacing = (r1 - r0) / rings;
            var radii = RingRadii(rings, r0, r1);
            var counts = DistributeCountOverRings(count, radii);
            var offset = 0f;

            for (var j = 0; j < rings; j++)
            {
                var ringCount = counts[j];

                for (var i = 0; i < ringCount; i++)
                {
                    if (positions.Count >= count)
                    {
                        return;
                    }

                    var angleJitter = Jitter(ctx, -0.5f * jitter, 0.5f * jitter);
                    var half = radialSpacing * 0.5f * jitter;
                    var radius = Jitter(ctx, MathF.Max(r0, radii[j] - half), MathF.Min(r1, radii[j] + half));
                    var angle = ((i + angleJitter) / ringCount + offset) % 1f * TwoPi;
                    var (sin, cos) = MathF.SinCos(angle);
                    positions.Add(Vector3.Transform(new Vector3(cos * radius, sin * radius, radius * 0f), planeRotation));
                }

                offset = (0.5f / ringCount + offset) % 1f;
            }
        }

        private static void RegularSphere(SmartPropContext ctx, int count, float r0, float r1, float jitter, List<Vector3> positions)
        {
            if (count <= 0)
            {
                return;
            }

            const float FourThirdsPi = 4.18879f;
            const float FourPi = 12.566371f;

            var spacing = 2f * MathF.Pow((r1 * FourThirdsPi * r1 * r1 - r0 * FourThirdsPi * r0 * r0) / count / FourThirdsPi, 0.33333334f);
            var shells = spacing <= 0f ? 1 : Math.Min(count, (int)((r1 - r0) / spacing + 0.5f) + 1);
            var radialSpacing = (r1 - r0) / shells;
            var radii = RingRadii(shells, r0, r1);
            var area = 0f;

            foreach (var radius in radii)
            {
                area += FourPi * radius * radius;
            }

            var perPoint = area / count;
            var remaining = count - shells;
            var shellCounts = new float[shells];

            for (var s = 0; s < shells - 1; s++)
            {
                var extra = Math.Clamp((int)(FourPi * radii[s] * radii[s] / perPoint) - 1, 0, Math.Max(remaining, 0));
                remaining -= extra;
                shellCounts[s] = extra + 1;
            }

            shellCounts[shells - 1] = remaining + 1;

            for (var s = 0; s < shells; s++)
            {
                var shellCount = (int)shellCounts[s];
                var shellRadius = radii[s];
                var ringSpacing = 2f * MathF.Sqrt(FourPi * shellRadius * shellRadius / shellCount / 3.1415927f);
                var rings = ringSpacing <= 0f ? 1 : Math.Min(shellCount, 2 - (int)(shellRadius * TwoPi / (ringSpacing * -2f)));
                var sines = new float[rings];
                var cosines = new float[rings];

                for (var k = 0; k < rings; k++)
                {
                    var theta = (float)k / Math.Max(rings - 1, 1) * 3.1415927f;
                    (sines[k], cosines[k]) = MathF.SinCos(theta);
                }

                var ringCounts = DistributeCountOverRings(shellCount, sines);

                for (var k = 0; k < rings; k++)
                {
                    for (var p = 0; p < ringCounts[k]; p++)
                    {
                        if (positions.Count >= count)
                        {
                            return;
                        }

                        var latitudeJitter = Jitter(ctx, -0.5f * jitter, 0.5f * jitter);
                        var neighbour = Math.Clamp(latitudeJitter < 0f ? k - 1 : k + 1, 0, rings - 1);
                        var f = MathF.Abs(latitudeJitter);
                        var z = (cosines[neighbour] - cosines[k]) * f + cosines[k];
                        var ringRadius = (sines[neighbour] - sines[k]) * f + sines[k];
                        var half = radialSpacing * 0.5f * jitter;
                        var radius = Jitter(ctx, MathF.Max(r0, shellRadius - half), MathF.Min(r1, shellRadius + half));
                        var azimuthJitter = Jitter(ctx, -0.5f * jitter, 0.5f * jitter);
                        var angle = (p + azimuthJitter) / ringCounts[k] * TwoPi;
                        var (sin, cos) = MathF.SinCos(angle);
                        positions.Add(new Vector3(cos * (radius * ringRadius), sin * (radius * ringRadius), radius * z));
                    }
                }
            }
        }
    }
}
