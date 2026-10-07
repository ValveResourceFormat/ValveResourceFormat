using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Modifiers
{
    /// <summary>Choice selection and tint application shared by the color and material group operations.</summary>
    internal static class ChoiceSelection
    {
        /// <summary>
        /// RANDOM draws once in [0, sum of weights) and takes the first choice whose running sum exceeds the draw;
        /// FIRST takes 0 and SPECIFIC the clamped index. Returns -1 without choices.
        /// </summary>
        public static int Select(SmartPropContext ctx, int mode, SmartPropAttribute specificIndex, IReadOnlyList<SmartPropAttribute> weights)
        {
            var count = weights.Count;

            if (count == 0)
            {
                return -1;
            }

            switch (mode)
            {
                case 0:
                {
                    var values = new float[count];
                    var total = 0f;

                    for (var i = 0; i < count; i++)
                    {
                        values[i] = weights[i].EvaluateFloat(ctx);
                        total += values[i];
                    }

                    var roll = ctx.GetRandomStream().RandomFloat(0f, total);
                    var sum = 0f;

                    for (var i = 0; i < count; i++)
                    {
                        sum += values[i];

                        if (sum > roll)
                        {
                            return i;
                        }
                    }

                    return 0;
                }
                case 2:
                    return Math.Clamp(specificIndex.EvaluateInt(ctx), 0, count - 1);
                default:
                    return 0;
            }
        }

        public static Color32 ApplyColorMode(SmartPropContext ctx, Color32 color, int mode) => mode switch
        {
            2 => color,
            1 => SmartPropMath.MultiplyColor(color, ctx.State.Tint),
            _ => SmartPropMath.MultiplyColor(color, ctx.ObjectTint),
        };
    }

    /// <summary>A <c>CColorGradient</c>: stops sorted by position, sampled with per-channel truncating lerp.</summary>
    internal sealed class SmartPropColorGradient
    {
        private readonly List<(float Position, Color32 Color)> stops = [];

        public SmartPropColorGradient(KVObject? data)
        {
            if (data == null)
            {
                return;
            }

            foreach (var stop in data.GetArray("m_Stops") ?? [])
            {
                var color = SmartPropValue.FromKV(SmartPropParser.Get(stop, "m_Color")).GetColor();
                stops.Add((Math.Clamp(stop.GetFloatProperty("m_flPosition"), 0f, 1f), color));
            }

            stops.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        }

        public int Count => stops.Count;

        public Color32 StopColor(int index) => stops[index].Color;

        public Color32 Sample(float t)
        {
            if (stops.Count == 0)
            {
                return Color32.White;
            }

            if (t <= stops[0].Position)
            {
                return stops[0].Color;
            }

            for (var i = 1; i < stops.Count; i++)
            {
                if (t <= stops[i].Position)
                {
                    var (p0, c0) = stops[i - 1];
                    var (p1, c1) = stops[i];
                    return p1 - p0 < 1e-5f ? c0 : SmartPropMath.LerpColor(c0, c1, (t - p0) / (p1 - p0));
                }
            }

            return stops[^1].Color;
        }
    }

    /// <summary>CSmartPropOperation_SetTintColor: picks a color from a list and applies it to the current tint.</summary>
    internal sealed class SetTintColorOperation : SmartPropModifier
    {
        private readonly SmartPropAttribute selectionMode;
        private readonly SmartPropAttribute colorSelection;
        private readonly SmartPropAttribute mode;
        private readonly List<SmartPropAttribute> colors = [];
        private readonly List<SmartPropAttribute> weights = [];

        public SetTintColorOperation(KVObject data) : base(data)
        {
            selectionMode = Attr.Enum(data, "m_SelectionMode", 0);
            colorSelection = Attr.Int(data, "m_ColorSelection", 0);
            mode = Attr.Enum(data, "m_Mode", 0);

            foreach (var choice in data.GetArray("m_ColorChoices") ?? [])
            {
                colors.Add(Attr.Color(choice, "m_Color", Color32.White));
                weights.Add(Attr.Float(choice, "m_flWeight", 1f));
            }
        }

        public override bool Apply(SmartPropContext ctx)
        {
            var index = ChoiceSelection.Select(ctx, selectionMode.EvaluateEnum(ctx, SmartPropEnums.ChoiceSelectionMode), colorSelection, weights);

            if (index >= 0)
            {
                var color = colors[index].EvaluateColor(ctx);
                ctx.State.Tint = ChoiceSelection.ApplyColorMode(ctx, color, mode.EvaluateEnum(ctx, SmartPropEnums.ApplyColorMode));
            }

            return false;
        }
    }

    /// <summary>CSmartPropOperation_RandomColorTintColor: samples a gradient and applies the color to the current tint.</summary>
    internal sealed class RandomColorTintColorOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute selectionMode = Attr.Enum(data, "m_SelectionMode", 0);
        private readonly SmartPropAttribute colorPosition = Attr.Float(data, "m_ColorPosition", 0f);
        private readonly int mode = SmartPropEnums.FromName(SmartPropEnums.ApplyColorMode, Attr.Plain(data, "m_Mode"));
        private readonly SmartPropColorGradient gradient = new(SmartPropParser.Get(data, "m_Gradient"));

        public override bool Apply(SmartPropContext ctx)
        {
            var t = selectionMode.EvaluateEnum(ctx, SmartPropEnums.ChoiceSelectionMode) switch
            {
                0 => ctx.GetRandomStream().RandomFloat(0f, 1f),
                2 => colorPosition.EvaluateFloat(ctx),
                _ => 0f,
            };

            ctx.State.Tint = ChoiceSelection.ApplyColorMode(ctx, gradient.Sample(t), mode);
            return false;
        }
    }

    /// <summary>
    /// CSmartPropOperation_MaterialTint: sets the tint of one material for the models placed after it. The tint set is
    /// copied before the first change in a scope and is not restored when the element ends.
    /// </summary>
    internal sealed class MaterialTintOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute material = Attr.String(data, "m_Material");
        private readonly SmartPropAttribute selectionMode = Attr.Enum(data, "m_SelectionMode", 0);
        private readonly SmartPropAttribute color = Attr.Color(data, "m_Color", Color32.White);
        private readonly SmartPropColorGradient gradient = new(SmartPropParser.Get(data, "m_Gradient"));
        private readonly SmartPropAttribute colorPosition = Attr.Float(data, "m_ColorPosition", 0f);

        public override bool Apply(SmartPropContext ctx)
        {
            var materialName = material.EvaluateString(ctx);

            var tint = selectionMode.EvaluateEnum(ctx, SmartPropEnums.ColorSelectionMode) switch
            {
                0 => color.EvaluateColor(ctx),
                1 => gradient.Sample(ctx.GetRandomStream().RandomFloat(0f, 1f)),
                2 => gradient.Count switch
                {
                    0 => gradient.Sample(0f),
                    1 => gradient.StopColor(0),
                    _ => gradient.StopColor(ctx.GetRandomStream().RandomInt(0, gradient.Count - 1)),
                },
                3 => gradient.Sample(colorPosition.EvaluateFloat(ctx)),
                _ => Color32.White,
            };

            if (materialName.Length > 0)
            {
                if (ctx.MaterialTintSetIndex < 0 || !ctx.MaterialTintSetOwned)
                {
                    var sets = ctx.Output.MaterialTintSets;
                    sets.Add(ctx.MaterialTintSetIndex >= 0 ? new(sets[ctx.MaterialTintSetIndex], StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase));
                    ctx.MaterialTintSetIndex = sets.Count - 1;
                    ctx.MaterialTintSetOwned = true;
                }

                ctx.Output.MaterialTintSets[ctx.MaterialTintSetIndex][materialName] = tint;
            }

            return false;
        }
    }

    /// <summary>
    /// CSmartPropOperation_MaterialOverride: replaces materials of the models placed after it, copying the override
    /// set before the first change in a scope.
    /// </summary>
    internal sealed class MaterialOverrideOperation : SmartPropModifier
    {
        private readonly SmartPropAttribute clearCurrentOverrides;
        private readonly List<(SmartPropAttribute Original, SmartPropAttribute Replacement)> replacements = [];

        public MaterialOverrideOperation(KVObject data) : base(data)
        {
            clearCurrentOverrides = Attr.Bool(data, "m_bClearCurrentOverrides", false);

            foreach (var replacement in data.GetArray("m_MaterialReplacements") ?? [])
            {
                replacements.Add((Attr.String(replacement, "m_OriginalMaterial"), Attr.String(replacement, "m_ReplacementMaterial")));
            }
        }

        public override bool Apply(SmartPropContext ctx)
        {
            var sets = ctx.Output.MaterialOverrideSets;

            if (clearCurrentOverrides.EvaluateBool(ctx))
            {
                if (ctx.MaterialOverrideSetOwned && ctx.MaterialOverrideSetIndex >= 0)
                {
                    sets[ctx.MaterialOverrideSetIndex].Clear();
                }
                else
                {
                    ctx.MaterialOverrideSetIndex = -1;
                }
            }

            foreach (var (original, replacement) in replacements)
            {
                if (ctx.MaterialOverrideSetIndex < 0 || !ctx.MaterialOverrideSetOwned)
                {
                    sets.Add(ctx.MaterialOverrideSetIndex >= 0 ? new(sets[ctx.MaterialOverrideSetIndex], StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase));
                    ctx.MaterialOverrideSetIndex = sets.Count - 1;
                    ctx.MaterialOverrideSetOwned = true;
                }

                sets[ctx.MaterialOverrideSetIndex][original.EvaluateString(ctx)] = replacement.EvaluateString(ctx);
            }

            return false;
        }
    }

    /// <summary>CSmartPropOperation_SetMateraialGroupChoice: picks a material group name into a variable.</summary>
    internal sealed class SetMaterialGroupChoiceOperation : SmartPropModifier
    {
        private readonly string variableName;
        private readonly SmartPropAttribute selectionMode;
        private readonly SmartPropAttribute choiceSelection;
        private readonly List<SmartPropAttribute> names = [];
        private readonly List<SmartPropAttribute> weights = [];

        public SetMaterialGroupChoiceOperation(KVObject data) : base(data)
        {
            variableName = Attr.Plain(data, "m_VariableName");
            selectionMode = Attr.Enum(data, "m_SelectionMode", 0);
            choiceSelection = Attr.Int(data, "m_ChoiceSelection", 0);

            foreach (var choice in data.GetArray("m_MaterialGroupChoices") ?? [])
            {
                names.Add(Attr.String(choice, "m_MaterialGroupName"));
                weights.Add(Attr.Float(choice, "m_flWeight", 1f));
            }
        }

        public override bool Apply(SmartPropContext ctx)
        {
            var index = ChoiceSelection.Select(ctx, selectionMode.EvaluateEnum(ctx, SmartPropEnums.ChoiceSelectionMode), choiceSelection, weights);

            if (index >= 0)
            {
                ctx.SetVariable(variableName, SmartPropValue.FromString(names[index].EvaluateString(ctx)));
            }

            return false;
        }
    }
}
