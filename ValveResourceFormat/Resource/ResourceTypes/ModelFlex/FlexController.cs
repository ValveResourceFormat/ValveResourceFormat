namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    /// <summary>
    /// Represents a flex controller for model facial animation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CFlexController">CFlexController</seealso>
    public class FlexController
    {
        /// <summary>
        /// Gets the name of the flex controller.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Gets the minimum value of the flex controller.
        /// </summary>
        public float Min { get; private set; }
        /// <summary>
        /// Gets the maximum value of the flex controller.
        /// </summary>
        public float Max { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexController"/> class.
        /// </summary>
        public FlexController(string name, string type, float min, float max)
        {
            // Older games store the controller's own name in m_szType instead of "default"
            if (type != "default" && type != name)
            {
                throw new NotImplementedException($"Unknown FlexController type: {type}");
            }

            Name = name;
            Min = min;
            Max = max;
        }
    }
}
