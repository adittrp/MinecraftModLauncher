using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MinecraftModLauncher.Views;

public partial class HomeView : UserControl {
    public HomeView() {
        InitializeComponent();

        // This view hand-writes InitializeComponent() (see below), which opts out
        // of Avalonia's auto-generated x:Name fields — so named elements are
        // looked up explicitly here instead of referenced as fields.
        this.FindControl<Button>("VersionButton")?.WireFlyoutSlide();
        this.FindControl<Button>("LoaderButton")?.WireFlyoutSlide();
        this.FindControl<Button>("CategoryButton")?.WireFlyoutSlide();
        this.FindControl<Button>("DateButton")?.WireFlyoutSlide();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}