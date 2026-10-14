using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Criteria;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_FitOnLine: lays children end to end along a line by their LinearLength criteria: an
    /// optional start cap, fill pieces until the line is used up, an optional end cap, then stretches the pieces
    /// by the scale mode.
    /// </summary>
    internal sealed class FitOnLineElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private const int MaxEntries = 1000;

        private enum EntryKind
        {
            StartCap,
            EndCap,
            Fill,
        }

        private readonly SmartPropAttribute start = Attr.Vector3(data, "m_vStart", Vector3.Zero);
        private readonly SmartPropAttribute end = Attr.Vector3(data, "m_vEnd", Vector3.Zero);
        private readonly SmartPropAttribute pointSpace = Attr.Enum(data, "m_PointSpace", 2);
        private readonly SmartPropAttribute orientAlongLine = Attr.Bool(data, "m_bOrientAlongLine", false);
        private readonly SmartPropAttribute upDirection = Attr.Vector3(data, "m_vUpDirection", Vector3.UnitZ);
        private readonly SmartPropAttribute upDirectionSpace = Attr.Enum(data, "m_UpDirectionSpace", 2);
        private readonly SmartPropAttribute prioritizeUp = Attr.Bool(data, "m_bPrioritizeUp", false);
        private readonly SmartPropAttribute scaleMode = Attr.Enum(data, "m_nScaleMode", 0);
        private readonly SmartPropAttribute pickMode = Attr.Enum(data, "m_nPickMode", 0);

        private sealed class Entry
        {
            public required SmartPropElement Element { get; init; }
            public EntryKind Kind { get; init; }
            public float Length { get; set; }
            public float Min { get; set; }
            public float Max { get; set; }
            public float Assigned { get; set; }
        }

        public override void Evaluate(SmartPropContext ctx)
        {
            var startPoint = start.EvaluateVector3(ctx);
            var lineStart = ctx.ConvertPosition(SmartPropSpace.Element, (SmartPropSpace)pointSpace.EvaluateEnum(ctx, SmartPropEnums.Space), startPoint);
            var endPoint = end.EvaluateVector3(ctx);
            var lineEnd = ctx.ConvertPosition(SmartPropSpace.Element, (SmartPropSpace)pointSpace.EvaluateEnum(ctx, SmartPropEnums.Space), endPoint);
            var direction = MathUtils.SafeNormalize(lineEnd - lineStart);
            var lineLength = Vector3.Distance(lineStart, lineEnd);
            ctx.LineLength = lineLength;

            var pick = (FitPickMode)pickMode.EvaluateEnum(ctx, SmartPropEnums.PickMode);
            var scale = (FitScaleMode)scaleMode.EvaluateEnum(ctx, SmartPropEnums.ScaleMode);
            var entries = SelectElements(ctx, pick, scale, lineLength);

            switch (scale)
            {
                case FitScaleMode.ScaleEndToFit:
                    ScaleEndToFit(entries, lineLength);
                    break;
                case FitScaleMode.ScaleEqually:
                    ScaleEqually(entries, lineLength);
                    break;
                case FitScaleMode.ScaleMaximize:
                    ScaleMaximize(entries, lineLength);
                    break;
                default:
                    break;
            }

            var parent = ctx.Transform;
            var rotation = parent.Rotation;

            if (orientAlongLine.EvaluateBool(ctx))
            {
                var forward = Vector3.Transform(direction, parent.Rotation);
                var upVector = upDirection.EvaluateVector3(ctx);
                var up = ctx.ConvertDirection(SmartPropSpace.World, (SmartPropSpace)upDirectionSpace.EvaluateEnum(ctx, SmartPropEnums.Space), upVector);
                rotation = SmartPropMath.OrientationFromForwardUp(forward, up, prioritizeUp.EvaluateBool(ctx));
            }

            var offset = 0f;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var saved = ctx.SaveInstanceValues();
                var local = lineStart + direction * offset;

                ctx.PushPath(entry.Kind == EntryKind.EndCap ? -1 : i);
                ctx.InstanceCount = entries.Count;
                ctx.InstanceIndex = i;
                ctx.PathParameter = offset / lineLength;
                ctx.LinearScale = entry.Assigned / entry.Length;
                ctx.Transform = new SmartPropTransform(parent.TransformPoint(local), parent.Scale, rotation);
                ctx.EvaluateElement(entry.Element);
                ctx.PopPath();
                ctx.RestoreInstanceValues(saved);

                offset += entry.Assigned;
            }
        }

        private static float Tolerance(float lineLength) => MathF.Min(MathF.Max(lineLength * 0.0009765625f, 0.03125f), 1f);

        private static float Remaining(float lineLength, float used) => MathF.Max(0f, Tolerance(lineLength) + (lineLength - used));

        private static Entry LengthInfo(SmartPropContext ctx, SmartPropElement element, EntryKind kind, bool scalable)
        {
            var entry = new Entry { Element = element, Kind = kind };
            var linearLength = SmartPropCriteria.FindFirst<LinearLengthCriteria>(element.SelectionCriteria, ctx);

            if (linearLength != null)
            {
                var length = linearLength.Length.EvaluateFloat(ctx);
                entry.Length = length;
                entry.Assigned = length;

                if (scalable && linearLength.AllowScale.EvaluateBool(ctx))
                {
                    entry.Min = linearLength.MinLength.EvaluateFloat(ctx);
                    entry.Max = linearLength.MaxLength.EvaluateFloat(ctx);
                }
                else
                {
                    entry.Min = length;
                    entry.Max = length;
                }
            }

            return entry;
        }

        private List<Entry> CollectCandidates(SmartPropContext ctx, EntryKind kind, float remaining, FitScaleMode scale)
        {
            var candidates = new List<Entry>();

            foreach (var child in Children)
            {
                if (!child.Enabled.EvaluateBool(ctx))
                {
                    continue;
                }

                var endCap = SmartPropCriteria.FindFirst<EndCapCriteria>(child.SelectionCriteria, ctx);
                var isStart = endCap != null && endCap.Start.EvaluateBool(ctx);
                var isEnd = endCap != null && endCap.End.EvaluateBool(ctx);

                var matches = kind switch
                {
                    EntryKind.StartCap => isStart,
                    EntryKind.EndCap => isEnd,
                    _ => !isStart && !isEnd,
                };

                if (!matches)
                {
                    continue;
                }

                var entry = LengthInfo(ctx, child, kind, scale != FitScaleMode.None);

                if ((kind == EntryKind.Fill && entry.Length < SmartPropMath.Epsilon) || entry.Min > remaining)
                {
                    continue;
                }

                if (scale == FitScaleMode.ScaleMaximize)
                {
                    entry.Assigned = MathF.Min(remaining, entry.Max);
                }

                candidates.Add(entry);
            }

            return candidates;
        }

        private static int PickLargestFitting(List<Entry> candidates, float remaining, bool scalable)
        {
            var best = -1;
            var bestLength = -1f;

            for (var i = 0; i < candidates.Count; i++)
            {
                var fits = remaining >= (scalable ? candidates[i].Min : candidates[i].Length);

                if (fits && candidates[i].Length > bestLength)
                {
                    best = i;
                    bestLength = candidates[i].Length;
                }
            }

            return best;
        }

        private static Entry? Pick(SmartPropContext ctx, List<Entry> candidates, FitPickMode pick, float remaining, FitScaleMode scale)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            var index = pick switch
            {
                FitPickMode.LargestFirst => PickLargestFitting(candidates, remaining, scale != FitScaleMode.None),
                FitPickMode.Random => candidates.Count - 1 > 0 ? ctx.GetRandomStream().RandomInt(0, candidates.Count - 1) : 0,
                _ => -1,
            };

            return index >= 0 ? candidates[index] : null;
        }

        private List<Entry> SelectElements(SmartPropContext ctx, FitPickMode pick, FitScaleMode scale, float lineLength)
        {
            var entries = new List<Entry>();

            if (pick == FitPickMode.AllInOrder)
            {
                foreach (var child in Children)
                {
                    if (child.Enabled.EvaluateBool(ctx))
                    {
                        entries.Add(LengthInfo(ctx, child, EntryKind.Fill, scale != FitScaleMode.None));
                    }
                }

                return entries;
            }

            var used = 0f;
            var startRemaining = Remaining(lineLength, used);
            var startCap = Pick(ctx, CollectCandidates(ctx, EntryKind.StartCap, startRemaining, scale), pick, startRemaining, scale);

            if (startCap != null)
            {
                entries.Add(startCap);
                used += startCap.Assigned;
            }

            var endRemaining = Remaining(lineLength, used);
            var endCap = Pick(ctx, CollectCandidates(ctx, EntryKind.EndCap, endRemaining, scale), pick, endRemaining, scale);
            used += endCap?.Assigned ?? 0f;

            while (lineLength > used && entries.Count < MaxEntries)
            {
                var remaining = Remaining(lineLength, used);
                var piece = Pick(ctx, CollectCandidates(ctx, EntryKind.Fill, remaining, scale), pick, remaining, scale);

                if (piece == null)
                {
                    break;
                }

                entries.Add(piece);
                used += piece.Assigned;
            }

            if (endCap != null)
            {
                entries.Add(endCap);
            }

            return entries;
        }

        private static float Total(List<Entry> entries)
        {
            var total = 0f;

            foreach (var entry in entries)
            {
                total += entry.Assigned;
            }

            return total;
        }

        private static void ScaleEndToFit(List<Entry> entries, float lineLength)
        {
            var total = Total(entries);

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (MathF.Abs(total - lineLength) <= SmartPropMath.Epsilon)
                {
                    break;
                }

                var entry = entries[i];
                var difference = lineLength - total;
                var previous = entry.Assigned;
                var assigned = previous;

                if (difference > 0f)
                {
                    assigned = MathF.Min(previous + difference, entry.Max);
                }
                else if (difference < 0f)
                {
                    assigned = MathF.Max(previous + difference, entry.Min);
                }

                entry.Assigned = assigned;
                total += assigned - previous;
            }
        }

        private static void ScaleEqually(List<Entry> entries, float lineLength)
        {
            var tolerance = Tolerance(lineLength);
            var total = Total(entries);

            for (var pass = 0; tolerance < MathF.Abs(total - lineLength);)
            {
                var factor = lineLength / total;
                var scaled = 0f;

                foreach (var entry in entries)
                {
                    entry.Assigned = MathF.Min(MathF.Max(factor * entry.Assigned, entry.Min), entry.Max);
                    scaled += entry.Assigned;
                }

                if (!(tolerance < MathF.Abs(total - scaled)) || ++pass >= 10)
                {
                    break;
                }

                total = scaled;
            }

            ScaleEndToFit(entries, lineLength);
        }

        private static void ScaleMaximize(List<Entry> entries, float lineLength)
        {
            var total = 0f;

            foreach (var entry in entries)
            {
                entry.Assigned = MathF.Min(MathF.Max(lineLength - total, entry.Min), entry.Max);
                total += entry.Assigned;
            }

            if (total > lineLength)
            {
                ScaleEndToFit(entries, lineLength);
            }
        }
    }
}
