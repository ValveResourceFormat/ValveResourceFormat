using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Modifiers
{
    /// <summary>CSmartPropOperation_Translate: moves the current transform in its own frame, scaled by its scale.</summary>
    internal sealed class TranslateOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute position = Attr.Vector3(data, "m_vPosition", Vector3.Zero);
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 2);

        public override bool Apply(SmartPropContext ctx)
        {
            var offset = position.EvaluateVector3(ctx);
            var delta = ctx.ConvertDirection(SmartPropSpace.Element, Space(ctx, space), offset);
            ctx.Transform = ctx.Transform.Concat(new SmartPropTransform(delta, 1f, Quaternion.Identity));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_Rotate: rotates the current transform by angles in its own frame.</summary>
    internal sealed class RotateOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute rotation = Attr.Vector3(data, "m_vRotation", Vector3.Zero);

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.Transform = ctx.Transform.Concat(new SmartPropTransform(Vector3.Zero, 1f, SmartPropMath.AngleQuaternion(rotation.EvaluateVector3(ctx))));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_Scale: multiplies the current scale.</summary>
    internal sealed class ScaleOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute scale = Attr.Float(data, "m_flScale", 1f);

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.Transform = ctx.Transform with { Scale = ctx.Transform.Scale * scale.EvaluateFloat(ctx) };
            return false;
        }
    }

    /// <summary>CSmartPropOperation_SetPosition: sets the current position.</summary>
    internal sealed class SetPositionOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute position = Attr.Vector3(data, "m_vPosition", Vector3.Zero);
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 0);

        public override bool Apply(SmartPropContext ctx)
        {
            var value = position.EvaluateVector3(ctx);
            ctx.Transform = ctx.Transform with { Position = ctx.ConvertPosition(SmartPropSpace.World, Space(ctx, space), value) };
            return false;
        }
    }

    /// <summary>CSmartPropOperation_SetOrientation: sets the current rotation from a forward and an up vector.</summary>
    internal sealed class SetOrientationOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute forward = Attr.Vector3(data, "m_vForwardVector", Vector3.UnitX);
        private readonly SmartPropAttribute forwardSpace = Attr.Enum(data, "m_ForwardDirectionSpace", 0);
        private readonly SmartPropAttribute up = Attr.Vector3(data, "m_vUpVector", Vector3.UnitZ);
        private readonly SmartPropAttribute upSpace = Attr.Enum(data, "m_UpDirectionSpace", 0);
        private readonly SmartPropAttribute prioritizeUp = Attr.Bool(data, "m_bPrioritizeUp", false);

        public override bool Apply(SmartPropContext ctx)
        {
            var f = ctx.ConvertDirection(SmartPropSpace.World, Space(ctx, forwardSpace), forward.EvaluateVector3(ctx));
            var u = ctx.ConvertDirection(SmartPropSpace.World, Space(ctx, upSpace), up.EvaluateVector3(ctx));
            ctx.Transform = ctx.Transform with { Rotation = SmartPropMath.OrientationFromForwardUp(f, u, prioritizeUp.EvaluateBool(ctx)) };
            return false;
        }
    }

    /// <summary>CSmartPropOperation_ResetRotation: replaces the chosen angles with those of the placement, or zero.</summary>
    internal sealed class ResetRotationOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute ignoreObjectRotation = Attr.Bool(data, "m_bIgnoreObjectRotation", false);
        private readonly SmartPropAttribute resetPitch = Attr.Bool(data, "m_bResetPitch", true);
        private readonly SmartPropAttribute resetYaw = Attr.Bool(data, "m_bResetYaw", true);
        private readonly SmartPropAttribute resetRoll = Attr.Bool(data, "m_bResetRoll", true);

        public override bool Apply(SmartPropContext ctx)
        {
            var angles = SmartPropMath.QuaternionAngles(ctx.Transform.Rotation);
            var baseAngles = ignoreObjectRotation.EvaluateBool(ctx) ? Vector3.Zero : SmartPropMath.QuaternionAngles(ctx.ObjectTransform.Rotation);

            if (resetPitch.EvaluateBool(ctx))
            {
                angles.X = baseAngles.X;
            }

            if (resetYaw.EvaluateBool(ctx))
            {
                angles.Y = baseAngles.Y;
            }

            if (resetRoll.EvaluateBool(ctx))
            {
                angles.Z = baseAngles.Z;
            }

            ctx.Transform = ctx.Transform with { Rotation = SmartPropMath.AngleQuaternion(angles) };
            return false;
        }
    }

    /// <summary>CSmartPropOperation_ResetScale: sets the scale to the placement scale, or 1.</summary>
    internal sealed class ResetScaleOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute ignoreObjectScale = Attr.Bool(data, "m_bIgnoreObjectScale", false);

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.Transform = ctx.Transform with { Scale = ignoreObjectScale.EvaluateBool(ctx) ? 1f : ctx.ObjectTransform.Scale };
            return false;
        }
    }

    /// <summary>Random draws shared by the random transform operations.</summary>
    internal static class RandomDraw
    {
        /// <summary>
        /// Draws from [min, max) only when min &lt; max, snapping the drawn value; otherwise min without drawing.
        /// </summary>
        public static float Draw(SmartPropContext ctx, float min, float max, float snap)
        {
            if (!(min < max))
            {
                return min;
            }

            var value = ctx.GetRandomStream().RandomFloat(min, max);

            if (snap > 0f)
            {
                value = MathF.Min(MathF.Max(MathF.Floor(value / snap + 0.5f) * snap, min), max);
            }

            return value;
        }

        /// <summary>Draws the Z, then Y, then X component.</summary>
        public static Vector3 Draw(SmartPropContext ctx, Vector3 min, Vector3 max, Vector3 snap)
        {
            var z = Draw(ctx, min.Z, max.Z, snap.Z);
            var y = Draw(ctx, min.Y, max.Y, snap.Y);
            var x = Draw(ctx, min.X, max.X, snap.X);
            return new Vector3(x, y, z);
        }
    }

    /// <summary>CSmartPropOperation_RandomOffset: moves the current transform by a random offset in its own frame.</summary>
    internal sealed class RandomOffsetOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute min = Attr.Vector3(data, "m_vRandomPositionMin", Vector3.Zero);
        private readonly SmartPropAttribute max = Attr.Vector3(data, "m_vRandomPositionMax", Vector3.Zero);
        private readonly SmartPropAttribute snap = Attr.Vector3(data, "m_vSnapIncrement", Vector3.Zero);

        public override bool Apply(SmartPropContext ctx)
        {
            var offset = RandomDraw.Draw(ctx, min.EvaluateVector3(ctx), max.EvaluateVector3(ctx), snap.EvaluateVector3(ctx));
            ctx.Transform = ctx.Transform.Concat(new SmartPropTransform(offset, 1f, Quaternion.Identity));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_RandomRotation: rotates the current transform by random angles.</summary>
    internal sealed class RandomRotationOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute min = Attr.Vector3(data, "m_vRandomRotationMin", Vector3.Zero);
        private readonly SmartPropAttribute max = Attr.Vector3(data, "m_vRandomRotationMax", Vector3.Zero);
        private readonly SmartPropAttribute snap = Attr.Vector3(data, "m_vSnapIncrement", Vector3.Zero);

        public override bool Apply(SmartPropContext ctx)
        {
            var angles = RandomDraw.Draw(ctx, min.EvaluateVector3(ctx), max.EvaluateVector3(ctx), snap.EvaluateVector3(ctx));
            ctx.Transform = ctx.Transform.Concat(new SmartPropTransform(Vector3.Zero, 1f, SmartPropMath.AngleQuaternion(angles)));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_RandomScale: multiplies the current scale by a random factor.</summary>
    internal sealed class RandomScaleOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute min = Attr.Float(data, "m_flRandomScaleMin", 1f);
        private readonly SmartPropAttribute max = Attr.Float(data, "m_flRandomScaleMax", 1f);
        private readonly SmartPropAttribute snap = Attr.Float(data, "m_flSnapIncrement", 0f);

        public override bool Apply(SmartPropContext ctx)
        {
            var factor = RandomDraw.Draw(ctx, min.EvaluateFloat(ctx), max.EvaluateFloat(ctx), snap.EvaluateFloat(ctx));
            ctx.Transform = ctx.Transform with { Scale = ctx.Transform.Scale * factor };
            return false;
        }
    }

    /// <summary>CSmartPropOperation_RigidDeformation: moves the current transform by the active deformer and clears it.</summary>
    internal sealed class RigidDeformationOperation(KVObject data) : SmartPropModifier(data)
    {
        public override bool Apply(SmartPropContext ctx)
        {
            if (ctx.DeformerIndex >= 0)
            {
                ctx.Transform = SmartPropLattice.DeformTransform(ctx.Output.Deformers[ctx.DeformerIndex], ctx.Transform);
                ctx.DeformerIndex = -1;
            }

            return false;
        }
    }

    /// <summary>
    /// CSmartPropOperation_TraceInDirection: casts a ray and moves the current transform to the hit, optionally
    /// tilting it towards the surface normal. A miss does nothing, discards the element, or moves to the ray's start or end.
    /// </summary>
    internal sealed class TraceInDirectionOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute origin = Attr.Vector3(data, "m_Origin", Vector3.Zero);
        private readonly SmartPropAttribute originSpace = Attr.Enum(data, "m_OriginSpace", 2);
        private readonly SmartPropAttribute originOffset = Attr.Float(data, "m_flOriginOffset", 0f);
        private readonly SmartPropAttribute surfaceUpInfluence = Attr.Float(data, "m_flSurfaceUpInfluence", 0f);
        private readonly SmartPropAttribute noHitResult = Attr.Enum(data, "m_nNoHitResult", 0);
        private readonly SmartPropAttribute direction = Attr.Vector3(data, "m_vTraceDirection", -Vector3.UnitZ);
        private readonly SmartPropAttribute directionSpace = Attr.Enum(data, "m_DirectionSpace", 0);
        private readonly SmartPropAttribute traceLength = Attr.Float(data, "m_flTraceLength", 1000f);

        public override bool Apply(SmartPropContext ctx)
        {
            var start = ctx.ConvertPosition(SmartPropSpace.World, Space(ctx, originSpace), origin.EvaluateVector3(ctx));
            var dir = ctx.ConvertDirection(SmartPropSpace.World, Space(ctx, directionSpace), direction.EvaluateVector3(ctx));
            return TraceAndApply(ctx, start, dir, traceLength.EvaluateFloat(ctx));
        }

        private bool TraceAndApply(SmartPropContext ctx, Vector3 origin, Vector3 dir, float length)
        {
            dir = MathUtils.SafeNormalize(dir);
            var offset = originOffset.EvaluateFloat(ctx);
            var start = origin + dir * offset;
            var maxLength = MathF.Max(length - offset, 0f);
            var influence = surfaceUpInfluence.EvaluateFloat(ctx);
            var miss = noHitResult.EvaluateEnum(ctx, SmartPropEnums.TraceNoHitResult);

            if (ctx.Trace(new SmartPropTraceQuery(start, dir, maxLength)) is { } hit)
            {
                AlignToSurface(ctx, hit.Position, hit.Normal, influence);
                ctx.State.SurfaceNormal = hit.Normal;
                ctx.State.SurfaceMaterial = hit.Material;
                return false;
            }

            switch (miss)
            {
                case 1:
                    return true;
                case 2:
                    ctx.Transform = ctx.Transform with { Position = start };
                    break;
                case 3:
                    ctx.Transform = ctx.Transform with { Position = start + dir * maxLength };
                    break;
                default:
                    break;
            }

            return false;
        }

        private static void AlignToSurface(SmartPropContext ctx, Vector3 position, Vector3 normal, float influence)
        {
            var current = ctx.Transform;

            if (influence <= 0f)
            {
                ctx.Transform = current with { Position = position };
                return;
            }

            var currentForward = Vector3.Transform(Vector3.UnitX, current.Rotation);
            var currentLeft = Vector3.Transform(Vector3.UnitY, current.Rotation);
            var currentUp = Vector3.Transform(Vector3.UnitZ, current.Rotation);
            var up = MathUtils.SafeNormalize(Vector3.Lerp(currentUp, normal, influence));
            Vector3 forward, left;

            if (MathF.Abs(Vector3.Dot(up, currentLeft)) <= MathF.Abs(Vector3.Dot(up, currentForward)))
            {
                forward = MathUtils.SafeNormalize(Vector3.Cross(currentLeft, up));
                left = Vector3.Cross(up, forward);
            }
            else
            {
                left = MathUtils.SafeNormalize(Vector3.Cross(up, currentForward));
                forward = Vector3.Cross(left, up);
            }

            ctx.Transform = new SmartPropTransform(position, current.Scale, SmartPropMath.QuaternionFromBasis(forward, left, up));
        }
    }
}
