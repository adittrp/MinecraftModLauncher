using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MinecraftModLauncher.Views;

public partial class LibraryView : UserControl {
    public LibraryView() {
        InitializeComponent();

        TargetInstanceFlyout.WireSlide();
        TypeFlyout.WireSlide();
        LoaderFlyout.WireSlide();
        LibraryCategoryFlyout.WireSlide();
        SortFlyout.WireSlide();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}