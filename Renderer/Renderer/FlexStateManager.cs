using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Manages flex controller state and morph target composition for facial animation.
    /// </summary>
    public class FlexStateManager
    {
        private readonly Dictionary<int, int> morphIdToRuleId = [];
        private readonly FlexController[] FlexControllers;
        private readonly FlexRule[] FlexRules;
        private readonly float[] controllerValues;
        /// <summary>Gets the morph composite that renders blended morph targets to the GPU.</summary>
        public MorphComposite MorphComposite { get; }

        /// <summary>Initializes a new flex state manager for the given morph data.</summary>
        /// <param name="renderContext">The renderer context used to create GPU resources.</param>
        /// <param name="morph">The morph data containing flex controllers and rules.</param>
        public FlexStateManager(RendererContext renderContext, Morph morph)
        {
            FlexRules = morph.FlexRules;
            FlexControllers = morph.FlexControllers;

            controllerValues = new float[FlexControllers.Length];

            for (var i = 0; i < FlexRules.Length; i++)
            {
                var rule = FlexRules[i];
                morphIdToRuleId[rule.FlexID] = i;
            }

            MorphComposite = new MorphComposite(renderContext, morph);
        }

        /// <summary>Sets a single flex controller value, clamped to its defined range.</summary>
        /// <param name="id">Index of the flex controller.</param>
        /// <param name="value">Desired value before clamping.</param>
        /// <returns><see langword="true"/> if the value changed; otherwise <see langword="false"/>.</returns>
        public bool SetControllerValue(int id, float value)
        {
            var controller = FlexControllers[id];
            value = Math.Clamp(value, controller.Min, controller.Max);
            if (controllerValues[id] == value)
            {
                return false;
            }

            controllerValues[id] = value;
            return true;
        }

        /// <summary>Sets multiple flex controller values in order, stopping at whichever array is shorter.</summary>
        /// <param name="datas">Array of controller values to apply.</param>
        /// <returns><see langword="true"/> if any value changed; otherwise <see langword="false"/>.</returns>
        public bool SetControllerValues(float[] datas)
        {
            var length = Math.Min(datas.Length, controllerValues.Length);
            var changed = false;
            for (var i = 0; i < length; i++)
            {
                if (SetControllerValue(i, datas[i]))
                {
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Resets all flex controller values to zero.</summary>
        public void ResetControllers()
        {
            for (var i = 0; i < controllerValues.Length; i++)
            {
                SetControllerValue(i, 0f);
            }
        }

        /// <summary>Evaluates the flex rule for the specified morph target using current controller values.</summary>
        /// <param name="morphId">Morph target identifier.</param>
        /// <returns>The computed morph weight.</returns>
        public float EvaluateMorph(int morphId)
        {
            var ruleId = morphIdToRuleId[morphId];
            var rule = FlexRules[ruleId];

            return rule.Evaluate(controllerValues);
        }

        /// <summary>Evaluates all morph rules and pushes the resulting weights to the morph composite.</summary>
        public void UpdateComposite()
        {
            foreach (var i in morphIdToRuleId.Keys)
            {
                var morphValue = EvaluateMorph(i);
                MorphComposite.SetMorphValue(i, morphValue);
            }
        }
    }
}
