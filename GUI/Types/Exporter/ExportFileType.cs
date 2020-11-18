using System;
using System.Collections.Generic;
using System.Text;

namespace GUI.Types.Exporter
{
    public enum ExportFileType
    {
        /// <summary>
        /// Automatically compute file type based on resource type
        /// </summary>
        Auto,

        /// <summary>
        /// Use the GLB format
        /// </summary>
        GLB
    }
}
