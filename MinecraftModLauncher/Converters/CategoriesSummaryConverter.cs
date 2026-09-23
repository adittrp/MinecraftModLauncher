using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace MinecraftModLauncher.Converters;

// Renders a category list as one short chip label, e.g. "technology, magic +2".
public class CategoriesSummaryConverter : IValueConverter
{
    public static readonly CategoriesSummaryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not List<string> categories || categories.Count == 0)
            return "Uncategorized";

        string shown = string.Join(", ", categories.Take(2));
        return categories.Count > 2 ? $"{shown} +{categories.Count - 2}" : shown;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
