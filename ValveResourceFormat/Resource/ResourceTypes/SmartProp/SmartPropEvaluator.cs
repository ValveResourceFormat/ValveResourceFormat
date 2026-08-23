using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// One evaluated element instance: the model to draw (empty when the element is a
    /// plain marker), its world transform, and the source element for inspection UIs.
    /// </summary>
    /// <param name="ElementId">The element's m_nElementID.</param>
    /// <param name="ModelName">Resolved m_sModelName, empty for non-model elements.</param>
    /// <param name="WorldMatrix">World transform including model-level scale.</param>
    /// <param name="ParentWorldMatrix">World transform the element was placed into.</param>
    /// <param name="Position">Decomposed world position.</param>
    /// <param name="PitchYawRoll">Decomposed world rotation, in degrees.</param>
    /// <param name="Scale">Decomposed world scale.</param>
    /// <param name="Element">The source element dictionary.</param>
    /// <param name="MaterialGroup">Optional active material group (skin) name.</param>
    /// <param name="TintColor">Optional active tint color.</param>
    public sealed record SmartPropEvaluatedModel(
        int ElementId,
        string ModelName,
        Matrix4x4 WorldMatrix,
        Matrix4x4 ParentWorldMatrix,
        Vector3 Position,
        Vector3 PitchYawRoll,
        Vector3 Scale,
        KVObject Element,
        string? MaterialGroup = null,
        Vector4? TintColor = null);

    /// <summary>
    /// The sampled geometry of a PlaceOnPath element: the smooth world space curve and
    /// its control points.
    /// </summary>
    public sealed record SmartPropPathInfo(
        int ElementId,
        Vector3[] CurveSamples,
        Vector3[] ControlPoints,
        Matrix4x4 WorldMatrix);

    /// <summary>
    /// The complete result of evaluating a smart prop: model instances, editing widgets
    /// and path curves, all in world space.
    /// </summary>
    public sealed record SmartPropEvaluationResult(
        IReadOnlyList<SmartPropEvaluatedModel> Models,
        IReadOnlyList<SmartPropWidget> Widgets,
        IReadOnlyList<SmartPropPathInfo> Paths);

    /// <summary>
    /// Walks a smart prop element tree: evaluates each element's modifier chain, places
    /// model elements, expands PickOne choices, FitOnLine scales and PlaceOnPath
    /// instances, and resolves nested smart prop references through an optional loader.
    /// </summary>
    public static class SmartPropEvaluator
    {
        /// <summary>Nesting depth limit matching the format's m_nMaxDepth default.</summary>
        public const int DefaultMaxDepth = 32;

        private static readonly Vector3[] DefaultPathPoints =
        [
            new(-400f, 0f, 0f),
            new(-200f, 32f, 0f),
            new(200f, -32f, 0f),
            new(400f, 0f, 0f),
        ];

        /// <summary>
        /// Evaluates a CSmartPropRoot dictionary. The root's own modifiers apply to the
        /// whole tree. Variables come from m_Variables unless a context is supplied.
        /// </summary>
        /// <param name="root">The smart prop root dictionary.</param>
        /// <param name="context">Optional prebuilt evaluation context.</param>
        /// <param name="nestedPropResolver">
        /// Optional loader resolving a nested smart prop path to its root dictionary;
        /// nested elements are skipped when absent.
        /// </param>
        /// <param name="maxDepth">Maximum smart prop nesting depth.</param>
        public static SmartPropEvaluationResult Evaluate(
            KVObject root,
            SmartPropEvaluationContext? context = null,
            Func<string, KVObject?>? nestedPropResolver = null,
            int maxDepth = DefaultMaxDepth)
        {
            context ??= new SmartPropEvaluationContext(SmartPropVariableMap.Build(root));

            var state = new EvaluationState();
            Traverse(root, Matrix4x4.Identity, context, state, [], nestedPropResolver, depth: 0, maxDepth);
            return new SmartPropEvaluationResult(state.Models, state.Widgets, state.Paths);
        }

        private sealed class EvaluationState
        {
            public List<SmartPropEvaluatedModel> Models { get; } = [];

            public List<SmartPropWidget> Widgets { get; } = [];

            public List<SmartPropPathInfo> Paths { get; } = [];
        }

        private static void Traverse(
            KVObject element,
            Matrix4x4 parentWorld,
            SmartPropEvaluationContext context,
            EvaluationState state,
            HashSet<string> activeNestedPaths,
            Func<string, KVObject?>? nestedResolver,
            int depth,
            int maxDepth,
            Vector4? inheritedTintColor = null)
        {
            if (IsDisabled(element))
            {
                return;
            }

            var modifiers = SmartPropModifierEvaluator.EvaluateElementModifiers(element, context, parentWorld, stateMap: null, parentTintColor: inheritedTintColor);
            if (modifiers.IsFilteredOut)
            {
                return;
            }

            var elementClass = SmartPropModifierEvaluator.GetClassName(element);
            state.Widgets.AddRange(modifiers.Widgets);

            var activeTint = modifiers.TintColor ?? inheritedTintColor;

            var elementId = GetInt32(element, "m_nElementID");
            if (elementId > 0 && elementClass is "Model" or "ModelEntity" or "PropPhysics" or "PropDynamic")
            {
                var (position, angles, scale) = SmartPropTransform.DecomposeTRS(modifiers.ModelWorldMatrix);
                var materialGroup = ResolveMaterialGroup(element, context);
                state.Models.Add(new SmartPropEvaluatedModel(
                    elementId,
                    context.ResolveString(GetOrDefault(element, "m_sModelName")),
                    modifiers.ModelWorldMatrix,
                    parentWorld,
                    position,
                    angles,
                    scale,
                    element,
                    materialGroup,
                    activeTint));
            }

            if (elementClass == "SmartProp")
            {
                TraverseNested(element, modifiers.WorldMatrix, context, state, activeNestedPaths, nestedResolver, depth, maxDepth, activeTint);
                return;
            }

            if (elementClass == "PlaceOnPath")
            {
                TraversePlaceOnPath(element, modifiers.WorldMatrix, context, state, activeNestedPaths, nestedResolver, depth, maxDepth, activeTint);
                return;
            }

            var children = GetChildren(element);
            if (children.Length == 0)
            {
                return;
            }

            if (elementClass == "PickOne")
            {
                var selected = PickOneChildIndex(element, children.Length, context);
                TraverseChild(children[selected], element, modifiers.WorldMatrix, context, state, activeNestedPaths, nestedResolver, depth, maxDepth, activeTint);
                return;
            }

            for (var i = 0; i < children.Length; i++)
            {
                TraverseChild(children[i], element, modifiers.WorldMatrix, context, state, activeNestedPaths, nestedResolver, depth, maxDepth, activeTint);
            }
        }

        private static string? ResolveMaterialGroup(KVObject element, SmartPropEvaluationContext context)
        {
            if (element.TryGetValue("m_MaterialGroupName", out var matGroupNode))
            {
                var resolved = context.ResolveString(matGroupNode);
                if (resolved.Length > 0)
                {
                    return resolved;
                }
            }

            return null;
        }

        private static void TraverseChild(
            KVObject child,
            KVObject parent,
            Matrix4x4 parentWorld,
            SmartPropEvaluationContext context,
            EvaluationState state,
            HashSet<string> activeNestedPaths,
            Func<string, KVObject?>? nestedResolver,
            int depth,
            int maxDepth,
            Vector4? activeTintColor = null)
        {
            if (child.ValueType != KVValueType.Collection)
            {
                return;
            }

            Traverse(child, parentWorld, DeriveChildContext(parent, child, context), state, activeNestedPaths, nestedResolver, depth, maxDepth, activeTintColor);
        }

        private static void TraverseNested(
            KVObject element,
            Matrix4x4 worldMatrix,
            SmartPropEvaluationContext context,
            EvaluationState state,
            HashSet<string> activeNestedPaths,
            Func<string, KVObject?>? nestedResolver,
            int depth,
            int maxDepth,
            Vector4? activeTintColor = null)
        {
            var nestedPath = context.ResolveString(GetOrDefault(element, "m_sSmartProp"));
            if (nestedPath.Length == 0 || depth >= maxDepth || nestedResolver == null || !activeNestedPaths.Add(nestedPath))
            {
                return;
            }

            var nestedRoot = nestedResolver(nestedPath);
            if (nestedRoot != null)
            {
                var nestedContext = new SmartPropEvaluationContext(SmartPropVariableMap.Build(nestedRoot));
                Traverse(nestedRoot, worldMatrix, nestedContext, state, activeNestedPaths, nestedResolver, depth + 1, maxDepth, activeTintColor);
            }

            activeNestedPaths.Remove(nestedPath);
        }

        private static void TraversePlaceOnPath(
            KVObject element,
            Matrix4x4 worldMatrix,
            SmartPropEvaluationContext context,
            EvaluationState state,
            HashSet<string> activeNestedPaths,
            Func<string, KVObject?>? nestedResolver,
            int depth,
            int maxDepth,
            Vector4? activeTintColor = null)
        {
            var path = SamplePlaceOnPath(element, worldMatrix, context);
            state.Paths.Add(new SmartPropPathInfo(GetInt32(element, "m_nElementID"), path.CurveSamples, path.ControlPoints, worldMatrix));

            var children = GetChildren(element);
            foreach (var instance in path.Instances)
            {
                var instanceContext = context.WithInstance(instanceIndex: instance.Index, instanceCount: instance.Count);
                for (var i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    if (child.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }

                    if (!SmartPropSelectionCriteria.MatchesSelectionCriteria(child, instance.Index, instance.Count, instanceContext))
                    {
                        continue;
                    }

                    Traverse(child, instance.WorldMatrix, DeriveChildContext(element, child, instanceContext), state, activeNestedPaths, nestedResolver, depth, maxDepth, activeTintColor);
                }
            }
        }

        private static int PickOneChildIndex(KVObject element, int childCount, SmartPropEvaluationContext context)
        {
            var elementId = GetInt32(element, "m_nElementID");
            if (elementId > 0 && context.TryGetPickOneSelection(elementId, out var selectedIndex))
            {
                return Math.Clamp(selectedIndex, 0, childCount - 1);
            }

            var index = 0;
            var mode = GetString(element, "m_SelectionMode", "RANDOM").ToUpperInvariant();
            if (mode is "SPECIFIC" or "SPECIFIC_CHILD")
            {
                index = (int)context.ResolveScalar(GetOrDefault(element, "m_SpecificChildIndex"));
            }

            return Math.Clamp(index, 0, childCount - 1);
        }

        /// <summary>
        /// Computes the context a child is evaluated in. A FitOnLine parent derives the
        /// child's linear scale from the line length and the child's LinearLength criteria.
        /// </summary>
        private static SmartPropEvaluationContext DeriveChildContext(KVObject parent, KVObject child, SmartPropEvaluationContext context)
        {
            if (SmartPropModifierEvaluator.GetClassName(parent) != "FitOnLine")
            {
                return context;
            }

            var start = context.ResolveVector3(GetOrDefault(parent, "m_vStart"));
            var end = context.ResolveVector3(GetOrDefault(parent, "m_vEnd"));
            var lineLength = Vector3.Distance(start, end);

            var linearScale = 1f;
            if (SmartPropSelectionCriteria.TryGetLinearLength(child, context, out var linearLength))
            {
                linearScale = linearLength.ComputeScale(lineLength);
            }

            return context.WithInstance(instanceIndex: 0, linearScale: linearScale);
        }

        private readonly record struct PathInstance(int Index, int Count, float Distance, Matrix4x4 WorldMatrix);

        private readonly record struct PlaceOnPathResult(PathInstance[] Instances, Vector3[] CurveSamples, Vector3[] ControlPoints);

        private static PlaceOnPathResult SamplePlaceOnPath(KVObject element, Matrix4x4 parentWorld, SmartPropEvaluationContext context)
        {
            // Extract and resolve control points
            List<Vector3> controlPoints = [];
            if (element.TryGetValue("m_DefaultPath", out var pathNode) && pathNode.IsArray)
            {
                var span = pathNode.AsArraySpan();
                for (var i = 0; i < span.Length; i++)
                {
                    controlPoints.Add(context.ResolveVector3(span[i]));
                }
            }

            if (controlPoints.Count == 0)
            {
                controlPoints.AddRange(DefaultPathPoints);
            }

            var controlPointsLocal = controlPoints.ToArray();
            var isWorldSpace = IsPathInWorldSpace(element);
            var evalParent = Matrix4x4.Identity;
            Vector3[] controlPointsWorld;
            if (isWorldSpace)
            {
                controlPointsWorld = controlPointsLocal;
            }
            else
            {
                controlPointsWorld = new Vector3[controlPointsLocal.Length];
                for (var i = 0; i < controlPointsLocal.Length; i++)
                {
                    controlPointsWorld[i] = SmartPropTransform.TransformPoint(parentWorld, controlPointsLocal[i]);
                }

                evalParent = parentWorld;
            }

            var up = context.ResolveVector3(GetOrDefault(element, "m_vUpDirection"), Vector3.UnitZ);
            Vector3? projectedUp = GetBool(element, "m_bUseProjectedDistance", fallback: false) ? up : null;

            var (samples, totalLength) = SmartPropSpline.ComputeSamples(controlPointsLocal, projectedUp: projectedUp);

            // World space curve samples for drawing
            var curveSamples = new Vector3[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                curveSamples[i] = isWorldSpace
                    ? samples[i].Position
                    : SmartPropTransform.TransformPoint(parentWorld, samples[i].Position);
            }

            var spacing = MathF.Max(0.001f, context.ResolveScalar(GetOrDefault(element, "m_flSpacing"), 1f));
            var offset = context.ResolveScalar(GetOrDefault(element, "m_flOffsetAlongPath"));
            var pathOffset = context.ResolveVector3(GetOrDefault(element, "m_vPathOffset"));
            var isOffsetWorldSpace = string.Equals(GetString(element, "m_PathSpace", "WORLD").Trim().ToUpperInvariant(), "WORLD", StringComparison.Ordinal);

            // Distances along the path at evenly spaced intervals, starting at the offset
            List<float> distances = [];
            if (totalLength < 1e-4f || controlPointsLocal.Length < 2)
            {
                distances.Add(0f);
            }
            else
            {
                for (var d = offset; d <= totalLength + 1e-4f; d += spacing)
                {
                    if (d >= -1e-4f)
                    {
                        distances.Add(Math.Clamp(d, 0f, totalLength));
                    }
                }

                if (distances.Count == 0)
                {
                    distances.Add(0f);
                }
            }

            var instances = new PathInstance[distances.Count];
            for (var i = 0; i < distances.Count; i++)
            {
                var (position, tangent) = SmartPropSpline.InterpolateAtDistance(samples, totalLength, distances[i]);
                var frame = SmartPropTransform.CreateFrame(position, tangent, up);
                frame = SmartPropTransform.ApplyPathOffset(frame, pathOffset, isOffsetWorldSpace);

                var world = isWorldSpace ? frame : frame * evalParent;
                instances[i] = new PathInstance(i, distances.Count, distances[i], world);
            }

            return new PlaceOnPathResult(instances, curveSamples, controlPointsWorld);
        }

        private static bool IsPathInWorldSpace(KVObject element)
        {
            var pathSpace = GetString(element, "m_PathSpace", "WORLD").Trim().ToUpperInvariant();
            if (!element.TryGetValue("m_DefaultPathInWorldSpace", out var inWorld) || inWorld.IsNull)
            {
                return string.Equals(pathSpace, "WORLD", StringComparison.Ordinal) || pathSpace.Length == 0;
            }

            return (inWorld.ValueType == KVValueType.Boolean && (bool)inWorld) || string.Equals(pathSpace, "WORLD", StringComparison.Ordinal);
        }

        private static ReadOnlySpan<KVObject> GetChildren(KVObject element)
            => element.TryGetValue("m_Children", out var children) && children.IsArray
                ? children.AsArraySpan()
                : [];

        private static bool IsDisabled(KVObject node)
        {
            if (!node.TryGetValue("m_bEnabled", out var enabled))
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

        private static KVObject? GetOrDefault(KVObject node, string key)
            => node.TryGetValue(key, out var value) ? value : null;

        private static string GetString(KVObject node, string key, string fallback = "")
        {
            return node.TryGetValue(key, out var value) && value.ValueType == KVValueType.String
                ? (string)value
                : fallback;
        }

        private static int GetInt32(KVObject node, string key)
        {
            return node.TryGetValue(key, out var value) && value.ValueType == KVValueType.Int32
                ? (int)value
                : 0;
        }
    }
}
