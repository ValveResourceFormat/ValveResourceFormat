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
        private Dictionary<string, int> controllerNameToId = new();
        private Dictionary<int, int> morphIdToRuleId = new();
        private float[] controllerValues;
        public MorphComposite MorphComposite { get; }

        public FlexStateManager(VrfGuiContext guiContext, Morph morph)
        {
            Morph = morph;

            for (var i = 0; i < Morph.FlexControllers.Length; i++)
            {
                var controller = Morph.FlexControllers[i];
                controllerNameToId.Add(controller.Name, i);
            }
            controllerValues = new float[Morph.FlexControllers.Length];

            for (var i = 0; i < Morph.FlexRules.Length; i++)
            {
                var rule = Morph.FlexRules[i];
                morphIdToRuleId[rule.FlexID] = i;
            }

            MorphComposite = new MorphComposite(guiContext, morph);
        }
        public int GetControllerId(string name)
        {
            if (controllerNameToId.TryGetValue(name, out var id))
            {
                return id;
            }
            else
            {
                return -1;
            }
        }
        public void SetControllerValue(string name, float value)
        {
            int id = GetControllerId(name);
            if (id != -1)
            {
                var controller = Morph.FlexControllers[id];

                controllerValues[id] = Math.Clamp(value, controller.Min, controller.Max);
            }
        }
        public void SetControllerValues(Dictionary<string, float> datas)
        {
            foreach (var item in datas)
            {
                SetControllerValue(item.Key, item.Value);
            }
        }
        public void ResetControllers()
        {
            for (var i = 0; i < controllerValues.Length; i++)
            {
                controllerValues[i] = 0f;
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
