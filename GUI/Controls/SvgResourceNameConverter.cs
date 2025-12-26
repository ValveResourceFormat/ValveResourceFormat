#if DEBUG
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace GUI.Controls;

/// <summary>
/// Type converter that provides a dropdown list of available SVG resources in the designer.
/// </summary>
public class SvgResourceNameConverter : TypeConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context)
    {
        return true;
    }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context)
    {
        return true;
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        var svgResources = new List<string> { string.Empty };

        var resourceNames = Program.Assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (resourceName.StartsWith("GUI.Icons.", StringComparison.Ordinal) && resourceName.EndsWith(".svg", StringComparison.Ordinal))
            {
                svgResources.Add(resourceName);
            }
        }

        svgResources.Sort();
        return new StandardValuesCollection(svgResources);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        if (sourceType == typeof(string))
        {
            return true;
        }

        return base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string stringValue)
        {
            return stringValue;
        }

        return base.ConvertFrom(context, culture, value);
    }
}
#endif
