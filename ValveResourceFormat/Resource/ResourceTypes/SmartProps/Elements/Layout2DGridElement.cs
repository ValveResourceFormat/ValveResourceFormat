using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_Layout2DGrid: evaluates the children at the cells of a grid, rows along the length outside,
    /// columns along the width inside.
    /// </summary>
    internal sealed class Layout2DGridElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private const int MaxCells = 4096;

        private readonly SmartPropAttribute width = Attr.Float(data, "m_flWidth", 100f);
        private readonly SmartPropAttribute length = Attr.Float(data, "m_flLength", 100f);
        private readonly SmartPropAttribute verticalLength = Attr.Bool(data, "m_bVerticalLength", false);
        private readonly SmartPropAttribute gridArrangement = Attr.Enum(data, "m_GridArrangement", 0);
        private readonly SmartPropAttribute gridOriginMode = Attr.Enum(data, "m_GridOriginMode", 0);
        private readonly SmartPropAttribute countW = Attr.Int(data, "m_nCountW", 5);
        private readonly SmartPropAttribute countL = Attr.Int(data, "m_nCountL", 5);
        private readonly SmartPropAttribute spacingWidth = Attr.Float(data, "m_flSpacingWidth", 20f);
        private readonly SmartPropAttribute spacingLength = Attr.Float(data, "m_flSpacingLength", 20f);
        private readonly SmartPropAttribute alternateShift = Attr.Bool(data, "m_bAlternateShift", false);
        private readonly SmartPropAttribute alternateShiftWidth = Attr.Float(data, "m_flAlternateShiftWidth", 0.5f);
        private readonly SmartPropAttribute alternateShiftLength = Attr.Float(data, "m_flAlternateShiftLength", 0f);

        public override void Evaluate(SmartPropContext ctx)
        {
            if (Children.Count == 0)
            {
                return;
            }

            var w = width.EvaluateFloat(ctx);
            var l = length.EvaluateFloat(ctx);
            var vertical = verticalLength.EvaluateBool(ctx);
            var isFill = gridArrangement.EvaluateEnum(ctx, SmartPropEnums.GridPlacementMode) == 1;
            var isCenter = gridOriginMode.EvaluateEnum(ctx, SmartPropEnums.GridOriginBasis) == 0;

            int nW, nL;
            float stepW, stepL, x0, y0;
            var shiftW = 0f;
            var shiftL = 0f;

            if (isFill)
            {
                stepW = MathF.Abs(spacingWidth.EvaluateFloat(ctx));
                stepL = MathF.Abs(spacingLength.EvaluateFloat(ctx));
                nW = Math.Max(Math.Abs((int)(w / stepW)), 1);
                nL = Math.Max(Math.Abs((int)(l / stepL)), 1);
                x0 = isCenter ? -(nW - 1) * stepW / 2f : 0f;
                y0 = isCenter ? -(nL - 1) * stepL / 2f : 0f;

                if (w < 0f)
                {
                    x0 = -x0;
                    stepW = -stepW;
                }

                if (l < 0f)
                {
                    y0 = -y0;
                    stepL = -stepL;
                }
            }
            else
            {
                nW = Math.Max(countW.EvaluateInt(ctx), 1);
                nL = Math.Max(countL.EvaluateInt(ctx), 1);
                stepW = nW == 1 ? 0f : w / (nW - 1);
                stepL = nL == 1 ? 0f : l / (nL - 1);

                if (alternateShift.EvaluateBool(ctx))
                {
                    shiftW = alternateShiftWidth.EvaluateFloat(ctx) * stepW;
                    shiftL = alternateShiftLength.EvaluateFloat(ctx) * stepL;
                    x0 = isCenter ? -w / 2f : 0f;
                    y0 = isCenter ? -l / 2f : 0f;
                }
                else
                {
                    x0 = isCenter && nW != 1 ? -w / 2f : 0f;
                    y0 = isCenter && nL != 1 ? -l / 2f : 0f;
                }
            }

            var saved = ctx.SaveInstanceValues();

            if ((long)nW * nL > MaxCells)
            {
                ctx.InstanceCount = 1;
                ctx.InstanceIndex = 0;
                ctx.EvaluateChildren(Children);
                ctx.RestoreInstanceValues(saved);
                return;
            }

            var parent = ctx.Transform;

            for (var r = 0; r < nL; r++)
            {
                for (var c = 0; c < nW; c++)
                {
                    var x = c * stepW + x0 + (r % 2 == 1 ? shiftW : 0f);
                    var y = r * stepL + y0 + (c % 2 == 1 ? shiftL : 0f);
                    var index = r * nW + c;

                    ctx.PushPath(index);
                    ctx.InstanceCount = nW * nL;
                    ctx.InstanceIndex = index;
                    ctx.Transform = parent.Concat(new SmartPropTransform(vertical ? new Vector3(x, 0f, y) : new Vector3(x, y, 0f), 1f, Quaternion.Identity));
                    ctx.EvaluateChildren(Children);
                    ctx.PopPath();
                }
            }

            ctx.Transform = parent;
            ctx.RestoreInstanceValues(saved);
        }
    }
}
