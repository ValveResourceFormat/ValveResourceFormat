using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Modifiers
{
    /// <summary>
    /// CSmartPropOperation_CreateLocator: a Hammer move/rotate/scale handle at an offset from the current transform. When
    /// configurable, a stored edit, masked by the allowed axes, is applied to the current transform. Translation is not
    /// allowed while a deformer is active, and the handle follows the deformer.
    /// </summary>
    internal sealed class CreateLocatorOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string name = Attr.Plain(data, "m_LocatorName");
        private readonly SmartPropAttribute offset = Attr.Vector3(data, "m_vOffset", Vector3.Zero);
        private readonly SmartPropAttribute displayScale = Attr.Float(data, "m_flDisplayScale", 1f);
        private readonly SmartPropAttribute configurable = Attr.Bool(data, "m_bConfigurable", true);
        private readonly SmartPropAttribute allowTranslation = Attr.Bool(data, "m_bAllowTranslation", true);
        private readonly SmartPropAttribute allowRotation = Attr.Bool(data, "m_bAllowRotation", true);
        private readonly SmartPropAttribute allowScale = Attr.Bool(data, "m_bAllowScale", false);

        public override bool Apply(SmartPropContext ctx)
        {
            var isConfigurable = configurable.EvaluateBool(ctx);
            var (translation, rotation, scale) = (false, false, false);

            if (isConfigurable)
            {
                translation = allowTranslation.EvaluateBool(ctx);
                rotation = allowRotation.EvaluateBool(ctx);
                scale = allowScale.EvaluateBool(ctx);
            }

            var handleTransform = ctx.Transform;
            var handleOffset = offset.EvaluateVector3(ctx);

            if (handleOffset != Vector3.Zero)
            {
                handleTransform = handleTransform.Concat(new SmartPropTransform(handleOffset, 1f, Quaternion.Identity));
            }

            if (ctx.DeformerIndex >= 0)
            {
                handleTransform = SmartPropLattice.DeformTransform(ctx.Output.Deformers[ctx.DeformerIndex], handleTransform);
                translation = false;
            }

            var path = ctx.CurrentElementPath;

            if (!ctx.Output.TryClaimHandle(path, name))
            {
                return false;
            }

            if (isConfigurable)
            {
                var delta = StoredHandleEdits.FindOrAdd(ctx, name).DeltaTransform;
                var masked = new SmartPropTransform(
                    translation ? delta.Position : Vector3.Zero,
                    scale ? delta.Scale : 1f,
                    rotation ? delta.Rotation : Quaternion.Identity);

                ctx.Transform = ctx.Transform.Concat(masked);
            }

            ctx.Output.Handles.Add(new SmartPropHandle
            {
                ElementPath = path,
                Name = name,
                Kind = SmartPropHandleKind.Locator,
                Transform = handleTransform,
                Configurable = isConfigurable,
                AllowedEdits = (translation, rotation, scale),
                DisplaySize = displayScale.EvaluateFloat(ctx),
            });

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
        private readonly SmartPropAttribute offset = Attr.Vector3(data, "m_vOffset", Vector3.Zero);
        private readonly SmartPropAttribute rotationAxis = Attr.Vector3(data, "m_vRotationAxis", Vector3.UnitZ);
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 2);
        private readonly SmartPropAttribute displayRadius = Attr.Float(data, "m_flDisplayRadius", 16f);
        private readonly SmartPropAttribute displayColor = Attr.Color(data, "m_DisplayColor", new Color32(0xAA, 0xAA, 0x6E, 0xFF));
        private readonly SmartPropAttribute applyToCurrentTransform = Attr.Bool(data, "m_bApplyToCurrentTransform", true);
        private readonly SmartPropAttribute snappingIncrement = Attr.Float(data, "m_flSnappingIncrement", 0f);
        private readonly SmartPropAttribute initialAngle = Attr.Float(data, "m_flInitialAngle", 0f);
        private readonly SmartPropAttribute enforceLimits = Attr.Bool(data, "m_bEnforceLimits", false);
        private readonly SmartPropAttribute minAngle = Attr.Float(data, "m_flMinAngle", 0f);
        private readonly SmartPropAttribute maxAngle = Attr.Float(data, "m_flMaxAngle", 0f);
        private readonly string outputVariable = Attr.Plain(data, "m_OutputVariable");

        public override bool Apply(SmartPropContext ctx)
        {
            var enforce = enforceLimits.EvaluateBool(ctx);
            var path = ctx.CurrentElementPath;

            if (!ctx.Output.TryClaimHandle(path, name))
            {
                return false;
            }

            var axis = rotationAxis.EvaluateVector3(ctx);
            var axisSpace = Space(ctx, space);
            var forward = MathUtils.SafeNormalize(ctx.ConvertDirection(SmartPropSpace.World, axisSpace, axis));
            var up = MathUtils.SafeNormalize(ctx.ConvertDirection(SmartPropSpace.World, axisSpace, SmartPropMath.PerpendicularComponent(Vector3.UnitZ, axis)));
            var left = MathUtils.SafeNormalize(Vector3.Cross(up, forward));
            var position = ctx.Transform.TransformPoint(offset.EvaluateVector3(ctx));
            var handleTransform = SmartPropTransform.FromBasis(forward, left, up, position) with { Scale = ctx.Transform.Scale };

            var initial = initialAngle.EvaluateFloat(ctx);
            (float Min, float Max) limits = enforce ? (minAngle.EvaluateFloat(ctx), maxAngle.EvaluateFloat(ctx)) : (0f, 0f);

            var handle = new SmartPropHandle
            {
                ElementPath = path,
                Name = name,
                Kind = SmartPropHandleKind.Rotator,
                Transform = handleTransform,
                SnappingIncrement = snappingIncrement.EvaluateFloat(ctx),
                DisplaySize = displayRadius.EvaluateFloat(ctx),
                DisplayColor = displayColor.EvaluateColor(ctx),
                InitialAngle = initial,
                AngleLimits = enforce ? limits : null,
            };

            ctx.Output.Handles.Add(handle);

            var angle = initial + StoredHandleEdits.FindOrAdd(ctx, name).DeltaValue;

            if (enforce)
            {
                angle = ClampAngleToRange(angle, limits.Min, limits.Max);
            }

            if (angle != 0f && applyToCurrentTransform.EvaluateBool(ctx))
            {
                var rotation = SmartPropMath.QuaternionFromAxisAngleDegrees(ctx.ConvertDirection(SmartPropSpace.Element, axisSpace, axis), angle);
                ctx.Transform = ctx.Transform.Concat(new SmartPropTransform(Vector3.Zero, 1f, rotation));
            }

            ctx.SetVariable(outputVariable, SmartPropValue.FromFloat(angle));
            return false;
        }

        /// <summary>
        /// Clamps an angle in degrees into [min, max] across the 360 degree wrap: relative to the middle of the wrapped
        /// range, the angle is folded into [-180, 180] and clamped to the half-widths.
        /// </summary>
        public static float ClampAngleToRange(float angle, float min, float max)
        {
            if (!(min <= max) || !(max - min < 360f))
            {
                return angle;
            }

            static float Wrap(float value)
            {
                var wrapped = value % 360f;
                return wrapped < 0f ? wrapped + 360f : wrapped;
            }

            var wrappedAngle = Wrap(angle);
            var wrappedMin = Wrap(min);
            var wrappedMax = Wrap(max);

            if (wrappedMin > wrappedMax)
            {
                wrappedMin -= 360f;
            }

            var middle = (wrappedMax + wrappedMin) * 0.5f;
            var low = MathF.Min(MathF.Max(wrappedMin - middle, -180f), 0f);
            var high = MathF.Min(MathF.Max(wrappedMax - middle, 0f), 180f);
            var relative = (wrappedAngle - middle) % 360f;

            if (relative < -180f)
            {
                relative += 360f;
            }
            else if (relative > 180f)
            {
                relative -= 360f;
            }

            return MathF.Min(MathF.Max(relative, low), high) + (min + max) * 0.5f;
        }
    }

    /// <summary>
    /// CSmartPropOperation_CreateSizer: a Hammer box handle. Writes the initial extents plus a stored edit, clamped to
    /// the constraints, to up to six variables; without any output variable it does nothing.
    /// </summary>
    internal sealed class CreateSizerOperation : SmartPropModifier
    {
        private readonly string name;
        private readonly SmartPropAttribute displayModel;
        private readonly SmartPropAttribute[] initialMin = new SmartPropAttribute[3];
        private readonly SmartPropAttribute[] initialMax = new SmartPropAttribute[3];
        private readonly SmartPropAttribute[] constraintMin = new SmartPropAttribute[3];
        private readonly SmartPropAttribute[] constraintMax = new SmartPropAttribute[3];
        private readonly string[] outputMin = new string[3];
        private readonly string[] outputMax = new string[3];

        public CreateSizerOperation(KVObject data) : base(data)
        {
            name = Attr.Plain(data, "m_Name");
            displayModel = Attr.Bool(data, "m_bDisplayModel", false);
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
            var editableMin = (outputMin[0].Length > 0, outputMin[1].Length > 0, outputMin[2].Length > 0);
            var editableMax = (outputMax[0].Length > 0, outputMax[1].Length > 0, outputMax[2].Length > 0);

            if (editableMin == (false, false, false) && editableMax == (false, false, false))
            {
                return false;
            }

            var showModel = displayModel.EvaluateBool(ctx);
            var path = ctx.CurrentElementPath;

            if (!ctx.Output.TryClaimHandle(path, name))
            {
                return false;
            }

            var initialExtentsMin = EvaluateZyx(ctx, initialMin);
            var initialExtentsMax = EvaluateZyx(ctx, initialMax);
            var limitsMin = EvaluateZyx(ctx, constraintMin);
            var limitsMax = EvaluateZyx(ctx, constraintMax);

            ctx.Output.Handles.Add(new SmartPropHandle
            {
                ElementPath = path,
                Name = name,
                Kind = SmartPropHandleKind.Sizer,
                Transform = ctx.Transform,
                InitialExtents = (initialExtentsMin, initialExtentsMax),
                ExtentLimits = (limitsMin, limitsMax),
                DisplayModel = showModel,
                EditableMin = editableMin,
                EditableMax = editableMax,
            });

            var edit = StoredHandleEdits.FindOrAdd(ctx, name);
            var min = initialExtentsMin + edit.DeltaMin;
            var max = initialExtentsMax + edit.DeltaMax;

            for (var i = 0; i < 3; i++)
            {
                if (limitsMax[i] > limitsMin[i])
                {
                    min[i] = MathF.Min(MathF.Max(min[i], limitsMin[i]), limitsMax[i]);
                    max[i] = MathF.Min(MathF.Max(max[i], limitsMin[i]), limitsMax[i]);
                }
            }

            for (var i = 0; i < 3; i++)
            {
                ctx.SetVariable(outputMin[i], SmartPropValue.FromFloat(min[i]));
                ctx.SetVariable(outputMax[i], SmartPropValue.FromFloat(max[i]));
            }

            return false;
        }

        private static Vector3 EvaluateZyx(SmartPropContext ctx, SmartPropAttribute[] attributes)
        {
            var z = attributes[2].EvaluateFloat(ctx);
            var y = attributes[1].EvaluateFloat(ctx);
            var x = attributes[0].EvaluateFloat(ctx);
            return new Vector3(x, y, z);
        }
    }

    internal static class StoredHandleEdits
    {
        /// <summary>The stored edit of a handle of the current element, added unedited when missing.</summary>
        public static SmartPropHandleEdit FindOrAdd(SmartPropContext ctx, string name)
        {
            var edits = ctx.GetElementState().HandleEdits;

            if (!edits.TryGetValue(name, out var edit))
            {
                edit = new SmartPropHandleEdit();
                edits[name] = edit;
            }

            return edit;
        }
    }
}
