using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_BendDeformer: bends a box around a circle and makes it the active deformer of the children.
    /// The children are evaluated whether the deformer is enabled or not.
    /// </summary>
    internal sealed class BendDeformerElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private readonly SmartPropAttribute enabled = Attr.Bool(data, "m_bDeformationEnabled", true);
        private readonly SmartPropAttribute origin = Attr.Vector3(data, "m_vOrigin", Vector3.Zero);
        private readonly SmartPropAttribute angles = Attr.Vector3(data, "m_vAngles", Vector3.Zero);
        private readonly SmartPropAttribute size = Attr.Vector3(data, "m_vSize", Vector3.Zero);
        private readonly SmartPropAttribute bendAngle = Attr.Float(data, "m_flBendAngle", 0f);
        private readonly SmartPropAttribute bendPoint = Attr.Float(data, "m_flBendPoint", 0f);
        private readonly SmartPropAttribute bendRadius = Attr.Float(data, "m_flBendRadius", 0f);

        public override void Evaluate(SmartPropContext ctx)
        {
            if (enabled.EvaluateBool(ctx))
            {
                var frame = ctx.Transform.Concat(new SmartPropTransform(origin.EvaluateVector3(ctx), 1f, SmartPropMath.AngleQuaternion(angles.EvaluateVector3(ctx))));
                var box = Vector3.Max(size.EvaluateVector3(ctx), new Vector3(0.0625f));
                var deformer = SmartPropLattice.BuildBend(frame, box, bendAngle.EvaluateFloat(ctx), Math.Clamp(bendPoint.EvaluateFloat(ctx), 0f, 1f), bendRadius.EvaluateFloat(ctx));

                ctx.Output.Deformers.Add(deformer);
                ctx.DeformerIndex = ctx.Output.Deformers.Count - 1;
            }

            ctx.EvaluateChildren(Children);
        }
    }

    /// <summary>
    /// CSmartPropElement_MidpointDeformer: moves the middle of a box along a line by an offset, rotation and scale,
    /// and makes it the active deformer of the children. A line shorter than 1/16 evaluates no children.
    /// </summary>
    internal sealed class MidpointDeformerElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private readonly SmartPropAttribute enabled = Attr.Bool(data, "m_bDeformationEnabled", true);
        private readonly SmartPropAttribute start = Attr.Vector3(data, "m_vStart", Vector3.Zero);
        private readonly SmartPropAttribute end = Attr.Vector3(data, "m_vEnd", Vector3.Zero);
        private readonly SmartPropAttribute radius = Attr.Float(data, "m_fRadius", 64f);
        private readonly SmartPropAttribute continuousSpline = Attr.Bool(data, "m_bContinuousSpline", false);
        private readonly SmartPropAttribute offset = Attr.Vector3(data, "m_vOffset", Vector3.Zero);
        private readonly SmartPropAttribute angles = Attr.Vector3(data, "m_vAngles", Vector3.Zero);
        private readonly SmartPropAttribute scale = Attr.Vector2(data, "m_vScale", Vector2.One);
        private readonly SmartPropAttribute falloff = Attr.Float(data, "m_fFalloff", 1f);
        private readonly string outputVariable = Attr.Plain(data, "m_OutputVariable");

        public override void Evaluate(SmartPropContext ctx)
        {
            if (!enabled.EvaluateBool(ctx))
            {
                ctx.EvaluateChildren(Children);
                return;
            }

            var lineStart = start.EvaluateVector3(ctx);
            var lineEnd = end.EvaluateVector3(ctx);
            var length = Vector3.Distance(lineStart, lineEnd);

            if (length < 0.0625f)
            {
                return;
            }

            var r = radius.EvaluateFloat(ctx);
            var rotation = SmartPropMath.QuaternionFromTwoVectors(Vector3.UnitX, MathUtils.SafeNormalize(lineEnd - lineStart));
            var corner = lineStart - Vector3.Transform(Vector3.UnitZ, rotation) * r * 0.5f - Vector3.Transform(Vector3.UnitY, rotation) * r * 0.5f;
            var frame = ctx.Transform.Concat(new SmartPropTransform(corner, 1f, rotation));
            var center = new Vector3(length / 2f, r / 2f, r / 2f);
            var scaleAcross = scale.EvaluateVector2(ctx);

            var matrix = Matrix4x4.CreateTranslation(-center)
                * Matrix4x4.CreateScale(falloff.EvaluateFloat(ctx), scaleAcross.X, scaleAcross.Y)
                * EntityTransformHelper.EulerAnglesToRotationMatrix(angles.EvaluateVector3(ctx))
                * Matrix4x4.CreateTranslation(offset.EvaluateVector3(ctx))
                * Matrix4x4.CreateTranslation(center);

            var (deformer, midpoint) = SmartPropLattice.BuildMidpoint(frame, new Vector3(MathF.Max(length, 0.0625f), r, r), matrix, continuousSpline.EvaluateBool(ctx));

            ctx.Output.Deformers.Add(deformer);
            ctx.DeformerIndex = ctx.Output.Deformers.Count - 1;
            ctx.SetVariable(outputVariable, SmartPropValue.FromVector(ctx.Transform.InverseTransformPoint(midpoint)));
            ctx.EvaluateChildren(Children);
        }
    }
}
