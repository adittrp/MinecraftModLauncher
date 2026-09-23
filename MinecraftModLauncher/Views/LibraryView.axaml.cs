using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MinecraftModLauncher.Views;

public partial class LibraryView : UserControl {
    public LibraryView() {
        InitializeComponent();

        TargetInstanceButton.WireFlyoutSlide();
        TypeButton.WireFlyoutSlide();
        LoaderButton.WireFlyoutSlide();
        LibraryCategoryButton.WireFlyoutSlide();
        SortButton.WireFlyoutSlide();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}