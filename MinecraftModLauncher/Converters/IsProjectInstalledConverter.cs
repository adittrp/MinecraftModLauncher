using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using MinecraftModLauncher.Models;

namespace MinecraftModLauncher.Converters;

// MultiBinding: [0] = the search hit's ProjectId (string), [1] = the selected
// instance's InstalledMod list, [2] = that list's Count (present only so the
// binding re-evaluates when mods are added/removed — ObservableCollection<T>
// raises PropertyChanged for Count on every Add/Remove).
// ConverterParameter "invert" flips the result, for pairing an "Install" button
// with an "Installed" badge off the exact same two source bindings.
public class IsProjectInstalledConverter : IMultiValueConverter
{
    public static readonly IsProjectInstalledConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        string? projectId = values.Count > 0 ? values[0] as string : null;
        bool installed = projectId != null
            && values.Count > 1
            && values[1] is IEnumerable<InstalledMod> mods
            && mods.Any(m => m.ProjectId == projectId);

        return Equals(parameter, "invert") ? !installed : installed;
    }
}
