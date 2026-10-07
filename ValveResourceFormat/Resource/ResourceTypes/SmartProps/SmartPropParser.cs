using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Criteria;
using ValveResourceFormat.ResourceTypes.SmartProps.Elements;
using ValveResourceFormat.ResourceTypes.SmartProps.Modifiers;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Builds elements, modifiers and selection criteria from KeyValues3 by class name. Unknown classes are dropped.
    /// </summary>
    internal sealed class SmartPropParser(Dictionary<KVObject, int>? assignedElementIds)
    {
        public static KVObject? Get(KVObject data, string key) => data.TryGetValue(key, out var value) ? value : null;

        public int ElementId(KVObject data)
            => assignedElementIds != null && assignedElementIds.TryGetValue(data, out var id) ? id : (int)data.GetIntegerProperty("m_nElementID");

        public List<SmartPropElement> ParseElements(KVObject data, string key)
        {
            var elements = new List<SmartPropElement>();

            foreach (var child in data.GetArray(key) ?? [])
            {
                var element = CreateElement(child);

                if (element != null)
                {
                    elements.Add(element);
                }
            }

            return elements;
        }

        public static List<SmartPropModifier> ParseModifiers(KVObject data)
        {
            var modifiers = new List<SmartPropModifier>();

            foreach (var child in data.GetArray("m_Modifiers") ?? [])
            {
                var modifier = CreateModifier(child);

                if (modifier != null)
                {
                    modifiers.Add(modifier);
                }
            }

            return modifiers;
        }

        public static List<SmartPropCriteria> ParseCriteria(KVObject data)
        {
            var criteria = new List<SmartPropCriteria>();

            foreach (var child in data.GetArray("m_SelectionCriteria") ?? [])
            {
                SmartPropCriteria? created = child.GetStringProperty("_class") switch
                {
                    "CSmartPropSelectionCriteria_IsValid" => new IsValidCriteria(child),
                    "CSmartPropSelectionCriteria_ChoiceWeight" => new ChoiceWeightCriteria(child),
                    "CSmartPropSelectionCriteria_EndCap" => new EndCapCriteria(child),
                    "CSmartPropSelectionCriteria_LinearLength" => new LinearLengthCriteria(child),
                    "CSmartPropSelectionCriteria_PathPosition" => new PathPositionCriteria(child),
                    _ => null,
                };

                if (created != null)
                {
                    criteria.Add(created);
                }
            }

            return criteria;
        }

        private SmartPropElement? CreateElement(KVObject data) => data.GetStringProperty("_class") switch
        {
            "CSmartPropElement_Group" => new GroupElement(data, this),
            "CSmartPropElement_ModifyState" => new ModifyStateElement(data, this),
            "CSmartPropElement_Model" => new ModelElement(data, this),
            "CSmartPropElement_ModelEntity" => new ModelEntityElement(data, this, null),
            "CSmartPropElement_PropDynamic" => new ModelEntityElement(data, this, "prop_dynamic"),
            "CSmartPropElement_PropPhysics" => new ModelEntityElement(data, this, "prop_physics"),
            "CSmartPropElement_SmartProp" => new SmartPropReferenceElement(data, this),
            "CSmartPropElement_PickOne" => new PickOneElement(data, this),
            "CSmartPropElement_PlaceMultiple" => new PlaceMultipleElement(data, this),
            "CSmartPropElement_PlaceInSphere" => new PlaceInSphereElement(data, this),
            "CSmartPropElement_Layout2DGrid" => new Layout2DGridElement(data, this),
            "CSmartPropElement_FitOnLine" => new FitOnLineElement(data, this),
            "CSmartPropElement_PlaceOnPath" => new PlaceOnPathElement(data, this),
            "CSmartPropElement_BendDeformer" => new BendDeformerElement(data, this),
            "CSmartPropElement_MidpointDeformer" => new MidpointDeformerElement(data, this),
            _ => null,
        };

        private static SmartPropModifier? CreateModifier(KVObject data)
        {
            var className = data.GetStringProperty("_class") ?? string.Empty;

            if (SmartPropLegacyUpgrade.GetLegacySetVariableType(className) is { } legacyType)
            {
                return SetVariableOperation.FromLegacy(data, legacyType);
            }

            return className switch
            {
                "CSmartPropFilter_Probability" => new ProbabilityFilter(data),
                "CSmartPropFilter_Expression" => new ExpressionFilter(data),
                "CSmartPropFilter_SurfaceAngle" => new SurfaceAngleFilter(data),
                "CSmartPropFilter_VariableValue" => new VariableValueFilter(data),
                "CSmartPropFilter_SurfaceProperties" => new SurfaceListFilter(data, "m_AllowedSurfaceProperties"),
                "CSmartPropFilter_MaterialAttributes" => new SurfaceListFilter(data, "m_AllowedMaterialAttributes"),
                "CSmartPropOperation_Translate" => new TranslateOperation(data),
                "CSmartPropOperation_Rotate" => new RotateOperation(data),
                "CSmartPropOperation_Scale" => new ScaleOperation(data),
                "CSmartPropOperation_SetPosition" => new SetPositionOperation(data),
                "CSmartPropOperation_SetOrientation" => new SetOrientationOperation(data),
                "CSmartPropOperation_ResetRotation" => new ResetRotationOperation(data),
                "CSmartPropOperation_ResetScale" => new ResetScaleOperation(data),
                "CSmartPropOperation_RandomOffset" => new RandomOffsetOperation(data),
                "CSmartPropOperation_RandomRotation" => new RandomRotationOperation(data),
                "CSmartPropOperation_RandomScale" => new RandomScaleOperation(data),
                "CSmartPropOperation_RigidDeformation" => new RigidDeformationOperation(data),
                "CSmartPropOperation_TraceInDirection" => new TraceInDirectionOperation(data),
                "CSmartPropOperation_CreateLocator" => new CreateLocatorOperation(data),
                "CSmartPropOperation_CreateRotator" => new CreateRotatorOperation(data),
                "CSmartPropOperation_CreateSizer" => new CreateSizerOperation(data),
                "CSmartPropOperation_SetTintColor" => new SetTintColorOperation(data),
                "CSmartPropOperation_RandomColorTintColor" => new RandomColorTintColorOperation(data),
                "CSmartPropOperation_MaterialTint" => new MaterialTintOperation(data),
                "CSmartPropOperation_MaterialOverride" => new MaterialOverrideOperation(data),
                "CSmartPropOperation_SetMateraialGroupChoice" => new SetMaterialGroupChoiceOperation(data),
                "CSmartPropOperation_SaveState" => new SaveStateOperation(data),
                "CSmartPropOperation_RestoreState" => new RestoreStateOperation(data),
                "CSmartPropOperation_SetVariable" => new SetVariableOperation(data),
                "CSmartPropOperation_SavePosition" => new SavePositionOperation(data),
                "CSmartPropOperation_SaveDirection" => new SaveDirectionOperation(data),
                "CSmartPropOperation_SaveScale" => new SaveScaleOperation(data),
                "CSmartPropOperation_SaveSurfaceNormal" => new SaveSurfaceNormalOperation(data),
                "CSmartPropOperation_SaveColor" => new SaveColorOperation(data),
                "CSmartPropOperation_ComputeDotProduct3D" => new ComputeOperation(data, ComputeKind.DotProduct),
                "CSmartPropOperation_ComputeCrossProduct3D" => new ComputeOperation(data, ComputeKind.CrossProduct),
                "CSmartPropOperation_ComputeDistance3D" => new ComputeOperation(data, ComputeKind.Distance),
                "CSmartPropOperation_ComputeVectorBetweenPoints3D" => new ComputeOperation(data, ComputeKind.VectorBetweenPoints),
                "CSmartPropOperation_ComputeNormalizedVector3D" => new ComputeOperation(data, ComputeKind.NormalizedVector),
                "CSmartPropOperation_ComputeProjectVector3D" => new ComputeOperation(data, ComputeKind.ProjectVector),
                _ => null,
            };
        }
    }

    /// <summary>
    /// Typed attribute members with their schema defaults.
    /// </summary>
    internal static class Attr
    {
        public static SmartPropAttribute Bool(KVObject data, string key, bool defaultValue)
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(SmartPropValue.FromBool(defaultValue)));

        public static SmartPropAttribute Int(KVObject data, string key, int defaultValue)
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(SmartPropValue.FromInt(defaultValue)));

        public static SmartPropAttribute Float(KVObject data, string key, float defaultValue)
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(SmartPropValue.FromFloat(defaultValue)));

        public static SmartPropAttribute String(KVObject data, string key, string defaultValue = "")
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(SmartPropValue.FromString(defaultValue)));

        public static SmartPropAttribute Enum(KVObject data, string key, int defaultValue)
            => Int(data, key, defaultValue);

        public static SmartPropAttribute Vector2(KVObject data, string key, Vector2 defaultValue)
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(SmartPropValue.FromFloat(defaultValue.X), SmartPropValue.FromFloat(defaultValue.Y)));

        public static SmartPropAttribute Vector3(KVObject data, string key, Vector3 defaultValue)
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(SmartPropValue.FromFloat(defaultValue.X), SmartPropValue.FromFloat(defaultValue.Y), SmartPropValue.FromFloat(defaultValue.Z)));

        public static SmartPropAttribute Color(KVObject data, string key, Color32 defaultValue)
            => SmartPropAttribute.Parse(SmartPropParser.Get(data, key), SmartPropAttribute.FromLiteral(
                SmartPropValue.FromInt(defaultValue.R), SmartPropValue.FromInt(defaultValue.G), SmartPropValue.FromInt(defaultValue.B), SmartPropValue.FromInt(defaultValue.A)));

        public static string Plain(KVObject data, string key) => data.GetStringProperty(key) ?? string.Empty;
    }
}
