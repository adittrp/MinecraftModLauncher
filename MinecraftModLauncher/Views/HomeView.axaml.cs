using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MinecraftModLauncher.Views;

public partial class HomeView : UserControl {
    public HomeView() {
        InitializeComponent();

        VersionFlyout.WireSlide();
        LoaderFlyout.WireSlide();
        CategoryFlyout.WireSlide();
        DateFlyout.WireSlide();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}