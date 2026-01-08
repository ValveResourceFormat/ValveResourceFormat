using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Renderer
{
    public class FlexStateManager
    {
        private readonly Dictionary<int, int> morphIdToRuleId = [];
        private readonly FlexController[] FlexControllers;
        private readonly FlexRule[] FlexRules;
        private readonly float[] controllerValues;
        public MorphComposite MorphComposite { get; }

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

        public void ResetControllers()
        {
            for (var i = 0; i < controllerValues.Length; i++)
            {
                SetControllerValue(i, 0f);
            }
        }

        public float EvaluateMorph(int morphId)
        {
            var ruleId = morphIdToRuleId[morphId];
            var rule = FlexRules[ruleId];

            return rule.Evaluate(controllerValues);
        }

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
