using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Modifiers
{
    /// <summary>CSmartPropOperation_SaveState: stores the transform, tint and surface under a name.</summary>
    internal sealed class SaveStateOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string stateName = Attr.Plain(data, "m_StateName");

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.SaveState(stateName);
            return false;
        }
    }

    /// <summary>CSmartPropOperation_RestoreState: restores a saved state, optionally discarding the element when it is unknown.</summary>
    internal sealed class RestoreStateOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string stateName = Attr.Plain(data, "m_StateName");
        private readonly SmartPropAttribute discardIfUnknown = Attr.Bool(data, "m_bDiscardIfUknown", false);

        public override bool Apply(SmartPropContext ctx) => !ctx.RestoreState(stateName) && discardIfUnknown.EvaluateBool(ctx);
    }

    /// <summary>CSmartPropOperation_SetVariable: assigns a typed value to an existing variable.</summary>
    internal sealed class SetVariableOperation : SmartPropModifier
    {
        private readonly SmartPropVariableAssignment assignment;

        public SetVariableOperation(KVObject data) : base(data)
        {
            assignment = new SmartPropVariableAssignment(SmartPropParser.Get(data, "m_VariableValue"));
        }

        private SetVariableOperation(KVObject data, SmartPropVariableAssignment assignment) : base(data)
        {
            this.assignment = assignment;
        }

        /// <summary>
        /// Reads a legacy typed SetVariable operation: <c>m_VariableName</c> and <c>m_VariableValue</c> become the
        /// target and value of an assignment of the given data type.
        /// </summary>
        public static SetVariableOperation FromLegacy(KVObject data, string dataType)
        {
            var assignment = new SmartPropVariableAssignment(Attr.Plain(data, "m_VariableName"), dataType, SmartPropParser.Get(data, "m_VariableValue"));
            return new SetVariableOperation(data, assignment);
        }

        public override bool Apply(SmartPropContext ctx)
        {
            if (ctx.FindVariable(assignment.TargetName) != null)
            {
                ctx.SetVariable(assignment.TargetName, assignment.Evaluate(ctx));
            }

            return false;
        }
    }

    /// <summary>CSmartPropOperation_SavePosition: writes the current position, in a chosen space, to a variable.</summary>
    internal sealed class SavePositionOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 0);
        private readonly string variableName = Attr.Plain(data, "m_VariableName");

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.SetVariable(variableName, SmartPropValue.FromVector(ctx.ConvertPosition(Space(ctx, space), SmartPropSpace.World, ctx.Transform.Position)));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_SaveDirection: writes an axis of the current rotation, in a chosen space, to a variable.</summary>
    internal sealed class SaveDirectionOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute direction = Attr.Enum(data, "m_DirectionVector", 0);
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 0);
        private readonly string variableName = Attr.Plain(data, "m_VariableName");

        public override bool Apply(SmartPropContext ctx)
        {
            var axis = direction.EvaluateEnum(ctx, SmartPropEnums.Direction) switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                2 => Vector3.UnitZ,
                _ => Vector3.Zero,
            };

            var world = Vector3.Transform(axis, ctx.Transform.Rotation);
            ctx.SetVariable(variableName, SmartPropValue.FromVector(ctx.ConvertDirection(Space(ctx, space), SmartPropSpace.World, world)));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_SaveScale: writes the current scale to a variable.</summary>
    internal sealed class SaveScaleOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string variableName = Attr.Plain(data, "m_VariableName");

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.SetVariable(variableName, SmartPropValue.FromFloat(ctx.Transform.Scale));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_SaveSurfaceNormal: writes the surface normal, in a chosen space, to a variable.</summary>
    internal sealed class SaveSurfaceNormalOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute space = Attr.Enum(data, "m_CoordinateSpace", 0);
        private readonly string variableName = Attr.Plain(data, "m_VariableName");

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.SetVariable(variableName, SmartPropValue.FromVector(ctx.ConvertDirection(Space(ctx, space), SmartPropSpace.World, ctx.State.SurfaceNormal)));
            return false;
        }
    }

    /// <summary>CSmartPropOperation_SaveColor: writes the current tint to a variable.</summary>
    internal sealed class SaveColorOperation(KVObject data) : SmartPropModifier(data)
    {
        private readonly string variableName = Attr.Plain(data, "m_VariableName");

        public override bool Apply(SmartPropContext ctx)
        {
            ctx.SetVariable(variableName, SmartPropValue.FromColor(ctx.State.Tint));
            return false;
        }
    }

    internal enum ComputeKind
    {
        DotProduct,
        CrossProduct,
        Distance,
        VectorBetweenPoints,
        NormalizedVector,
        ProjectVector,
    }

    /// <summary>The CSmartPropOperation_Compute* operations: vector math written to a variable.</summary>
    internal sealed class ComputeOperation(KVObject data, ComputeKind kind) : SmartPropModifier(data)
    {
        private readonly string output = Attr.Plain(data, "m_OutputVariableName");
        private readonly SmartPropAttribute outputSpace = Attr.Enum(data, "m_OutputCoordinateSpace", 0);
        private readonly SmartPropAttribute normalized = Attr.Bool(data, "m_bNormalized", false);
        private readonly SmartPropAttribute plane = Attr.Bool(data, "m_bPlane", false);
        private readonly SmartPropAttribute a = Attr.Vector3(data, kind switch
        {
            ComputeKind.DotProduct or ComputeKind.CrossProduct or ComputeKind.ProjectVector => "m_InputVectorA",
            ComputeKind.NormalizedVector => "m_InputVector",
            _ => "m_InputPositionA",
        }, Vector3.Zero);
        private readonly SmartPropAttribute b = Attr.Vector3(data, kind switch
        {
            ComputeKind.DotProduct or ComputeKind.CrossProduct or ComputeKind.ProjectVector => "m_InputVectorB",
            _ => "m_InputPositionB",
        }, Vector3.Zero);
        private readonly SmartPropAttribute spaceA = Attr.Enum(data, "m_CoordinateSpaceA", 0);
        private readonly SmartPropAttribute spaceB = Attr.Enum(data, "m_CoordinateSpaceB", 0);

        public override bool Apply(SmartPropContext ctx)
        {
            if (output.Length == 0)
            {
                return false;
            }

            var value = kind switch
            {
                ComputeKind.DotProduct => SmartPropValue.FromFloat(Vector3.Dot(a.EvaluateVector3(ctx), b.EvaluateVector3(ctx))),
                ComputeKind.CrossProduct => SmartPropValue.FromVector(Vector3.Cross(a.EvaluateVector3(ctx), b.EvaluateVector3(ctx))),
                ComputeKind.NormalizedVector => SmartPropValue.FromVector(MathUtils.SafeNormalize(a.EvaluateVector3(ctx))),
                ComputeKind.Distance => SmartPropValue.FromFloat(Vector3.Distance(PositionA(ctx), PositionB(ctx))),
                ComputeKind.VectorBetweenPoints => SmartPropValue.FromVector(VectorBetween(ctx)),
                _ => SmartPropValue.FromVector(Project(ctx)),
            };

            ctx.SetVariable(output, value);
            return false;
        }

        private Vector3 PositionA(SmartPropContext ctx) => ctx.ConvertPosition(Space(ctx, outputSpace), Space(ctx, spaceA), a.EvaluateVector3(ctx));

        private Vector3 PositionB(SmartPropContext ctx) => ctx.ConvertPosition(Space(ctx, outputSpace), Space(ctx, spaceB), b.EvaluateVector3(ctx));

        private Vector3 VectorBetween(SmartPropContext ctx)
        {
            var vector = PositionB(ctx) - PositionA(ctx);
            return normalized.EvaluateBool(ctx) ? MathUtils.SafeNormalize(vector) : vector;
        }

        private Vector3 Project(SmartPropContext ctx)
        {
            var vectorA = ctx.ConvertDirection(Space(ctx, outputSpace), Space(ctx, spaceA), a.EvaluateVector3(ctx));
            var vectorB = ctx.ConvertDirection(Space(ctx, outputSpace), Space(ctx, spaceB), b.EvaluateVector3(ctx));
            var n = MathUtils.SafeNormalize(vectorB);
            var projection = n * Vector3.Dot(vectorA, n) / Vector3.Dot(n, n);
            return plane.EvaluateBool(ctx) ? vectorA - projection : projection;
        }
    }
}
