using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MinecraftModLauncher.Views;

public partial class HomeView : UserControl {
    public HomeView() {
        InitializeComponent();

        VersionButton.WireFlyoutSlide();
        LoaderButton.WireFlyoutSlide();
        CategoryButton.WireFlyoutSlide();
        DateButton.WireFlyoutSlide();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}