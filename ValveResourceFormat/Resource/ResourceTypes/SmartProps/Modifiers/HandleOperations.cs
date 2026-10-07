using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Modifiers
{
    /// <summary>
    /// CSmartPropOperation_CreateLocator: a Hammer move/rotate/scale handle. A stored edit, masked by the allowed
    /// axes, is applied to the current transform; without one nothing changes.
    /// </summary>
    internal sealed class CreateLocatorOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string name = Attr.Plain(data, "m_LocatorName");
        private readonly SmartPropAttribute configurable = Attr.Bool(data, "m_bConfigurable", true);
        private readonly SmartPropAttribute allowTranslation = Attr.Bool(data, "m_bAllowTranslation", true);
        private readonly SmartPropAttribute allowRotation = Attr.Bool(data, "m_bAllowRotation", true);
        private readonly SmartPropAttribute allowScale = Attr.Bool(data, "m_bAllowScale", false);

        public override bool Apply(SmartPropContext ctx)
        {
            if (!configurable.EvaluateBool(ctx))
            {
                return false;
            }

            var translation = allowTranslation.EvaluateBool(ctx) && ctx.DeformerIndex < 0;
            var rotation = allowRotation.EvaluateBool(ctx);
            var scale = allowScale.EvaluateBool(ctx);

            ctx.Output.Handles.Add(new SmartPropHandle
            {
                ElementPath = ctx.CurrentElementPath,
                Name = name,
                Kind = SmartPropHandleKind.Locator,
                AllowedEdits = (translation, rotation, scale),
            });

            if (ctx.GetElementState().HandleEdits.TryGetValue(name, out var edit))
            {
                var delta = edit.DeltaTransform;
                var masked = new SmartPropTransform(
                    translation ? delta.Position : Vector3.Zero,
                    scale ? delta.Scale : 1f,
                    rotation ? delta.Rotation : Quaternion.Identity);

                ctx.Transform = ctx.Transform.Concat(masked);
            }

            return false;
        }
    }

    /// <summary>
    /// CSmartPropOperation_CreateRotator: a Hammer angle handle. Rotates the current transform by the initial angle
    /// plus a stored edit about an axis and writes the angle to a variable.
    /// </summary>
    internal sealed class CreateRotatorOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string name = Attr.Plain(data, "m_Name");
        private readonly SmartPropAttribute rotationAxis = Attr.Vector3(data, "m_vRotationAxis", Vector3.UnitZ);
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 2);
        private readonly SmartPropAttribute applyToCurrentTransform = Attr.Bool(data, "m_bApplyToCurrentTransform", true);
        private readonly SmartPropAttribute initialAngle = Attr.Float(data, "m_flInitialAngle", 0f);
        private readonly SmartPropAttribute enforceLimits = Attr.Bool(data, "m_bEnforceLimits", false);
        private readonly SmartPropAttribute minAngle = Attr.Float(data, "m_flMinAngle", 0f);
        private readonly SmartPropAttribute maxAngle = Attr.Float(data, "m_flMaxAngle", 0f);
        private readonly string outputVariable = Attr.Plain(data, "m_OutputVariable");

        public override bool Apply(SmartPropContext ctx)
        {
            var initial = initialAngle.EvaluateFloat(ctx);
            var angle = initial;

            if (ctx.GetElementState().HandleEdits.TryGetValue(name, out var edit))
            {
                angle += edit.DeltaValue;
            }

            (float Min, float Max)? limits = enforceLimits.EvaluateBool(ctx) ? (minAngle.EvaluateFloat(ctx), maxAngle.EvaluateFloat(ctx)) : null;

            if (limits is var (min, max))
            {
                angle = ClampAngleToRange(angle, min, max);
            }

            ctx.Output.Handles.Add(new SmartPropHandle
            {
                ElementPath = ctx.CurrentElementPath,
                Name = name,
                Kind = SmartPropHandleKind.Rotator,
                InitialAngle = initial,
                AngleLimits = limits,
            });

            if (angle != 0f && applyToCurrentTransform.EvaluateBool(ctx))
            {
                var axis = ctx.ConvertDirection(SmartPropSpace.Element, Space(ctx, space), rotationAxis.EvaluateVector3(ctx));

                if (axis != Vector3.Zero)
                {
                    var (sin, cos) = MathF.SinCos(angle * 0.017453292f * 0.5f);
                    ctx.Transform = ctx.Transform.Concat(new SmartPropTransform(Vector3.Zero, 1f, new Quaternion(axis * sin, cos)));
                }
            }

            ctx.SetVariable(outputVariable, SmartPropValue.FromFloat(angle));
            return false;
        }

        private static float ClampAngleToRange(float angle, float min, float max)
        {
            if (!(min <= max) || !(max - min < 360f))
            {
                return angle;
            }

            var middle = (min + max) * 0.5f;
            var relative = angle - middle;
            relative -= 360f * MathF.Floor((relative + 180f) / 360f);
            return Math.Clamp(middle + relative, min, max);
        }
    }

    /// <summary>
    /// CSmartPropOperation_CreateSizer: a Hammer box handle. Writes the initial extents plus a stored edit, clamped to
    /// the constraints, to up to six variables.
    /// </summary>
    internal sealed class CreateSizerOperation : SmartPropModifier
    {
        private readonly string name;
        private readonly SmartPropAttribute[] initialMin = new SmartPropAttribute[3];
        private readonly SmartPropAttribute[] initialMax = new SmartPropAttribute[3];
        private readonly SmartPropAttribute[] constraintMin = new SmartPropAttribute[3];
        private readonly SmartPropAttribute[] constraintMax = new SmartPropAttribute[3];
        private readonly string[] outputMin = new string[3];
        private readonly string[] outputMax = new string[3];

        public CreateSizerOperation(KVObject data) : base(data)
        {
            name = Attr.Plain(data, "m_Name");
            var axes = "XYZ";

            for (var i = 0; i < 3; i++)
            {
                initialMin[i] = Attr.Float(data, $"m_flInitialMin{axes[i]}", 0f);
                initialMax[i] = Attr.Float(data, $"m_flInitialMax{axes[i]}", 0f);
                constraintMin[i] = Attr.Float(data, $"m_flConstraintMin{axes[i]}", 0f);
                constraintMax[i] = Attr.Float(data, $"m_flConstraintMax{axes[i]}", 0f);
                outputMin[i] = Attr.Plain(data, $"m_OutputVariableMin{axes[i]}");
                outputMax[i] = Attr.Plain(data, $"m_OutputVariableMax{axes[i]}");
            }
        }

        public override bool Apply(SmartPropContext ctx)
        {
            if (outputMin.All(string.IsNullOrEmpty) && outputMax.All(string.IsNullOrEmpty))
            {
                return false;
            }

            ctx.GetElementState().HandleEdits.TryGetValue(name, out var edit);
            var initialExtentsMin = Vector3.Zero;
            var initialExtentsMax = Vector3.Zero;

            for (var i = 0; i < 3; i++)
            {
                initialExtentsMin[i] = initialMin[i].EvaluateFloat(ctx);
                initialExtentsMax[i] = initialMax[i].EvaluateFloat(ctx);
                var min = initialExtentsMin[i] + (edit?.DeltaMin[i] ?? 0f);
                var max = initialExtentsMax[i] + (edit?.DeltaMax[i] ?? 0f);
                var low = constraintMin[i].EvaluateFloat(ctx);
                var high = constraintMax[i].EvaluateFloat(ctx);

                if (high > low)
                {
                    min = Math.Clamp(min, low, high);
                    max = Math.Clamp(max, low, high);
                }

                ctx.SetVariable(outputMin[i], SmartPropValue.FromFloat(min));
                ctx.SetVariable(outputMax[i], SmartPropValue.FromFloat(max));
            }

            ctx.Output.Handles.Add(new SmartPropHandle
            {
                ElementPath = ctx.CurrentElementPath,
                Name = name,
                Kind = SmartPropHandleKind.Sizer,
                InitialExtents = (initialExtentsMin, initialExtentsMax),
            });

            return false;
        }
    }
}
