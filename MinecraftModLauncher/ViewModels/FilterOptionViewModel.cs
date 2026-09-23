using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MinecraftModLauncher.ViewModels {
    // One checkable entry in a filter/category flyout (e.g. "1.21.1 (3)").
    // Reused for the Home instance filters and the New Instance category picker.
    public partial class FilterOptionViewModel : ObservableObject {
        public string Value { get; }
        public string Label { get; }

        [ObservableProperty] private bool _isSelected;

        public Action? OnChanged { get; set; }

        public FilterOptionViewModel(string value, int? count = null) {
            Value = value;
            Label = count.HasValue ? $"{value} ({count})" : value;
        }

        partial void OnIsSelectedChanged(bool value) => OnChanged?.Invoke();
    }
}
