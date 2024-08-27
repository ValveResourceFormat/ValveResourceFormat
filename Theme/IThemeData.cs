using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Theme
{
    public interface IThemeData
    {
        string Name { get; }
        /// <summary>
        /// The back color.
        /// </summary>
        Color Primary { get; }
        /// <summary>
        /// The second back color if any (e.g. for gradient brushes).
        /// </summary>
        Color Secondary { get; }
        /// <summary>
        /// The text (fore) color.
        /// </summary>
        Color Tertiary { get; }
        /// <summary>
        /// Border Color.
        /// </summary>
        Color Quaternary { get; }
        /// <summary>
        /// Hover Color.
        /// </summary>
        Color Quinary { get; }
        /// <summary>
        /// Pressed Color.
        /// </summary>
        Color Senary { get; }
    }
}
