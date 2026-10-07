using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>CSmartPropElement_Model: places a model at the current transform.</summary>
    internal sealed class ModelElement(KVObject data, SmartPropParser parser) : SmartPropElement(data, parser)
    {
        public const string ErrorModel = "models/dev/error.vmdl";

        private static readonly (float Start, float End)[] DetailFadeSizes =
        [
            (0f, 0f),
            (0.0375f, 0.01875f),
            (0.025f, 0.0125f),
            (0.01675f, 0.008375f),
            (0.01125f, 0.005625f),
            (0.0075f, 0.00375f),
        ];

        private readonly SmartPropAttribute modelName = Attr.String(data, "m_sModelName");
        private readonly SmartPropAttribute materialGroupName = Attr.String(data, "m_MaterialGroupName");
        private readonly SmartPropAttribute detailObject = Attr.Bool(data, "m_bDetailObject", false);
        private readonly SmartPropAttribute modelScale = Attr.Vector3(data, "m_vModelScale", Vector3.One);
        private readonly SmartPropAttribute uniformModelScale = Attr.Float(data, "m_flUniformModelScale", 1f);
        private readonly SmartPropAttribute lodLevel = Attr.Int(data, "m_nLodLevel", -1);
        private readonly SmartPropAttribute surfacePropertyOverride = Attr.String(data, "m_SurfacePropertyOverride");
        private readonly SmartPropAttribute detailObjectFadeLevel = Attr.Enum(data, "m_nDetailObjectFadeLevel", 3);
        private readonly SmartPropAttribute castShadows = Attr.Bool(data, "m_bCastShadows", true);
        private readonly SmartPropAttribute rigidDeformation = Attr.Bool(data, "m_bRigidDeformation", false);
        private readonly SmartPropAttribute disableDynamicDeformable = Attr.Bool(data, "m_bDisableDynamicDeformable", false);

        public override void Evaluate(SmartPropContext ctx)
        {
            var transform = ctx.Transform;
            var scale = modelScale.EvaluateVector3(ctx);
            var isDetail = detailObject.EvaluateBool(ctx);

            if (isDetail)
            {
                scale = new Vector3(uniformModelScale.EvaluateFloat(ctx));
            }

            var (model, finalTransform, finalScale) = ResolveModel(ctx, transform, scale, modelName);
            var isRigid = rigidDeformation.EvaluateBool(ctx);
            var materialGroup = materialGroupName.EvaluateString(ctx);
            var lod = lodLevel.EvaluateInt(ctx);
            var shadows = castShadows.EvaluateBool(ctx);
            var fade = isDetail ? DetailFadeSizes[Math.Clamp(detailObjectFadeLevel.EvaluateEnum(ctx, SmartPropEnums.DetailFadeLevel), 0, DetailFadeSizes.Length - 1)] : default;
            var disableDeformable = disableDynamicDeformable.EvaluateBool(ctx);
            var surfaceProperty = surfacePropertyOverride.EvaluateString(ctx);

            ctx.AddModelInstance(new SmartPropModelInstance
            {
                ElementPath = ctx.CurrentElementPath,
                ModelName = model,
                Transform = finalTransform,
                ModelScale = finalScale,
                Tint = ctx.State.Tint,
                MaterialGroup = IsDefaultMaterialGroup(materialGroup) ? null : materialGroup,
                LodLevel = lod >= 0 ? lod : -1,
                CastShadows = shadows,
                DetailObject = isDetail,
                DetailFadeStartSize = fade.Start,
                DetailFadeEndSize = fade.End,
                DisableDynamicDeformable = disableDeformable,
                SurfacePropertyOverride = string.IsNullOrEmpty(surfaceProperty) ? null : surfaceProperty,
                MaterialOverrideSetIndex = ctx.MaterialOverrideSetIndex,
                MaterialTintSetIndex = ctx.MaterialTintSetIndex,
                DeformerIndex = ctx.DeformerIndex,
                DeformationMode = isRigid ? SmartPropDeformationMode.Rigid : SmartPropDeformationMode.Vertex,
            });
        }

        public static bool IsDefaultMaterialGroup(string name) => name.Length == 0 || name.Equals("default", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The model name, or the error model at the placement transform when the transform or scale is not finite.
        /// </summary>
        public static (string Model, SmartPropTransform Transform, Vector3 Scale) ResolveModel(SmartPropContext ctx, SmartPropTransform transform, Vector3 scale, SmartPropAttribute modelName)
        {
            var finiteTransform = SmartPropMath.IsFinite(transform.Position) && float.IsFinite(transform.Scale)
                && float.IsFinite(transform.Rotation.X) && float.IsFinite(transform.Rotation.Y) && float.IsFinite(transform.Rotation.Z) && float.IsFinite(transform.Rotation.W);
            var finiteScale = SmartPropMath.IsFinite(scale);

            if (finiteTransform && finiteScale)
            {
                return (modelName.EvaluateString(ctx), transform, scale);
            }

            return (ErrorModel, finiteTransform ? transform : ctx.ObjectTransform, finiteScale ? scale : Vector3.One);
        }
    }

    /// <summary>CSmartPropElement_ModelEntity, PropDynamic and PropPhysics: a model placed as an entity.</summary>
    internal sealed class ModelEntityElement(KVObject data, SmartPropParser parser, string? className) : SmartPropElement(data, parser)
    {
        private readonly SmartPropAttribute modelName = Attr.String(data, "m_sModelName");
        private readonly SmartPropAttribute materialGroupName = Attr.String(data, "m_MaterialGroupName");
        private readonly SmartPropAttribute castShadows = Attr.Bool(data, "m_bCastShadows", true);
        private readonly SmartPropAttribute forceStatic = Attr.Bool(data, "m_bForceStatic", false);
        private readonly SmartPropAttribute attachmentMode = Attr.Enum(data, "m_nDeformableAttachmentMode", 0);
        private readonly SmartPropAttribute orientationMode = Attr.Enum(data, "m_nDeformableOrientationMode", 4);
        private readonly SmartPropAttribute startAsleep = Attr.Bool(data, "m_bStartAsleep", false);

        public override void Evaluate(SmartPropContext ctx)
        {
            var (model, transform, _) = ModelElement.ResolveModel(ctx, ctx.Transform, Vector3.One, modelName);
            var materialGroup = materialGroupName.EvaluateString(ctx);
            var shadows = castShadows.EvaluateBool(ctx);
            Dictionary<string, string>? keys = null;

            if (!forceStatic.EvaluateBool(ctx))
            {
                keys = [];

                if (className != null)
                {
                    keys["classname"] = className;
                }

                if (className == "prop_physics" && startAsleep.EvaluateBool(ctx))
                {
                    keys["spawnflags"] = "Start Asleep";
                }

                keys["deformable_attach_mode"] = SmartPropEnums.DeformableAttachMode[Math.Clamp(attachmentMode.EvaluateEnum(ctx, SmartPropEnums.DeformableAttachMode), 0, 2)];
                keys["deformable_attach_orientation_mode"] = SmartPropEnums.DeformableOrientMode[Math.Clamp(orientationMode.EvaluateEnum(ctx, SmartPropEnums.DeformableOrientMode), 0, 4)];
            }

            ctx.AddModelInstance(new SmartPropModelInstance
            {
                ElementPath = ctx.CurrentElementPath,
                ModelName = model,
                Transform = transform,
                Tint = ctx.State.Tint,
                MaterialGroup = ModelElement.IsDefaultMaterialGroup(materialGroup) ? null : materialGroup,
                CastShadows = shadows,
                EntityKeys = keys,
                MaterialOverrideSetIndex = ctx.MaterialOverrideSetIndex,
                MaterialTintSetIndex = ctx.MaterialTintSetIndex,
                DeformerIndex = ctx.DeformerIndex,
                DeformationMode = SmartPropDeformationMode.Rigid,
            });
        }
    }
}
