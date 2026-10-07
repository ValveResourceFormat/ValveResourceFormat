using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Criteria;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_PlaceOnPath: evaluates the children at points spaced along a path (the default path; named
    /// input paths only exist on Hammer map objects), and the CONTROL_POINTS children at the path's knots.
    /// </summary>
    internal sealed class PlaceOnPathElement : SmartPropGroupElement
    {
        private static readonly Vector3[] FallbackPath = [new(-100f, 0f, 0f), new(100f, 0f, 0f)];

        private readonly SmartPropAttribute spacing;
        private readonly SmartPropAttribute offsetAlongPath;
        private readonly SmartPropAttribute pathOffset;
        private readonly SmartPropAttribute pathSpace;
        private readonly SmartPropAttribute useFixedUpDirection;
        private readonly SmartPropAttribute noRoll;
        private readonly SmartPropAttribute useProjectedDistance;
        private readonly SmartPropAttribute upDirection;
        private readonly SmartPropAttribute upDirectionSpace;
        private readonly SmartPropAttribute defaultPathInWorldSpace;
        private readonly List<SmartPropAttribute> defaultPath = [];

        private readonly record struct Placement(SmartPropTransform Transform, float Parameter);

        public PlaceOnPathElement(KVObject data, SmartPropParser parser) : base(data, parser)
        {
            spacing = Attr.Float(data, "m_flSpacing", 1f);
            offsetAlongPath = Attr.Float(data, "m_flOffsetAlongPath", 0f);
            pathOffset = Attr.Vector2(data, "m_vPathOffset", Vector2.Zero);
            pathSpace = Attr.Enum(data, "m_PathSpace", 0);
            useFixedUpDirection = Attr.Bool(data, "m_bUseFixedUpDirection", false);
            noRoll = Attr.Bool(data, "m_bNoRoll", false);
            useProjectedDistance = Attr.Bool(data, "m_bUseProjectedDistance", false);
            upDirection = Attr.Vector3(data, "m_vUpDirection", Vector3.UnitZ);
            upDirectionSpace = Attr.Enum(data, "m_UpDirectionSpace", 0);
            defaultPathInWorldSpace = Attr.Bool(data, "m_DefaultPathInWorldSpace", false);

            var defaultPoint = SmartPropAttribute.FromLiteral(SmartPropValue.FromFloat(0), SmartPropValue.FromFloat(0), SmartPropValue.FromFloat(0));

            foreach (var point in data.GetArray("m_DefaultPath") ?? [])
            {
                defaultPath.Add(SmartPropAttribute.Parse(point, defaultPoint));
            }
        }

        public override void Evaluate(SmartPropContext ctx)
        {
            var points = defaultPath.Count > 0 ? defaultPath.Select(point => point.EvaluateVector3(ctx)).ToList() : [.. FallbackPath];
            var curve = SmartPropPathCurve.Build(points);

            if (curve == null)
            {
                return;
            }

            var baseTransform = defaultPathInWorldSpace.EvaluateBool(ctx) ? SmartPropTransform.Identity : ctx.ObjectTransform;
            var pathTransform = (SmartPropSpace)pathSpace.EvaluateEnum(ctx, SmartPropEnums.Space) switch
            {
                SmartPropSpace.Object => ctx.ObjectTransform,
                SmartPropSpace.Element => ctx.Transform,
                _ => baseTransform,
            };

            var up = MathUtils.SafeNormalize(upDirection.EvaluateVector3(ctx));
            up = (SmartPropSpace)upDirectionSpace.EvaluateEnum(ctx, SmartPropEnums.Space) switch
            {
                SmartPropSpace.Object => Vector3.Transform(up, ctx.ObjectTransform.Rotation),
                SmartPropSpace.Element => Vector3.Transform(up, ctx.Transform.Rotation),
                _ => up,
            };

            var fixedUp = useFixedUpDirection.EvaluateBool(ctx);
            var spaced = ComputePositions(
                curve,
                pathTransform,
                offsetAlongPath.EvaluateFloat(ctx),
                spacing.EvaluateFloat(ctx),
                useProjectedDistance.EvaluateBool(ctx),
                fixedUp,
                noRoll.EvaluateBool(ctx),
                up,
                pathOffset.EvaluateVector2(ctx));

            var knots = new List<Placement>(curve.Knots.Count);

            for (var i = 0; i < curve.Knots.Count; i++)
            {
                var knot = curve.Knots[i];
                var tangent = i == curve.Knots.Count - 1 ? -MathUtils.SafeNormalize(knot.InTangent) : MathUtils.SafeNormalize(knot.OutTangent);
                tangent = Vector3.Transform(tangent, pathTransform.Rotation);

                Vector3 forward, frameUp;

                if (fixedUp)
                {
                    forward = SmartPropMath.PerpendicularComponent(tangent, up);
                    frameUp = up;
                }
                else
                {
                    forward = tangent;
                    frameUp = SmartPropMath.PerpendicularComponent(up, tangent);
                }

                var transform = SmartPropTransform.FromBasis(forward, Vector3.Cross(frameUp, forward), frameUp, pathTransform.TransformPoint(knot.Position));
                knots.Add(new Placement(transform, curve.Parameters[i]));
            }

            EvaluateChildrenAtPositions(ctx, spaced, controlPoints: false);
            EvaluateChildrenAtPositions(ctx, knots, controlPoints: true);
        }

        private void EvaluateChildrenAtPositions(SmartPropContext ctx, List<Placement> placements, bool controlPoints)
        {
            var saved = ctx.SaveInstanceValues();

            for (var i = 0; i < placements.Count; i++)
            {
                ctx.PushPath(i);
                ctx.InstanceCount = placements.Count;
                ctx.InstanceIndex = i;
                ctx.PathParameter = placements[i].Parameter;
                ctx.Transform = placements[i].Transform;

                foreach (var child in Children)
                {
                    if (PlacesAt(ctx, child, i, placements.Count, controlPoints))
                    {
                        ctx.EvaluateElement(child);
                    }
                }

                ctx.PopPath();
            }

            ctx.RestoreInstanceValues(saved);
        }

        private static bool PlacesAt(SmartPropContext ctx, SmartPropElement child, int index, int count, bool controlPoints)
        {
            var criteria = SmartPropCriteria.FindFirst<PathPositionCriteria>(child.SelectionCriteria, ctx);
            var mode = criteria?.PlaceAtPositions.EvaluateEnum(ctx, SmartPropEnums.PathPositions) ?? 0;

            if ((mode == 3) != controlPoints)
            {
                return false;
            }

            if (criteria == null)
            {
                return true;
            }

            if ((index == 0 && !criteria.AllowAtStart.EvaluateBool(ctx)) || (index == count - 1 && !criteria.AllowAtEnd.EvaluateBool(ctx)))
            {
                return false;
            }

            return mode switch
            {
                1 => (index + criteria.NthPositionIndexOffset.EvaluateInt(ctx)) % Math.Max(criteria.PlaceEveryNthPosition.EvaluateInt(ctx), 1) == 0,
                2 => index == 0 || index == count - 1,
                _ => true,
            };
        }

        private static (Vector3 Forward, Vector3 Left, Vector3 Up) ComputeFrame(Vector3 tangent, Vector3 frameUp, Vector3 referenceUp, bool fixedUp, bool noRoll)
        {
            var forward = fixedUp ? SmartPropMath.PerpendicularComponent(tangent, referenceUp) : tangent;

            if (noRoll)
            {
                var left = MathUtils.SafeNormalize(Vector3.Cross(referenceUp, forward));
                return (forward, left, MathUtils.SafeNormalize(Vector3.Cross(forward, left)));
            }

            var up = fixedUp ? referenceUp : frameUp;
            return (forward, MathUtils.SafeNormalize(Vector3.Cross(up, forward)), up);
        }

        private static (Vector3[] Ups, Vector3[] Tangents) ComputeTransportFrames(Vector3 referenceUp, IReadOnlyList<Vector3> points)
        {
            var count = points.Count;
            var ups = new Vector3[count];
            var tangents = new Vector3[count];

            for (var k = 0; k < count - 1; k++)
            {
                tangents[k] = MathUtils.SafeNormalize(points[k + 1] - points[k]);

                if (k == 0)
                {
                    ups[0] = SmartPropMath.PerpendicularComponent(MathUtils.SafeNormalize(referenceUp), tangents[0]);
                    continue;
                }

                var up = ups[k - 1];
                var previousTangent = MathUtils.SafeNormalize(points[k] - points[k - 1]);
                var dot = Math.Clamp(Vector3.Dot(previousTangent, tangents[k]), -1f, 1f);

                if (MathF.Abs(dot) <= 0.999f)
                {
                    var axis = MathUtils.SafeNormalize(Vector3.Cross(previousTangent, tangents[k]));
                    var angle = (float)(Math.Acos(dot) * 57.29577951308232);
                    up = Vector3.Transform(up, SmartPropMath.QuaternionFromAxisAngleDegrees(axis, angle));
                }

                var left = MathUtils.SafeNormalize(Vector3.Cross(up, tangents[k]));
                ups[k] = MathUtils.SafeNormalize(Vector3.Cross(tangents[k], left));
            }

            ups[count - 1] = ups[count - 2];
            tangents[count - 1] = tangents[count - 2];
            return (ups, tangents);
        }

        private static bool SegmentSphereIntersect(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t0)
        {
            var lengthSquared = direction.LengthSquared();
            var toOrigin = origin - center;

            if (lengthSquared == 0f)
            {
                t0 = 0f;
                return toOrigin.Length() <= radius;
            }

            var middle = -Vector3.Dot(toOrigin, direction) / lengthSquared;
            var closest = toOrigin + direction * middle;
            var discriminant = (radius * radius - closest.LengthSquared()) / lengthSquared;

            if (discriminant < 0f)
            {
                t0 = 0f;
                return false;
            }

            var half = MathF.Sqrt(discriminant);
            t0 = middle - half;
            var t1 = middle + half;

            if (t0 > 1f || t1 < 0f)
            {
                return false;
            }

            t0 = MathF.Max(t0, 0f);
            return true;
        }

        private static List<Placement> ComputePositions(
            SmartPropPathCurve curve,
            SmartPropTransform pathTransform,
            float offsetAlong,
            float spacing,
            bool projected,
            bool fixedUp,
            bool noRoll,
            Vector3 referenceUp,
            Vector2 pathOffset)
        {
            var placements = new List<Placement>();
            var length = curve.TotalLength;

            if (!(spacing > SmartPropMath.Epsilon) || !float.IsFinite(spacing) || !(length > SmartPropMath.Epsilon) || !float.IsFinite(length))
            {
                return placements;
            }

            var step = spacing / 10f;
            var n = (int)(length / step);
            var dt = n > 0 ? n * step / length / n : 0f;
            var sampleCount = n + 2;
            var samples = new Vector3[sampleCount];
            var parameters = new float[sampleCount];

            for (var k = 0; k < sampleCount; k++)
            {
                parameters[k] = MathF.Min(k * dt, 1f);
                samples[k] = pathTransform.TransformPoint(curve.Evaluate(parameters[k]).Position);
            }

            if (pathOffset != Vector2.Zero)
            {
                var (ups, tangents) = ComputeTransportFrames(referenceUp, samples);

                for (var k = 0; k < sampleCount; k++)
                {
                    var (_, left, up) = ComputeFrame(tangents[k], ups[k], referenceUp, fixedUp, noRoll);
                    samples[k] += left * pathOffset.X + up * pathOffset.Y;
                }
            }

            var current = samples[0];
            var currentParameter = 0f;
            var segment = 0;

            if (offsetAlong > 0f)
            {
                var accumulated = 0f;

                for (var i = 1; i < sampleCount; i++)
                {
                    var distance = Vector3.Distance(samples[i], samples[i - 1]);

                    if (distance + accumulated >= offsetAlong)
                    {
                        var f = Math.Clamp(MathF.Max(offsetAlong - accumulated, 0f) / distance, 0f, 1f);
                        current = Vector3.Lerp(samples[i - 1], samples[i], f);
                        currentParameter = float.Lerp(parameters[i - 1], parameters[i], f);
                        segment = i - 1;
                        break;
                    }

                    accumulated += distance;
                }
            }

            var points = new List<Vector3> { current };
            var pointParameters = new List<float> { currentParameter };

            Vector3 Project(Vector3 v) => projected ? v - referenceUp * Vector3.Dot(v, referenceUp) : v;

            for (var k = segment + 1; k < sampleCount; k++)
            {
                var candidate = samples[k];

                if ((Project(current) - Project(candidate)).LengthSquared() < spacing * spacing)
                {
                    continue;
                }

                Vector3 point;
                float parameter;

                if (SegmentSphereIntersect(Project(candidate), Project(samples[k - 1]) - Project(candidate), Project(current), spacing, out var t0))
                {
                    point = Vector3.Lerp(candidate, samples[k - 1], t0);
                    parameter = float.Lerp(parameters[k], parameters[k - 1], t0);
                }
                else
                {
                    point = candidate;
                    parameter = parameters[k];
                }

                points.Add(point);
                pointParameters.Add(parameter);
                current = point;
            }

            Vector3[] frameUps;
            Vector3[] frameTangents;

            if (points.Count >= 2)
            {
                (frameUps, frameTangents) = ComputeTransportFrames(referenceUp, points);
            }
            else
            {
                frameUps = [referenceUp];
                frameTangents = [SmartPropMath.OrthogonalFallback(referenceUp)];
            }

            var lastTangent = Vector3.Transform(curve.Evaluate(pointParameters[^1]).Tangent, pathTransform.Rotation);
            frameUps[^1] = SmartPropMath.PerpendicularComponent(frameUps[^1], lastTangent);

            for (var i = 0; i < points.Count; i++)
            {
                var (forward, left, up) = ComputeFrame(frameTangents[i], frameUps[i], referenceUp, fixedUp, noRoll);
                placements.Add(new Placement(SmartPropTransform.FromBasis(forward, left, up, points[i]), pointParameters[i]));
            }

            return placements;
        }
    }
}
