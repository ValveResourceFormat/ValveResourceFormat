using GUI.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Renderer
{
    class FlexStateManager
    {
        public Morph Morph { get; }
        private Dictionary<int, int> morphIdToRuleId = new();
        private float[] controllerValues;
        public MorphComposite MorphComposite { get; }

        public FlexStateManager(VrfGuiContext guiContext, Morph morph)
        {
            Morph = morph;

            controllerValues = new float[Morph.FlexControllers.Length];

            for (var i = 0; i < Morph.FlexRules.Length; i++)
            {
                var rule = Morph.FlexRules[i];
                morphIdToRuleId[rule.FlexID] = i;
            }

            MorphComposite = new MorphComposite(guiContext, morph);
        }
        public bool SetControllerValue(int id, float value)
        {
            var controller = Morph.FlexControllers[id];
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
            var rule = Morph.FlexRules[ruleId];

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
