using System.Globalization;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// The result of evaluating an element's modifier chain: the accumulated local
    /// transform, its world transform for children, the world transform including
    /// model-level scale, any widgets emitted along the chain, whether the element
    /// is filtered out, and any active tint color.
    /// </summary>
    public readonly record struct SmartPropModifierResult(
        Matrix4x4 LocalMatrix,
        Matrix4x4 WorldMatrix,
        Matrix4x4 ModelWorldMatrix,
        IReadOnlyList<SmartPropWidget> Widgets,
        bool IsFilteredOut = false,
        Vector4? TintColor = null);

    /// <summary>
    /// Evaluates an element's m_Modifiers list strictly sequentially, top to bottom.
    /// Each modifier operates on the active local transform matrix; coordinate space
    /// selects whether a transform pre-multiplies (element space) or post-multiplies
    /// (world or parent space). Widget operations capture the world frame at their
    /// exact chain position.
    /// </summary>
    public static class SmartPropModifierEvaluator
    {
        private const string OperationPrefix = "CSmartPropOperation_";
        private const string PulsePrefix = "CSmartPropPulse_";
        private const string ElementPrefix = "CSmartPropElement_";
        private const string CriteriaPrefix = "CSmartPropSelectionCriteria_";
        private const string FilterPrefix = "CSmartPropFilter_";

        private static readonly string[] ClassPrefixes = [OperationPrefix, PulsePrefix, ElementPrefix, CriteriaPrefix, FilterPrefix];
        private static readonly string[] SetValueKeys = ["m_Value", "m_flValue", "m_nValue", "m_bValue"];

        /// <summary>
        /// Reads a node's smart prop class name: the generic_data_type string with its
        /// known prefix stripped (e.g. "CSmartPropOperation_Translate" to "Translate").
        /// Falls back to the editor _class field, then to an empty string.
        /// </summary>
        public static string GetClassName(KVObject node)
        {
            if ((!node.TryGetValue("generic_data_type", out var classNode) || classNode.ValueType != KVValueType.String)
                && (!node.TryGetValue("_class", out classNode) || classNode.ValueType != KVValueType.String))
            {
                return string.Empty;
            }

            var name = (string)classNode;
            foreach (var prefix in ClassPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return name[prefix.Length..];
                }
            }

            return name;
        }

        /// <summary>
        /// Sequentially evaluates all modifiers of an element and collects its widgets.
        /// The parent world matrix defaults to identity.
        /// </summary>
        /// <param name="element">The element dictionary (KV3 collection).</param>
        /// <param name="context">Evaluation context for variable and expression bindings.</param>
        /// <param name="parentWorldMatrix">World transform of the parent element.</param>
        /// <param name="stateMap">Optional map of named states for SaveState/RestoreState.</param>
        /// <param name="parentTintColor">Active tint color inherited from parent.</param>
        public static SmartPropModifierResult EvaluateElementModifiers(
            KVObject element,
            SmartPropEvaluationContext context,
            Matrix4x4? parentWorldMatrix = null,
            Dictionary<string, Matrix4x4>? stateMap = null,
            Vector4? parentTintColor = null)
        {
            var parent = parentWorldMatrix ?? Matrix4x4.Identity;
            var elementClass = GetClassName(element);
            var elementId = GetInt32(element, "m_nElementID");
            var localMatrix = Matrix4x4.Identity;
            var activeTintColor = parentTintColor;
            List<SmartPropWidget> widgets = [];

            // Check selection criteria
            if (element.TryGetValue("m_SelectionCriteria", out var criteriaNode) && criteriaNode.IsArray)
            {
                var criteria = criteriaNode.AsArraySpan();
                for (var i = 0; i < criteria.Length; i++)
                {
                    var crit = criteria[i];
                    if (crit.ValueType == KVValueType.Collection && !EvaluateFilter(crit, context, elementId))
                    {
                        return new SmartPropModifierResult(Matrix4x4.Identity, parent, parent, [], IsFilteredOut: true);
                    }
                }
            }

            if (element.TryGetValue("m_Modifiers", out var modifiersNode) && modifiersNode.IsArray)
            {
                var modifiers = modifiersNode.AsArraySpan();
                for (var i = 0; i < modifiers.Length; i++)
                {
                    var modifier = modifiers[i];
                    if (modifier.ValueType != KVValueType.Collection || IsDisabled(modifier))
                    {
                        continue;
                    }

                    var modClass = GetClassName(modifier);
                    if (IsFilterClass(modClass) || modifier.ContainsKey("m_VariableComparison"))
                    {
                        if (!EvaluateFilter(modifier, context, elementId))
                        {
                            return new SmartPropModifierResult(Matrix4x4.Identity, parent, parent, [], IsFilteredOut: true);
                        }

                        continue;
                    }

                    if (modClass is "SetTintColor")
                    {
                        var tint = EvaluateSetTintColor(modifier, context, elementId);
                        if (tint.HasValue)
                        {
                            activeTintColor = tint.Value;
                        }

                        continue;
                    }

                    localMatrix = EvaluateSingleModifier(modifier, localMatrix, parent, context, stateMap, elementId, out var widget);
                    if (widget != null)
                    {
                        widgets.Add(widget);
                    }
                }
            }

            var worldMatrix = localMatrix * parent;

            if (elementClass == "PickOne")
            {
                if (TryBuildPickOneHandle(element, worldMatrix, elementId, context, out var handle))
                {
                    widgets.Add(handle);
                }
            }

            var modelScale = Vector3.One;
            if (elementClass is "Model" or "ModelEntity" or "PropPhysics" or "PropDynamic")
            {
                modelScale = ResolveModelScale(element, context);
            }

            var modelWorldMatrix = Matrix4x4.CreateScale(modelScale) * worldMatrix;

            return new SmartPropModifierResult(localMatrix, worldMatrix, modelWorldMatrix, widgets, IsFilteredOut: false, TintColor: activeTintColor);
        }

        private static Matrix4x4 EvaluateSingleModifier(
            KVObject modifier,
            Matrix4x4 localMatrix,
            Matrix4x4 parentWorldMatrix,
            SmartPropEvaluationContext context,
            Dictionary<string, Matrix4x4>? stateMap,
            int elementId,
            out SmartPropWidget? widget)
        {
            widget = null;
            if (IsDisabled(modifier))
            {
                return localMatrix;
            }

            switch (GetClassName(modifier))
            {
                case "Translate" when modifier.ContainsKey("m_vPosition"):
                {
                    var translation = Matrix4x4.CreateTranslation(context.ResolveVector3(modifier["m_vPosition"]));
                    return IsWorldOrParentSpace(modifier, "ELEMENT")
                        ? localMatrix * translation
                        : translation * localMatrix;
                }

                case "SetPosition" when modifier.ContainsKey("m_vPosition"):
                {
                    var position = context.ResolveVector3(modifier["m_vPosition"]);
                    localMatrix.M41 = position.X;
                    localMatrix.M42 = position.Y;
                    localMatrix.M43 = position.Z;
                    return localMatrix;
                }

                case "Rotate" when modifier.ContainsKey("m_vRotation"):
                {
                    var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(context.ResolveAngles(modifier["m_vRotation"]));
                    return IsWorldOrParentSpace(modifier, "ELEMENT")
                        ? localMatrix * rotation
                        : rotation * localMatrix;
                }

                case "SetOrientation":
                    return EvaluateSetOrientation(modifier, localMatrix, context);

                case "ResetRotation":
                {
                    var (position, angles, scale) = SmartPropTransform.DecomposeTRS(localMatrix);
                    var reset = new Vector3(
                        GetBool(modifier, "m_bResetPitch", true) ? 0f : angles.X,
                        GetBool(modifier, "m_bResetYaw", true) ? 0f : angles.Y,
                        GetBool(modifier, "m_bResetRoll", true) ? 0f : angles.Z);
                    return ComposeTRS(scale, reset, position);
                }

                case "Scale":
                {
                    var scale = modifier.TryGetValue("m_vScale", out var scaleVector)
                        ? context.ResolveVector3(scaleVector, Vector3.One)
                        : Vector3.One * context.ResolveScalar(GetOrDefault(modifier, "m_flScale"), 1f);
                    return Matrix4x4.CreateScale(scale) * localMatrix;
                }

                case "ResetScale":
                {
                    var (position, angles, _) = SmartPropTransform.DecomposeTRS(localMatrix);
                    return ComposeTRS(Vector3.One, angles, position);
                }

                case "RandomOffset":
                {
                    var min = context.ResolveVector3(GetOrDefault(modifier, "m_vRandomPositionMin"));
                    var max = context.ResolveVector3(GetOrDefault(modifier, "m_vRandomPositionMax"));
                    var offset = RandomPerAxis(min, max, elementId, context.InstanceIndex, salt: 11);
                    return Matrix4x4.CreateTranslation(offset) * localMatrix;
                }

                case "RandomRotation":
                {
                    var min = context.ResolveVector3(GetOrDefault(modifier, "m_vRandomRotationMin"));
                    var max = context.ResolveVector3(GetOrDefault(modifier, "m_vRandomRotationMax"));
                    var angles = RandomPerAxis(min, max, elementId, context.InstanceIndex, salt: 101);
                    return EntityTransformHelper.EulerAnglesToRotationMatrix(angles) * localMatrix;
                }

                case "RandomScale":
                {
                    var min = context.ResolveScalar(GetOrDefault(modifier, "m_flRandomScaleMin"), 1f);
                    var max = context.ResolveScalar(GetOrDefault(modifier, "m_flRandomScaleMax"), 1f);
                    var factor = min + (DeterministicRandom(elementId, context.InstanceIndex, 0, 202) * (max - min));
                    return Matrix4x4.CreateScale(factor, factor, factor) * localMatrix;
                }

                case "SaveState":
                    if (stateMap != null)
                    {
                        stateMap[GetStateName(modifier)] = localMatrix;
                    }

                    return localMatrix;

                case "RestoreState":
                    if (stateMap != null && stateMap.TryGetValue(GetStateName(modifier), out var saved))
                    {
                        return saved;
                    }

                    return localMatrix;

                case "SetVariable" or "SetVariableFloat" or "SetVariableInt" or "SetVariableBool":
                    ApplySetVariable(modifier, context);
                    return localMatrix;

                case "CreateSizer":
                    if (TryBuildSizer(modifier, localMatrix * parentWorldMatrix, elementId, context, out var sizer))
                    {
                        widget = sizer;
                    }

                    return localMatrix;

                case "CreateLocator":
                    widget = BuildLocator(modifier, localMatrix * parentWorldMatrix, elementId, context);
                    return localMatrix;

                case "CreateRotator":
                    widget = BuildRotator(modifier, localMatrix * parentWorldMatrix, elementId, context);
                    return localMatrix;

                default:
                    return localMatrix;
            }
        }

        private static Matrix4x4 EvaluateSetOrientation(KVObject modifier, Matrix4x4 localMatrix, SmartPropEvaluationContext context)
        {
            var (position, _, scale) = SmartPropTransform.DecomposeTRS(localMatrix);
            Matrix4x4 rotation;
            if (modifier.TryGetValue("m_vRotation", out var angles))
            {
                rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(context.ResolveAngles(angles));
            }
            else if (modifier.TryGetValue("m_vForwardVector", out var forwardNode) && modifier.TryGetValue("m_vUpVector", out var upNode))
            {
                var forward = context.ResolveVector3(forwardNode, Vector3.UnitX);
                var up = context.ResolveVector3(upNode, Vector3.UnitZ);
                rotation = SmartPropTransform.CreateFrame(Vector3.Zero, forward, up);
            }
            else
            {
                return localMatrix;
            }

            return ComposeTRS(scale, rotation, position);
        }

        private static void ApplySetVariable(KVObject modifier, SmartPropEvaluationContext context)
        {
            if (modifier.TryGetValue("m_VariableValue", out var variableValue) && variableValue.ValueType == KVValueType.Collection)
            {
                if (variableValue.TryGetValue("m_TargetName", out var nameNode) && nameNode.ValueType == KVValueType.String)
                {
                    var name = (string)nameNode;
                    if (name.Length > 0)
                    {
                        variableValue.TryGetValue("m_Value", out var valueNode);
                        context.SetOverride(name, ToOverrideValue(valueNode));
                    }
                }

                return;
            }

            // Flat legacy form: the target name and the value sit directly on the modifier
            if (modifier.TryGetValue("m_VariableName", out var flatName) && flatName.ValueType == KVValueType.String)
            {
                var name = (string)flatName;
                if (name.Length == 0)
                {
                    return;
                }

                foreach (var key in SetValueKeys)
                {
                    if (modifier.TryGetValue(key, out var valueNode) && !valueNode.IsNull)
                    {
                        context.SetOverride(name, ToOverrideValue(valueNode));
                        return;
                    }
                }
            }
        }

        private static bool TryBuildSizer(KVObject modifier, Matrix4x4 worldMatrix, int elementId, SmartPropEvaluationContext context, out SmartPropSizerWidget? widget)
        {
            var minX = context.ResolveScalar(GetOrDefault(modifier, "m_flInitialMinX"));
            var maxX = context.ResolveScalar(GetOrDefault(modifier, "m_flInitialMaxX"));
            var minY = context.ResolveScalar(GetOrDefault(modifier, "m_flInitialMinY"));
            var maxY = context.ResolveScalar(GetOrDefault(modifier, "m_flInitialMaxY"));
            var minZ = context.ResolveScalar(GetOrDefault(modifier, "m_flInitialMinZ"));
            var maxZ = context.ResolveScalar(GetOrDefault(modifier, "m_flInitialMaxZ"));

            var outMinX = GetString(modifier, "m_OutputVariableMinX");
            var outMaxX = GetString(modifier, "m_OutputVariableMaxX");
            var outMinY = GetString(modifier, "m_OutputVariableMinY");
            var outMaxY = GetString(modifier, "m_OutputVariableMaxY");
            var outMinZ = GetString(modifier, "m_OutputVariableMinZ");
            var outMaxZ = GetString(modifier, "m_OutputVariableMaxZ");

            var hasX = outMinX.Length > 0 || outMaxX.Length > 0 || minX != 0f || maxX != 0f;
            var hasY = outMinY.Length > 0 || outMaxY.Length > 0 || minY != 0f || maxY != 0f;
            var hasZ = outMinZ.Length > 0 || outMaxZ.Length > 0 || minZ != 0f || maxZ != 0f;

            widget = null;
            if (!hasX && !hasY && !hasZ)
            {
                return false;
            }

            var (position, angles, _) = SmartPropTransform.DecomposeTRS(worldMatrix);
            widget = new SmartPropSizerWidget(
                GetInt32(modifier, "m_nElementID", elementId),
                worldMatrix,
                position,
                angles,
                GetString(modifier, "m_Name"),
                new Vector3(minX, minY, minZ),
                new Vector3(maxX, maxY, maxZ),
                new SmartPropSizerHandles(
                    outMinX.Length > 0, outMaxX.Length > 0,
                    outMinY.Length > 0, outMaxY.Length > 0,
                    outMinZ.Length > 0, outMaxZ.Length > 0),
                new SmartPropSizerAxes(hasX, hasY, hasZ));
            return true;
        }

        private static SmartPropLocatorWidget BuildLocator(KVObject modifier, Matrix4x4 worldMatrix, int elementId, SmartPropEvaluationContext context)
        {
            var (position, angles, _) = SmartPropTransform.DecomposeTRS(worldMatrix);
            var offset = context.ResolveVector3(GetOrDefault(modifier, "m_vOffset"));

            return new SmartPropLocatorWidget(
                GetInt32(modifier, "m_nElementID", elementId),
                worldMatrix,
                SmartPropTransform.TransformPoint(worldMatrix, offset),
                angles,
                GetString(modifier, "m_LocatorName"),
                offset,
                MathF.Max(0.01f, context.ResolveScalar(GetOrDefault(modifier, "m_flDisplayScale"), 1f)));
        }

        private static SmartPropRotatorWidget BuildRotator(KVObject modifier, Matrix4x4 worldMatrix, int elementId, SmartPropEvaluationContext context)
        {
            var (position, angles, _) = SmartPropTransform.DecomposeTRS(worldMatrix);
            var offset = context.ResolveVector3(GetOrDefault(modifier, "m_vOffset"));
            var axis = context.ResolveVector3(GetOrDefault(modifier, "m_vRotationAxis"), Vector3.UnitZ);

            var space = GetSpace(modifier, "WORLD");
            if (space is "ELEMENT" or "OBJECT")
            {
                // The axis is authored in the element frame, so rotate it into world space
                var worldAxis = Vector3.TransformNormal(axis, worldMatrix);
                if (worldAxis.LengthSquared() > 1e-12f)
                {
                    axis = Vector3.Normalize(worldAxis);
                }
            }

            return new SmartPropRotatorWidget(
                GetInt32(modifier, "m_nElementID", elementId),
                worldMatrix,
                SmartPropTransform.TransformPoint(worldMatrix, offset),
                angles,
                GetString(modifier, "m_Name"),
                offset,
                axis,
                MathF.Max(1f, context.ResolveScalar(GetOrDefault(modifier, "m_flDisplayRadius"), 16f)),
                context.ResolveScalar(GetOrDefault(modifier, "m_flInitialAngle")),
                ResolveColor(GetOrDefault(modifier, "m_DisplayColor"), context, new Vector3(0.72f, 0.74f, 0.48f)));
        }

        private static bool TryBuildPickOneHandle(KVObject element, Matrix4x4 worldMatrix, int elementId, SmartPropEvaluationContext context, out SmartPropPickOneHandleWidget widget)
        {
            // Some authored files carry a triple-f typo in the handle offset field name
            if (!element.TryGetValue("m_vHandleOffset", out var offsetNode)
                && !element.TryGetValue("m_vHandleOfffset", out offsetNode))
            {
                offsetNode = null;
            }

            var offset = context.ResolveVector3(offsetNode);
            var (position, angles, _) = SmartPropTransform.DecomposeTRS(worldMatrix);
            var shape = GetString(element, "m_HandleShape", "SQUARE").ToUpperInvariant();

            widget = new SmartPropPickOneHandleWidget(
                elementId,
                worldMatrix,
                SmartPropTransform.TransformPoint(worldMatrix, offset),
                angles,
                GetString(element, "m_OutputChoiceVariableName"),
                offset,
                MathF.Max(1f, context.ResolveScalar(GetOrDefault(element, "m_HandleSize"), 8f)),
                ResolveColor(GetOrDefault(element, "m_HandleColor"), context, new Vector3(0.6f, 0.6f, 0.6f)),
                shape);
            return true;
        }

        private static Vector3 ResolveModelScale(KVObject element, SmartPropEvaluationContext context)
        {
            if (element.TryGetValue("m_vModelScale", out var scaleVector))
            {
                return context.ResolveVector3(scaleVector, Vector3.One);
            }

            if (element.TryGetValue("m_flUniformModelScale", out var uniform))
            {
                return Vector3.One * context.ResolveScalar(uniform, 1f);
            }

            return Vector3.One;
        }

        private static Vector3 RandomPerAxis(Vector3 min, Vector3 max, int elementId, int instanceIndex, int salt)
            => new(
                min.X + (DeterministicRandom(elementId, instanceIndex, 0, salt) * (max.X - min.X)),
                min.Y + (DeterministicRandom(elementId, instanceIndex, 1, salt) * (max.Y - min.Y)),
                min.Z + (DeterministicRandom(elementId, instanceIndex, 2, salt) * (max.Z - min.Z)));

        // A small avalanche hash mixing element id, instance index and component so the
        // same prop instance always renders with the same random transform, independent
        // of evaluation order. The intermediate products wrap at 32 bits, which the final
        // 31 bit masks make equivalent to full width math.
        private static float DeterministicRandom(int elementId, int instanceIndex, int component, int salt)
        {
            unchecked
            {
                var h = ((elementId * 374761393) + (instanceIndex * 668265263) + (component * 964729) + salt) & 0x7FFFFFFF;
                h = ((h ^ (h >> 13)) * 1274126177) & 0x7FFFFFFF;
                return (h ^ (h >> 16)) / (float)0x7FFFFFFF;
            }
        }

        /// <summary>Builds scale * rotation * position in the row-vector convention.</summary>
        private static Matrix4x4 ComposeTRS(Vector3 scale, Vector3 pitchYawRoll, Vector3 position)
            => Matrix4x4.CreateScale(scale) * EntityTransformHelper.EulerAnglesToRotationMatrix(pitchYawRoll) * Matrix4x4.CreateTranslation(position);

        private static Matrix4x4 ComposeTRS(Vector3 scale, Matrix4x4 rotation, Vector3 position)
            => Matrix4x4.CreateScale(scale) * rotation * Matrix4x4.CreateTranslation(position);

        /// <summary>
        /// Resolves a colour field to RGB components in 0 to 1. Hammer stores colours as
        /// 0-255 values, so any component above 1 rescales the whole colour.
        /// </summary>
        private static Vector3 ResolveColor(KVObject? value, SmartPropEvaluationContext context, Vector3 defaultColor)
        {
            if (value is null)
            {
                return defaultColor;
            }

            var color = context.ResolveVector3(value, defaultColor);
            if (color.X > 1f || color.Y > 1f || color.Z > 1f)
            {
                color /= 255f;
            }

            return Vector3.Clamp(color, Vector3.Zero, Vector3.One);
        }

        private static bool IsDisabled(KVObject modifier)
        {
            if (!modifier.TryGetValue("m_bEnabled", out var enabled))
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

        private static bool IsWorldOrParentSpace(KVObject modifier, string fallback)
            => GetSpace(modifier, fallback) is "WORLD" or "PARENT";

        private static string GetSpace(KVObject modifier, string fallback)
        {
            if (!modifier.TryGetValue("m_CoordinateSpace", out var space) || space.ValueType != KVValueType.String)
            {
                return fallback;
            }

            return ((string)space).Trim().ToUpperInvariant();
        }

        private static string GetStateName(KVObject modifier)
            => GetString(modifier, "m_StateName", "State");

        private static object? ToOverrideValue(KVObject? value)
        {
            if (value is null || value.IsNull)
            {
                return null;
            }

            switch (value.ValueType)
            {
                case KVValueType.String:
                    return (string)value;
                case KVValueType.Boolean:
                    return (bool)value;
                case KVValueType.Int32:
                    return (int)value;
                case KVValueType.Int64:
                    return (long)value;
                case KVValueType.FloatingPoint:
                    return (float)value;
                case KVValueType.FloatingPoint64:
                    return (double)value;
                case KVValueType.Array:
                {
                    var span = value.AsArraySpan();
                    var result = new float[span.Length];
                    for (var i = 0; i < span.Length; i++)
                    {
                        result[i] = span[i].ValueType switch
                        {
                            KVValueType.FloatingPoint or KVValueType.FloatingPoint64 or KVValueType.Int32
                                or KVValueType.Int64 or KVValueType.UInt32 or KVValueType.UInt64 => (float)span[i],
                            KVValueType.Boolean => (bool)span[i] ? 1f : 0f,
                            KVValueType.String when float.TryParse((string)span[i], CultureInfo.InvariantCulture, out var parsed) => parsed,
                            _ => 0f,
                        };
                    }

                    return result;
                }

                default:
                    return null;
            }
        }

        private static KVObject? GetOrDefault(KVObject node, string key)
            => node.TryGetValue(key, out var value) ? value : null;

        private static string GetString(KVObject node, string key, string fallback = "")
        {
            return node.TryGetValue(key, out var value) && value.ValueType == KVValueType.String
                ? (string)value
                : fallback;
        }

        private static int GetInt32(KVObject node, string key, int fallback = 0)
        {
            return node.TryGetValue(key, out var value) && value.ValueType == KVValueType.Int32
                ? (int)value
                : fallback;
        }

        private static bool IsFilterClass(string className)
            => className.StartsWith("Filter_", StringComparison.Ordinal)
                || className is "VariableValue" or "Expression" or "Probability" or "SurfaceProperties";

        private static bool EvaluateFilter(KVObject filterNode, SmartPropEvaluationContext context, int elementId)
        {
            var className = GetClassName(filterNode);
            if (className is "Filter_VariableValue" or "VariableValue" || filterNode.ContainsKey("m_VariableComparison"))
            {
                var comparisonNode = filterNode.TryGetValue("m_VariableComparison", out var comp) && comp.ValueType == KVValueType.Collection
                    ? comp
                    : filterNode;
                return EvaluateVariableComparison(comparisonNode, context);
            }

            if (className is "Filter_Expression" or "Expression")
            {
                if (filterNode.TryGetValue("m_Expression", out var exprNode) && exprNode.ValueType == KVValueType.String)
                {
                    var expr = (string)exprNode;
                    return SmartPropExpressionEvaluator.Evaluate(expr, context, 0f) != 0f;
                }

                return true;
            }

            if (className is "Filter_Probability" or "Probability")
            {
                var probability = context.ResolveScalar(GetOrDefault(filterNode, "m_flProbability"), 1f);
                var rand = DeterministicRandom(elementId, context.InstanceIndex, 0, 777);
                return rand <= probability;
            }

            return true;
        }

        private static bool EvaluateVariableComparison(KVObject comparison, SmartPropEvaluationContext context)
        {
            var name = GetString(comparison, "m_Name");
            if (name.Length == 0)
            {
                name = GetString(comparison, "m_VariableName");
            }

            if (name.Length == 0)
            {
                return true;
            }

            var op = GetString(comparison, "m_Comparison", "EQUAL").ToUpperInvariant();
            var actual = context.GetVariable(name);

            if (!comparison.TryGetValue("m_Value", out var expectedNode))
            {
                return op switch
                {
                    "NOT_EQUAL" or "!=" => actual is null or false or 0 or 0f or "",
                    _ => actual is not (null or false or 0 or 0f or ""),
                };
            }

            if (actual is int or long or float or double or short or byte || expectedNode.ValueType is KVValueType.FloatingPoint or KVValueType.FloatingPoint64 or KVValueType.Int32 or KVValueType.Int64 or KVValueType.UInt32 or KVValueType.UInt64)
            {
                var actualNum = actual switch
                {
                    int i => (float)i,
                    long l => (float)l,
                    float f => f,
                    double d => (float)d,
                    string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    bool b => b ? 1f : 0f,
                    _ => 0f,
                };
                var expectedNum = context.ResolveScalar(expectedNode);

                return op switch
                {
                    "EQUAL" or "==" or "=" => MathF.Abs(actualNum - expectedNum) < 1e-4f,
                    "NOT_EQUAL" or "!=" => MathF.Abs(actualNum - expectedNum) >= 1e-4f,
                    "LESS" or "<" => actualNum < expectedNum,
                    "LESS_OR_EQUAL" or "<=" => actualNum <= expectedNum,
                    "GREATER" or ">" => actualNum > expectedNum,
                    "GREATER_OR_EQUAL" or ">=" => actualNum >= expectedNum,
                    _ => MathF.Abs(actualNum - expectedNum) < 1e-4f,
                };
            }

            if (actual is bool actualBool)
            {
                var expectedBool = expectedNode.ValueType == KVValueType.Boolean ? (bool)expectedNode : (expectedNode.ToString()?.Trim().ToLowerInvariant() is "1" or "true");
                return op switch
                {
                    "NOT_EQUAL" or "!=" => actualBool != expectedBool,
                    _ => actualBool == expectedBool,
                };
            }

            var actualStr = actual?.ToString() ?? string.Empty;
            var expectedStr = context.ResolveString(expectedNode);
            return op switch
            {
                "NOT_EQUAL" or "!=" => !string.Equals(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase),
            };
        }

        private static Vector4? EvaluateSetTintColor(KVObject modifier, SmartPropEvaluationContext context, int elementId)
        {
            if (modifier.TryGetValue("m_ColorChoices", out var choicesNode) && choicesNode.IsArray)
            {
                var choices = choicesNode.AsArraySpan();
                if (choices.Length == 0)
                {
                    return null;
                }

                var selectedChoice = choices[0];
                if (choices.Length > 1)
                {
                    var totalWeight = 0f;
                    Span<float> weights = stackalloc float[choices.Length];
                    for (var i = 0; i < choices.Length; i++)
                    {
                        weights[i] = context.ResolveScalar(GetOrDefault(choices[i], "m_flWeight"), 1f);
                        totalWeight += weights[i];
                    }

                    var randomVal = DeterministicRandom(elementId, context.InstanceIndex, 0, 311) * totalWeight;
                    var accumulated = 0f;
                    for (var i = 0; i < choices.Length; i++)
                    {
                        accumulated += weights[i];
                        if (randomVal <= accumulated)
                        {
                            selectedChoice = choices[i];
                            break;
                        }
                    }
                }

                return ResolveColorValue(selectedChoice.TryGetValue("m_Color", out var col) ? col : selectedChoice, context);
            }

            if (modifier.TryGetValue("m_Color", out var singleColor))
            {
                return ResolveColorValue(singleColor, context);
            }

            return null;
        }

        private static Vector4 ResolveColorValue(KVObject colorNode, SmartPropEvaluationContext context)
        {
            var raw = context.ResolveVector4(colorNode, Vector4.One);
            var r = raw.X > 1f ? raw.X / 255f : raw.X;
            var g = raw.Y > 1f ? raw.Y / 255f : raw.Y;
            var b = raw.Z > 1f ? raw.Z / 255f : raw.Z;
            var a = raw.W > 1f ? raw.W / 255f : (raw.W <= 0f ? 1f : raw.W);
            return new Vector4(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f), Math.Clamp(a, 0f, 1f));
        }

        private static bool GetBool(KVObject node, string key, bool fallback)
        {
            if (!node.TryGetValue(key, out var value))
            {
                return fallback;
            }

            return value.ValueType switch
            {
                KVValueType.Boolean => (bool)value,
                KVValueType.String => !((string)value).Trim().Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => fallback,
            };
        }
    }
}
