using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Renderer
{
    public class FlexStateManager
    {
        public FlexRule[] Rules { get; }
        public FlexController[] Controllers { get; }
        private Dictionary<string, int> controllerNameToId = new();
        private Dictionary<int, int> morphIdToRuleId = new();
        private float[] controllerValues;

        public FlexStateManager(FlexRule[] rules, FlexController[] controllers)
        {
            Rules = rules;
            Controllers = controllers;

            for (var i = 0; i < Controllers.Length; i++)
            {
                var controller = Controllers[i];
                controllerNameToId.Add(controller.Name, i);
            }
            controllerValues = new float[Controllers.Length];

            for (var i = 0; i < Rules.Length; i++)
            {
                var rule = Rules[i];
                morphIdToRuleId[rule.FlexID] = i;
            }
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
                var controller = Controllers[id];

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
        public float EvaluateMorph(int morphId)
        {
            var ruleId = morphIdToRuleId[morphId];
            var rule = Rules[ruleId];

            return rule.Evaluate(controllerValues);
        }
    }
}
