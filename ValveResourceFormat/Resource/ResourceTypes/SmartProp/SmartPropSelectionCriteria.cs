using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// The linear length an element declares for itself, consumed when it is fitted onto
    /// a line. The scale a FitOnLine parent applies is the line length divided by this
    /// length, clamped to the min and max length ratio when scaling is allowed.
    /// </summary>
    /// <param name="Length">Length of the line the element takes up when selected.</param>
    /// <param name="AllowScale">Whether the element may be scaled at all.</param>
    /// <param name="MinLength">Minimum allowable length, must be at most Length.</param>
    /// <param name="MaxLength">Maximum allowable length, must be at least Length.</param>
    public readonly record struct SmartPropLinearLength(float Length, bool AllowScale, float MinLength, float MaxLength)
    {
        /// <summary>
        /// Computes the scale to apply when fitting onto a line of the given length.
        /// Returns 1 when either length is non-positive, mirroring the engine's refusal
        /// to divide by zero.
        /// </summary>
        public float ComputeScale(float lineLength)
        {
            if (Length <= 0f || lineLength <= 0f)
            {
                return 1f;
            }

            var scale = lineLength / Length;
            if (AllowScale)
            {
                if (MinLength > 0f)
                {
                    scale = MathF.Max(scale, MinLength / Length);
                }

                if (MaxLength > 0f)
                {
                    scale = MathF.Min(scale, MaxLength / Length);
                }
            }

            return scale;
        }
    }

    /// <summary>
    /// Matches child elements against their m_SelectionCriteria for a given placement
    /// instance, and extracts the linear length and choice weight criteria that parent
    /// elements consume while placing their children.
    /// </summary>
    public static class SmartPropSelectionCriteria
    {
        /// <summary>
        /// Checks whether a child element should be placed at instance
        /// <paramref name="instanceIndex"/> out of <paramref name="instanceCount"/>.
        /// Elements without selection criteria always match; individual criteria must
        /// all pass. Disabled criteria are skipped.
        /// </summary>
        public static bool MatchesSelectionCriteria(KVObject? child, int instanceIndex, int instanceCount, SmartPropEvaluationContext context)
        {
            if (child is null
                || !child.TryGetValue("m_SelectionCriteria", out var criteriaNode)
                || !criteriaNode.IsArray)
            {
                return true;
            }

            var span = criteriaNode.AsArraySpan();
            for (var i = 0; i < span.Length; i++)
            {
                var criteria = span[i];
                if (criteria.ValueType != KVValueType.Collection || IsDisabled(criteria))
                {
                    continue;
                }

                switch (SmartPropModifierEvaluator.GetClassName(criteria))
                {
                    case "PathPosition":
                        if (!MatchesPathPosition(criteria, instanceIndex, instanceCount, context))
                        {
                            return false;
                        }

                        break;

                    case "EndCap":
                        if (!MatchesEndCap(criteria, instanceIndex, instanceCount))
                        {
                            return false;
                        }

                        break;

                    case "IsValid":
                        if (criteria.TryGetValue("m_Expression", out var expression))
                        {
                            if (MathF.Abs(context.ResolveScalar(expression, 1f)) < 1e-6f)
                            {
                                return false;
                            }
                        }

                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// Reads a child's LinearLength criteria. Returns false when the child declares
        /// none, leaving <paramref name="linearLength"/> at its default.
        /// </summary>
        public static bool TryGetLinearLength(KVObject? child, SmartPropEvaluationContext context, out SmartPropLinearLength linearLength)
        {
            linearLength = default;
            if (child is null
                || !child.TryGetValue("m_SelectionCriteria", out var criteriaNode)
                || !criteriaNode.IsArray)
            {
                return false;
            }

            var span = criteriaNode.AsArraySpan();
            for (var i = 0; i < span.Length; i++)
            {
                var criteria = span[i];
                if (criteria.ValueType != KVValueType.Collection
                    || IsDisabled(criteria)
                    || SmartPropModifierEvaluator.GetClassName(criteria) != "LinearLength")
                {
                    continue;
                }

                linearLength = new SmartPropLinearLength(
                    context.ResolveScalar(GetOrDefault(criteria, "m_flLength")),
                    GetBool(criteria, "m_bAllowScale", fallback: true),
                    context.ResolveScalar(GetOrDefault(criteria, "m_flMinLength")),
                    context.ResolveScalar(GetOrDefault(criteria, "m_flMaxLength")));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a child's ChoiceWeight criteria value, defaulting to 1 when the child
        /// declares none. Weighted parents draw children proportionally to this value.
        /// </summary>
        public static float GetChoiceWeight(KVObject? child, SmartPropEvaluationContext context)
        {
            if (child is null
                || !child.TryGetValue("m_SelectionCriteria", out var criteriaNode)
                || !criteriaNode.IsArray)
            {
                return 1f;
            }

            var span = criteriaNode.AsArraySpan();
            for (var i = 0; i < span.Length; i++)
            {
                var criteria = span[i];
                if (criteria.ValueType == KVValueType.Collection
                    && !IsDisabled(criteria)
                    && SmartPropModifierEvaluator.GetClassName(criteria) == "ChoiceWeight")
                {
                    return context.ResolveScalar(GetOrDefault(criteria, "m_flWeight"), 1f);
                }
            }

            return 1f;
        }

        private static bool MatchesPathPosition(KVObject criteria, int instanceIndex, int instanceCount, SmartPropEvaluationContext context)
        {
            var isStart = instanceIndex == 0;
            var isEnd = instanceIndex == instanceCount - 1;

            if (isStart && !GetBool(criteria, "m_bAllowAtStart", fallback: true))
            {
                return false;
            }

            if (isEnd && !GetBool(criteria, "m_bAllowAtEnd", fallback: true))
            {
                return false;
            }

            switch (GetPositionMode(criteria, context))
            {
                case "START_AND_END":
                case "CONTROL_POINTS":
                    return isStart || isEnd;

                case "START":
                    return isStart;

                case "END":
                    return isEnd;

                case "INTERNAL":
                    return !isStart && !isEnd;

                case "NTH":
                {
                    var step = MathF.Max(1f, context.ResolveScalar(GetOrDefault(criteria, "m_nPlaceEveryNthPosition"), 1f));
                    var offset = context.ResolveScalar(GetOrDefault(criteria, "m_nNthPositionIndexOffset"));

                    // Normalize to a non-negative remainder so negative offsets behave
                    // the same as the engine's modulo
                    var remainder = ((int)(instanceIndex - offset) % (int)step + (int)step) % (int)step;
                    return remainder == 0;
                }

                default:
                    return true;
            }
        }

        private static bool MatchesEndCap(KVObject criteria, int instanceIndex, int instanceCount)
        {
            var isStart = instanceIndex == 0;
            var isEnd = instanceIndex == instanceCount - 1;

            if (!isStart && !isEnd)
            {
                return false;
            }

            if (isStart && !GetBool(criteria, "m_bStart", fallback: false))
            {
                return false;
            }

            if (isEnd && !GetBool(criteria, "m_bEnd", fallback: false))
            {
                return false;
            }

            return true;
        }

        private static string GetPositionMode(KVObject criteria, SmartPropEvaluationContext context)
        {
            if (!criteria.TryGetValue("m_PlaceAtPositions", out var node))
            {
                return "ALL";
            }

            var text = string.Empty;
            switch (node.ValueType)
            {
                case KVValueType.Int32:
                case KVValueType.Int64:
                case KVValueType.UInt32:
                case KVValueType.UInt64:
                case KVValueType.FloatingPoint:
                case KVValueType.FloatingPoint64:
                    return ((int)context.ResolveScalar(node)) switch
                    {
                        1 => "NTH",
                        2 => "START_AND_END",
                        3 => "CONTROL_POINTS",
                        _ => "ALL",
                    };

                case KVValueType.String:
                    text = ((string)node).Trim().ToUpperInvariant();
                    break;

                default:
                    text = context.ResolveString(node, "ALL").Trim().ToUpperInvariant();
                    break;
            }

            return text switch
            {
                "0" => "ALL",
                "1" => "NTH",
                "2" => "START_AND_END",
                "3" => "CONTROL_POINTS",
                _ => text,
            };
        }

        private static bool IsDisabled(KVObject node)
        {
            if (!node.TryGetValue("m_bEnabled", out var enabled))
            {
                return false;
            }

            return enabled.ValueType switch
            {
                KVValueType.Boolean => !(bool)enabled,
                KVValueType.String => ((string)enabled).Trim().Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        private static KVObject? GetOrDefault(KVObject node, string key)
            => node.TryGetValue(key, out var value) ? value : null;

        private static bool GetBool(KVObject node, string key, bool fallback)
        {
            if (!node.TryGetValue(key, out var value))
            {
                return fallback;
            }

            return value.ValueType switch
            {
                KVValueType.Boolean => (bool)value,
                KVValueType.String => !((string)value).Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
                    && !((string)value).Trim().Equals("0", StringComparison.Ordinal),
                KVValueType.Int32 or KVValueType.Int64 or KVValueType.UInt32 or KVValueType.UInt64
                    or KVValueType.FloatingPoint or KVValueType.FloatingPoint64 => MathF.Abs((float)value) > 1e-6f,
                _ => fallback,
            };
        }
    }
}
