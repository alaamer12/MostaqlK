using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

public class AppIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppIconGlyph glyph)
        {
            Color? color = null;
            if (parameter is Color c) color = c;
            else if (parameter is string s) color = Color.FromArgb(s);
            
            return glyph.ToImageSource(color);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
