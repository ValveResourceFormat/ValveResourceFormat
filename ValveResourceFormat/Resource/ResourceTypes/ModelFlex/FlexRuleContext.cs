using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    /// <summary>
    /// Context for evaluating flex rules.
    /// </summary>
    public readonly struct FlexRuleContext
    {
        /// <summary>
        /// Gets the controller values.
        /// </summary>
        public float[] ControllerValues { get; }
        /// <summary>
        /// Gets the stack for flex operations.
        /// </summary>
        public Stack<float> Stack { get; }
        /// <summary>
        /// Gets the controllers, whose ranges the eyelid operations remap against.
        /// </summary>
        public FlexController[]? Controllers { get; }
        /// <summary>
        /// Gets the values of the flexes evaluated so far, which a fetch2 operation reads.
        /// </summary>
        public float[]? FlexValues { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexRuleContext"/> struct.
        /// </summary>
        public FlexRuleContext(Stack<float> stack, float[] controllerValues)
            : this(stack, controllerValues, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexRuleContext"/> struct.
        /// </summary>
        public FlexRuleContext(Stack<float> stack, float[] controllerValues, FlexController[]? controllers, float[]? flexValues)
        {
            ControllerValues = controllerValues;
            Stack = stack;
            Controllers = controllers;
            FlexValues = flexValues;
        }

        /// <summary>
        /// Pops a value, yielding zero on an empty stack the way the engine reads past its own.
        /// </summary>
        public readonly float Pop()
        {
            return Stack.Count > 0 ? Stack.Pop() : 0f;
        }

        /// <summary>
        /// Pops a value and reinterprets its bits as an index, the way flex ops encode operand indices as floats.
        /// </summary>
        public readonly int PopIndex()
        {
            return BitConverter.SingleToInt32Bits(Pop());
        }

        /// <summary>
        /// Gets a controller value, or zero when the index is out of range.
        /// </summary>
        public readonly float GetControllerValue(int index)
        {
            return index >= 0 && index < ControllerValues.Length ? ControllerValues[index] : 0f;
        }

        /// <summary>
        /// Gets a controller value mapped from its own range onto the given one.
        /// </summary>
        public readonly float GetRemappedControllerValue(int index, float min, float max)
        {
            var value = GetControllerValue(index);

            if (Controllers == null || index < 0 || index >= Controllers.Length)
            {
                return value;
            }

            var controller = Controllers[index];
            return MathUtils.RemapValClamped(value, controller.Min, controller.Max, min, max);
        }

        /// <summary>
        /// Gets the value of an already evaluated flex, or zero when it has no value yet.
        /// </summary>
        public readonly float GetFlexValue(int index)
        {
            return FlexValues != null && index >= 0 && index < FlexValues.Length ? FlexValues[index] : 0f;
        }
    }
}
